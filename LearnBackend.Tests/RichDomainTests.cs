using LearnBackend.Common.Exceptions;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Gacha;
using LearnBackend.Modules.Gacha.Entities;
using LearnBackend.Modules.MasterData.Models;
using LearnBackend.Modules.Player.Entities;
using LearnBackend.Shared.Contracts;

namespace LearnBackend.Tests;

public class RichDomainTests
{
    private static readonly GameRulesConfig Rules = new()
    {
        StarterGold = 1000,
        StarterGems = 300,
        BaseMaxStamina = 100,
        StaminaPerLevel = 5,
        StaminaRegenIntervalSeconds = 300,
        StaminaRefillGemCost = 50,
        StaminaRefillAmount = 60,
        BaseExpPerLevel = 100
    };

    [Fact]
    public void PlayerProfile_SpendGoldAndGems_EnforcesNonNegativeInvariantsAndProducesLedgers()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var (player, starterLedgers) = PlayerProfile.CreateNew(Guid.NewGuid(), "RichHero", Rules, now);

        Assert.Equal(1000, player.Gold);
        Assert.Equal(300, player.Gems);
        Assert.Equal(2, starterLedgers.Count);

        var spendLedger = player.SpendGold(400, "ShopPurchase", now, "shop_exp_pack");
        Assert.Equal(600, player.Gold);
        Assert.Equal(-400, spendLedger.Delta);
        Assert.Equal(600, spendLedger.BalanceAfter);

        var ex = Assert.Throws<GameException>(() => player.SpendGold(700, "TooExpensive", now));
        Assert.Equal(GameErrorCodes.InsufficientGold, ex.ErrorCode);
        Assert.Equal(600, player.Gold); // Số dư giữ nguyên không bao giờ âm!
    }

    [Fact]
    public void PlayerProfile_AddExpAndLevelUp_HandlesMultiLevelAndRefillsStamina()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var (player, _) = PlayerProfile.CreateNew(Guid.NewGuid(), "LevelHero", Rules, now);

        player.SpendStamina(40, Rules, now);
        Assert.Equal(60, player.StoredStamina);

        // Lv1 cần 100 EXP, Lv2 cần 200 EXP -> 350 EXP sẽ lên Lv3 dư 50 EXP, MaxStamina mới = 110
        var gained = player.AddExpAndLevelUp(350, Rules, now);

        Assert.Equal(2, gained);
        Assert.Equal(3, player.Level);
        Assert.Equal(50, player.Exp);
        Assert.Equal(110, player.StoredStamina);
    }

    [Fact]
    public void CombatUnit_TakeDamageAndHeal_AlwaysStayWithinBounds()
    {
        var unit = new CombatUnit("u1", "Arthur", "Hero", maxHp: 500, initialHp: 500, atk: 100, def: 100, speed: 100, ["skill_slash"]);

        var dmg = unit.TakeDamage(200); // DEF 100 -> 50% giảm sát thương -> nhận 100 DMG
        Assert.Equal(100, dmg);
        Assert.Equal(400, unit.Hp);

        var healed = unit.Heal(9999);
        Assert.Equal(100, healed);
        Assert.Equal(500, unit.Hp);

        unit.TakeDamage(99999);
        Assert.Equal(0, unit.Hp);
        Assert.False(unit.IsAlive);
        Assert.Equal(0, unit.Heal(100)); // Đã chết thì không thể hồi máu thường
    }

    [Fact]
    public void PlayerGachaPity_ApplyRollOutcome_IncrementsAndResetsProperly()
    {
        var pity = PlayerGachaPity.Create(Guid.NewGuid(), "banner_standard");

        pity.ApplyRollOutcome(new GachaRollOutcome("hero_kael", 3, false, 1));
        pity.ApplyRollOutcome(new GachaRollOutcome("hero_arthur", 4, false, 2));
        Assert.Equal(2, pity.PityCounter);
        Assert.Equal(2, pity.TotalPulls);

        pity.ApplyRollOutcome(new GachaRollOutcome("hero_celestia", 5, true, 0));
        Assert.Equal(0, pity.PityCounter);
        Assert.Equal(3, pity.TotalPulls);
    }
}
