using LearnBackend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LearnBackend.Modules.Arena;

/// <summary>
/// Trừu tượng hóa Bảng Xếp Hạng Arena:
/// - Khi chưa có Redis: đọc từ SQL (DbArenaLeaderboardStore).
/// - Khi bật Redis: dùng Sorted Set (ZSET) đạt độ phức tạp O(log N) cho hàng triệu người chơi!
/// </summary>
public interface IArenaLeaderboardStore
{
    Task UpdateScoreAsync(Guid playerId, int newRating, CancellationToken ct = default);
    Task<int> GetRankAsync(Guid playerId, int fallbackRating, CancellationToken ct = default);
    Task<List<(Guid PlayerId, int Rating)>> GetTopAsync(int top, CancellationToken ct = default);
}

public sealed class DbArenaLeaderboardStore(GameDbContext db) : IArenaLeaderboardStore
{
    public Task UpdateScoreAsync(Guid playerId, int newRating, CancellationToken ct = default)
        => Task.CompletedTask; // DB đã lưu trực tiếp trong PlayerProfile.ArenaRating

    public async Task<int> GetRankAsync(Guid playerId, int fallbackRating, CancellationToken ct = default)
    {
        var higherCount = await db.Players
            .AsNoTracking()
            .CountAsync(p => p.ArenaRating > fallbackRating, ct);
        return higherCount + 1;
    }

    public async Task<List<(Guid PlayerId, int Rating)>> GetTopAsync(int top, CancellationToken ct = default)
    {
        var rows = await db.Players
            .AsNoTracking()
            .OrderByDescending(p => p.ArenaRating)
            .ThenByDescending(p => p.ArenaWins)
            .Take(top)
            .Select(p => new { p.Id, p.ArenaRating })
            .ToListAsync(ct);

        return rows.Select(r => (r.Id, r.ArenaRating)).ToList();
    }
}

/// <summary>
/// Bảng Xếp Hạng Arena dùng Redis Sorted Set (ZSET):
/// - ZADD leaderboard:arena {rating} {playerId}
/// - ZREVRANK leaderboard:arena {playerId} -> lấy hạng chính xác trong < 1ms!
/// - ZREVRANGE leaderboard:arena 0 {top-1} WITHSCORES -> lấy Top 100 trong < 1ms!
/// </summary>
public sealed class RedisArenaLeaderboardStore(IConnectionMultiplexer redis, GameDbContext db) : IArenaLeaderboardStore
{
    private const string LeaderboardKey = "leaderboard:arena";

    public async Task UpdateScoreAsync(Guid playerId, int newRating, CancellationToken ct = default)
    {
        var rdb = redis.GetDatabase();
        await rdb.SortedSetAddAsync(LeaderboardKey, playerId.ToString("N"), newRating);
    }

    public async Task<int> GetRankAsync(Guid playerId, int fallbackRating, CancellationToken ct = default)
    {
        var rdb = redis.GetDatabase();
        var member = playerId.ToString("N");
        var rankZeroBased = await rdb.SortedSetRankAsync(LeaderboardKey, member, Order.Descending);

        if (!rankZeroBased.HasValue)
        {
            await rdb.SortedSetAddAsync(LeaderboardKey, member, fallbackRating);
            rankZeroBased = await rdb.SortedSetRankAsync(LeaderboardKey, member, Order.Descending);
        }

        return (int)(rankZeroBased ?? 0) + 1;
    }

    public async Task<List<(Guid PlayerId, int Rating)>> GetTopAsync(int top, CancellationToken ct = default)
    {
        var rdb = redis.GetDatabase();
        var entries = await rdb.SortedSetRangeByRankWithScoresAsync(LeaderboardKey, 0, top - 1, Order.Descending);

        if (entries.Length == 0)
        {
            // Warm-up Redis ZSET từ DB nếu Redis vừa khởi động trống
            var seedRows = await db.Players
                .AsNoTracking()
                .OrderByDescending(p => p.ArenaRating)
                .Take(Math.Max(top, 100))
                .Select(p => new { p.Id, p.ArenaRating })
                .ToListAsync(ct);

            if (seedRows.Count > 0)
            {
                var zsetEntries = seedRows
                    .Select(r => new SortedSetEntry(r.Id.ToString("N"), r.ArenaRating))
                    .ToArray();
                await rdb.SortedSetAddAsync(LeaderboardKey, zsetEntries);
                return seedRows.Take(top).Select(r => (r.Id, r.ArenaRating)).ToList();
            }
        }

        var result = new List<(Guid, int)>(entries.Length);
        foreach (var e in entries)
        {
            if (Guid.TryParseExact(e.Element.ToString(), "N", out var pid))
            {
                result.Add((pid, (int)e.Score));
            }
        }
        return result;
    }
}
