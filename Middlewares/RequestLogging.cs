using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using System.Text;

namespace MatriX.API.Middlewares
{
    public class RequestLogging
    {
        private readonly RequestDelegate _next;
        private static StreamWriter _logWriter;
        private static StreamWriter _fullLogWriter;
        private static readonly object _writerLock = new();
        private static bool _initialized = false;

        public RequestLogging(RequestDelegate next, IHostEnvironment env)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            EnsureInitialized(env);
        }

        private void EnsureInitialized(IHostEnvironment env)
        {
            if (_initialized) return;

            lock (_writerLock)
            {
                if (_initialized) return;

                var path = Path.Combine(env.ContentRootPath, "request.log");
                var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _logWriter = new StreamWriter(fs) { AutoFlush = true };

                var fullPath = Path.Combine(env.ContentRootPath, "request_full.log");
                var fsFull = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _fullLogWriter = new StreamWriter(fsFull) { AutoFlush = true };

                _initialized = true;
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (AppInit.settings.log == false)
            {
                await _next(context);
                return;
            }

            var originalBody = context.Response.Body;
            var countingStream = new ResponseBodyCopyingStream(originalBody);
            context.Response.Body = countingStream;

            #region requestBodyText
            string requestBodyText = string.Empty;

            try
            {
                if (context.Request.ContentLength > 0 || HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method))
                {
                    context.Request.EnableBuffering();
                    context.Request.Body.Seek(0, SeekOrigin.Begin);

                    using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
                    {
                        requestBodyText = await reader.ReadToEndAsync().ConfigureAwait(false);
                        context.Request.Body.Seek(0, SeekOrigin.Begin);
                    }
                }
            }
            catch
            {
                requestBodyText = string.Empty;
            }
            #endregion

            try
            {
                await _next(context);
                // ensure any buffered content is flushed to countingStream
                await countingStream.FlushAsync();
            }
            finally
            {
                context.Response.Body = originalBody;

                #region logformat
                string logLine = AppInit.settings.logformat;
                logLine = logLine.Replace("{time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                                 .Replace("{ip}", context.Connection.RemoteIpAddress?.ToString() ?? "-")
                                 .Replace("{bytes}", countingStream.BytesWritten.ToString())
                                 .Replace("{method}", context.Request.Method)
                                 .Replace("{host}", context.Request.Host.Host ?? "-")
                                 .Replace("{url}", context.Request.Path + context.Request.QueryString);

                try
                {
                    var headerRegex = new Regex("\\{headers:([^}]+)\\}", RegexOptions.IgnoreCase);
                    logLine = headerRegex.Replace(logLine, match =>
                    {
                        try
                        {
                            var headerName = match.Groups[1].Value.Trim();
                            if (string.IsNullOrEmpty(headerName))
                                return string.Empty;

                            if (context.Request.Headers.TryGetValue(headerName, out var values))
                                return string.Join(",", values.ToArray());

                            foreach (var h in context.Request.Headers)
                            {
                                if (string.Equals(h.Key, headerName, StringComparison.OrdinalIgnoreCase))
                                    return string.Join(",", h.Value.ToArray());
                            }

                            return string.Empty;
                        }
                        catch { return string.Empty; }
                    });
                }
                catch { }

                if (_logWriter != null)
                {
                    lock (_writerLock)
                    {
                        _logWriter.WriteLine(logLine);
                    }
                }
                #endregion

                #region Full logging when IP matches
                try
                {
                    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "-";
                    var fullToIp = AppInit.settings.logfullToIP;
                    if (!string.IsNullOrEmpty(fullToIp) && string.Equals(fullToIp, clientIp, StringComparison.OrdinalIgnoreCase))
                    {
                        var sb = new StringBuilder();

                        sb.AppendLine("-----REQUEST START-----");
                        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        sb.AppendLine($"IP: {clientIp}");
                        sb.AppendLine($"Method: {context.Request.Method}");
                        sb.AppendLine($"Host: {context.Request.Host}");
                        sb.AppendLine($"URL: {context.Request.Path + context.Request.QueryString}");
                        sb.AppendLine("");
                        sb.AppendLine("Request Headers:");
                        foreach (var h in context.Request.Headers)
                        {
                            sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value.ToArray())}");
                        }
                        sb.AppendLine("");
                        sb.AppendLine("Request Body:");
                        sb.AppendLine(string.IsNullOrEmpty(requestBodyText) ? "<empty>" : requestBodyText);
                        sb.AppendLine("");
                        sb.AppendLine("Response Headers:");
                        try
                        {
                            foreach (var h in context.Response.Headers)
                            {
                                sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value.ToArray())}");
                            }
                        }
                        catch { sb.AppendLine("<unable to read response headers>"); }

                        sb.AppendLine("");
                        sb.AppendLine("Response Body:");
                        try
                        {
                            var respBody = countingStream.GetCapturedBodyAsString();
                            sb.AppendLine(string.IsNullOrEmpty(respBody) ? "<empty>" : respBody);
                        }
                        catch
                        {
                            sb.AppendLine("<unable to read response body>");
                        }

                        sb.AppendLine("-----REQUEST END-----");
                        sb.AppendLine();

                        if (_fullLogWriter != null)
                        {
                            lock (_writerLock)
                            {
                                _fullLogWriter.Write(sb.ToString());
                            }
                        }
                    }
                }
                catch { }
                #endregion
            }
        }


        private sealed class ResponseBodyCopyingStream : Stream
        {
            private readonly Stream _inner;
            private readonly MemoryStream _copyStream = new();
            private long _bytesWritten;

            public long BytesWritten => Interlocked.Read(ref _bytesWritten);

            public ResponseBodyCopyingStream(Stream inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();

            public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                _inner.Write(buffer, offset, count);
                try
                {
                    _copyStream.Write(buffer, offset, count);
                }
                catch { }
                Interlocked.Add(ref _bytesWritten, count);
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                try
                {
                    await _copyStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                }
                catch { }
                Interlocked.Add(ref _bytesWritten, count);
            }

            public string GetCapturedBodyAsString()
            {
                try
                {
                    var arr = _copyStream.ToArray();
                    // try to decode as UTF8, fallback to ISO-8859-1 as last resort
                    var text = Encoding.UTF8.GetString(arr);
                    return text;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
    }
}