namespace LearnBackend.Modules.Quest.Entities;

/// <summary>
/// Tiến độ nhiệm vụ hằng ngày của người chơi (dùng PeriodKey "yyyy-MM-dd" UTC để tự reset mỗi ngày).
/// </summary>
public sealed class PlayerQuestProgress
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public string QuestId { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public int CurrentProgress { get; set; }
    public bool IsClaimed { get; set; }
}
