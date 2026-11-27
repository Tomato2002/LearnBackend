using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.LiveOps.Entities;
using LearnBackend.Modules.Player.Entities;
using LearnBackend.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.LiveOps;

public sealed class LiveOpsService(GameDbContext db, LiveOpsFeatureStore featureStore)
{
    public const int HourlyCooldownSeconds = 3600; // 1 giờ
    public const long HourlyRewardGold = 250;
    public const long HourlyRewardGems = 25;

    public static readonly List<DailyBonusDayDto> DailySchedule =
    [
        new() { Day = 1, Gold = 300, Gems = 50, ItemId = "potion_exp_small", ItemQty = 2 },
        new() { Day = 2, Gold = 400, Gems = 50, ItemId = "stone_enhance", ItemQty = 2 },
        new() { Day = 3, Gold = 500, Gems = 100, ItemId = "potion_exp_small", ItemQty = 3 },
        new() { Day = 4, Gold = 600, Gems = 100, ItemId = "stone_enhance", ItemQty = 3 },
        new() { Day = 5, Gold = 800, Gems = 150, ItemId = "potion_exp_small", ItemQty = 5 },
        new() { Day = 6, Gold = 1000, Gems = 150, ItemId = "stone_enhance", ItemQty = 5 },
        new() { Day = 7, Gold = 2000, Gems = 500, ItemId = "stone_enhance", ItemQty = 10 }
    ];

    public LiveOpsBootstrapResponse GetBootstrap()
    {
        return new LiveOpsBootstrapResponse
        {
            Status = "OK",
            MasterDataVersion = "2026.11.v5",
            ServerTimeUtc = DateTime.UtcNow,
            FeatureFlags = featureStore.GetSnapshot(),
            Endpoints = new Dictionary<string, string>
            {
                ["authRegister"] = "/api/auth/register",
                ["authLogin"] = "/api/auth/login",
                ["authRefresh"] = "/api/auth/refresh",
                ["masterData"] = "/api/master-data",
                ["playerMe"] = "/api/player/me",
                ["heroes"] = "/api/heroes",
                ["inventory"] = "/api/inventory",
                ["battleRun"] = "/api/battle/run",
                ["gachaBanners"] = "/api/gacha/banners",
                ["gachaPull"] = "/api/gacha/pull",
                ["shop"] = "/api/shop",
                ["quests"] = "/api/quests",
                ["mail"] = "/api/mail",
                ["arenaLeaderboard"] = "/api/arena/leaderboard",
                ["guilds"] = "/api/guilds",
                ["dailyBonus"] = "/api/liveops/daily-bonus",
                ["hourlyBonus"] = "/api/liveops/hourly-bonus",
                ["realtimeHub"] = "/hubs/game"
            }
        };
    }

    public async Task<DailyBonusStatusResponse> GetDailyBonusStatusAsync(Guid playerId, CancellationToken ct)
    {
        var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var state = await db.PlayerDailyBonuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);

        var currentStreak = state?.CurrentStreakDay ?? 0;
        var canClaimToday = !string.Equals(state?.LastClaimedDateUtc, todayKey, StringComparison.Ordinal);
        var nextDay = (currentStreak >= 7) ? 1 : currentStreak + 1;

