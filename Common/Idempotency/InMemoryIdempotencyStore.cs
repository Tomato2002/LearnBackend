using Microsoft.Extensions.Caching.Memory;

namespace LearnBackend.Common.Idempotency;

/// <summary>
/// Lưu kết quả Idempotency trong IMemoryCache có TTL tự hết hạn (không rò rỉ RAM).
/// </summary>
public sealed class InMemoryIdempotencyStore(IMemoryCache cache) : IIdempotencyStore
{
    public bool TryGet(string compositeKey, out IdempotentResponseRecord? record)
    {
        return cache.TryGetValue(compositeKey, out record);
    }

    public void Set(string compositeKey, IdempotentResponseRecord record, TimeSpan ttl)
    {
        cache.Set(compositeKey, record, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });
    }
}
