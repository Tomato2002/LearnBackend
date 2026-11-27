using LearnBackend.Common.Extensions;
using LearnBackend.Common.Idempotency;

namespace LearnBackend.Common.Middleware;

public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Idempotency-Key";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store)
    {
        if (!MutatingMethods.Contains(context.Request.Method) ||
            !context.Request.Headers.TryGetValue(HeaderName, out var rawKey))
        {
            await next(context);
            return;
        }

        var idempotencyKey = rawKey.ToString().Trim();
        if (string.IsNullOrEmpty(idempotencyKey) || idempotencyKey.Length > 128)
        {
            await next(context);
            return;
        }

        var scopePrefix = context.User.TryGetPlayerId(out var playerId)
            ? playerId.ToString("N")
            : "anon";

        var compositeKey = $"idem:{scopePrefix}:{context.Request.Method}:{context.Request.Path}:{idempotencyKey}";

        var cached = await store.GetAsync(compositeKey, context.RequestAborted);
        if (cached is not null)
        {
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = cached.ContentType;
            context.Response.Headers["X-Idempotency-Replayed"] = "true";
            await context.Response.WriteAsync(cached.Body, context.RequestAborted);
            return;
        }

        var originalBodyStream = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            buffer.Position = 0;
            using var reader = new StreamReader(buffer, leaveOpen: true);
            var responseBody = await reader.ReadToEndAsync();

            if (context.Response.StatusCode is >= 200 and < 300)
            {
                var record = new IdempotentResponseRecord(
                    StatusCode: context.Response.StatusCode,
                    ContentType: context.Response.ContentType ?? "application/json",
                    Body: responseBody,
                    CachedAtUtc: DateTime.UtcNow);

                await store.SetAsync(compositeKey, record, CacheTtl, context.RequestAborted);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBodyStream, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }
}
