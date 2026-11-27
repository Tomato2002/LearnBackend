using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.DTOs;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Player;

public sealed class PlayerService(GameDbContext db, MasterDataStore masterData)
{
    public async Task<PlayerProfileResponse> GetProfileAsync(Guid playerId, CancellationToken ct)
    {
        var player = await GetPlayerOrThrowAsync(playerId, ct);
        var nowUtc = DateTime.UtcNow;
        var stamina = player.ComputeStamina(masterData.GameRules, nowUtc);

        return ToResponse(player, stamina, nowUtc);
    }

    public async Task<PlayerProfileResponse> RefillStaminaAsync(Guid playerId, CancellationToken ct)
    {
        var rules = masterData.GameRules;
        var nowUtc = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var player = await GetPlayerOrThrowAsync(playerId, ct);
        var ledger = player.RefillStaminaWithGems(rules, nowUtc);
        db.CurrencyLedgers.Add(ledger);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToResponse(player, player.ComputeStamina(rules, nowUtc), nowUtc);
    }

    public async Task<PlayerProfileResponse> SpendStaminaAsync(Guid playerId, int amount, CancellationToken ct)
    {
        var rules = masterData.GameRules;
        var nowUtc = DateTime.UtcNow;

        var player = await GetPlayerOrThrowAsync(playerId, ct);
        var finalStamina = player.SpendStamina(amount, rules, nowUtc);

        await db.SaveChangesAsync(ct);
        return ToResponse(player, finalStamina, nowUtc);
    }

    public async Task<List<CurrencyLedgerDto>> GetCurrencyLedgerAsync(Guid playerId, int limit, CancellationToken ct)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);

        return await db.CurrencyLedgers
            .AsNoTracking()
            .Where(x => x.PlayerId == playerId)
            .OrderByDescending(x => x.Id)
            .Take(safeLimit)
            .Select(x => new CurrencyLedgerDto(
                x.Id,
                x.CurrencyType,
                x.Delta,
                x.BalanceAfter,
                x.Reason,
                x.ReferenceId,
                x.CreatedAtUtc))
            .ToListAsync(ct);
    }

    private async Task<PlayerProfile> GetPlayerOrThrowAsync(Guid playerId, CancellationToken ct)
    {
        return await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy hồ sơ người chơi.", HttpStatusCode.NotFound);
    }

    private PlayerProfileResponse ToResponse(PlayerProfile player, StaminaCalculationResult stamina, DateTime nowUtc)
    {
        var rules = masterData.GameRules;
        return new PlayerProfileResponse(
            PlayerId: player.Id,
            Nickname: player.Nickname,
            Level: player.Level,
            Exp: player.Exp,
            ExpToNextLevel: rules.GetExpToNextLevel(player.Level),
            Gold: player.Gold,
            Gems: player.Gems,
            Stamina: new StaminaSnapshotDto(
                Current: stamina.CurrentStamina,
                Max: stamina.MaxStamina,
                NextRegenInSeconds: stamina.NextRegenInSeconds,
                RegenIntervalSeconds: rules.StaminaRegenIntervalSeconds),
            ServerTimeUtc: nowUtc);
    }
}
