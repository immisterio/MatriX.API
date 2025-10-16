using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

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

            var startTime = DateTime.Now;
            var method = context.Request.Method;
            var url = context.Request.Path + context.Request.QueryString;

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
                var bytesWritten = countingStream.BytesWritten;
                var timeString = startTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"{timeString} - {method} - {bytesWritten} - {url}";

                if (_logWriter != null)
                {
                    lock (_writerLock)
                    {
                        _logWriter.WriteLine(logLine);
                    }
                }
            }
        }

        // Simple wrapper stream that counts bytes written
        private sealed class ResponseBodyCountingStream : Stream
        {
            private readonly Stream _inner;
            // use a backing field so we can pass it by ref to Interlocked APIs
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