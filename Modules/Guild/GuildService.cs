using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using LearnBackend.Common.Exceptions;
using LearnBackend.Hubs;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Battle.DTOs;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Guild.Entities;
using LearnBackend.Modules.Hero;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Guild;

public sealed class CreateGuildRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(32)]
    public string Name { get; init; } = string.Empty;
}

public sealed record GuildSummaryDto(
    Guid GuildId,
    string Name,
    int MemberCount,
    int BossLevel,
    string BossName,
    int BossCurrentHp,
    int BossMaxHp);

public sealed record GuildMemberDto(
    Guid PlayerId,
    string Nickname,
    string Role,
    long TotalBossDamage);

public sealed record MyGuildResponse(
    Guid GuildId,
    string Name,
    int BossLevel,
    string BossName,
    int BossCurrentHp,
    int BossMaxHp,
    int BossVersion,
    List<GuildMemberDto> Members);

public sealed record AttackGuildBossResponse(
    int DamageDealt,
    bool KilledBoss,
    int BossLevelAfter,
    int BossHpAfter,
    int BossMaxHpAfter,
    long GoldReward,
    long LastHitBonusGems,
    List<BattleTurnLogDto> CombatLogs);

public sealed class GuildService(
    GameDbContext db,
    MasterDataStore masterData,
    TurnBasedCombatEngine combatEngine,
    IGameRealtimeBroadcaster broadcaster)
{
    private const int BossAttackStaminaCost = 10;
    private const long ParticipationGold = 100;
    private const long LastHitBonusGold = 500;
    private const long LastHitBonusGems = 300;

    public async Task<List<GuildSummaryDto>> ListGuildsAsync(CancellationToken ct)
    {
        var guilds = await db.Guilds.AsNoTracking().OrderByDescending(g => g.BossLevel).ToListAsync(ct);
        var memberCounts = await db.GuildMembers
            .AsNoTracking()
            .GroupBy(m => m.GuildId)
            .Select(g => new { GuildId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GuildId, x => x.Count, ct);

        return guilds.Select(g => new GuildSummaryDto(
            g.Id,
            g.Name,
            memberCounts.GetValueOrDefault(g.Id, 0),
            g.BossLevel,
            g.BossName,
            g.BossCurrentHp,
            g.BossMaxHp)).ToList();
    }

    public async Task<MyGuildResponse?> GetMyGuildAsync(Guid playerId, CancellationToken ct)
    {
        var membership = await db.GuildMembers.AsNoTracking().FirstOrDefaultAsync(m => m.PlayerId == playerId, ct);
        if (membership is null) return null;

        var guild = await db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == membership.GuildId, ct);
        if (guild is null) return null;

        var members = await (
            from m in db.GuildMembers.AsNoTracking()
            join p in db.Players.AsNoTracking() on m.PlayerId equals p.Id
            where m.GuildId == guild.Id
            orderby m.TotalBossDamage descending
            select new GuildMemberDto(m.PlayerId, p.Nickname, m.Role, m.TotalBossDamage)
        ).ToListAsync(ct);

        return new MyGuildResponse(
            guild.Id,
            guild.Name,
            guild.BossLevel,
            guild.BossName,
            guild.BossCurrentHp,
            guild.BossMaxHp,
            guild.BossVersion,
            members);
    }

    public async Task<MyGuildResponse> CreateGuildAsync(Guid playerId, CreateGuildRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var alreadyInGuild = await db.GuildMembers.AnyAsync(m => m.PlayerId == playerId, ct);
        if (alreadyInGuild)
            throw new GameException("ALREADY_IN_GUILD", "Bạn đã ở trong một Bang Hội rồi.");

        var nameExists = await db.Guilds.AnyAsync(g => g.Name == name, ct);
        if (nameExists)
            throw new GameException("DUPLICATE_GUILD_NAME", "Tên Bang Hội đã tồn tại.", HttpStatusCode.Conflict);

        var nowUtc = DateTime.UtcNow;
        var guild = new Entities.Guild
        {
            Id = Guid.NewGuid(),
            Name = name,
            LeaderPlayerId = playerId,
            BossLevel = 1,
            BossName = "Hắc Long Cổ Đại (Guild Boss Lv.1)",
            BossMaxHp = 5000,
            BossCurrentHp = 5000,
            BossVersion = 1,
            CreatedAtUtc = nowUtc
        };

        db.Guilds.Add(guild);
        db.GuildMembers.Add(new GuildMember
        {
            GuildId = guild.Id,
            PlayerId = playerId,
            Role = "Leader",
            TotalBossDamage = 0,
            JoinedAtUtc = nowUtc
        });

        await db.SaveChangesAsync(ct);
        return (await GetMyGuildAsync(playerId, ct))!;
    }

    public async Task<MyGuildResponse> JoinGuildAsync(Guid playerId, Guid guildId, CancellationToken ct)
    {
        var alreadyInGuild = await db.GuildMembers.AnyAsync(m => m.PlayerId == playerId, ct);
        if (alreadyInGuild)
            throw new GameException("ALREADY_IN_GUILD", "Bạn đã ở trong một Bang Hội rồi.");

        var guildExists = await db.Guilds.AnyAsync(g => g.Id == guildId, ct);
        if (!guildExists)
            throw new GameException("GUILD_NOT_FOUND", "Không tìm thấy Bang Hội này.", HttpStatusCode.NotFound);

        db.GuildMembers.Add(new GuildMember
        {
            GuildId = guildId,
            PlayerId = playerId,
            Role = "Member",
            TotalBossDamage = 0,
            JoinedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return (await GetMyGuildAsync(playerId, ct))!;
    }

    public async Task LeaveGuildAsync(Guid playerId, CancellationToken ct)
    {
        var membership = await db.GuildMembers.FirstOrDefaultAsync(m => m.PlayerId == playerId, ct)
            ?? throw new GameException("NOT_IN_GUILD", "Bạn chưa gia nhập Bang Hội nào.");

        db.GuildMembers.Remove(membership);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Đánh Guild Boss chung của cả bang với Optimistic Concurrency ([ConcurrencyCheck] BossVersion):
    /// Nếu 2 thành viên cùng đánh Boss 1 lúc, EF Core phát hiện xung đột BossVersion và tự retry sạch sẽ!
    /// </summary>
    public async Task<AttackGuildBossResponse> AttackBossAsync(Guid playerId, CancellationToken ct)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var nowUtc = DateTime.UtcNow;
            var rules = masterData.GameRules;

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var membership = await db.GuildMembers.FirstOrDefaultAsync(m => m.PlayerId == playerId, ct)
                ?? throw new GameException("NOT_IN_GUILD", "Bạn cần gia nhập Bang Hội trước khi đánh Guild Boss.");

            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == membership.GuildId, ct)
                ?? throw new GameException("GUILD_NOT_FOUND", "Không tìm thấy Bang Hội.", HttpStatusCode.NotFound);

            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
                ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

            // Trừ 10 Stamina
            var maxStamina = rules.GetMaxStaminaForLevel(player.Level);
            var stamina = StaminaCalculator.Calculate(
                player.StoredStamina,
                player.LastStaminaUpdatedAtUtc,
                maxStamina,
                rules.StaminaRegenIntervalSeconds,
                nowUtc);

            if (stamina.CurrentStamina < BossAttackStaminaCost)
                throw new GameException("INSUFFICIENT_STAMINA", $"Cần {BossAttackStaminaCost} thể lực để đánh Guild Boss.");

            var wasFull = stamina.CurrentStamina >= maxStamina;
            player.StoredStamina = stamina.CurrentStamina - BossAttackStaminaCost;
            player.LastStaminaUpdatedAtUtc = wasFull && player.StoredStamina < maxStamina
                ? nowUtc
                : stamina.UpdatedTimestampUtc;

            // Mô phỏng đội hình đánh Guild Boss bằng Lua Skills
            var heroUnits = await BuildTeamUnitsAsync(playerId, ct);
            if (heroUnits.Count == 0)
                throw new GameException("EMPTY_TEAM", "Đội hình ra trận đang trống.");

            var bossUnit = new CombatUnit
            {
                Id = $"guild_boss_{guild.BossLevel}",
                Name = guild.BossName,
                Side = "Enemy",
                MaxHp = guild.BossMaxHp,
                Hp = guild.BossCurrentHp,
                Atk = 140 + guild.BossLevel * 25,
                Def = 60 + guild.BossLevel * 10,
                Speed = 100,
                SkillIds = ["enemy_bite", "skill_fireball"]
            };

            var seed = RandomNumberGenerator.GetInt32(100_000, int.MaxValue);
            var sim = combatEngine.Simulate(heroUnits, [bossUnit], seed);

            var damageDealt = Math.Max(1, guild.BossCurrentHp - bossUnit.Hp);
            guild.BossCurrentHp = Math.Max(0, guild.BossCurrentHp - damageDealt);
            guild.BossVersion += 1; // Tăng version để kích hoạt kiểm tra Optimistic Concurrency!
            membership.TotalBossDamage += damageDealt;

            var killedBoss = guild.BossCurrentHp == 0;
            var totalGold = ParticipationGold + (killedBoss ? LastHitBonusGold : 0);
            var bonusGems = killedBoss ? LastHitBonusGems : 0;

            player.Gold += totalGold;
            db.CurrencyLedgers.Add(new CurrencyLedger
            {
                PlayerId = playerId,
                CurrencyType = "Gold",
                Delta = totalGold,
                BalanceAfter = player.Gold,
                Reason = killedBoss ? "GuildBossLastHit" : "GuildBossAttack",
                ReferenceId = $"{guild.Name}:Lv{guild.BossLevel}",
                CreatedAtUtc = nowUtc
            });

            var defeatedBossName = guild.BossName;
            if (killedBoss)
            {
                player.Gems += bonusGems;
                db.CurrencyLedgers.Add(new CurrencyLedger
                {
                    PlayerId = playerId,
                    CurrencyType = "Gems",
                    Delta = bonusGems,
                    BalanceAfter = player.Gems,
                    Reason = "GuildBossLastHit",
                    ReferenceId = $"{guild.Name}:Lv{guild.BossLevel}",
                    CreatedAtUtc = nowUtc
                });

                // Hồi sinh Guild Boss cấp tiếp theo mạnh hơn!
                guild.BossLevel += 1;
                guild.BossMaxHp = 5000 + (guild.BossLevel - 1) * 3500;
                guild.BossCurrentHp = guild.BossMaxHp;
                guild.BossName = $"Hắc Long Cổ Đại (Guild Boss Lv.{guild.BossLevel})";
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                if (killedBoss)
                {
                    await broadcaster.BroadcastAnnouncementAsync(
                        "GUILD_BOSS_KILL",
                        $"🔥 {player.Nickname} (Bang {guild.Name}) vừa kết liễu {defeatedBossName} và nhận thưởng Last Hit +{bonusGems} Gems!",
                        ct);
                }

                return new AttackGuildBossResponse(
                    DamageDealt: damageDealt,
                    KilledBoss: killedBoss,
                    BossLevelAfter: guild.BossLevel,
                    BossHpAfter: guild.BossCurrentHp,
                    BossMaxHpAfter: guild.BossMaxHp,
                    GoldReward: totalGold,
                    LastHitBonusGems: bonusGems,
                    CombatLogs: sim.Logs);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        throw new GameException("GUILD_BOSS_BUSY", "Quá nhiều thành viên đang đánh Boss cùng lúc, vui lòng thử lại!", HttpStatusCode.Conflict);
    }

    private async Task<List<CombatUnit>> BuildTeamUnitsAsync(Guid playerId, CancellationToken ct)
    {
        var teamHeroes = await db.PlayerHeroes
            .AsNoTracking()
            .Where(h => h.PlayerId == playerId && h.TeamSlot != null)
            .OrderBy(h => h.TeamSlot)
            .ToListAsync(ct);

        var heroIds = teamHeroes.Select(h => h.Id).ToList();
        var gear = await db.Equipments
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId && e.EquippedHeroId != null && heroIds.Contains(e.EquippedHeroId.Value))
            .ToListAsync(ct);

        var gearByHero = gear.GroupBy(g => g.EquippedHeroId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var units = new List<CombatUnit>();

        foreach (var h in teamHeroes)
        {
            if (!masterData.Heroes.TryGetValue(h.HeroTemplateId, out var template)) continue;
            var stats = HeroStatCalculator.Calculate(template, h, gearByHero.GetValueOrDefault(h.Id) ?? []);
            units.Add(new CombatUnit
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

        return units;
    }
}
