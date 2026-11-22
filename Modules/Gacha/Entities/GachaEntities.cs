namespace LearnBackend.Modules.Gacha.Entities;

/// <summary>
/// Lưu trạng thái bảo hiểm (Pity) theo từng Player + Banner.
/// </summary>
public sealed class PlayerGachaPity
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public string BannerId { get; set; } = string.Empty;
    public int PityCounter { get; set; } // Số lần liên tiếp chưa ra 5★
    public int TotalPulls { get; set; }
}

/// <summary>
/// Lịch sử triệu hồi (Audit Log Gacha - minh bạch tỉ lệ & bảo hiểm).
/// </summary>
public sealed class GachaHistory
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public string BannerId { get; set; } = string.Empty;
    public string HeroTemplateId { get; set; } = string.Empty;
    public int Rarity { get; set; }
    public bool WasPityTriggered { get; set; }
    public bool WasDuplicate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
