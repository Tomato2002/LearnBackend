using LearnBackend.Common.Extensions;
using LearnBackend.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Hubs;

public interface IGameRealtimeBroadcaster
{
    Task BroadcastAnnouncementAsync(string category, string message, CancellationToken ct = default);
}

public sealed class SignalRGameBroadcaster(IHubContext<GameHub> hubContext) : IGameRealtimeBroadcaster
{
    public Task BroadcastAnnouncementAsync(string category, string message, CancellationToken ct = default)
    {
        return hubContext.Clients.All.SendAsync(
            "ReceiveAnnouncement",
            new
            {
                category,
                message,
                sentAtUtc = DateTime.UtcNow
            },
            ct);
    }
}

/// <summary>
/// SignalR WebSocket Hub (tích hợp sẵn trong ASP.NET Core):
/// - Chat Thế Giới thời gian thực.
/// - Nhận loa thông báo toàn server (nổ Gacha 5★, hạ Guild Boss...).
/// </summary>
[Authorize]
public sealed class GameHub(GameDbContext db) : Hub
{
    public async Task SendWorldChat(string message)
    {
        var trimmed = (message ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        if (trimmed.Length > 200) trimmed = trimmed[..200];

        var playerId = Context.User?.GetPlayerId()
            ?? throw new HubException("Chưa xác thực người chơi.");

        var nickname = await db.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.Nickname)
            .FirstOrDefaultAsync() ?? "Unknown";

        await Clients.All.SendAsync("ReceiveWorldChat", new
        {
            playerId,
            senderName = nickname,
            message = trimmed,
            sentAtUtc = DateTime.UtcNow
        });
    }
}
