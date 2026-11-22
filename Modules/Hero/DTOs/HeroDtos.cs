using System.ComponentModel.DataAnnotations;

namespace LearnBackend.Modules.Hero.DTOs;

public sealed record EquippedGearSummaryDto(
    Guid EquipmentId,
    string TemplateId,
    string Name,
    string Slot,
    int EnhanceLevel,
    int BonusAtk,
    int BonusHp,
    int BonusDef);

public sealed record PlayerHeroDto(
    Guid HeroId,
    string TemplateId,
    string Name,
    string Element,
    int Rarity,
    int Level,
    int? TeamSlot,
    int MaxHp,
    int Atk,
    int Def,
    int Speed,
    List<string> SkillIds,
    int NextLevelPotionCost,
    long NextLevelGoldCost,
    List<EquippedGearSummaryDto> EquippedGear);

public sealed class UpdateTeamRequest
{
    /// <summary>
    /// Danh sách tối đa 3 HeroId theo thứ tự Slot 1, 2, 3.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(3)]
    public List<Guid> HeroIds { get; init; } = [];
}
