using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Auth.Entities;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData.Models;
using LearnBackend.Modules.Player.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Tests;

public class DatabaseConstraintTests
{
    private static (SqliteConnection Conn, DbContextOptions<GameDbContext> Options) CreateInMemorySqlite()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlite(conn)
            .Options;

        using var db = new GameDbContext(options);
        db.Database.EnsureCreated();

        return (conn, options);
    }

    [Fact]
    public async Task SqlCheckConstraint_BlocksNegativeGoldEvenViaRawSql()
    {
        var (conn, options) = CreateInMemorySqlite();
        await using var _ = conn;

        var now = DateTime.UtcNow;
        var accountId = Guid.NewGuid();
        var (player, _) = PlayerProfile.CreateNew(accountId, "SafeWallet", new GameRulesConfig(), now);

        await using (var db = new GameDbContext(options))
        {
            db.UserAccounts.Add(new UserAccount
            {
                Id = accountId,
                Username = "safe01",
                NormalizedUsername = "SAFE01",
                PasswordHash = "hash",
                CreatedAtUtc = now
            });
            db.Players.Add(player);
            await db.SaveChangesAsync();
        }

        // Cố tình chạy SQL thô sửa Gold thành -100 -> DB phải chặn bằng CHECK constraint!
        await using (var db = new GameDbContext(options))
        {
            await Assert.ThrowsAsync<SqliteException>(async () =>
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Players SET Gold = -100 WHERE Id = {player.Id}"));
        }
    }

    [Fact]
    public async Task SqlForeignKey_BlocksOrphanHeroInsert()
    {
        var (conn, options) = CreateInMemorySqlite();
        await using var _ = conn;

        await using var db = new GameDbContext(options);
        db.PlayerHeroes.Add(new PlayerHero
        {
            Id = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(), // PlayerId không tồn tại!
            HeroTemplateId = "hero_arthur",
            Level = 1,
            AcquiredAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task OptimisticConcurrency_WalletVersionBlocksConcurrentOverwrite()
    {
        var (conn, options) = CreateInMemorySqlite();
        await using var _ = conn;

        var now = DateTime.UtcNow;
        var accountId = Guid.NewGuid();
        var (player, _) = PlayerProfile.CreateNew(accountId, "RaceTest", new GameRulesConfig { StarterGold = 1000 }, now);

        await using (var seedDb = new GameDbContext(options))
        {
            seedDb.UserAccounts.Add(new UserAccount
            {
                Id = accountId,
                Username = "race01",
                NormalizedUsername = "RACE01",
                PasswordHash = "hash",
                CreatedAtUtc = now
            });
            seedDb.Players.Add(player);
            await seedDb.SaveChangesAsync();
        }

        // Giả lập 2 server cùng đọc 1 người chơi (WalletVersion = 3) cùng 1 thời điểm
        await using var dbServerA = new GameDbContext(options);
        await using var dbServerB = new GameDbContext(options);

        var pA = await dbServerA.Players.FirstAsync(p => p.Id == player.Id);
        var pB = await dbServerB.Players.FirstAsync(p => p.Id == player.Id);

        // Server A trừ 800 Gold và lưu thành công -> WalletVersion tăng lên 4
        pA.SpendGold(800, "BuyA", now);
        await dbServerA.SaveChangesAsync();

        // Server B vẫn giữ ảnh cũ (Gold = 1000, WalletVersion = 3) cố trừ 800 Gold -> DB PHẢI CHẶN!
        pB.SpendGold(800, "BuyB", now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbServerB.SaveChangesAsync());
    }

    [Fact]
    public async Task SqlCheckConstraint_BlocksNegativeInventoryQuantity()
    {
        var (conn, options) = CreateInMemorySqlite();
        await using var _ = conn;

        var now = DateTime.UtcNow;
        var accountId = Guid.NewGuid();
        var (player, _) = PlayerProfile.CreateNew(accountId, "InvTest", new GameRulesConfig(), now);

        await using var db = new GameDbContext(options);
        db.UserAccounts.Add(new UserAccount
        {
            Id = accountId,
            Username = "inv01",
            NormalizedUsername = "INV01",
            PasswordHash = "hash",
            CreatedAtUtc = now
        });
        db.Players.Add(player);
        await db.SaveChangesAsync();

        db.InventoryItems.Add(new InventoryItem
        {
            PlayerId = player.Id,
            ItemId = "stone_enhance",
            Quantity = -5
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
