namespace LearnBackend.Modules.Inventory.Entities;

/// <summary>
/// Trang bị duy nhất (Non-Stackable): mỗi món có Guid riêng, cấp cường hóa riêng,
/// dòng chỉ số phụ ngẫu nhiên và có thể gắn lên 1 PlayerHero.
/// </summary>
public sealed class EquipmentInstance
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public int EnhanceLevel { get; set; } = 0;

    // Chỉ số cộng thêm thực tế (đã gồm chỉ số gốc + bonus roll + cường hóa)
    public int BonusAtk { get; set; }
    public int BonusHp { get; set; }
    public int BonusDef { get; set; }

    public Guid? EquippedHeroId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
