using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;

namespace MatriX.API.Middlewares
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private static StreamWriter _logWriter;
        private static readonly object _writerLock = new();
        private static bool _initialized = false;

        public RequestLoggingMiddleware(RequestDelegate next, IHostEnvironment env)
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
            var countingStream = new ResponseBodyCountingStream(originalBody);
            context.Response.Body = countingStream;

            try
            {
                await _next(context);
                // ensure any buffered content is flushed to countingStream
                await countingStream.FlushAsync();
            }
            finally
            {
                context.Response.Body = originalBody;

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
            }
        }


        private sealed class ResponseBodyCountingStream : Stream
        {
            private readonly Stream _inner;

            private long _bytesWritten;

            public long BytesWritten => Interlocked.Read(ref _bytesWritten);

            public ResponseBodyCountingStream(Stream inner)
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
                Interlocked.Add(ref _bytesWritten, count);
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref _bytesWritten, count);
            }
        }
    }
}