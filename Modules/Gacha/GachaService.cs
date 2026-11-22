using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using LearnBackend.Common.Events;
using LearnBackend.Common.Exceptions;
using LearnBackend.Hubs;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Gacha.Entities;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Gacha;

public sealed class GachaPullRequest
{
    [Required]
    public string BannerId { get; init; } = "banner_standard";

    /// <summary>
    /// Chỉ cho phép quay 1 lần hoặc 10 lần.
    /// </summary>
    public int Count { get; init; } = 1;
}

public sealed record BannerStatusDto(
    string Id,
    string Name,
    string Description,
    long CostGemsPerPull,
    int CurrentPity,
    int SoftPityStart,
    int HardPity,
    double CurrentFiveStarRate,
    int TotalPulls);

public sealed record GachaPullItemDto(
    string HeroTemplateId,
    string HeroName,
    string Element,
    int Rarity,
    bool WasPityTriggered,
    bool IsNewHero,
    string? DuplicateCompensation);

public sealed record GachaPullResponse(
    string BannerId,
    long GemsSpent,
    long RemainingGems,
    int NewPityCounter,
    int HardPity,
    List<GachaPullItemDto> Results);

public sealed class GachaService(
    GameDbContext db,
    MasterDataStore masterData,
    IGameEventBus eventBus,
    IGameRealtimeBroadcaster broadcaster)
{
    public async Task<List<BannerStatusDto>> GetBannersAsync(Guid playerId, CancellationToken ct)
    {
        var pities = await db.PlayerGachaPities
            .AsNoTracking()
            .Where(p => p.PlayerId == playerId)
            .ToDictionaryAsync(p => p.BannerId, ct);

        return masterData.Banners.Values.Select(b =>
        {
            var pity = pities.GetValueOrDefault(b.Id);
            var currentPity = pity?.PityCounter ?? 0;
            var nextRate = GachaRoller.CalculateFiveStarRate(b, currentPity + 1);
            return new BannerStatusDto(
                b.Id,
                b.Name,
                b.Description,
                b.CostGemsPerPull,
                currentPity,
                b.SoftPityStart,
                b.HardPity,
                nextRate,
                pity?.TotalPulls ?? 0);
        }).ToList();
    }

    public async Task<GachaPullResponse> PullAsync(Guid playerId, GachaPullRequest request, CancellationToken ct)
    {
        if (request.Count is not (1 or 10))
        {
            throw new GameException("INVALID_PULL_COUNT", "Chỉ hỗ trợ triệu hồi 1 lần hoặc 10 lần.");
        }

        if (!masterData.Banners.TryGetValue(request.BannerId, out var banner))
        {
            throw new GameException("BANNER_NOT_FOUND", $"Không tìm thấy Banner '{request.BannerId}'.", HttpStatusCode.NotFound);
        }

        var totalCost = banner.CostGemsPerPull * request.Count;
        var nowUtc = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        if (player.Gems < totalCost)
        {
            throw new GameException("INSUFFICIENT_GEMS", $"Cần {totalCost} Gems để quay {request.Count} lần (hiện có {player.Gems}).");
        }

        player.Gems -= totalCost;
        db.CurrencyLedgers.Add(new CurrencyLedger
        {
            PlayerId = playerId,
            CurrencyType = "Gems",
            Delta = -totalCost,
            BalanceAfter = player.Gems,
            Reason = "GachaPull",
            ReferenceId = $"{banner.Id}x{request.Count}",
            CreatedAtUtc = nowUtc
        });

        var pityState = await db.PlayerGachaPities
            .FirstOrDefaultAsync(p => p.PlayerId == playerId && p.BannerId == banner.Id, ct);

        if (pityState is null)
        {
            pityState = new PlayerGachaPity
            {
                PlayerId = playerId,
                BannerId = banner.Id,
                PityCounter = 0,
                TotalPulls = 0
            };
            db.PlayerGachaPities.Add(pityState);
        }

        var ownedHeroIds = (await db.PlayerHeroes
            .Where(h => h.PlayerId == playerId)
            .Select(h => h.HeroTemplateId)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rng = new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
        var results = new List<GachaPullItemDto>(request.Count);

        for (var i = 0; i < request.Count; i++)
        {
            var outcome = GachaRoller.RollSingle(banner, pityState.PityCounter, rng);
            pityState.PityCounter = outcome.NewPityCounter;
            pityState.TotalPulls += 1;

            var heroCfg = masterData.Heroes[outcome.HeroTemplateId];
            var isNew = ownedHeroIds.Add(outcome.HeroTemplateId);
            string? compensation = null;

            if (isNew)
            {
                db.PlayerHeroes.Add(new PlayerHero
                {
                    Id = Guid.NewGuid(),
                    PlayerId = playerId,
                    HeroTemplateId = outcome.HeroTemplateId,
                    Level = 1,
                    TeamSlot = null,
                    AcquiredAtUtc = nowUtc
                });
            }
            else
            {
                // Trùng tướng -> quy đổi thành Bình EXP & Đá Cường Hóa theo độ hiếm
                var expPotions = outcome.Rarity switch { 5 => 10, 4 => 4, _ => 2 };
                var stones = outcome.Rarity switch { 5 => 8, 4 => 3, _ => 1 };
                await AddMaterialAsync(playerId, "potion_exp_small", expPotions, ct);
                await AddMaterialAsync(playerId, "stone_enhance", stones, ct);
                compensation = $"+{expPotions} Bình EXP, +{stones} Đá Cường Hóa";
            }

            db.GachaHistories.Add(new GachaHistory
            {
                PlayerId = playerId,
                BannerId = banner.Id,
                HeroTemplateId = outcome.HeroTemplateId,
                Rarity = outcome.Rarity,
                WasPityTriggered = outcome.WasPityTriggered,
                WasDuplicate = !isNew,
                CreatedAtUtc = nowUtc
            });

            results.Add(new GachaPullItemDto(
                heroCfg.Id,
                heroCfg.Name,
                heroCfg.Element,
                heroCfg.Rarity,
                outcome.WasPityTriggered,
                isNew,
                compensation));
        }

        await db.SaveChangesAsync(ct);

        // Phát sự kiện nội bộ -> Module Quest tự lắng nghe cộng tiến độ (cùng Transaction!)
        await eventBus.PublishAsync(new GachaSummonedEvent(playerId, banner.Id, request.Count, nowUtc), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Phát loa toàn server qua SignalR cho mỗi tướng 5★ vừa nổ!
        foreach (var fiveStar in results.Where(r => r.Rarity == 5))
        {
            await broadcaster.BroadcastAnnouncementAsync(
                "GACHA_5STAR",
                $"🌟 {player.Nickname} vừa triệu hồi thành công 5★ {fiveStar.HeroName}!",
                ct);
        }

        return new GachaPullResponse(
            banner.Id,
            totalCost,
            player.Gems,
            pityState.PityCounter,
            banner.HardPity,
            results);
    }

    private async Task AddMaterialAsync(Guid playerId, string itemId, int qty, CancellationToken ct)
    {
        var item = await db.InventoryItems
            .FirstOrDefaultAsync(x => x.PlayerId == playerId && x.ItemId == itemId, ct);
        if (item is null)
        {
            db.InventoryItems.Add(new InventoryItem { PlayerId = playerId, ItemId = itemId, Quantity = qty });
        }
        else
        {
            item.Quantity += qty;
        }
    }
}
