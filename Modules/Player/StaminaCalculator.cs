namespace LearnBackend.Modules.Player;

public sealed record StaminaCalculationResult(
    int CurrentStamina,
    int MaxStamina,
    DateTime UpdatedTimestampUtc,
    int NextRegenInSeconds);

/// <summary>
/// Tính thể lực theo cơ chế Lazy Calculation:
/// - Không cần chạy timer/cron cộng thể lực cho toàn bộ DB.
/// - Chỉ tính khi người chơi gọi API dựa trên độ lệch thời gian UTC.
/// - Giữ nguyên phần giây dư để người chơi không bị mất thời gian tích lũy.
/// </summary>
public static class StaminaCalculator
{
    public static StaminaCalculationResult Calculate(
        int storedStamina,
        DateTime lastUpdatedAtUtc,
        int maxStamina,
        int regenIntervalSeconds,
        DateTime nowUtc)
    {
        if (storedStamina >= maxStamina)
        {
            return new StaminaCalculationResult(
                CurrentStamina: storedStamina,
                MaxStamina: maxStamina,
                UpdatedTimestampUtc: nowUtc,
                NextRegenInSeconds: 0);
        }

        var elapsedSeconds = Math.Max(0, (nowUtc - lastUpdatedAtUtc).TotalSeconds);
        var gained = (int)(elapsedSeconds / regenIntervalSeconds);

        if (gained <= 0)
        {
            var remaining = Math.Max(1, regenIntervalSeconds - (int)elapsedSeconds);
            return new StaminaCalculationResult(
                CurrentStamina: storedStamina,
                MaxStamina: maxStamina,
                UpdatedTimestampUtc: lastUpdatedAtUtc,
                NextRegenInSeconds: remaining);
        }

        var newStamina = Math.Min(maxStamina, storedStamina + gained);
        if (newStamina >= maxStamina)
        {
            return new StaminaCalculationResult(
                CurrentStamina: maxStamina,
                MaxStamina: maxStamina,
                UpdatedTimestampUtc: nowUtc,
                NextRegenInSeconds: 0);
        }

        // Chỉ tịnh tiến mốc thời gian đúng bằng số điểm đã hồi -> bảo toàn phần giây dư!
        var newTimestamp = lastUpdatedAtUtc.AddSeconds((double)gained * regenIntervalSeconds);
        var remainderElapsed = Math.Max(0, (nowUtc - newTimestamp).TotalSeconds);
        var nextRegenInSeconds = Math.Max(1, regenIntervalSeconds - (int)remainderElapsed);

        return new StaminaCalculationResult(
            CurrentStamina: newStamina,
            MaxStamina: maxStamina,
            UpdatedTimestampUtc: newTimestamp,
            NextRegenInSeconds: nextRegenInSeconds);
    }
}
