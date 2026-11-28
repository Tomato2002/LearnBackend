namespace LearnBackend.Common.Middleware;

/// <summary>
/// Gắn X-Correlation-Id cho mọi Request và đẩy vào Log Scope (chuẩn Observability của hệ thống phân tán).
/// Khi người chơi báo lỗi, chỉ cần tra đúng 1 CorrelationId là lần ra toàn bộ chuỗi log từ API -> DB -> Outbox.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming) &&
                            !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString().Trim()
            : Guid.NewGuid().ToString("N")[..12];

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
