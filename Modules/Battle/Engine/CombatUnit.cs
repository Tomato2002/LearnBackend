namespace LearnBackend.Modules.Battle.Engine;

public sealed class CombatUnit
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty; // "Hero" | "Enemy"
    public int MaxHp { get; init; }
    public int Hp { get; set; }
    public int Atk { get; init; }
    public int Def { get; init; }
    public int Speed { get; init; }
    public List<string> SkillIds { get; init; } = [];
    public int TurnCounter { get; set; }

    public bool IsAlive => Hp > 0;
}
