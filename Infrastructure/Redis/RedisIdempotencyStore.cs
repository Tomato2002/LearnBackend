using System.Text.Json;
using LearnBackend.Common.Idempotency;
using StackExchange.Redis;

namespace LearnBackend.Infrastructure.Redis;

/// <summary>
/// Lưu trữ kết quả Idempotency trên Redis để mọi node Game Server chia sẻ chung trạng thái chống double-charge.
/// </summary>
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    public bool TryGet(string compositeKey, out IdempotentResponseRecord? record)
    {
        var db = redis.GetDatabase();
        var raw = db.StringGet(compositeKey);
        if (!raw.HasValue)
        {
            record = null;
            return false;
        }

        record = JsonSerializer.Deserialize<IdempotentResponseRecord>(raw.ToString());
        return record is not null;
    }

    public void Set(string compositeKey, IdempotentResponseRecord record, TimeSpan ttl)
    {
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(record);
        db.StringSet(compositeKey, json, ttl);
    }
}
