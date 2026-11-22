using LearnBackend.Modules.MasterData.Models;

namespace LearnBackend.Modules.Gacha;

public sealed record GachaRollOutcome(
    string HeroTemplateId,
    int Rarity,
    bool WasPityTriggered,
    int NewPityCounter);

/// <summary>
/// Thuật toán Weighted Random + Soft Pity + Hard Pity thuần túy (dễ viết Unit Test).
/// </summary>
public static class GachaRoller
{
    public static double CalculateFiveStarRate(BannerConfig banner, int nextPullNumber)
    {
        if (nextPullNumber >= banner.HardPity)
        {
            return 1.0;
        }

        if (nextPullNumber >= banner.SoftPityStart)
        {
            var steps = nextPullNumber - banner.SoftPityStart + 1;
            return Math.Min(1.0, banner.BaseFiveStarRate + steps * banner.SoftPityRateStep);
        }

        return banner.BaseFiveStarRate;
    }

    public static GachaRollOutcome RollSingle(BannerConfig banner, int currentPityCounter, Random rng)
    {
        var nextPullNumber = currentPityCounter + 1;
        var fiveStarRate = CalculateFiveStarRate(banner, nextPullNumber);
        var isHardPity = nextPullNumber >= banner.HardPity;

        var roll = rng.NextDouble();

        if (isHardPity || roll < fiveStarRate)
        {
            var heroId = PickRandom(banner.FiveStarHeroIds, rng);
            return new GachaRollOutcome(heroId, Rarity: 5, WasPityTriggered: isHardPity, NewPityCounter: 0);
        }

        if (roll < fiveStarRate + banner.BaseFourStarRate)
        {
            var heroId = PickRandom(banner.FourStarHeroIds, rng);
            return new GachaRollOutcome(heroId, Rarity: 4, WasPityTriggered: false, NewPityCounter: nextPullNumber);
        }

        var threeStarId = PickRandom(banner.ThreeStarHeroIds, rng);
        return new GachaRollOutcome(threeStarId, Rarity: 3, WasPityTriggered: false, NewPityCounter: nextPullNumber);
    }

    private static string PickRandom(List<string> pool, Random rng)
    {
        if (pool.Count == 0)
            throw new InvalidOperationException("Gacha pool không được rỗng.");
        return pool[rng.Next(pool.Count)];
    }
}
