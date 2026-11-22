namespace LearnBackend.Modules.Player.Entities;

/// <summary>
/// Sổ cái kinh tế (Economy Audit Log): ghi lại MỌI biến động Gold/Gems.
/// Cực kỳ quan trọng trong game server chuyên nghiệp để truy vết hack, dupe hoặc hỗ trợ CSKH.
/// </summary>
public sealed class CurrencyLedger
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public string CurrencyType { get; set; } = string.Empty; // "Gold", "Gems"
    public long Delta { get; set; }                          // Dương = cộng, Âm = trừ
    public long BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;       // "StarterBonus", "StaminaRefill", ...
    public string? ReferenceId { get; set; }                 // Mã giao dịch / mã trận đánh nếu có
    public DateTime CreatedAtUtc { get; set; }
}
