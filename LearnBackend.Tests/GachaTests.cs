using LearnBackend.Modules.Gacha;
using LearnBackend.Modules.MasterData.Models;

namespace LearnBackend.Tests;

public class GachaTests
{
    private static BannerConfig CreateBanner() => new()
    {
        Id = "banner_standard",
        BaseFiveStarRate = 0.05,
        BaseFourStarRate = 0.25,
        SoftPityStart = 7,
        SoftPityRateStep = 0.15,
        HardPity = 10,
        FiveStarHeroIds = ["hero_celestia", "hero_ignis"],
        FourStarHeroIds = ["hero_arthur", "hero_lyra"],
        ThreeStarHeroIds = ["hero_kael"]
    };

    [Fact]
    public void CalculateFiveStarRate_AppliesSoftPityAndHardPityCorrectly()
    {
        var banner = CreateBanner();

        Assert.Equal(0.05, GachaRoller.CalculateFiveStarRate(banner, 1), precision: 4);
        Assert.Equal(0.05, GachaRoller.CalculateFiveStarRate(banner, 6), precision: 4);
        Assert.Equal(0.20, GachaRoller.CalculateFiveStarRate(banner, 7), precision: 4);  // Soft Pity step 1
        Assert.Equal(0.35, GachaRoller.CalculateFiveStarRate(banner, 8), precision: 4);  // Soft Pity step 2
        Assert.Equal(0.50, GachaRoller.CalculateFiveStarRate(banner, 9), precision: 4);  // Soft Pity step 3
        Assert.Equal(1.00, GachaRoller.CalculateFiveStarRate(banner, 10), precision: 4); // Hard Pity 100%
    }

    [Fact]
    public void RollSingle_AtHardPity_AlwaysGuaranteesFiveStarAndResetsPity()
    {
        var banner = CreateBanner();
        var rng = new Random(42);

        // currentPityCounter = 9 -> next pull is #10 (Hard Pity!)
        var outcome = GachaRoller.RollSingle(banner, currentPityCounter: 9, rng);

        Assert.Equal(5, outcome.Rarity);
        Assert.True(outcome.WasPityTriggered);
        Assert.Equal(0, outcome.NewPityCounter);
        Assert.Contains(outcome.HeroTemplateId, banner.FiveStarHeroIds);
    }
}
