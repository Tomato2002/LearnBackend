using System.Net;
using System.Security.Cryptography;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Battle.DTOs;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Hero;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player;
using LearnBackend.Modules.Player.DTOs;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Battle;

public sealed class BattleService(
    GameDbContext db,
    MasterDataStore masterData,
    TurnBasedCombatEngine combatEngine)
{
    public async Task<BattleRunResponse> RunStageAsync(Guid playerId, string stageId, CancellationToken ct)
    {
        if (!masterData.Stages.TryGetValue(stageId, out var stage))
        {
            throw new GameException("STAGE_NOT_FOUND", $"Không tìm thấy ải '{stageId}'.", HttpStatusCode.NotFound);
        }

        var nowUtc = DateTime.UtcNow;
        var rules = masterData.GameRules;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        // 1. Đồng bộ & trừ Stamina (Server-Authoritative)
        var maxStamina = rules.GetMaxStaminaForLevel(player.Level);
        var staminaCalc = StaminaCalculator.Calculate(
            player.StoredStamina,
            player.LastStaminaUpdatedAtUtc,
            maxStamina,
            rules.StaminaRegenIntervalSeconds,
            nowUtc);

        if (staminaCalc.CurrentStamina < stage.StaminaCost)
        {
            throw new GameException(
                "INSUFFICIENT_STAMINA",
                $"Không đủ thể lực vào ải '{stage.Name}'! Cần {stage.StaminaCost}, hiện có {staminaCalc.CurrentStamina}.");
        }

        var wasAtOrAboveCap = staminaCalc.CurrentStamina >= maxStamina;
        player.StoredStamina = staminaCalc.CurrentStamina - stage.StaminaCost;
        player.LastStaminaUpdatedAtUtc = wasAtOrAboveCap && player.StoredStamina < maxStamina
            ? nowUtc
            : staminaCalc.UpdatedTimestampUtc;

        // 2. Lấy đội hình Hero + Trang bị đang mặc của Player
        var teamHeroes = await db.PlayerHeroes
            .AsNoTracking()
            .Where(h => h.PlayerId == playerId && h.TeamSlot != null)
            .OrderBy(h => h.TeamSlot)
            .ToListAsync(ct);

        if (teamHeroes.Count == 0)
        {
            throw new GameException("EMPTY_TEAM", "Bạn chưa xếp Hero nào vào đội hình ra trận!");
        }

        var heroIds = teamHeroes.Select(h => h.Id).ToList();
        var equippedGear = await db.Equipments
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId && e.EquippedHeroId != null && heroIds.Contains(e.EquippedHeroId.Value))
            .ToListAsync(ct);

        var gearByHero = equippedGear
            .GroupBy(e => e.EquippedHeroId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var heroUnits = new List<CombatUnit>();
        foreach (var h in teamHeroes)
        {
            if (!masterData.Heroes.TryGetValue(h.HeroTemplateId, out var template)) continue;
            var stats = HeroStatCalculator.Calculate(template, h, gearByHero.GetValueOrDefault(h.Id) ?? []);
            heroUnits.Add(new CombatUnit
            {
                Id = h.Id.ToString(),
                Name = $"{template.Name.Split(' ')[0]} (Lv.{h.Level})",
                Side = "Hero",
                MaxHp = stats.MaxHp,
                Hp = stats.MaxHp,
                Atk = stats.Atk,
                Def = stats.Def,
                Speed = stats.Speed,
                SkillIds = [.. template.SkillIds]
            });
        }

        // 3. Tạo đội hình Quái vật từ MasterData
        var enemyUnits = new List<CombatUnit>();
        for (var i = 0; i < stage.EnemyIds.Count; i++)
        {
            var enemyId = stage.EnemyIds[i];
            if (!masterData.Enemies.TryGetValue(enemyId, out var enemyCfg)) continue;

            var suffix = stage.EnemyIds.Count > 1 ? $" #{i + 1}" : string.Empty;
            enemyUnits.Add(new CombatUnit
            {
                Id = $"{enemyCfg.Id}_{i + 1}",
                Name = $"{enemyCfg.Name}{suffix}",
                Side = "Enemy",
                MaxHp = enemyCfg.Hp,
                Hp = enemyCfg.Hp,
                Atk = enemyCfg.Atk,
                Def = enemyCfg.Def,
                Speed = enemyCfg.Speed,
                SkillIds = [.. enemyCfg.SkillIds]
            });
        }

        // 4. Chạy mô phỏng Turn-Based Combat với Lua Skills
        var seed = RandomNumberGenerator.GetInt32(100_000, int.MaxValue);
        var sim = combatEngine.Simulate(heroUnits, enemyUnits, seed);

        BattleRewardsDto? rewards = null;

        // 5. Nếu thắng -> Cộng Gold, EXP, lên Level, rớt đồ vào Inventory & ghi CurrencyLedger
        if (sim.IsVictory)
        {
            player.Gold += stage.GoldReward;
            player.Exp += stage.ExpReward;

            var levelsGained = 0;
            while (player.Exp >= rules.GetExpToNextLevel(player.Level))
            {
                player.Exp -= rules.GetExpToNextLevel(player.Level);
                player.Level++;
                levelsGained++;
            }

            // Lên cấp thưởng hồi đầy Stamina theo MaxStamina mới
            if (levelsGained > 0)
            {
                var newMax = rules.GetMaxStaminaForLevel(player.Level);
                if (player.StoredStamina < newMax)
                {
                    player.StoredStamina = newMax;
                    player.LastStaminaUpdatedAtUtc = nowUtc;
                }
            }

            db.CurrencyLedgers.Add(new CurrencyLedger
            {
                PlayerId = player.Id,
                CurrencyType = "Gold",
                Delta = stage.GoldReward,
                BalanceAfter = player.Gold,
                Reason = "StageClear",
                ReferenceId = stage.Id,
                CreatedAtUtc = nowUtc
            });

            // Roll vật phẩm rơi từ DropTable
            var lootRng = new Random(seed ^ 0x5F3759DF);
            var drops = await RollAndGrantDropsAsync(player.Id, stage.DropTable, lootRng, nowUtc, ct);

            rewards = new BattleRewardsDto(
                GoldEarned: stage.GoldReward,
                ExpEarned: stage.ExpReward,
                LeveledUp: levelsGained > 0,
                NewPlayerLevel: player.Level,
                Drops: drops);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var finalStamina = StaminaCalculator.Calculate(
            player.StoredStamina,
            player.LastStaminaUpdatedAtUtc,
            rules.GetMaxStaminaForLevel(player.Level),
            rules.StaminaRegenIntervalSeconds,
            nowUtc);

        var updatedProfile = new PlayerProfileResponse(
            PlayerId: player.Id,
            Nickname: player.Nickname,
            Level: player.Level,
            Exp: player.Exp,
            ExpToNextLevel: rules.GetExpToNextLevel(player.Level),
            Gold: player.Gold,
            Gems: player.Gems,
            Stamina: new StaminaSnapshotDto(
                Current: finalStamina.CurrentStamina,
                Max: finalStamina.MaxStamina,
                NextRegenInSeconds: finalStamina.NextRegenInSeconds,
                RegenIntervalSeconds: rules.StaminaRegenIntervalSeconds),
            ServerTimeUtc: nowUtc);

        return new BattleRunResponse(
            StageId: stage.Id,
            StageName: stage.Name,
            IsVictory: sim.IsVictory,
            TotalRounds: sim.TotalRounds,
            RandomSeed: sim.RandomSeed,
            Rewards: rewards,
            FinalUnits: sim.FinalUnits,
            CombatLogs: sim.Logs,
            UpdatedProfile: updatedProfile);
    }

    private async Task<List<DroppedItemDto>> RollAndGrantDropsAsync(
        Guid playerId,
        List<Modules.MasterData.Models.StageDropEntry> dropTable,
        Random rng,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var drops = new List<DroppedItemDto>();

        foreach (var entry in dropTable)
        {
            if (rng.NextDouble() > entry.Chance) continue;
            if (!masterData.Items.TryGetValue(entry.ItemId, out var itemCfg)) continue;

            var qty = entry.MinQty >= entry.MaxQty
                ? entry.MinQty
                : rng.Next(entry.MinQty, entry.MaxQty + 1);

            if (string.Equals(itemCfg.Category, "Equipment", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < qty; i++)
                {
                    // Roll nhẹ chỉ số phụ ngẫu nhiên (+0% đến +20%) cho trang bị rơi ra
                    var rollBonus = 1.0 + rng.Next(0, 21) / 100.0;
                    var equip = new EquipmentInstance
                    {
                        Id = Guid.NewGuid(),
                        PlayerId = playerId,
                        TemplateId = itemCfg.Id,
                        EnhanceLevel = 0,
                        BonusAtk = (int)Math.Round(itemCfg.BaseAtkBonus * rollBonus),
                        BonusHp = (int)Math.Round(itemCfg.BaseHpBonus * rollBonus),
                        BonusDef = (int)Math.Round(itemCfg.BaseDefBonus * rollBonus),
                        EquippedHeroId = null,
                        CreatedAtUtc = nowUtc
                    };
                    db.Equipments.Add(equip);

                    drops.Add(new DroppedItemDto(itemCfg.Id, itemCfg.Name, itemCfg.Category, 1, equip.Id));
                }
            }
            else
            {
                var existing = await db.InventoryItems
                    .FirstOrDefaultAsync(x => x.PlayerId == playerId && x.ItemId == itemCfg.Id, ct);

                if (existing is null)
                {
                    db.InventoryItems.Add(new InventoryItem
                    {
                        PlayerId = playerId,
                        ItemId = itemCfg.Id,
                        Quantity = qty
                    });
                }
                else
                {
                    existing.Quantity += qty;
                }

                drops.Add(new DroppedItemDto(itemCfg.Id, itemCfg.Name, itemCfg.Category, qty, null));
            }
        }

        return drops;
    }
}
