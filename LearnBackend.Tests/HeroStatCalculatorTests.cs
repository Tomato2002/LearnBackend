using LearnBackend.Modules.Hero;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData.Models;

namespace LearnBackend.Tests;

public class HeroStatCalculatorTests
{
    [Fact]
    public void Calculate_AppliesLevelScalingAndEquippedGearBonuses()
    {
        var template = new HeroConfig
        {
            Id = "hero_arthur",
            Name = "Arthur",
            BaseHp = 1000,
            BaseAtk = 100,
            BaseDef = 100,
            BaseSpeed = 100
        };

        var hero = new PlayerHero
        {
            Id = Guid.NewGuid(),
            HeroTemplateId = "hero_arthur",
            Level = 3 // +2 extra levels -> +20% HP, +20% ATK, +16% DEF, +2 SPD
        };

        var gear = new List<EquipmentInstance>
        {
            new() { BonusHp = 150, BonusAtk = 40, BonusDef = 10 }
        };

        var stats = HeroStatCalculator.Calculate(template, hero, gear);

        Assert.Equal(1350, stats.MaxHp);
        Assert.Equal(160, stats.Atk);
        Assert.Equal(126, stats.Def);
        Assert.Equal(102, stats.Speed);
    }
}
