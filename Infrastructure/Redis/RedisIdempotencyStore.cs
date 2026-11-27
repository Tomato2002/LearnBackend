using System.Text.Json;
using LearnBackend.Common.Idempotency;
using StackExchange.Redis;

namespace LearnBackend.Infrastructure.Redis;

/// <summary>
/// Lưu trữ Idempotency trên Redis bất đồng bộ (StringGetAsync / StringSetAsync),
/// không block ThreadPool ở tải cao 10k+ CCU.
/// </summary>
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    public async Task<IdempotentResponseRecord?> GetAsync(string compositeKey, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var raw = await db.StringGetAsync(compositeKey);
        if (!raw.HasValue) return null;

        return JsonSerializer.Deserialize<IdempotentResponseRecord>(raw.ToString());
    }

    public async Task SetAsync(string compositeKey, IdempotentResponseRecord record, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(record);
        await db.StringSetAsync(compositeKey, json, ttl);
    }
}
