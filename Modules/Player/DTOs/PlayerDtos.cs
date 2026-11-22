using System.ComponentModel.DataAnnotations;

namespace LearnBackend.Modules.Player.DTOs;

public sealed record StaminaSnapshotDto(
    int Current,
    int Max,
    int NextRegenInSeconds,
    int RegenIntervalSeconds);

public sealed record PlayerProfileResponse(
    Guid PlayerId,
    string Nickname,
    int Level,
    int Exp,
    int ExpToNextLevel,
    long Gold,
    long Gems,
    StaminaSnapshotDto Stamina,
    DateTime ServerTimeUtc);

public sealed class SpendStaminaRequest
{
    [Range(1, 500)]
    public int Amount { get; init; }

    [MaxLength(64)]
    public string Reason { get; init; } = "ManualTest";
}

public sealed record CurrencyLedgerDto(
    long Id,
    string CurrencyType,
    long Delta,
    long BalanceAfter,
    string Reason,
    string? ReferenceId,
    DateTime CreatedAtUtc);
