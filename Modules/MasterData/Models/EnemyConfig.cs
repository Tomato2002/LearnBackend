namespace LearnBackend.Modules.MasterData.Models;

public sealed class EnemyConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Element { get; init; } = string.Empty;
    public int Hp { get; init; }
    public int Atk { get; init; }
    public int Def { get; init; }
    public int Speed { get; init; }
    public List<string> SkillIds { get; init; } = [];
}
