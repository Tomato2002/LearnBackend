using System.Security.Cryptography;
using LearnBackend.Modules.MasterData.Models;

namespace LearnBackend.Modules.Gacha;

public sealed record GachaRollOutcome(
    string HeroTemplateId,
    int Rarity,
    bool WasPityTriggered,
    int NewPityCounter);

/// <summary>
/// Thuật toán Weighted Random + Soft Pity + Hard Pity chuẩn Production:
/// - Production gọi RollSingleCrypto() sử dụng RandomNumberGenerator (CSPRNG không thể đoán seed).
/// - Unit Test gọi RollSingle(..., Random rng) để kiểm thử tất định.
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

    public static GachaRollOutcome RollSingleCrypto(BannerConfig banner, int currentPityCounter)
    {
        var roll = RandomNumberGenerator.GetInt32(0, 1_000_000) / 1_000_000.0;
        return RollCore(banner, currentPityCounter, roll, count => RandomNumberGenerator.GetInt32(0, count));
    }

    public static GachaRollOutcome RollSingle(BannerConfig banner, int currentPityCounter, Random rng)
    {
        return RollCore(banner, currentPityCounter, rng.NextDouble(), count => rng.Next(count));
    }

    private static GachaRollOutcome RollCore(
        BannerConfig banner,
        int currentPityCounter,
        double roll,
        Func<int, int> pickIndex)
    {
        var nextPullNumber = currentPityCounter + 1;
        var fiveStarRate = CalculateFiveStarRate(banner, nextPullNumber);
        var isHardPity = nextPullNumber >= banner.HardPity;

        if (isHardPity || roll < fiveStarRate)
        {
            var heroId = PickRandom(banner.FiveStarHeroIds, pickIndex);
            return new GachaRollOutcome(heroId, Rarity: 5, WasPityTriggered: isHardPity, NewPityCounter: 0);
        }

        if (roll < fiveStarRate + banner.BaseFourStarRate)
        {
            var heroId = PickRandom(banner.FourStarHeroIds, pickIndex);
            return new GachaRollOutcome(heroId, Rarity: 4, WasPityTriggered: false, NewPityCounter: nextPullNumber);
        }

        var threeStarId = PickRandom(banner.ThreeStarHeroIds, pickIndex);
        return new GachaRollOutcome(threeStarId, Rarity: 3, WasPityTriggered: false, NewPityCounter: nextPullNumber);
    }

    private static string PickRandom(List<string> pool, Func<int, int> pickIndex)
    {
        if (pool.Count == 0)
            throw new InvalidOperationException("Gacha pool không được rỗng.");
        return pool[pickIndex(pool.Count)];
    }
}
