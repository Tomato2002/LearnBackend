namespace LearnBackend.Modules.Arena.Entities;

public sealed class ArenaMatchHistory
{
    public long Id { get; set; }
    public Guid AttackerPlayerId { get; set; }
    public Guid? DefenderPlayerId { get; set; }
    public string DefenderName { get; set; } = string.Empty;
    public bool IsBot { get; set; }
    public bool AttackerWon { get; set; }
    public int RatingDelta { get; set; }
    public int RandomSeed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
