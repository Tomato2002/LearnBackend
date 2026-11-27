using LearnBackend.Modules.Gacha;
using LearnBackend.Modules.LiveOps;
using LearnBackend.Modules.MasterData.Models;
using LearnBackend.Shared.Contracts;

namespace LearnBackend.Tests;

public class LiveOpsTests
{
    [Fact]
    public void HourlyCooldown_FirstTimeOrExpired_ReturnsZero()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(0, LiveOpsService.CalculateHourlyCooldownRemaining(null, now));
        Assert.Equal(0, LiveOpsService.CalculateHourlyCooldownRemaining(now.AddHours(-2), now));
    }

    [Fact]
    public void HourlyCooldown_WithinInterval_ReturnsRemainingSeconds()
    {
        var claimedAt = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var now = claimedAt.AddMinutes(20); // Còn 40 phút = 2400 giây

        var remaining = LiveOpsService.CalculateHourlyCooldownRemaining(claimedAt, now);
        Assert.Equal(2400, remaining);
    }

    [Fact]
    public void FeatureStore_UpdatesFlagsAndReturnsIsolatedSnapshot()
    {
        var store = new LiveOpsFeatureStore();

        var initial = store.GetSnapshot();
        Assert.False(initial.MaintenanceMode);
        Assert.True(initial.EnableGacha);

        store.Update(new FeatureFlagsDto
        {
            MaintenanceMode = true,
            EnableGacha = false,
            EnableShop = true
        });

        var updated = store.GetSnapshot();
        Assert.True(updated.MaintenanceMode);
        Assert.False(updated.EnableGacha);

        // Sửa snapshot trả ra không được làm ảnh hưởng state bên trong Store
        updated.EnableGacha = true;
        Assert.False(store.GetSnapshot().EnableGacha);
    }

    [Fact]
    public void GachaCryptoRoller_AtHardPity_AlwaysReturnsFiveStar()
    {
        var banner = new BannerConfig
        {
            Id = "banner_standard",
            HardPity = 10,
            SoftPityStart = 7,
            FiveStarHeroIds = ["hero_celestia"],
            FourStarHeroIds = ["hero_arthur"],
            ThreeStarHeroIds = ["hero_kael"]
        };

        var outcome = GachaRoller.RollSingleCrypto(banner, currentPityCounter: 9);
        Assert.Equal(5, outcome.Rarity);
        Assert.True(outcome.WasPityTriggered);
        Assert.Equal(0, outcome.NewPityCounter);
    }
}