        return new DailyBonusStatusResponse
        {
            CurrentStreakDay = currentStreak,
            NextClaimDay = nextDay,
            CanClaimToday = canClaimToday,
            LastClaimedDateUtc = state?.LastClaimedDateUtc,
            Schedule = DailySchedule
        };
    }

    public async Task<LiveOpsBonusClaimResponse> ClaimDailyBonusAsync(Guid playerId, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var todayKey = nowUtc.ToString("yyyy-MM-dd");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var state = await db.PlayerDailyBonuses
            .FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);

        if (state is not null && string.Equals(state.LastClaimedDateUtc, todayKey, StringComparison.Ordinal))
        {
            throw new GameException(
                GameErrorCodes.DailyBonusAlreadyClaimed,
                "Bạn đã điểm danh nhận quà hôm nay rồi! Hãy quay lại vào ngày mai (00:00 UTC).");
        }

        var nextDay = (state is null || state.CurrentStreakDay >= 7) ? 1 : state.CurrentStreakDay + 1;
        var reward = DailySchedule.First(d => d.Day == nextDay);

        if (state is null)
        {
            state = new PlayerDailyBonus
            {
                PlayerId = playerId,
                CurrentStreakDay = nextDay,
                LastClaimedDateUtc = todayKey,
                UpdatedAtUtc = nowUtc
            };
            db.PlayerDailyBonuses.Add(state);
        }
        else
        {
            state.CurrentStreakDay = nextDay;
            state.LastClaimedDateUtc = todayKey;
            state.UpdatedAtUtc = nowUtc;
        }

        var player = await GrantRewardsAsync(
            playerId,
            reward.Gold,
            reward.Gems,
            reward.ItemId,
            reward.ItemQty,
            reason: "DailyBonus",
            referenceId: $"Day{nextDay}:{todayKey}",
            nowUtc,
            ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new LiveOpsBonusClaimResponse
        {
            BonusType = "DailyBonus",
            ClaimedDay = nextDay,
            GoldEarned = reward.Gold,
            GemsEarned = reward.Gems,
            ItemId = reward.ItemId,
            ItemQty = reward.ItemQty,
            BalanceGold = player.Gold,
            BalanceGems = player.Gems
        };
    }

    public async Task<HourlyBonusStatusResponse> GetHourlyBonusStatusAsync(Guid playerId, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var state = await db.PlayerHourlyBonuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);

        var remaining = CalculateHourlyCooldownRemaining(state?.LastClaimedAtUtc, nowUtc);

        return new HourlyBonusStatusResponse
        {
            CanClaimNow = remaining == 0,
            CooldownSecondsRemaining = remaining,
            CooldownIntervalSeconds = HourlyCooldownSeconds,
            RewardGold = HourlyRewardGold,
            RewardGems = HourlyRewardGems,
            LastClaimedAtUtc = state?.LastClaimedAtUtc
        };
    }

    public async Task<LiveOpsBonusClaimResponse> ClaimHourlyBonusAsync(Guid playerId, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var state = await db.PlayerHourlyBonuses
            .FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);

        var remaining = CalculateHourlyCooldownRemaining(state?.LastClaimedAtUtc, nowUtc);
        if (remaining > 0)
        {
            throw new GameException(
                GameErrorCodes.HourlyBonusCooldown,
                $"Rương Quà Theo Giờ đang hồi, vui lòng chờ thêm {remaining} giây nữa.");
        }

        if (state is null)
        {
            db.PlayerHourlyBonuses.Add(new PlayerHourlyBonus
            {
                PlayerId = playerId,
                LastClaimedAtUtc = nowUtc,
                TotalClaims = 1
            });
        }
        else
        {
            state.LastClaimedAtUtc = nowUtc;
            state.TotalClaims += 1;
        }

        var player = await GrantRewardsAsync(
            playerId,
            HourlyRewardGold,
            HourlyRewardGems,
            itemId: null,
            itemQty: 0,
            reason: "HourlyBonus",
            referenceId: nowUtc.ToString("yyyyMMddHH"),
            nowUtc,
            ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new LiveOpsBonusClaimResponse
        {
            BonusType = "HourlyBonus",
            GoldEarned = HourlyRewardGold,
            GemsEarned = HourlyRewardGems,
            BalanceGold = player.Gold,
            BalanceGems = player.Gems
        };
    }

    public static int CalculateHourlyCooldownRemaining(DateTime? lastClaimedAtUtc, DateTime nowUtc)
    {
        if (lastClaimedAtUtc is null) return 0;
        var elapsed = (nowUtc - lastClaimedAtUtc.Value).TotalSeconds;
        if (elapsed >= HourlyCooldownSeconds) return 0;
        return Math.Max(1, (int)Math.Ceiling(HourlyCooldownSeconds - elapsed));
    }

    private async Task<PlayerProfile> GrantRewardsAsync(
        Guid playerId,
        long gold,
        long gems,
        string? itemId,
        int itemQty,
        string reason,
        string referenceId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        if (gold > 0)
        {
            player.Gold += gold;
            db.CurrencyLedgers.Add(new CurrencyLedger
            {
                PlayerId = playerId,
                CurrencyType = "Gold",
                Delta = gold,
                BalanceAfter = player.Gold,
                Reason = reason,
                ReferenceId = referenceId,
                CreatedAtUtc = nowUtc
            });
        }

        if (gems > 0)
        {
            player.Gems += gems;
            db.CurrencyLedgers.Add(new CurrencyLedger
            {
                PlayerId = playerId,
                CurrencyType = "Gems",
                Delta = gems,
                BalanceAfter = player.Gems,
                Reason = reason,
                ReferenceId = referenceId,
                CreatedAtUtc = nowUtc
            });
        }

        if (!string.IsNullOrEmpty(itemId) && itemQty > 0)
        {
            var inv = await db.InventoryItems
                .FirstOrDefaultAsync(i => i.PlayerId == playerId && i.ItemId == itemId, ct);
            if (inv is null)
                db.InventoryItems.Add(new InventoryItem { PlayerId = playerId, ItemId = itemId, Quantity = itemQty });
            else
                inv.Quantity += itemQty;
        }

        return player;
    }
}
