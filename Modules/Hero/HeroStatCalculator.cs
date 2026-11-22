using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData.Models;
using LearnBackend.Modules.Hero.Entities;

namespace LearnBackend.Modules.Hero;

public sealed record ComputedHeroStats(int MaxHp, int Atk, int Def, int Speed);

/// <summary>
/// Công thức tính chỉ số cuối cùng của Hero = Chỉ số gốc (MasterData) + Tăng theo Level + Cộng dồn Trang bị đang mặc.
/// Tách thành hàm thuần để cả HeroService (hiển thị) và BattleService (đánh trận) dùng chung 1 nguồn sự thật.
/// </summary>
public static class HeroStatCalculator
{
    public static ComputedHeroStats Calculate(
        HeroConfig template,
        PlayerHero hero,
        IEnumerable<EquipmentInstance> equippedGear)
    {
        var extraLevels = Math.Max(0, hero.Level - 1);

        // Mỗi cấp tăng ~10% chỉ số gốc
        var maxHp = template.BaseHp + (int)(template.BaseHp * 0.10 * extraLevels);
        var atk = template.BaseAtk + (int)(template.BaseAtk * 0.10 * extraLevels);
        var def = template.BaseDef + (int)(template.BaseDef * 0.08 * extraLevels);
        var speed = template.BaseSpeed + extraLevels;

        foreach (var gear in equippedGear)
        {
            maxHp += gear.BonusHp;
            atk += gear.BonusAtk;
            def += gear.BonusDef;
        }

        return new ComputedHeroStats(maxHp, atk, def, speed);
    }

    public static (int PotionCost, long GoldCost) GetLevelUpCost(int currentLevel)
    {
        var potionCost = Math.Max(1, currentLevel);
        var goldCost = currentLevel * 100L;
        return (potionCost, goldCost);
    }
}
