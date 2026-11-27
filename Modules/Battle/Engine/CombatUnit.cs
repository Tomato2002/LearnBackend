namespace LearnBackend.Modules.Battle.Engine;

/// <summary>
/// Rich Domain Entity đại diện cho 1 đơn vị chiến đấu (Hero hoặc Quái/Boss).
/// Khóa Hp & TurnCounter bằng private set để đảm bảo HP luôn nằm trong [0, MaxHp].
/// </summary>
public sealed class CombatUnit
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty; // "Hero" | "Enemy"
    public int MaxHp { get; init; }
    public int Hp { get; private set; }
    public int Atk { get; init; }
    public int Def { get; init; }
    public int Speed { get; init; }
    public IReadOnlyList<string> SkillIds { get; init; } = [];
    public int TurnCounter { get; private set; }

    public bool IsAlive => Hp > 0;

    public CombatUnit() { }

    public CombatUnit(
        string id,
        string name,
        string side,
        int maxHp,
        int initialHp,
        int atk,
        int def,
        int speed,
        IReadOnlyList<string> skillIds)
    {
        Id = id;
        Name = name;
        Side = side;
        MaxHp = Math.Max(1, maxHp);
        Hp = Math.Clamp(initialHp, 0, MaxHp);
        Atk = Math.Max(0, atk);
        Def = Math.Max(0, def);
        Speed = Math.Max(1, speed);
        SkillIds = skillIds;
    }

    public int TakeDamage(int rawPower)
    {
        var mitigation = 100.0 / (100.0 + Math.Max(0, Def));
        var finalDamage = Math.Max(1, (int)Math.Round(Math.Max(1, rawPower) * mitigation));
        Hp = Math.Max(0, Hp - finalDamage);
        return finalDamage;
    }

    public int Heal(int amount)
    {
        if (!IsAlive) return 0;
        var safeAmount = Math.Max(1, amount);
        var before = Hp;
        Hp = Math.Min(MaxHp, Hp + safeAmount);
        return Hp - before;
    }

    public string AdvanceTurnAndSelectSkill()
    {
        TurnCounter++;
        if (SkillIds.Count == 0) return "basic_attack";
        if (SkillIds.Count == 1) return SkillIds[0];
        return (TurnCounter % 2 == 0) ? SkillIds[1] : SkillIds[0];
    }
}
