namespace LearnBackend.Common.Idempotency;

public sealed record IdempotentResponseRecord(
    int StatusCode,
    string ContentType,
    string Body,
    DateTime CachedAtUtc);

/// <summary>
/// Lưu trữ kết quả các request có gắn header X-Idempotency-Key.
/// Tách Interface để sau này scale nhiều server chỉ cần thay bằng Redis.
/// </summary>
public interface IIdempotencyStore
{
    bool TryGet(string compositeKey, out IdempotentResponseRecord? record);
    void Set(string compositeKey, IdempotentResponseRecord record, TimeSpan ttl);
}
