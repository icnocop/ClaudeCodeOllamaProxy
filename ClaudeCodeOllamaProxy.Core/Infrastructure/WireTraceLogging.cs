using System.Text;

namespace ClaudeCodeOllamaProxy.Infrastructure;

/// <summary>
/// Logs full HTTP requests and responses (headers + bodies, including streamed SSE/NDJSON chunks)
/// at <see cref="LogLevel.Trace"/> under the <c>ClaudeCodeOllamaProxy.Wire</c> category. Inactive
/// (zero overhead) unless that category is enabled for Trace.
/// </summary>
public static class WireTraceLogging
{
    public static IApplicationBuilder UseWireTraceLogging(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ClaudeCodeOllamaProxy.Wire");

        // Cap on bodies/chunks logged. Default 1 MB captures complete (tens-of-KB) system prompts;
        // 0 = unlimited. Configurable via Wire:MaxBytes.
        var configured = app.Configuration.GetValue("Wire:MaxBytes", 1_048_576);
        var maxBytes = configured <= 0 ? int.MaxValue : configured;

        app.Use(async (ctx, next) =>
        {
            if (!logger.IsEnabled(LogLevel.Trace))
            {
                await next(ctx);
                return;
            }

            await LogRequestAsync(ctx, logger, maxBytes);

            var originalBody = ctx.Response.Body;
            var tee = new TeeStream(originalBody, logger, maxBytes);
            ctx.Response.Body = tee;
            try
            {
                await next(ctx);
            }
            finally
            {
                ctx.Response.Body = originalBody;
                logger.LogTrace("HTTP <= {Method} {Path} responded {Status} {ContentType} ({Bytes} body bytes)",
                    ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, ctx.Response.ContentType, tee.BytesWritten);
            }
        });

        return app;
    }

    private static async Task LogRequestAsync(HttpContext ctx, ILogger logger, int maxBytes)
    {
        ctx.Request.EnableBuffering();

        string body = string.Empty;
        if ((ctx.Request.ContentLength ?? 0) > 0)
        {
            var buffer = new byte[Math.Min(maxBytes, (int)ctx.Request.ContentLength!.Value)];
            var read = await ctx.Request.Body.ReadAsync(buffer);
            body = Encoding.UTF8.GetString(buffer, 0, read);
            ctx.Request.Body.Position = 0;
        }

        var headers = string.Join("; ", ctx.Request.Headers.Select(h => $"{h.Key}={h.Value}"));
        logger.LogTrace("HTTP => {Method} {Path}{Query}\n  headers: {Headers}\n  body: {Body}",
            ctx.Request.Method, ctx.Request.Path, ctx.Request.QueryString, headers, JsonDefaults.Prettify(body));
    }

    /// <summary>Pass-through write stream that also logs each chunk written to the response.</summary>
    private sealed class TeeStream(Stream inner, ILogger logger, int maxBytes) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Log(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            Log(buffer.AsSpan(offset, count));
            await inner.WriteAsync(buffer.AsMemory(offset, count), ct);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            Log(buffer.Span);
            await inner.WriteAsync(buffer, ct);
            BytesWritten += buffer.Length;
        }

        private void Log(ReadOnlySpan<byte> span)
        {
            var slice = span.Length > maxBytes ? span[..maxBytes] : span;
            logger.LogTrace("HTTP <= {Chunk}", JsonDefaults.Prettify(Encoding.UTF8.GetString(slice)));
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
