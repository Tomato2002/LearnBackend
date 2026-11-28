using System.Text.Json;
using LearnBackend.Hubs;
using LearnBackend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Infrastructure.Outbox;

/// <summary>
/// BackgroundService dùng PeriodicTimer (chuẩn .NET hiện đại) xử lý bảng OutboxMessages mỗi 2 giây.
/// Tạo IServiceScope riêng cho mỗi vòng lặp để dùng DbContext đúng vòng đời Scoped.
/// </summary>
public sealed class OutboxProcessorWorker(
    IServiceScopeFactory scopeFactory,
    IGameRealtimeBroadcaster broadcaster,
    ILogger<OutboxProcessorWorker> logger) : BackgroundService
{
    private const int MaxRetries = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessPendingBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi ngoài dự kiến khi chạy OutboxProcessorWorker");
            }
        }
    }

    public async Task<int> ProcessPendingBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(20)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var processedCount = 0;
        foreach (var msg in pending)
        {
            try
            {
                await DispatchAsync(msg, ct);
                msg.ProcessedAtUtc = DateTime.UtcNow;
                msg.LastError = null;
                processedCount++;
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.LastError = ex.Message.Length > 250 ? ex.Message[..250] : ex.Message;
                logger.LogWarning(ex, "Outbox message {MessageId} lỗi lần {RetryCount}", msg.Id, msg.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
        return processedCount;
    }

    private async Task DispatchAsync(OutboxMessage msg, CancellationToken ct)
    {
        if (string.Equals(msg.Type, "BroadcastAnnouncement", StringComparison.OrdinalIgnoreCase))
        {
            var payload = JsonSerializer.Deserialize<BroadcastAnnouncementPayload>(msg.PayloadJson);
            if (payload is not null)
            {
                await broadcaster.BroadcastAnnouncementAsync(payload.Category, payload.Message, ct);
            }
        }
    }
}
