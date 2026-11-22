namespace LearnBackend.Modules.Mail.Entities;

/// <summary>
/// Global Mail Pattern: Khi GM gửi quà đền bù cho 100.000 người chơi,
/// CHỈ INSERT ĐÚNG 1 DÒNG vào bảng này thay vì 100.000 dòng!
/// </summary>
public sealed class GlobalMail
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public long RewardGold { get; set; }
    public long RewardGems { get; set; }
    public string? RewardItemId { get; set; }
    public int RewardItemQty { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
/// Ghi nhận người chơi nào đã nhận GlobalMail nào (Unique Index chống nhận 2 lần).
/// </summary>
public sealed class PlayerGlobalMailClaim
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid GlobalMailId { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
}
