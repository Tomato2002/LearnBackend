using System.ComponentModel.DataAnnotations;
using System.Net;
using LearnBackend.Common.Events;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using LearnBackend.Modules.Shop.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Shop;

public sealed class BuyShopItemRequest
{
    [Required]
    public string ShopItemId { get; init; } = string.Empty;

    [Range(1, 20)]
    public int Quantity { get; init; } = 1;
}

public sealed record ShopItemStatusDto(
    string Id,
    string Name,
    string CurrencyType,
    long Price,
    string RewardItemId,
    int RewardQuantity,
    int DailyLimit,
    int PurchasedToday,
    int RemainingToday,
    string PeriodKeyUtc);

public sealed record BuyShopItemResponse(
    string ShopItemId,
    int QuantityBought,
    long TotalPaid,
    string CurrencyType,
    long RemainingBalance,
    int RemainingDailyLimit);

public sealed class ShopService(
    GameDbContext db,
    MasterDataStore masterData,
    IGameEventBus eventBus)
{
    public async Task<List<ShopItemStatusDto>> GetShopAsync(Guid playerId, CancellationToken ct)
    {
        var periodKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var purchases = await db.PlayerShopPurchases
            .AsNoTracking()
            .Where(p => p.PlayerId == playerId && p.PeriodKey == periodKey)
            .ToDictionaryAsync(p => p.ShopItemId, p => p.PurchasedCount, ct);

        return masterData.ShopItems.Values.Select(s =>
        {
            var bought = purchases.GetValueOrDefault(s.Id, 0);
            return new ShopItemStatusDto(
                s.Id,
                s.Name,
                s.CurrencyType,
                s.Price,
                s.RewardItemId,
                s.RewardQuantity,
                s.DailyLimit,
                bought,
                Math.Max(0, s.DailyLimit - bought),
                periodKey);
        }).ToList();
    }

    public async Task<BuyShopItemResponse> BuyAsync(Guid playerId, BuyShopItemRequest request, CancellationToken ct)
    {
        if (!masterData.ShopItems.TryGetValue(request.ShopItemId, out var shopItem))
        {
            throw new GameException("SHOP_ITEM_NOT_FOUND", $"Không tìm thấy món hàng '{request.ShopItemId}'.", HttpStatusCode.NotFound);
        }

        if (!masterData.Items.TryGetValue(shopItem.RewardItemId, out var rewardCfg))
        {
            throw new GameException("ITEM_CONFIG_MISSING", $"Thiếu cấu hình vật phẩm '{shopItem.RewardItemId}'.");
        }

        var nowUtc = DateTime.UtcNow;
        var periodKey = nowUtc.ToString("yyyy-MM-dd");
        var totalCost = shopItem.Price * request.Quantity;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var purchase = await db.PlayerShopPurchases
            .FirstOrDefaultAsync(p => p.PlayerId == playerId && p.ShopItemId == shopItem.Id && p.PeriodKey == periodKey, ct);

        var alreadyBought = purchase?.PurchasedCount ?? 0;
        if (alreadyBought + request.Quantity > shopItem.DailyLimit)
        {
            throw new GameException(
                "DAILY_LIMIT_EXCEEDED",
                $"Vượt quá giới hạn mua hôm nay! Còn lại {Math.Max(0, shopItem.DailyLimit - alreadyBought)} lượt.");
        }

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        long balanceAfter;
        if (string.Equals(shopItem.CurrencyType, "Gems", StringComparison.OrdinalIgnoreCase))
        {
            if (player.Gems < totalCost)
                throw new GameException("INSUFFICIENT_GEMS", $"Cần {totalCost} Gems (hiện có {player.Gems}).");
            player.Gems -= totalCost;
            balanceAfter = player.Gems;
        }
        else
        {
            if (player.Gold < totalCost)
                throw new GameException("INSUFFICIENT_GOLD", $"Cần {totalCost} Gold (hiện có {player.Gold}).");
            player.Gold -= totalCost;
            balanceAfter = player.Gold;
        }

        if (purchase is null)
        {
            purchase = new PlayerShopPurchase
            {
                PlayerId = playerId,
                ShopItemId = shopItem.Id,
                PeriodKey = periodKey,
                PurchasedCount = request.Quantity
            };
            db.PlayerShopPurchases.Add(purchase);
        }
        else
        {
            purchase.PurchasedCount += request.Quantity;
        }

        db.CurrencyLedgers.Add(new CurrencyLedger
        {
            PlayerId = playerId,
            CurrencyType = shopItem.CurrencyType,
            Delta = -totalCost,
            BalanceAfter = balanceAfter,
            Reason = "ShopPurchase",
            ReferenceId = $"{shopItem.Id}x{request.Quantity}",
            CreatedAtUtc = nowUtc
        });

        var totalGrantQty = shopItem.RewardQuantity * request.Quantity;
        if (string.Equals(rewardCfg.Category, "Equipment", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < totalGrantQty; i++)
            {
                db.Equipments.Add(new EquipmentInstance
                {
                    Id = Guid.NewGuid(),
                    PlayerId = playerId,
                    TemplateId = rewardCfg.Id,
                    EnhanceLevel = 0,
                    BonusAtk = rewardCfg.BaseAtkBonus,
                    BonusHp = rewardCfg.BaseHpBonus,
                    BonusDef = rewardCfg.BaseDefBonus,
                    CreatedAtUtc = nowUtc
                });
            }
        }
        else
        {
            var inv = await db.InventoryItems
                .FirstOrDefaultAsync(i => i.PlayerId == playerId && i.ItemId == rewardCfg.Id, ct);
            if (inv is null)
                db.InventoryItems.Add(new InventoryItem { PlayerId = playerId, ItemId = rewardCfg.Id, Quantity = totalGrantQty });
            else
                inv.Quantity += totalGrantQty;
        }

        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(new ShopPurchasedEvent(playerId, shopItem.Id, request.Quantity, nowUtc), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new BuyShopItemResponse(
            shopItem.Id,
            request.Quantity,
            totalCost,
            shopItem.CurrencyType,
            balanceAfter,
            Math.Max(0, shopItem.DailyLimit - purchase.PurchasedCount));
    }
}
