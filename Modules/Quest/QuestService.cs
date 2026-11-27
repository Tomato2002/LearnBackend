using System.Net;
using LearnBackend.Common.Events;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using LearnBackend.Modules.Quest.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Quest;

public sealed record QuestStatusDto(
    string Id,
    string Title,
    string Description,
    int CurrentProgress,
    int TargetCount,
    bool IsCompleted,
    bool IsClaimed,
    long RewardGold,
    long RewardGems,
    string? RewardItemId,
    int RewardItemQty);

public sealed class QuestService(GameDbContext db, MasterDataStore masterData)
{
    public async Task<List<QuestStatusDto>> GetDailyQuestsAsync(Guid playerId, CancellationToken ct)
    {
        var periodKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var progressMap = await db.PlayerQuestProgresses
            .AsNoTracking()
            .Where(q => q.PlayerId == playerId && q.PeriodKey == periodKey)
            .ToDictionaryAsync(q => q.QuestId, ct);

        return masterData.Quests.Values.Select(cfg =>
        {
            var p = progressMap.GetValueOrDefault(cfg.Id);
            var current = Math.Min(cfg.TargetCount, p?.CurrentProgress ?? 0);
            return new QuestStatusDto(
                cfg.Id,
                cfg.Title,
                cfg.Description,
                current,
                cfg.TargetCount,
                IsCompleted: current >= cfg.TargetCount,
                IsClaimed: p?.IsClaimed ?? false,
                cfg.RewardGold,
                cfg.RewardGems,
                cfg.RewardItemId,
                cfg.RewardItemQty);
        }).ToList();
    }

    public async Task IncrementProgressAsync(Guid playerId, string eventType, int amount, DateTime occurredAtUtc, CancellationToken ct)
    {
        var periodKey = occurredAtUtc.ToString("yyyy-MM-dd");
        var matchingQuests = masterData.Quests.Values
            .Where(q => string.Equals(q.EventType, eventType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingQuests.Count == 0) return;

        foreach (var quest in matchingQuests)
        {
            var entry = await db.PlayerQuestProgresses
                .FirstOrDefaultAsync(x => x.PlayerId == playerId && x.QuestId == quest.Id && x.PeriodKey == periodKey, ct);

            if (entry is null)
            {
                db.PlayerQuestProgresses.Add(new PlayerQuestProgress
                {
                    PlayerId = playerId,
                    QuestId = quest.Id,
                    PeriodKey = periodKey,
                    CurrentProgress = Math.Min(quest.TargetCount, amount),
                    IsClaimed = false
                });
            }
            else if (!entry.IsClaimed)
            {
                entry.CurrentProgress = Math.Min(quest.TargetCount, entry.CurrentProgress + amount);
            }
        }
    }

    public async Task<QuestStatusDto> ClaimAsync(Guid playerId, string questId, CancellationToken ct)
    {
        if (!masterData.Quests.TryGetValue(questId, out var questCfg))
        {
            throw new GameException("QUEST_NOT_FOUND", $"Không tìm thấy nhiệm vụ '{questId}'.", HttpStatusCode.NotFound);
        }

        var nowUtc = DateTime.UtcNow;
        var periodKey = nowUtc.ToString("yyyy-MM-dd");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var entry = await db.PlayerQuestProgresses
            .FirstOrDefaultAsync(x => x.PlayerId == playerId && x.QuestId == questCfg.Id && x.PeriodKey == periodKey, ct);

        if (entry is null || entry.CurrentProgress < questCfg.TargetCount)
        {
            throw new GameException("QUEST_NOT_COMPLETED", "Bạn chưa hoàn thành nhiệm vụ này.");
        }

        if (entry.IsClaimed)
        {
            throw new GameException("QUEST_ALREADY_CLAIMED", "Bạn đã nhận thưởng nhiệm vụ này rồi.");
        }

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        entry.IsClaimed = true;

        if (questCfg.RewardGold > 0)
            db.CurrencyLedgers.Add(player.CreditGold(questCfg.RewardGold, "QuestReward", nowUtc, questCfg.Id));

        if (questCfg.RewardGems > 0)
            db.CurrencyLedgers.Add(player.CreditGems(questCfg.RewardGems, "QuestReward", nowUtc, questCfg.Id));

        if (!string.IsNullOrEmpty(questCfg.RewardItemId) && questCfg.RewardItemQty > 0)
        {
            var inv = await db.InventoryItems
                .FirstOrDefaultAsync(i => i.PlayerId == playerId && i.ItemId == questCfg.RewardItemId, ct);
            if (inv is null)
                db.InventoryItems.Add(new InventoryItem { PlayerId = playerId, ItemId = questCfg.RewardItemId, Quantity = questCfg.RewardItemQty });
            else
                inv.Quantity += questCfg.RewardItemQty;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new QuestStatusDto(
            questCfg.Id,
            questCfg.Title,
            questCfg.Description,
            entry.CurrentProgress,
            questCfg.TargetCount,
            true,
            true,
            questCfg.RewardGold,
            questCfg.RewardGems,
            questCfg.RewardItemId,
            questCfg.RewardItemQty);
    }
}

/// <summary>
/// Event Handlers lắng nghe sự kiện từ Battle, Gacha, Shop để tự động cộng tiến độ Quest.
/// Nhờ Event Bus, các module kia hoàn toàn không cần biết sự tồn tại của QuestService!
/// </summary>
public sealed class QuestEventHandlers(QuestService questService) :
    IGameEventHandler<StageClearedEvent>,
    IGameEventHandler<GachaSummonedEvent>,
    IGameEventHandler<ShopPurchasedEvent>
{
    public Task HandleAsync(StageClearedEvent gameEvent, CancellationToken ct)
        => questService.IncrementProgressAsync(gameEvent.PlayerId, "StageCleared", 1, gameEvent.OccurredAtUtc, ct);

    public Task HandleAsync(GachaSummonedEvent gameEvent, CancellationToken ct)
        => questService.IncrementProgressAsync(gameEvent.PlayerId, "GachaSummoned", gameEvent.PullCount, gameEvent.OccurredAtUtc, ct);

    public Task HandleAsync(ShopPurchasedEvent gameEvent, CancellationToken ct)
        => questService.IncrementProgressAsync(gameEvent.PlayerId, "ShopPurchased", gameEvent.Quantity, gameEvent.OccurredAtUtc, ct);
}
