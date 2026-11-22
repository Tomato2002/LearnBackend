using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.Mail.Entities;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Mail;

public sealed record MailItemDto(
    Guid Id,
    string Title,
    string Body,
    long RewardGold,
    long RewardGems,
    string? RewardItemId,
    int RewardItemQty,
    bool IsClaimed,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);

public sealed class MailService(GameDbContext db)
{
    public async Task<List<MailItemDto>> GetInboxAsync(Guid playerId, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;

        var activeMails = await db.GlobalMails
            .AsNoTracking()
            .Where(m => m.ExpiresAtUtc > nowUtc)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(ct);

        var claimedIds = (await db.PlayerGlobalMailClaims
            .AsNoTracking()
            .Where(c => c.PlayerId == playerId)
            .Select(c => c.GlobalMailId)
            .ToListAsync(ct))
            .ToHashSet();

        return activeMails.Select(m => new MailItemDto(
            m.Id,
            m.Title,
            m.Body,
            m.RewardGold,
            m.RewardGems,
            m.RewardItemId,
            m.RewardItemQty,
            IsClaimed: claimedIds.Contains(m.Id),
            m.CreatedAtUtc,
            m.ExpiresAtUtc)).ToList();
    }

    public async Task<MailItemDto> ClaimAsync(Guid playerId, Guid mailId, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var mail = await db.GlobalMails
            .FirstOrDefaultAsync(m => m.Id == mailId, ct)
            ?? throw new GameException("MAIL_NOT_FOUND", "Không tìm thấy thư này.", HttpStatusCode.NotFound);

        if (mail.ExpiresAtUtc <= nowUtc)
        {
            throw new GameException("MAIL_EXPIRED", "Thư này đã hết hạn nhận quà.");
        }

        var alreadyClaimed = await db.PlayerGlobalMailClaims
            .AnyAsync(c => c.PlayerId == playerId && c.GlobalMailId == mailId, ct);

        if (alreadyClaimed)
        {
            throw new GameException("MAIL_ALREADY_CLAIMED", "Bạn đã nhận quà từ thư này rồi.");
        }

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        db.PlayerGlobalMailClaims.Add(new PlayerGlobalMailClaim
        {
            PlayerId = playerId,
            GlobalMailId = mail.Id,
            ClaimedAtUtc = nowUtc
        });

        if (mail.RewardGold > 0)
        {
            player.Gold += mail.RewardGold;
            db.CurrencyLedgers.Add(new CurrencyLedger
            {
                PlayerId = playerId,
                CurrencyType = "Gold",
                Delta = mail.RewardGold,
                BalanceAfter = player.Gold,
                Reason = "GlobalMailClaim",
                ReferenceId = mail.Id.ToString("N")[..8],
                CreatedAtUtc = nowUtc
            });
        }

        if (mail.RewardGems > 0)
        {
            player.Gems += mail.RewardGems;
            db.CurrencyLedgers.Add(new CurrencyLedger
            {
                PlayerId = playerId,
                CurrencyType = "Gems",
                Delta = mail.RewardGems,
                BalanceAfter = player.Gems,
                Reason = "GlobalMailClaim",
                ReferenceId = mail.Id.ToString("N")[..8],
                CreatedAtUtc = nowUtc
            });
        }

        if (!string.IsNullOrEmpty(mail.RewardItemId) && mail.RewardItemQty > 0)
        {
            var inv = await db.InventoryItems
                .FirstOrDefaultAsync(i => i.PlayerId == playerId && i.ItemId == mail.RewardItemId, ct);
            if (inv is null)
                db.InventoryItems.Add(new InventoryItem { PlayerId = playerId, ItemId = mail.RewardItemId, Quantity = mail.RewardItemQty });
            else
                inv.Quantity += mail.RewardItemQty;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new MailItemDto(
            mail.Id,
            mail.Title,
            mail.Body,
            mail.RewardGold,
            mail.RewardGems,
            mail.RewardItemId,
            mail.RewardItemQty,
            IsClaimed: true,
            mail.CreatedAtUtc,
            mail.ExpiresAtUtc);
    }
}
