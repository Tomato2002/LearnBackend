using LearnBackend.Common.Events;
using LearnBackend.Common.Middleware;
using LearnBackend.Hubs;
using LearnBackend.Infrastructure.DependencyInjection;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Auth.Entities;
using LearnBackend.Modules.LiveOps;
using LearnBackend.Modules.Mail.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Nền tảng dùng chung cho MỌI game (DB, JWT, RateLimit, Lock, Idempotency, Redis, SignalR, Outbox, Auth, Player, Mail)
var (usePostgres, useRedis) = builder.Services.AddGamePlatformCore(builder.Configuration);

// 2. Module LiveOps dùng chung cho MỌI game (Bootstrap, Hot Feature Flags, Maintenance, Daily 7-Day & Hourly Bonus)
builder.Services.AddLiveOpsModule();

// 3. Module đặc thù của thể loại Turn-Based RPG (khi làm game mới chỉ cần thay dòng này!)
builder.Services.AddRpgGameModules();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

app.Logger.LogInformation(
    "🚀 Game Server starting | Database={Database} | ScaleMode={ScaleMode}",
    usePostgres ? "PostgreSQL" : "SQLite (Local)",
    useRedis ? "Redis Distributed (Lock + Idempotency + ZSET + SignalR Backplane)" : "In-Memory (Single Node)");

// Khởi tạo schema DB, seed Global Mail mẫu & tài khoản GM (Development)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    db.Database.EnsureCreated();

    if (!usePostgres)
    {
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='OutboxMessages') +
                (SELECT COUNT(*) FROM pragma_table_info('UserAccounts') WHERE name='DeviceId');
            """;
        var hasLatestSchema = Convert.ToInt64(cmd.ExecuteScalar()) == 2;
        conn.Close();

        if (!hasLatestSchema)
        {
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }
    }

    if (!db.GlobalMails.Any())
    {
        var nowUtc = DateTime.UtcNow;
        db.GlobalMails.AddRange(
            new GlobalMail
            {
                Id = Guid.NewGuid(),
                Title = "🎁 Quà Chào Mừng Mở Server (LiveOps Platform)",
                Body = "Tặng bạn 1.000 Gems (đủ quay x10 Gacha CSPRNG) và 1.000 Gold. Hãy thử cả Điểm Danh 7 Ngày và Rương Quà Theo Giờ ở tab LiveOps!",
                RewardGold = 1000,
                RewardGems = 1000,
                RewardItemId = "stone_enhance",
                RewardItemQty = 5,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.AddDays(30)
            },
            new GlobalMail
            {
                Id = Guid.NewGuid(),
                Title = "🛠️ Hệ Thống Sẵn Sàng Tái Sử Dụng",
                Body = "Đã tích hợp Outbox Pattern, Guest DeviceId Login, RBAC Admin và Architecture Tests!",
                RewardGold = 500,
                RewardGems = 200,
                RewardItemId = "potion_exp_small",
                RewardItemQty = 5,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.AddDays(30)
            });
        db.SaveChanges();
    }

    // Seed sẵn tài khoản GameMaster (Role = "Admin") ở môi trường Development để test RBAC ngay
    if (app.Environment.IsDevelopment() && !db.UserAccounts.Any(u => u.Role == "Admin"))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var masterData = scope.ServiceProvider.GetRequiredService<MasterDataStore>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IGameEventBus>();
        var nowUtc = DateTime.UtcNow;

        var adminAccount = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            NormalizedUsername = "ADMIN",
            PasswordHash = hasher.Hash("Admin123!"),
            Role = "Admin",
            CreatedAtUtc = nowUtc,
            LastLoginAtUtc = nowUtc
        };
        var (adminPlayer, starterLedgers) = PlayerProfile.CreateNew(adminAccount.Id, "GameMaster", masterData.GameRules, nowUtc);
        db.UserAccounts.Add(adminAccount);
        db.Players.Add(adminPlayer);
        db.CurrencyLedgers.AddRange(starterLedgers);
        eventBus.PublishAsync(new PlayerRegisteredEvent(adminPlayer.Id, nowUtc), CancellationToken.None).GetAwaiter().GetResult();
        db.SaveChanges();

        app.Logger.LogInformation("👑 Seeded Dev GM Account: username=admin / password=Admin123! (Role=Admin)");
    }
}

// 4. HTTP & WebSocket Pipeline (Thứ tự chuẩn Production)
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();

// Cầu dao tổng LiveOps (chặn Maintenance Mode / Feature Flags bị tắt ngay trước khi xử lý nghiệp vụ)
app.UseMiddleware<LiveOpsGateMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<PlayerLockMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
