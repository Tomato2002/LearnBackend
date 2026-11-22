namespace LearnBackend.Modules.Hero.Entities;

/// <summary>
/// Tướng thuộc sở hữu của người chơi (Instance riêng của từng Player).
/// Chỉ số gốc lấy từ MasterData (HeroConfig) + cộng thêm theo Level và Trang bị đang mặc.
/// </summary>
public sealed class PlayerHero
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string HeroTemplateId { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int? TeamSlot { get; set; } // 1..3 nếu đang trong đội hình ra trận, null nếu dự bị
    public DateTime AcquiredAtUtc { get; set; }
}
