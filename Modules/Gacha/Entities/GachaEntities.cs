namespace LearnBackend.Modules.Gacha.Entities;

/// <summary>
/// Rich Domain Entity quản lý trạng thái Bảo Hiểm (Pity) theo từng Player + Banner.
/// Khóa PityCounter & TotalPulls bằng private set -> chỉ được cập nhật qua ApplyRollOutcome().
/// </summary>
public sealed class PlayerGachaPity
{
    public long Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string BannerId { get; private set; } = string.Empty;
    public int PityCounter { get; private set; }
    public int TotalPulls { get; private set; }

    private PlayerGachaPity() { }

    public static PlayerGachaPity Create(Guid playerId, string bannerId) => new()
    {
        PlayerId = playerId,
        BannerId = bannerId,
        PityCounter = 0,
        TotalPulls = 0
    };

    public void ApplyRollOutcome(GachaRollOutcome outcome)
    {
        PityCounter = outcome.NewPityCounter;
        TotalPulls += 1;
    }
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
