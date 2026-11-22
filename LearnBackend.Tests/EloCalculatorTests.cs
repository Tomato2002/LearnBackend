using LearnBackend.Modules.Arena;

namespace LearnBackend.Tests;

public class EloCalculatorTests
{
    [Fact]
    public void Calculate_UnderdogVictory_EarnsMorePointsThanFavoriteVictory()
    {
        var (underdogWinDelta, _) = EloCalculator.Calculate(attackerRating: 900, defenderRating: 1200, attackerWon: true);
        var (favoriteWinDelta, _) = EloCalculator.Calculate(attackerRating: 1200, defenderRating: 900, attackerWon: true);

        Assert.True(underdogWinDelta > favoriteWinDelta);
        Assert.True(favoriteWinDelta >= 5);
    }

    [Fact]
    public void Calculate_IsZeroSumBetweenAttackerAndDefender()
    {
        var (attackerDelta, defenderDelta) = EloCalculator.Calculate(1050, 1000, attackerWon: false);
        Assert.Equal(-attackerDelta, defenderDelta);
        Assert.True(attackerDelta <= -5);
    }
}
