using System.Collections.Concurrent;
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
/// SignalR WebSocket Hub tối ưu cho quy mô lớn (10k+ CCU):
/// - Chia Kênh Chat (Channel 1..10) bằng SignalR Groups thay vì Clients.All (tránh bão fan-out O(N^2)).
/// - Chống spam Chat bằng cooldown 2 giây / người chơi.
/// </summary>
[Authorize]
public sealed class GameHub(GameDbContext db) : Hub
{
    private const int MinChannel = 1;
    private const int MaxChannel = 10;
    private const string ChannelItemKey = "chat_channel_id";
    private static readonly TimeSpan ChatCooldown = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<Guid, DateTime> LastChatAtUtc = new();

    public override async Task OnConnectedAsync()
    {
        // Mặc định vào Kênh 1 khi vừa kết nối
        const int defaultChannel = 1;
        Context.Items[ChannelItemKey] = defaultChannel;
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(defaultChannel));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.User?.TryGetPlayerId(out var playerId) == true)
        {
            LastChatAtUtc.TryRemove(playerId, out _);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Chuyển kênh chat thế giới (Kênh 1..10) để tránh nghẽn băng thông khi server đông người.
    /// </summary>
    public async Task JoinChatChannel(int channelId)
    {
        var safeChannel = Math.Clamp(channelId, MinChannel, MaxChannel);

        if (Context.Items.TryGetValue(ChannelItemKey, out var oldObj) && oldObj is int oldChannel)
        {
            if (oldChannel == safeChannel) return;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(oldChannel));
        }

        Context.Items[ChannelItemKey] = safeChannel;
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(safeChannel));

        await Clients.Caller.SendAsync("ChannelJoined", new
        {
            channelId = safeChannel,
            message = $"Đã chuyển sang Kênh Thế Giới #{safeChannel}."
        });
    }

    public async Task SendWorldChat(string message)
    {
        var trimmed = (message ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        if (trimmed.Length > 200) trimmed = trimmed[..200];

        var playerId = Context.User?.GetPlayerId()
            ?? throw new HubException("Chưa xác thực người chơi.");

        // Chống spam chat (Rate Limit 1 tin / 2 giây / Player)
        var nowUtc = DateTime.UtcNow;
        if (LastChatAtUtc.TryGetValue(playerId, out var lastSent) && (nowUtc - lastSent) < ChatCooldown)
        {
            throw new HubException("CHAT_COOLDOWN: Bạn nhắn quá nhanh, vui lòng chờ 2 giây!");
        }
        LastChatAtUtc[playerId] = nowUtc;

        var channelId = Context.Items.TryGetValue(ChannelItemKey, out var chObj) && chObj is int ch ? ch : 1;

        var nickname = await db.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.Nickname)
            .FirstOrDefaultAsync() ?? "Unknown";

        // Chỉ gửi cho những người chơi cùng Kênh (Group) thay vì toàn bộ 10.000 kết nối!
        await Clients.Group(GetGroupName(channelId)).SendAsync("ReceiveWorldChat", new
        {
            playerId,
            channelId,
            senderName = nickname,
            message = trimmed,
            sentAtUtc = nowUtc
        });
    }

    private static string GetGroupName(int channelId) => $"world_ch_{channelId}";
}
