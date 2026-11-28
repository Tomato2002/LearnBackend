using System.Text.Json;
using LearnBackend.Common.Events;
using LearnBackend.Hubs;
using LearnBackend.Infrastructure.Outbox;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Auth;
using LearnBackend.Modules.Auth.DTOs;
using LearnBackend.Modules.Hero;
using LearnBackend.Modules.MasterData;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LearnBackend.Tests;

public class OutboxAndAuthTests
{
    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LearnBackend.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeBroadcaster : IGameRealtimeBroadcaster
    {
        public bool ShouldFail { get; set; }
        public List<(string Category, string Message)> Sent { get; } = [];

        public Task BroadcastAnnouncementAsync(string category, string message, CancellationToken ct)
        {
            if (ShouldFail)
                throw new InvalidOperationException("Simulated SignalR/Redis outage");

            Sent.Add((category, message));
            return Task.CompletedTask;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Data")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục Data/ của LearnBackend.");
    }

    private static (SqliteConnection Conn, ServiceProvider Provider) BuildTestContainer(FakeBroadcaster broadcaster)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(FindRepoRoot()));
        services.AddDbContext<GameDbContext>(o => o.UseSqlite(conn));
        services.AddSingleton<MasterDataStore>();
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<IOptions<JwtOptions>>(Options.Create(new JwtOptions
        {
            Issuer = "LearnBackend.Test",
            Audience = "LearnBackend.Test",
            SecretKey = "SuperSecretUnitTestKey_Minimum32Chars_123456!",
            AccessTokenExpirationMinutes = 60,
            RefreshTokenExpirationDays = 7
        }));
        services.AddSingleton<JwtTokenGenerator>();
        services.AddScoped<IGameEventBus, InProcessGameEventBus>();
        services.AddScoped<IGameEventHandler<PlayerRegisteredEvent>, RpgStarterPackHandler>();
        services.AddScoped<AuthService>();
        services.AddSingleton<IGameRealtimeBroadcaster>(broadcaster);

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GameDbContext>().Database.EnsureCreated();
        }

        return (conn, provider);
    }

    [Fact]
    public async Task OutboxWorker_DispatchesPendingMessages_AndMarksProcessed()
    {
        var fakeBroadcaster = new FakeBroadcaster();
        var (conn, provider) = BuildTestContainer(fakeBroadcaster);
        await using var _ = conn;
        await using var __ = provider;

        var msgId = Guid.NewGuid();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = msgId,
                Type = "BroadcastAnnouncement",
                PayloadJson = JsonSerializer.Serialize(new BroadcastAnnouncementPayload("GACHA_5STAR", "Test 5★ Pull!")),
                OccurredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var worker = new OutboxProcessorWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fakeBroadcaster,
            NullLogger<OutboxProcessorWorker>.Instance);

        var processed = await worker.ProcessPendingBatchAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Single(fakeBroadcaster.Sent);
        Assert.Equal("GACHA_5STAR", fakeBroadcaster.Sent[0].Category);

        using (var verifyScope = provider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<GameDbContext>();
            var saved = await db.OutboxMessages.FindAsync(msgId);
            Assert.NotNull(saved!.ProcessedAtUtc);
            Assert.Equal(0, saved.RetryCount);
        }
    }

    [Fact]
    public async Task OutboxWorker_RetriesFailedMessage_ThenSucceedsWhenBroadcasterRecovers()
    {
        var fakeBroadcaster = new FakeBroadcaster { ShouldFail = true };
        var (conn, provider) = BuildTestContainer(fakeBroadcaster);
        await using var _ = conn;
        await using var __ = provider;

        var msgId = Guid.NewGuid();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = msgId,
                Type = "BroadcastAnnouncement",
                PayloadJson = JsonSerializer.Serialize(new BroadcastAnnouncementPayload("GUILD_BOSS_KILL", "Boss Down!")),
                OccurredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var worker = new OutboxProcessorWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fakeBroadcaster,
            NullLogger<OutboxProcessorWorker>.Instance);

        // Lần 1: SignalR sập -> chưa Processed, RetryCount tăng lên 1
        var firstRun = await worker.ProcessPendingBatchAsync(CancellationToken.None);
        Assert.Equal(0, firstRun);

        using (var checkScope = provider.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<GameDbContext>();
            var failed = await db.OutboxMessages.FindAsync(msgId);
            Assert.Null(failed!.ProcessedAtUtc);
            Assert.Equal(1, failed.RetryCount);
            Assert.Contains("Simulated SignalR/Redis outage", failed.LastError);
        }

        // Lần 2: SignalR hồi phục -> tự gửi bù thành công (At-Least-Once Delivery!)
        fakeBroadcaster.ShouldFail = false;
        var secondRun = await worker.ProcessPendingBatchAsync(CancellationToken.None);
        Assert.Equal(1, secondRun);
        Assert.Single(fakeBroadcaster.Sent);
    }

    [Fact]
    public async Task NakamaGuestFlow_CreatesGuest_IsIdempotent_AndLinksPreservingPlayerIdAndStarterPack()
    {
        var fakeBroadcaster = new FakeBroadcaster();
        var (conn, provider) = BuildTestContainer(fakeBroadcaster);
        await using var _ = conn;
        await using var __ = provider;

        using var scope = provider.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        // 1. Vào game lần đầu bằng DeviceId -> tự tạo tài khoản Guest + nhận 3 Hero tân thủ
        var first = await auth.GuestLoginAsync(new GuestLoginRequest { DeviceId = "ios-device-xyz-999", Nickname = "SpeedRunner" }, CancellationToken.None);
        Assert.True(first.IsGuest);
        Assert.Equal("Player", first.Role);

        var heroCountBeforeLink = await db.PlayerHeroes.CountAsync(h => h.PlayerId == first.PlayerId);
        Assert.Equal(3, heroCountBeforeLink);

        // 2. Mở game lại trên cùng thiết bị -> trả về đúng PlayerId cũ (Idempotent)
        var second = await auth.GuestLoginAsync(new GuestLoginRequest { DeviceId = "ios-device-xyz-999" }, CancellationToken.None);
        Assert.Equal(first.PlayerId, second.PlayerId);
        Assert.True(second.IsGuest);

        // 3. Liên kết tài khoản Guest với Username/Password -> không còn là Guest, giữ nguyên PlayerId & 3 Hero
        var linked = await auth.LinkGuestAccountAsync(first.PlayerId, new LinkAccountRequest
        {
            Username = "speed_pro",
            Password = "Password123!"
        }, CancellationToken.None);

        Assert.Equal(first.PlayerId, linked.PlayerId);
        Assert.False(linked.IsGuest);

        // 4. Đăng nhập trên máy khác bằng Username/Password mới liên kết -> vào đúng tài khoản cũ!
        var loginOtherDevice = await auth.LoginAsync(new LoginRequest
        {
            Username = "speed_pro",
            Password = "Password123!"
        }, CancellationToken.None);

        Assert.Equal(first.PlayerId, loginOtherDevice.PlayerId);
        Assert.Equal(3, await db.PlayerHeroes.CountAsync(h => h.PlayerId == loginOtherDevice.PlayerId));
    }
}
