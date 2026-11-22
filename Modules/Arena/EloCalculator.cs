namespace LearnBackend.Modules.Arena;

/// <summary>
/// Công thức tính điểm Elo chuẩn cho PvP Arena (K-Factor = 32).
/// Thắng đối thủ điểm cao hơn được cộng nhiều điểm hơn; thua đối thủ điểm thấp bị trừ nhiều hơn.
/// </summary>
public static class EloCalculator
{
    private const double KFactor = 32.0;

    public static (int AttackerDelta, int DefenderDelta) Calculate(
        int attackerRating,
        int defenderRating,
        bool attackerWon)
    {
        var expectedAttacker = 1.0 / (1.0 + Math.Pow(10.0, (defenderRating - attackerRating) / 400.0));
        var actualAttacker = attackerWon ? 1.0 : 0.0;

        var attackerDelta = (int)Math.Round(KFactor * (actualAttacker - expectedAttacker));
        if (attackerWon && attackerDelta < 5) attackerDelta = 5;
        if (!attackerWon && attackerDelta > -5) attackerDelta = -5;

        var defenderDelta = -attackerDelta;
        return (attackerDelta, defenderDelta);
    }
}
