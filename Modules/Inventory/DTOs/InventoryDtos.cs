namespace LearnBackend.Modules.Inventory.DTOs;

public sealed record InventoryMaterialDto(
    string ItemId,
    string Name,
    int Rarity,
    string Description,
    int Quantity);

public sealed record EquipmentDto(
    Guid EquipmentId,
    string TemplateId,
    string Name,
    string Slot,
    int Rarity,
    int EnhanceLevel,
    int BonusAtk,
    int BonusHp,
    int BonusDef,
    Guid? EquippedHeroId,
    int NextEnhanceStoneCost,
    long NextEnhanceGoldCost);

public sealed record InventoryResponse(
    List<InventoryMaterialDto> Materials,
    List<EquipmentDto> Equipments);

public sealed class EquipRequest
{
    /// <summary>
    /// Truyền HeroId để trang bị, hoặc null để tháo trang bị.
    /// </summary>
    public Guid? HeroId { get; init; }
}
