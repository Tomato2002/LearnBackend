using System.Net;
using LearnBackend.Common.Exceptions;

namespace LearnBackend.Common.Locking;

/// <summary>
/// Cài đặt khóa theo PlayerId trong RAM với cơ chế Reference Counting để KHÔNG BỊ MEMORY LEAK:
/// - Khi người chơi có request đang chạy -> tăng RefCount và giữ SemaphoreSlim.
/// - Khi request cuối cùng của người chơi đó xong -> tự động xóa khỏi Dictionary và Dispose SemaphoreSlim.
/// - Người chơi A và B vẫn chạy song song hoàn toàn độc lập.
/// </summary>
public sealed class InMemoryPlayerLockManager : IPlayerLockManager
{
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(5);

    private readonly object _registryGate = new();
    private readonly Dictionary<Guid, RefCountedLock> _locks = new();

    /// <summary>
    /// Dùng cho giám sát / Unit Test để kiểm chứng không rò rỉ bộ nhớ sau khi giải phóng khóa.
    /// </summary>
    public int ActiveLockCount
    {
        get
        {
            lock (_registryGate)
            {
                return _locks.Count;
            }
        }
    }

    public async Task<IDisposable> AcquireAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        RefCountedLock entry;
        lock (_registryGate)
        {
            if (!_locks.TryGetValue(playerId, out entry!))
            {
                entry = new RefCountedLock();
                _locks[playerId] = entry;
            }

            entry.RefCount++;
        }

        var acquired = false;
        try
        {
            acquired = await entry.Semaphore.WaitAsync(AcquireTimeout, cancellationToken);
        }
        catch
        {
            DecrementAndCleanup(playerId, entry);
            throw;
        }

        if (!acquired)
        {
            DecrementAndCleanup(playerId, entry);
            throw new GameException(
                "PLAYER_BUSY",
                "Tài khoản đang xử lý một yêu cầu khác, vui lòng thử lại sau giây lát.",
                HttpStatusCode.Conflict);
        }

        return new LockReleaser(this, playerId, entry);
    }

    private void Release(Guid playerId, RefCountedLock entry)
    {
        entry.Semaphore.Release();
        DecrementAndCleanup(playerId, entry);
    }

    private void DecrementAndCleanup(Guid playerId, RefCountedLock entry)
    {
        var shouldDispose = false;
        lock (_registryGate)
        {
            entry.RefCount--;
            if (entry.RefCount == 0 && _locks.TryGetValue(playerId, out var current) && ReferenceEquals(current, entry))
            {
                _locks.Remove(playerId);
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class RefCountedLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount { get; set; }
    }

    private sealed class LockReleaser(
        InMemoryPlayerLockManager owner,
        Guid playerId,
        RefCountedLock entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(playerId, entry);
            }
        }
    }
}
