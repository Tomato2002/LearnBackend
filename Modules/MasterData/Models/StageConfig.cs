namespace LearnBackend.Modules.MasterData.Models;

public sealed class StageConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int StaminaCost { get; init; }
    public int RecommendedLevel { get; init; }
    public int ExpReward { get; init; }
    public long GoldReward { get; init; }
    public List<string> EnemyIds { get; init; } = [];
}
