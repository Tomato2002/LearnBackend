using LearnBackend.Modules.Player;

namespace LearnBackend.Tests;

public class StaminaCalculatorTests
{
    private const int IntervalSeconds = 300; // 5 phút hồi 1 thể lực

    [Fact]
    public void Calculate_WhenBelowCap_RegensCorrectlyAndPreservesRemainderSeconds()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        // 7 phút 20 giây = 440 giây -> hồi 1 điểm (300s), dư 140s -> còn 160s tới điểm tiếp theo
        var now = start.AddSeconds(440);

        var result = StaminaCalculator.Calculate(
            storedStamina: 50,
            lastUpdatedAtUtc: start,
            maxStamina: 100,
            regenIntervalSeconds: IntervalSeconds,
            nowUtc: now);

        Assert.Equal(51, result.CurrentStamina);
        Assert.Equal(start.AddSeconds(300), result.UpdatedTimestampUtc);
        Assert.Equal(160, result.NextRegenInSeconds);
    }

    [Fact]
    public void Calculate_WhenElapsedExceedsCap_CapsAtMaxStaminaAndResetsTimer()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var now = start.AddHours(10);

        var result = StaminaCalculator.Calculate(
            storedStamina: 20,
            lastUpdatedAtUtc: start,
            maxStamina: 100,
            regenIntervalSeconds: IntervalSeconds,
            nowUtc: now);

        Assert.Equal(100, result.CurrentStamina);
        Assert.Equal(now, result.UpdatedTimestampUtc);
        Assert.Equal(0, result.NextRegenInSeconds);
    }

    [Fact]
    public void Calculate_WhenOvercappedByRefill_DoesNotRegenOrReduce()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var now = start.AddHours(2);

        var result = StaminaCalculator.Calculate(
            storedStamina: 140,
            lastUpdatedAtUtc: start,
            maxStamina: 100,
            regenIntervalSeconds: IntervalSeconds,
            nowUtc: now);

        Assert.Equal(140, result.CurrentStamina);
        Assert.Equal(0, result.NextRegenInSeconds);
    }
}
