namespace LearnBackend.Modules.MasterData.Models;

public sealed class ItemConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty; // "Material" | "Equipment"
    public string? Slot { get; init; }                    // "Weapon" | "Armor" | "Accessory"
    public int Rarity { get; init; }
    public string Description { get; init; } = string.Empty;
    public int BaseAtkBonus { get; init; }
    public int BaseHpBonus { get; init; }
    public int BaseDefBonus { get; init; }
}
