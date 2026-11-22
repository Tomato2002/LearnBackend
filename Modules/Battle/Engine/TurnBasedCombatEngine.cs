using LearnBackend.Modules.Battle.DTOs;

namespace LearnBackend.Modules.Battle.Engine;

public sealed record BattleSimulationResult(
    bool IsVictory,
    int TotalRounds,
    int RandomSeed,
    List<BattleUnitSummaryDto> FinalUnits,
    List<BattleTurnLogDto> Logs);

/// <summary>
/// Bộ mô phỏng chiến đấu theo lượt tất định (Deterministic Turn-Based Combat Engine):
/// - Cùng Đội hình + Quái + Seed -> luôn cho ra kết quả giống nhau 100%.
/// - Lượt đánh sắp xếp theo Speed giảm dần.
/// - Mọi Skill đều chạy qua LuaSkillRunner (MoonSharp Sandbox).
/// </summary>
public sealed class TurnBasedCombatEngine(LuaSkillRunner luaSkillRunner)
{
    private const int MaxRounds = 30;

    public BattleSimulationResult Simulate(
        List<CombatUnit> heroes,
        List<CombatUnit> enemies,
        int seed)
    {
        var rng = new Random(seed);
        var logs = new List<BattleTurnLogDto>();
        var allUnits = heroes.Concat(enemies).ToList();

        var round = 0;
        while (round < MaxRounds && heroes.Any(h => h.IsAlive) && enemies.Any(e => e.IsAlive))
        {
            round++;

            // Sắp xếp thứ tự ra đòn theo Speed giảm dần (bằng nhau thì ưu tiên phe Hero)
            var turnOrder = allUnits
                .Where(u => u.IsAlive)
                .OrderByDescending(u => u.Speed)
                .ThenBy(u => u.Side == "Hero" ? 0 : 1)
                .ToList();

            foreach (var actor in turnOrder)
            {
                if (!actor.IsAlive) continue;

                var allies = actor.Side == "Hero" ? heroes : enemies;
                var opponents = actor.Side == "Hero" ? enemies : heroes;

                var target = opponents.FirstOrDefault(o => o.IsAlive);
                if (target is null) break;

                actor.TurnCounter++;

                // Cứ mỗi lượt chẵn (turn 2, 4, 6...) dùng tuyệt chiêu số 2 nếu có, lượt lẻ dùng chiêu cơ bản
                var skillId = SelectSkill(actor);

                var ctx = new LuaBattleContext(
                    round: round,
                    skillId: skillId,
                    caster: actor,
                    primaryTarget: target,
                    allies: allies,
                    seededRng: rng,
                    logs: logs);

                luaSkillRunner.ExecuteSkill(skillId, ctx);

                if (!heroes.Any(h => h.IsAlive) || !enemies.Any(e => e.IsAlive))
                {
                    break;
                }
            }
        }

        var isVictory = heroes.Any(h => h.IsAlive) && !enemies.Any(e => e.IsAlive);
        var summaries = allUnits
            .Select(u => new BattleUnitSummaryDto(u.Name, u.Side, u.Hp, u.MaxHp, u.IsAlive))
            .ToList();

        return new BattleSimulationResult(isVictory, round, seed, summaries, logs);
    }

    private static string SelectSkill(CombatUnit actor)
    {
        if (actor.SkillIds.Count == 0) return "basic_attack";
        if (actor.SkillIds.Count == 1) return actor.SkillIds[0];

        return (actor.TurnCounter % 2 == 0) ? actor.SkillIds[1] : actor.SkillIds[0];
    }
}
