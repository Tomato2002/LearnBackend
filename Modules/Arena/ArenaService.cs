using System.Net;
using System.Security.Cryptography;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Arena.Entities;
using LearnBackend.Modules.Battle.DTOs;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Hero;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Arena;

public sealed record LeaderboardEntryDto(
    int Rank,
    Guid PlayerId,
    string Nickname,
    int Level,
    int ArenaRating,
    int Wins,
    int Losses);

public sealed record ArenaLeaderboardResponse(
    int MyRank,
    int MyRating,
    int MyWins,
    int MyLosses,
    List<LeaderboardEntryDto> TopPlayers);

public sealed record ArenaOpponentDto(
    Guid? PlayerId,
    string? BotId,
    string Name,
    int Rating,
    bool IsBot,
    string TeamSummary);

public sealed class ArenaChallengeRequest
{
    public Guid? DefenderPlayerId { get; init; }
    public string? BotId { get; init; }
}

public sealed record ArenaChallengeResponse(
    string DefenderName,
    bool IsBot,
    bool IsVictory,
    int RatingBefore,
    int RatingAfter,
    int RatingDelta,
    long GoldEarned,
    int TotalRounds,
    int RandomSeed,
    List<BattleTurnLogDto> CombatLogs);

public sealed class ArenaService(
    GameDbContext db,
    MasterDataStore masterData,
    TurnBasedCombatEngine combatEngine,
    IArenaLeaderboardStore leaderboardStore)
{
    private const long WinGoldReward = 150;

    private static readonly List<(string BotId, string Name, int Rating, int PowerScale)> ArenaBots =
    [
        ("bot_silver", "🤖 Bot Hiệp Sĩ Bạc", 960, 85),
        ("bot_flame", "🤖 Bot Hỏa Pháp Sư", 1040, 100),
        ("bot_celestial", "🤖 Bot Thánh Nữ Tinh Tú", 1150, 120)
    ];

    public async Task<ArenaLeaderboardResponse> GetLeaderboardAsync(Guid playerId, int top, CancellationToken ct)
    {
        var safeTop = Math.Clamp(top, 5, 100);

        var me = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        var myRank = await leaderboardStore.GetRankAsync(playerId, me.ArenaRating, ct);
        var topScores = await leaderboardStore.GetTopAsync(safeTop, ct);

        var topIds = topScores.Select(x => x.PlayerId).ToList();
        var playerMap = await db.Players
            .AsNoTracking()
            .Where(p => topIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var entries = new List<LeaderboardEntryDto>(topScores.Count);
        for (var i = 0; i < topScores.Count; i++)
        {
            var (pid, score) = topScores[i];
            if (!playerMap.TryGetValue(pid, out var p)) continue;

            entries.Add(new LeaderboardEntryDto(
                Rank: i + 1,
                PlayerId: p.Id,
                Nickname: p.Nickname,
                Level: p.Level,
                ArenaRating: score,
                Wins: p.ArenaWins,
                Losses: p.ArenaLosses));
        }

        return new ArenaLeaderboardResponse(
            MyRank: myRank,
            MyRating: me.ArenaRating,
            MyWins: me.ArenaWins,
            MyLosses: me.ArenaLosses,
            TopPlayers: entries);
    }

    public async Task<List<ArenaOpponentDto>> GetOpponentsAsync(Guid playerId, CancellationToken ct)
    {
        var me = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        // Tìm tối đa 3 người chơi thật có điểm Elo gần nhất
        var realOpponents = await db.Players
            .AsNoTracking()
            .Where(p => p.Id != playerId)
            .OrderBy(p => Math.Abs(p.ArenaRating - me.ArenaRating))
            .Take(3)
            .ToListAsync(ct);

        var list = realOpponents.Select(p => new ArenaOpponentDto(
            PlayerId: p.Id,
            BotId: null,
            Name: p.Nickname,
            Rating: p.ArenaRating,
            IsBot: false,
            TeamSummary: $"Người chơi thật • Lv.{p.Level}")).ToList();

        // Nếu chưa đủ 3 đối thủ thật (khi test 1 mình) -> bổ sung Bot Arena để luôn đánh được ngay
        foreach (var bot in ArenaBots)
        {
            if (list.Count >= 3) break;
            list.Add(new ArenaOpponentDto(
                PlayerId: null,
                BotId: bot.BotId,
                Name: bot.Name,
                Rating: bot.Rating,
                IsBot: true,
                TeamSummary: $"Đội hình Bot ({bot.PowerScale}% sức mạnh)"));
        }

        return list;
    }

    public async Task<ArenaChallengeResponse> ChallengeAsync(Guid playerId, ArenaChallengeRequest request, CancellationToken ct)
    {
        if (request.DefenderPlayerId is null && string.IsNullOrWhiteSpace(request.BotId))
        {
            throw new GameException("INVALID_OPPONENT", "Vui lòng chọn đối thủ (DefenderPlayerId hoặc BotId).");
        }

        if (request.DefenderPlayerId == playerId)
        {
            throw new GameException("CANNOT_CHALLENGE_SELF", "Không thể tự khiêu chiến chính mình!");
        }

        var nowUtc = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var attacker = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        var attackerUnits = await BuildPlayerTeamUnitsAsync(playerId, "Hero", ct);
        if (attackerUnits.Count == 0)
        {
            throw new GameException("EMPTY_TEAM", "Bạn chưa có Hero nào trong đội hình!");
        }

        PlayerProfile? defenderPlayer = null;
        string defenderName;
        int defenderRating;
        bool isBot;
        List<CombatUnit> defenderUnits;

        if (request.DefenderPlayerId.HasValue)
        {
            defenderPlayer = await db.Players.FirstOrDefaultAsync(p => p.Id == request.DefenderPlayerId.Value, ct)
                ?? throw new GameException("DEFENDER_NOT_FOUND", "Không tìm thấy đối thủ.", HttpStatusCode.NotFound);

            defenderName = defenderPlayer.Nickname;
            defenderRating = defenderPlayer.ArenaRating;
            isBot = false;
            defenderUnits = await BuildPlayerTeamUnitsAsync(defenderPlayer.Id, "Enemy", ct);
            if (defenderUnits.Count == 0)
            {
                defenderUnits = BuildBotUnits("bot_silver");
            }
        }
        else
        {
            var bot = ArenaBots.FirstOrDefault(b => b.BotId.Equals(request.BotId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(bot.BotId))
            {
                throw new GameException("BOT_NOT_FOUND", $"Không tìm thấy Bot '{request.BotId}'.", HttpStatusCode.NotFound);
            }

            defenderName = bot.Name;
            defenderRating = bot.Rating;
            isBot = true;
            defenderUnits = BuildBotUnits(bot.BotId);
        }

        var seed = RandomNumberGenerator.GetInt32(100_000, int.MaxValue);
        var sim = combatEngine.Simulate(attackerUnits, defenderUnits, seed);

        var (attackerDelta, defenderDelta) = EloCalculator.Calculate(attacker.ArenaRating, defenderRating, sim.IsVictory);
        var ratingBefore = attacker.ArenaRating;
        attacker.ArenaRating = Math.Max(100, attacker.ArenaRating + attackerDelta);

        long goldEarned = 0;
        if (sim.IsVictory)
        {
            attacker.ArenaWins++;
            goldEarned = WinGoldReward;
            attacker.Gold += goldEarned;
            db.CurrencyLedgers.Add(new CurrencyLedger
            {
                PlayerId = attacker.Id,
                CurrencyType = "Gold",
                Delta = goldEarned,
                BalanceAfter = attacker.Gold,
                Reason = "ArenaVictory",
                ReferenceId = defenderName,
                CreatedAtUtc = nowUtc
            });
        }
        else
        {
            attacker.ArenaLosses++;
        }

        if (defenderPlayer is not null)
        {
            defenderPlayer.ArenaRating = Math.Max(100, defenderPlayer.ArenaRating + defenderDelta);
            if (sim.IsVictory) defenderPlayer.ArenaLosses++;
            else defenderPlayer.ArenaWins++;
        }

        db.ArenaMatchHistories.Add(new ArenaMatchHistory
        {
            AttackerPlayerId = attacker.Id,
            DefenderPlayerId = defenderPlayer?.Id,
            DefenderName = defenderName,
            IsBot = isBot,
            AttackerWon = sim.IsVictory,
            RatingDelta = attackerDelta,
            RandomSeed = seed,
            CreatedAtUtc = nowUtc
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Đồng bộ điểm Elo mới lên Redis ZSET (nếu đang bật Redis)
        await leaderboardStore.UpdateScoreAsync(attacker.Id, attacker.ArenaRating, ct);
        if (defenderPlayer is not null)
        {
            await leaderboardStore.UpdateScoreAsync(defenderPlayer.Id, defenderPlayer.ArenaRating, ct);
        }

        return new ArenaChallengeResponse(
            DefenderName: defenderName,
            IsBot: isBot,
            IsVictory: sim.IsVictory,
            RatingBefore: ratingBefore,
            RatingAfter: attacker.ArenaRating,
            RatingDelta: attackerDelta,
            GoldEarned: goldEarned,
            TotalRounds: sim.TotalRounds,
            RandomSeed: seed,
            CombatLogs: sim.Logs);
    }

    private async Task<List<CombatUnit>> BuildPlayerTeamUnitsAsync(Guid ownerPlayerId, string side, CancellationToken ct)
    {
        var teamHeroes = await db.PlayerHeroes
            .AsNoTracking()
            .Where(h => h.PlayerId == ownerPlayerId && h.TeamSlot != null)
            .OrderBy(h => h.TeamSlot)
            .ToListAsync(ct);

        var heroIds = teamHeroes.Select(h => h.Id).ToList();
        var gear = await db.Equipments
            .AsNoTracking()
            .Where(e => e.PlayerId == ownerPlayerId && e.EquippedHeroId != null && heroIds.Contains(e.EquippedHeroId.Value))
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
                Side = side,
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

    private List<CombatUnit> BuildBotUnits(string botId)
    {
        var scale = botId switch
        {
            "bot_silver" => 0.80,
            "bot_flame" => 0.98,
            _ => 1.18
        };

        string[] templateIds = ["hero_arthur", "hero_lyra", "hero_ignis"];
        var units = new List<CombatUnit>();

        foreach (var tid in templateIds)
        {
            if (!masterData.Heroes.TryGetValue(tid, out var t)) continue;
            var hp = (int)(t.BaseHp * scale);
            units.Add(new CombatUnit
            {
                Id = $"{botId}_{tid}",
                Name = $"[Bot] {t.Name.Split(' ')[0]}",
                Side = "Enemy",
                MaxHp = hp,
                Hp = hp,
                Atk = (int)(t.BaseAtk * scale),
                Def = (int)(t.BaseDef * scale),
                Speed = t.BaseSpeed - 2,
                SkillIds = [.. t.SkillIds]
            });
        }

        return units;
    }
}
