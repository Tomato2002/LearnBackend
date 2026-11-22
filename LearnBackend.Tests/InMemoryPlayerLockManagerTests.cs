using LearnBackend.Common.Locking;

namespace LearnBackend.Tests;

public class InMemoryPlayerLockManagerTests
{
    [Fact]
    public async Task AcquireAsync_SamePlayer_ExecutesSequentiallyAndCleansUpMemory()
    {
        var manager = new InMemoryPlayerLockManager();
        var playerId = Guid.NewGuid();
        var activeCount = 0;
        var maxConcurrent = 0;

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var _lock = await manager.AcquireAsync(playerId);
            var current = Interlocked.Increment(ref activeCount);
            maxConcurrent = Math.Max(maxConcurrent, current);
            await Task.Delay(15);
            Interlocked.Decrement(ref activeCount);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(1, maxConcurrent);
        // Kiểm chứng đã dọn sạch khỏi Dictionary -> 0 memory leak!
        Assert.Equal(0, manager.ActiveLockCount);
    }

    [Fact]
    public async Task AcquireAsync_DifferentPlayers_RunInParallel()
    {
        var manager = new InMemoryPlayerLockManager();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        using var lockA = await manager.AcquireAsync(playerA);
        using var lockB = await manager.AcquireAsync(playerB);

        Assert.Equal(2, manager.ActiveLockCount);
    }
}
