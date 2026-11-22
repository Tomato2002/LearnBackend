using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Common.Locking;
using StackExchange.Redis;

namespace LearnBackend.Infrastructure.Redis;

/// <summary>
/// Redis Distributed Lock chuẩn Production cho mô hình nhiều Game Server sau Load Balancer:
/// - Khóa bằng lệnh nguyên tử: SET lock:player:{id} {uniqueToken} NX PX 10000
/// - Mở khóa bằng Lua Script trên Redis: chỉ DEL đúng key nếu token trùng khớp (tránh Server A xóa nhầm khóa của Server B).
/// </summary>
public sealed class RedisPlayerLockManager(IConnectionMultiplexer redis) : IPlayerLockManager
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);

    private const string SafeUnlockLuaScript = """
        if redis.call("get", KEYS[1]) == ARGV[1] then
            return redis.call("del", KEYS[1])
        else
            return 0
        end
        """;

    public async Task<IDisposable> AcquireAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var lockKey = $"lock:player:{playerId:N}";
        var lockToken = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow + AcquireTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var acquired = await db.StringSetAsync(lockKey, lockToken, LockTtl, When.NotExists);
            if (acquired)
            {
                return new RedisLockReleaser(db, lockKey, lockToken);
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        throw new GameException(
            "PLAYER_BUSY",
            "Tài khoản đang xử lý yêu cầu trên một server khác, vui lòng thử lại sau giây lát.",
            HttpStatusCode.Conflict);
    }

    private sealed class RedisLockReleaser(IDatabase db, string lockKey, string lockToken) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                db.ScriptEvaluate(
                    SafeUnlockLuaScript,
                    [(RedisKey)lockKey],
                    [(RedisValue)lockToken],
                    CommandFlags.FireAndForget);
            }
            catch
            {
                // Nếu Redis tạm thời gián đoạn, khóa vẫn tự hết hạn sau LockTtl (10s) -> không bao giờ deadlock vĩnh viễn!
            }
        }
    }
}
