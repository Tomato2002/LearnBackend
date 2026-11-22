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
        var stamina = ComputeStamina(player, nowUtc);

        return ToResponse(player, stamina, nowUtc);
    }

    /// <summary>
    /// Dùng Gems để mua thêm Stamina (có Transaction + ghi CurrencyLedger chống gian lận).
    /// </summary>
    public async Task<PlayerProfileResponse> RefillStaminaAsync(Guid playerId, CancellationToken ct)
    {
        var rules = masterData.GameRules;
        var nowUtc = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var player = await GetPlayerOrThrowAsync(playerId, ct);
        if (player.Gems < rules.StaminaRefillGemCost)
        {
            throw new GameException(
                "INSUFFICIENT_GEMS",
                $"Cần {rules.StaminaRefillGemCost} Gems để hồi thể lực (hiện có {player.Gems}).");
        }

        // Đồng bộ lượng thể lực đã tự hồi trước khi cộng thêm
        var synced = SyncStamina(player, nowUtc);

        player.Gems -= rules.StaminaRefillGemCost;
        player.StoredStamina = synced.CurrentStamina + rules.StaminaRefillAmount;
        player.LastStaminaUpdatedAtUtc = player.StoredStamina >= synced.MaxStamina
            ? nowUtc
            : synced.UpdatedTimestampUtc;

        db.CurrencyLedgers.Add(new CurrencyLedger
        {
            PlayerId = player.Id,
            CurrencyType = "Gems",
            Delta = -rules.StaminaRefillGemCost,
            BalanceAfter = player.Gems,
            Reason = "StaminaRefill",
            CreatedAtUtc = nowUtc
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var finalStamina = ComputeStamina(player, nowUtc);
        return ToResponse(player, finalStamina, nowUtc);
    }

    /// <summary>
    /// Endpoint phụ trợ để bạn test trừ thể lực và quan sát cơ chế Lazy Stamina hồi lại theo thời gian.
    /// Sau này ở Chặng 2, BattleService sẽ gọi trực tiếp hàm này khi StartBattle.
    /// </summary>
    public async Task<PlayerProfileResponse> SpendStaminaAsync(Guid playerId, int amount, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var player = await GetPlayerOrThrowAsync(playerId, ct);
        var synced = SyncStamina(player, nowUtc);

        if (synced.CurrentStamina < amount)
        {
            throw new GameException(
                "INSUFFICIENT_STAMINA",
                $"Không đủ thể lực! Cần {amount}, hiện có {synced.CurrentStamina}.");
        }

        var wasAtOrAboveCap = synced.CurrentStamina >= synced.MaxStamina;
        player.StoredStamina = synced.CurrentStamina - amount;
        // Nếu trước đó đang đầy (hoặc tràn) thể lực và giờ tụt xuống dưới cap -> bắt đầu tính giờ hồi từ nowUtc
        player.LastStaminaUpdatedAtUtc = wasAtOrAboveCap && player.StoredStamina < synced.MaxStamina
            ? nowUtc
            : synced.UpdatedTimestampUtc;

        await db.SaveChangesAsync(ct);

        var finalStamina = ComputeStamina(player, nowUtc);
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

    private StaminaCalculationResult ComputeStamina(PlayerProfile player, DateTime nowUtc)
    {
        var rules = masterData.GameRules;
        return StaminaCalculator.Calculate(
            player.StoredStamina,
            player.LastStaminaUpdatedAtUtc,
            rules.GetMaxStaminaForLevel(player.Level),
            rules.StaminaRegenIntervalSeconds,
            nowUtc);
    }

    private StaminaCalculationResult SyncStamina(PlayerProfile player, DateTime nowUtc)
    {
        var result = ComputeStamina(player, nowUtc);
        player.StoredStamina = result.CurrentStamina;
        player.LastStaminaUpdatedAtUtc = result.UpdatedTimestampUtc;
        return result;
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
