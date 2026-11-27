using LearnBackend.Common.Exceptions;
using LearnBackend.Modules.MasterData.Models;
using LearnBackend.Shared.Contracts;

namespace LearnBackend.Modules.Player.Entities;

/// <summary>
/// Rich Domain Entity cho Hồ sơ & Ví tiền người chơi:
/// - Khóa cứng Gold, Gems, Level, Exp, StoredStamina, ArenaRating bằng private set.
/// - Mọi biến động tiền tệ BẮT BUỘC đi qua Spend/Credit -> tự kiểm tra không bao giờ âm tiền
///   và tự sinh ra CurrencyLedger (không thể quên ghi sổ cái kinh tế!).
/// </summary>
public sealed class PlayerProfile
{
    public Guid Id { get; private set; }
    public Guid UserAccountId { get; private set; }
    public string Nickname { get; private set; } = string.Empty;

    public int Level { get; private set; } = 1;
    public int Exp { get; private set; } = 0;

    public long Gold { get; private set; }
    public long Gems { get; private set; }

    public int StoredStamina { get; private set; }
    public DateTime LastStaminaUpdatedAtUtc { get; private set; }

    public int ArenaRating { get; private set; } = 1000;
    public int ArenaWins { get; private set; }
    public int ArenaLosses { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    // Parameterless constructor cho EF Core
    private PlayerProfile() { }

    public static (PlayerProfile Player, List<CurrencyLedger> StarterLedgers) CreateNew(
        Guid userAccountId,
        string nickname,
        GameRulesConfig rules,
        DateTime nowUtc)
    {
        var player = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            UserAccountId = userAccountId,
            Nickname = nickname,
            Level = 1,
            Exp = 0,
            Gold = 0,
            Gems = 0,
            StoredStamina = rules.GetMaxStaminaForLevel(1),
            LastStaminaUpdatedAtUtc = nowUtc,
            ArenaRating = 1000,
            CreatedAtUtc = nowUtc
        };

        var ledgers = new List<CurrencyLedger>();
        if (rules.StarterGold > 0)
            ledgers.Add(player.CreditGold(rules.StarterGold, "StarterBonus", nowUtc));
        if (rules.StarterGems > 0)
            ledgers.Add(player.CreditGems(rules.StarterGems, "StarterBonus", nowUtc));

        return (player, ledgers);
    }

    public CurrencyLedger CreditGold(long amount, string reason, DateTime nowUtc, string? referenceId = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Số Gold cộng phải lớn hơn 0.");

        Gold += amount;
        return BuildLedger("Gold", amount, Gold, reason, referenceId, nowUtc);
    }

    public CurrencyLedger SpendGold(long amount, string reason, DateTime nowUtc, string? referenceId = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Số Gold trừ phải lớn hơn 0.");

        if (Gold < amount)
        {
            throw new GameException(
                GameErrorCodes.InsufficientGold,
                $"Không đủ Gold! Cần {amount}, hiện có {Gold}.");
        }

        Gold -= amount;
        return BuildLedger("Gold", -amount, Gold, reason, referenceId, nowUtc);
    }

    public CurrencyLedger CreditGems(long amount, string reason, DateTime nowUtc, string? referenceId = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Số Gems cộng phải lớn hơn 0.");

        Gems += amount;
        return BuildLedger("Gems", amount, Gems, reason, referenceId, nowUtc);
    }

    public CurrencyLedger SpendGems(long amount, string reason, DateTime nowUtc, string? referenceId = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Số Gems trừ phải lớn hơn 0.");

        if (Gems < amount)
        {
            throw new GameException(
                GameErrorCodes.InsufficientGems,
                $"Không đủ Gems! Cần {amount}, hiện có {Gems}.");
        }

        Gems -= amount;
        return BuildLedger("Gems", -amount, Gems, reason, referenceId, nowUtc);
    }

    public StaminaCalculationResult ComputeStamina(GameRulesConfig rules, DateTime nowUtc)
    {
        return StaminaCalculator.Calculate(
            StoredStamina,
            LastStaminaUpdatedAtUtc,
            rules.GetMaxStaminaForLevel(Level),
            rules.StaminaRegenIntervalSeconds,
            nowUtc);
    }

    public StaminaCalculationResult SpendStamina(int amount, GameRulesConfig rules, DateTime nowUtc)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Thể lực tiêu hao phải lớn hơn 0.");

        var synced = ComputeStamina(rules, nowUtc);
        if (synced.CurrentStamina < amount)
        {
            throw new GameException(
                GameErrorCodes.InsufficientStamina,
                $"Không đủ thể lực! Cần {amount}, hiện có {synced.CurrentStamina}.");
        }

        var wasAtOrAboveCap = synced.CurrentStamina >= synced.MaxStamina;
        StoredStamina = synced.CurrentStamina - amount;
        LastStaminaUpdatedAtUtc = wasAtOrAboveCap && StoredStamina < synced.MaxStamina
            ? nowUtc
            : synced.UpdatedTimestampUtc;

        return ComputeStamina(rules, nowUtc);
    }

    public CurrencyLedger RefillStaminaWithGems(GameRulesConfig rules, DateTime nowUtc)
    {
        var ledger = SpendGems(rules.StaminaRefillGemCost, "StaminaRefill", nowUtc);
        var synced = ComputeStamina(rules, nowUtc);

        StoredStamina = synced.CurrentStamina + rules.StaminaRefillAmount;
        LastStaminaUpdatedAtUtc = StoredStamina >= synced.MaxStamina
            ? nowUtc
            : synced.UpdatedTimestampUtc;

        return ledger;
    }

    public int AddExpAndLevelUp(int expEarned, GameRulesConfig rules, DateTime nowUtc)
    {
        if (expEarned <= 0) return 0;

        Exp += expEarned;
        var levelsGained = 0;

        while (Exp >= rules.GetExpToNextLevel(Level))
        {
            Exp -= rules.GetExpToNextLevel(Level);
            Level++;
            levelsGained++;
        }

        if (levelsGained > 0)
        {
            var newMax = rules.GetMaxStaminaForLevel(Level);
            if (StoredStamina < newMax)
            {
                StoredStamina = newMax;
                LastStaminaUpdatedAtUtc = nowUtc;
            }
        }

        return levelsGained;
    }

    public void ApplyArenaResult(bool won, int ratingDelta)
    {
        ArenaRating = Math.Max(100, ArenaRating + ratingDelta);
        if (won) ArenaWins++;
        else ArenaLosses++;
    }

    private CurrencyLedger BuildLedger(
        string currencyType,
        long delta,
        long balanceAfter,
        string reason,
        string? referenceId,
        DateTime nowUtc) => new()
    {
        PlayerId = Id,
        CurrencyType = currencyType,
        Delta = delta,
        BalanceAfter = balanceAfter,
        Reason = reason,
        ReferenceId = referenceId,
        CreatedAtUtc = nowUtc
    };
}
