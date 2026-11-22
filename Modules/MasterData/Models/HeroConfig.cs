namespace LearnBackend.Modules.MasterData.Models;

public sealed class HeroConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Element { get; init; } = string.Empty;
    public int Rarity { get; init; }
    public int BaseHp { get; init; }
    public int BaseAtk { get; init; }
    public int BaseDef { get; init; }
    public int BaseSpeed { get; init; }
    public List<string> SkillIds { get; init; } = [];
}
