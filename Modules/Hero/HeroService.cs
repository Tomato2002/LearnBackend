using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Hero.DTOs;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Hero;

public sealed class HeroService(GameDbContext db, MasterDataStore masterData)
{
    public async Task<List<PlayerHeroDto>> GetMyHeroesAsync(Guid playerId, CancellationToken ct)
    {
        var heroes = await db.PlayerHeroes
            .AsNoTracking()
            .Where(h => h.PlayerId == playerId)
            .OrderBy(h => h.TeamSlot ?? 99)
            .ThenBy(h => h.AcquiredAtUtc)
            .ToListAsync(ct);

        var equipments = await db.Equipments
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId && e.EquippedHeroId != null)
            .ToListAsync(ct);

        var equipByHero = equipments
            .GroupBy(e => e.EquippedHeroId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<PlayerHeroDto>(heroes.Count);
        foreach (var hero in heroes)
        {
            if (!masterData.Heroes.TryGetValue(hero.HeroTemplateId, out var template))
                continue;

            var gearList = equipByHero.GetValueOrDefault(hero.Id) ?? [];
            var stats = HeroStatCalculator.Calculate(template, hero, gearList);
            var (potionCost, goldCost) = HeroStatCalculator.GetLevelUpCost(hero.Level);

            var gearDtos = gearList.Select(g =>
            {
                var itemCfg = masterData.Items.GetValueOrDefault(g.TemplateId);
                return new EquippedGearSummaryDto(
                    g.Id,
                    g.TemplateId,
                    itemCfg?.Name ?? g.TemplateId,
                    itemCfg?.Slot ?? "Unknown",
                    g.EnhanceLevel,
                    g.BonusAtk,
                    g.BonusHp,
                    g.BonusDef);
            }).ToList();

            result.Add(new PlayerHeroDto(
                HeroId: hero.Id,
                TemplateId: hero.HeroTemplateId,
                Name: template.Name,
                Element: template.Element,
                Rarity: template.Rarity,
                Level: hero.Level,
                TeamSlot: hero.TeamSlot,
                MaxHp: stats.MaxHp,
                Atk: stats.Atk,
                Def: stats.Def,
                Speed: stats.Speed,
                SkillIds: template.SkillIds,
                NextLevelPotionCost: potionCost,
                NextLevelGoldCost: goldCost,
                EquippedGear: gearDtos));
        }

        return result;
    }

    public async Task<PlayerHeroDto> LevelUpHeroAsync(Guid playerId, Guid heroId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var hero = await db.PlayerHeroes
            .FirstOrDefaultAsync(h => h.Id == heroId && h.PlayerId == playerId, ct)
            ?? throw new GameException("HERO_NOT_FOUND", "Không tìm thấy Hero này.", HttpStatusCode.NotFound);

        var player = await db.Players
            .FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        var (potionCost, goldCost) = HeroStatCalculator.GetLevelUpCost(hero.Level);

        var potionItem = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.PlayerId == playerId && i.ItemId == "potion_exp_small", ct);

        if (potionItem is null || potionItem.Quantity < potionCost)
        {
            var have = potionItem?.Quantity ?? 0;
            throw new GameException(
                "INSUFFICIENT_MATERIAL",
                $"Cần {potionCost} Bình Kinh Nghiệm Nhỏ để nâng cấp (hiện có {have}).");
        }

        hero.Level += 1;
        potionItem.Quantity -= potionCost;
        db.CurrencyLedgers.Add(player.SpendGold(
            goldCost,
            reason: "HeroLevelUp",
            nowUtc: DateTime.UtcNow,
            referenceId: $"{hero.HeroTemplateId}:Lv{hero.Level}"));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var all = await GetMyHeroesAsync(playerId, ct);
        return all.First(x => x.HeroId == heroId);
    }

    public async Task<List<PlayerHeroDto>> UpdateTeamAsync(Guid playerId, UpdateTeamRequest request, CancellationToken ct)
    {
        var distinctIds = request.HeroIds.Distinct().ToList();
        if (distinctIds.Count != request.HeroIds.Count)
        {
            throw new GameException("DUPLICATE_TEAM_HERO", "Không thể xếp trùng 1 Hero vào nhiều vị trí.");
        }

        var heroes = await db.PlayerHeroes
            .Where(h => h.PlayerId == playerId)
            .ToListAsync(ct);

        var heroMap = heroes.ToDictionary(h => h.Id);
        foreach (var id in distinctIds)
        {
            if (!heroMap.ContainsKey(id))
            {
                throw new GameException("HERO_NOT_FOUND", $"Hero {id} không thuộc sở hữu của bạn.", HttpStatusCode.NotFound);
            }
        }

        foreach (var h in heroes)
        {
            h.TeamSlot = null;
        }

        for (var i = 0; i < distinctIds.Count; i++)
        {
            heroMap[distinctIds[i]].TeamSlot = i + 1;
        }

        await db.SaveChangesAsync(ct);
        return await GetMyHeroesAsync(playerId, ct);
    }
}
