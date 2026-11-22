using System.Collections.Concurrent;
using System.Net;
using LearnBackend.Common.Exceptions;

namespace LearnBackend.Common.Locking;

/// <summary>
/// Cài đặt khóa trong RAM cho mô hình 1 server (Modular Monolith).
/// Mỗi PlayerId có 1 SemaphoreSlim riêng: người chơi A và B vẫn chạy song song thoải mái,
/// chỉ các request trùng 1 PlayerId mới phải xếp hàng tuần tự.
/// </summary>
public sealed class InMemoryPlayerLockManager : IPlayerLockManager
{
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(playerId, _ => new SemaphoreSlim(1, 1));
        var acquired = await semaphore.WaitAsync(AcquireTimeout, cancellationToken);

        if (!acquired)
        {
            throw new GameException(
                "PLAYER_BUSY",
                "Tài khoản đang xử lý một yêu cầu khác, vui lòng thử lại sau giây lát.",
                HttpStatusCode.Conflict);
        }

        return new LockReleaser(semaphore);
    }

    private sealed class LockReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            semaphore.Release();
        }
    }
}
