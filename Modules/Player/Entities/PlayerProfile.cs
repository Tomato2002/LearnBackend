namespace LearnBackend.Modules.Player.Entities;

/// <summary>
/// Hồ sơ người chơi: cấp độ, ví tiền tệ và trạng thái thể lực (Stamina).
/// </summary>
public sealed class PlayerProfile
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string Nickname { get; set; } = string.Empty;

    public int Level { get; set; } = 1;
    public int Exp { get; set; } = 0;

    // Ví tiền tệ (dùng long để tránh tràn số khi game chạy lâu dài)
    public long Gold { get; set; }
    public long Gems { get; set; }

    // Trạng thái Stamina cho cơ chế Lazy Calculation (không cần timer chạy nền)
    public int StoredStamina { get; set; }
    public DateTime LastStaminaUpdatedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
