using System.ComponentModel.DataAnnotations;
using LearnBackend.Modules.Player.DTOs;

namespace LearnBackend.Modules.Battle.DTOs;

public sealed class RunBattleRequest
{
    [Required]
    public string StageId { get; init; } = string.Empty;
}

public sealed record BattleTurnLogDto(
    int Round,
    string ActorName,
    string ActorSide,      // "Hero" | "Enemy"
    string SkillId,
    string SkillLabel,
    string ActionType,     // "Damage" | "Heal"
    string TargetName,
    int Amount,
    int TargetHpAfter,
    int TargetMaxHp,
    bool TargetDefeated);

public sealed record DroppedItemDto(
    string ItemId,
    string Name,
    string Category,
    int Quantity,
    Guid? EquipmentInstanceId);

public sealed record BattleRewardsDto(
    long GoldEarned,
    int ExpEarned,
    bool LeveledUp,
    int NewPlayerLevel,
    List<DroppedItemDto> Drops);

public sealed record BattleUnitSummaryDto(
    string Name,
    string Side,
    int RemainingHp,
    int MaxHp,
    bool IsAlive);

public sealed record BattleRunResponse(
    string StageId,
    string StageName,
    bool IsVictory,
    int TotalRounds,
    int RandomSeed,
    BattleRewardsDto? Rewards,
    List<BattleUnitSummaryDto> FinalUnits,
    List<BattleTurnLogDto> CombatLogs,
    PlayerProfileResponse UpdatedProfile);
