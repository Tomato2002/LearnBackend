using Microsoft.Extensions.Caching.Memory;

namespace LearnBackend.Common.Idempotency;

public sealed class InMemoryIdempotencyStore(IMemoryCache cache) : IIdempotencyStore
{
    public Task<IdempotentResponseRecord?> GetAsync(string compositeKey, CancellationToken ct = default)
    {
        cache.TryGetValue(compositeKey, out IdempotentResponseRecord? record);
        return Task.FromResult(record);
    }

    public Task SetAsync(string compositeKey, IdempotentResponseRecord record, TimeSpan ttl, CancellationToken ct = default)
    {
        cache.Set(compositeKey, record, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });
        return Task.CompletedTask;
    }
}
