namespace LearnBackend.Common.Idempotency;

public sealed record IdempotentResponseRecord(
    int StatusCode,
    string ContentType,
    string Body,
    DateTime CachedAtUtc);

/// <summary>
/// Lưu trữ kết quả các request gắn header X-Idempotency-Key hoàn toàn bất đồng bộ (Async).
/// Tránh chặn ThreadPool khi dùng Redis ở quy mô 10k+ CCU.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotentResponseRecord?> GetAsync(string compositeKey, CancellationToken ct = default);
    Task SetAsync(string compositeKey, IdempotentResponseRecord record, TimeSpan ttl, CancellationToken ct = default);
}
