namespace LearnBackend.Modules.MasterData.Models;

public sealed class GameRulesConfig
{
    public long StarterGold { get; init; } = 1000;
    public long StarterGems { get; init; } = 300;
    public int BaseMaxStamina { get; init; } = 100;
    public int StaminaPerLevel { get; init; } = 5;
    public int StaminaRegenIntervalSeconds { get; init; } = 300; // 5 phút hồi 1 thể lực
    public long StaminaRefillGemCost { get; init; } = 50;
    public int StaminaRefillAmount { get; init; } = 60;
    public int BaseExpPerLevel { get; init; } = 100;

    public int GetMaxStaminaForLevel(int level)
        => BaseMaxStamina + Math.Max(0, level - 1) * StaminaPerLevel;

    public int GetExpToNextLevel(int level)
        => Math.Max(1, level) * BaseExpPerLevel;
}
