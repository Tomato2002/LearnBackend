namespace LearnBackend.Modules.LiveOps.Entities;

/// <summary>
/// Trạng thái chuỗi Điểm Danh 7 Ngày (Daily Bonus) của người chơi.
/// </summary>
public sealed class PlayerDailyBonus
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public int CurrentStreakDay { get; set; } // 0..7
    public string? LastClaimedDateUtc { get; set; } // "yyyy-MM-dd"
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Trạng thái Rương Quà Theo Giờ (Hourly Bonus) — tính hồi chiêu bằng Lazy UTC timestamp.
/// </summary>
public sealed class PlayerHourlyBonus
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public DateTime LastClaimedAtUtc { get; set; }
    public int TotalClaims { get; set; }
}
