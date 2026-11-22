using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Inventory.DTOs;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Inventory;

public sealed class InventoryService(GameDbContext db, MasterDataStore masterData)
{
    public async Task<InventoryResponse> GetInventoryAsync(Guid playerId, CancellationToken ct)
    {
        var rawItems = await db.InventoryItems
            .AsNoTracking()
            .Where(i => i.PlayerId == playerId && i.Quantity > 0)
            .ToListAsync(ct);

        var materials = rawItems.Select(i =>
        {
            var cfg = masterData.Items.GetValueOrDefault(i.ItemId);
            return new InventoryMaterialDto(
                ItemId: i.ItemId,
                Name: cfg?.Name ?? i.ItemId,
                Rarity: cfg?.Rarity ?? 1,
                Description: cfg?.Description ?? string.Empty,
                Quantity: i.Quantity);
        }).ToList();

        var rawEquipments = await db.Equipments
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId)
            .OrderByDescending(e => e.EquippedHeroId != null)
            .ThenByDescending(e => e.EnhanceLevel)
            .ThenByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);

        var equipments = rawEquipments.Select(ToEquipmentDto).ToList();

        return new InventoryResponse(materials, equipments);
    }

    public async Task<EquipmentDto> EquipAsync(Guid playerId, Guid equipmentId, Guid? heroId, CancellationToken ct)
    {
        var equipment = await db.Equipments
            .FirstOrDefaultAsync(e => e.Id == equipmentId && e.PlayerId == playerId, ct)
            ?? throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị này.", HttpStatusCode.NotFound);

        if (heroId is null)
        {
            equipment.EquippedHeroId = null;
            await db.SaveChangesAsync(ct);
            return ToEquipmentDto(equipment);
        }

        var heroExists = await db.PlayerHeroes
            .AnyAsync(h => h.Id == heroId.Value && h.PlayerId == playerId, ct);
        if (!heroExists)
        {
            throw new GameException("HERO_NOT_FOUND", "Không tìm thấy Hero để trang bị.", HttpStatusCode.NotFound);
        }

        var targetSlot = masterData.Items.GetValueOrDefault(equipment.TemplateId)?.Slot;

        // Nếu Hero đã mặc 1 món cùng Slot (VD: đã có Weapon) thì tự tháo món cũ ra trước
        if (!string.IsNullOrEmpty(targetSlot))
        {
            var currentlyEquipped = await db.Equipments
                .Where(e => e.PlayerId == playerId && e.EquippedHeroId == heroId.Value && e.Id != equipment.Id)
                .ToListAsync(ct);

            foreach (var old in currentlyEquipped)
            {
                var oldSlot = masterData.Items.GetValueOrDefault(old.TemplateId)?.Slot;
                if (string.Equals(oldSlot, targetSlot, StringComparison.OrdinalIgnoreCase))
                {
                    old.EquippedHeroId = null;
                }
            }
        }

        equipment.EquippedHeroId = heroId.Value;
        await db.SaveChangesAsync(ct);

        return ToEquipmentDto(equipment);
    }

    public async Task<EquipmentDto> EnhanceAsync(Guid playerId, Guid equipmentId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var equipment = await db.Equipments
            .FirstOrDefaultAsync(e => e.Id == equipmentId && e.PlayerId == playerId, ct)
            ?? throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị này.", HttpStatusCode.NotFound);

        var player = await db.Players
            .FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        var (stoneCost, goldCost) = GetEnhanceCost(equipment.EnhanceLevel);

        if (player.Gold < goldCost)
        {
            throw new GameException("INSUFFICIENT_GOLD", $"Cần {goldCost} Gold để cường hóa (hiện có {player.Gold}).");
        }

        var stoneItem = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.PlayerId == playerId && i.ItemId == "stone_enhance", ct);

        if (stoneItem is null || stoneItem.Quantity < stoneCost)
        {
            var have = stoneItem?.Quantity ?? 0;
            throw new GameException(
                "INSUFFICIENT_MATERIAL",
                $"Cần {stoneCost} Đá Cường Hóa để nâng cấp (hiện có {have}).");
        }

        player.Gold -= goldCost;
        stoneItem.Quantity -= stoneCost;
        equipment.EnhanceLevel += 1;

        // Tăng chỉ số trang bị theo cấp cường hóa
        var cfg = masterData.Items.GetValueOrDefault(equipment.TemplateId);
        equipment.BonusAtk += Math.Max(2, (int)Math.Ceiling((cfg?.BaseAtkBonus ?? 10) * 0.20));
        equipment.BonusHp += Math.Max(10, (int)Math.Ceiling((cfg?.BaseHpBonus ?? 50) * 0.20));
        equipment.BonusDef += Math.Max(1, (int)Math.Ceiling((cfg?.BaseDefBonus ?? 5) * 0.20));

        db.CurrencyLedgers.Add(new CurrencyLedger
        {
            PlayerId = playerId,
            CurrencyType = "Gold",
            Delta = -goldCost,
            BalanceAfter = player.Gold,
            Reason = "EquipmentEnhance",
            ReferenceId = $"{equipment.TemplateId}:+{equipment.EnhanceLevel}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToEquipmentDto(equipment);
    }

    public static (int StoneCost, long GoldCost) GetEnhanceCost(int currentEnhanceLevel)
    {
        var stoneCost = currentEnhanceLevel + 1;
        var goldCost = (currentEnhanceLevel + 1) * 150L;
        return (stoneCost, goldCost);
    }

    private EquipmentDto ToEquipmentDto(EquipmentInstance e)
    {
        var cfg = masterData.Items.GetValueOrDefault(e.TemplateId);
        var (stoneCost, goldCost) = GetEnhanceCost(e.EnhanceLevel);

        return new EquipmentDto(
            EquipmentId: e.Id,
            TemplateId: e.TemplateId,
            Name: cfg?.Name ?? e.TemplateId,
            Slot: cfg?.Slot ?? "Unknown",
            Rarity: cfg?.Rarity ?? 1,
            EnhanceLevel: e.EnhanceLevel,
            BonusAtk: e.BonusAtk,
            BonusHp: e.BonusHp,
            BonusDef: e.BonusDef,
            EquippedHeroId: e.EquippedHeroId,
            NextEnhanceStoneCost: stoneCost,
            NextEnhanceGoldCost: goldCost);
    }
}
