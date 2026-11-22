using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using LearnBackend.Common.DTOs;
using LearnBackend.Common.Events;
using LearnBackend.Common.Idempotency;
using LearnBackend.Common.Locking;
using LearnBackend.Common.Middleware;
using LearnBackend.Hubs;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Redis;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Arena;
using LearnBackend.Modules.Auth;
using LearnBackend.Modules.Battle;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Gacha;
using LearnBackend.Modules.Guild;
using LearnBackend.Modules.Hero;
using LearnBackend.Modules.Inventory;
using LearnBackend.Modules.Mail;
using LearnBackend.Modules.Mail.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player;
using LearnBackend.Modules.Quest;
using LearnBackend.Modules.Shop;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Provider Switching (SQLite mặc định cho local; đổi sang PostgreSQL khi chạy Docker/Production)
var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var usePostgres = string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<GameDbContext>(options =>
{
    if (usePostgres)
    {
        var pgConn = builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Postgres khi bật PostgreSQL.");
        options.UseNpgsql(pgConn);
    }
    else
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("GameDb"));
    }
});

// 2. Security & JWT Authentication (Fail-fast nếu thiếu hoặc SecretKey quá ngắn)
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Thiếu cấu hình Jwt trong appsettings.");

if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey) || jwtOptions.SecretKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey phải dài tối thiểu 32 ký tự. Ở môi trường Production, hãy cấu hình qua biến môi trường Jwt__SecretKey.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Cho phép SignalR WebSocket truyền JWT qua query string ?access_token=...
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// 3. Rate Limiting (Chống brute-force đăng nhập & spam API)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(
            ApiResponse.Fail("RATE_LIMITED", "Bạn gửi yêu cầu quá nhanh, vui lòng thử lại sau giây lát."),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.HttpContext.Response.WriteAsync(payload, ct);
    };

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// 4. Health Checks (Giám sát server + kết nối Database tại GET /health)
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// 5. Common, Infrastructure & Redis Scale-Out Switching (Chặng 4B!)
// Nếu cấu hình ConnectionStrings:Redis -> tự động bật Redis Distributed Lock, Redis Idempotency,
// Redis ZSET Leaderboard và SignalR Redis Backplane để chạy song song nhiều server!
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<MasterDataStore>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JwtTokenGenerator>();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var useRedis = !string.IsNullOrWhiteSpace(redisConnectionString);

var signalRBuilder = builder.Services.AddSignalR();

if (useRedis)
{
    var multiplexer = ConnectionMultiplexer.Connect(redisConnectionString!);
    builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
    builder.Services.AddSingleton<IPlayerLockManager, RedisPlayerLockManager>();
    builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
    builder.Services.AddScoped<IArenaLeaderboardStore, RedisArenaLeaderboardStore>();

    signalRBuilder.AddStackExchangeRedis(redisConnectionString!, o =>
    {
        o.Configuration.ChannelPrefix = RedisChannel.Literal("RpgServer");
    });
}
else
{
    builder.Services.AddSingleton<IPlayerLockManager, InMemoryPlayerLockManager>();
    builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
    builder.Services.AddScoped<IArenaLeaderboardStore, DbArenaLeaderboardStore>();
}

builder.Services.AddSingleton<IGameRealtimeBroadcaster, SignalRGameBroadcaster>();

// 6. Internal Domain Event Bus + Event Handlers (Decoupled Architecture)
builder.Services.AddScoped<IGameEventBus, InProcessGameEventBus>();
builder.Services.AddScoped<IGameEventHandler<StageClearedEvent>, QuestEventHandlers>();
builder.Services.AddScoped<IGameEventHandler<GachaSummonedEvent>, QuestEventHandlers>();
builder.Services.AddScoped<IGameEventHandler<ShopPurchasedEvent>, QuestEventHandlers>();

// 7. Combat Engine + Sandboxed Lua Skill Runner (MoonSharp VM Object Pool)
builder.Services.AddSingleton<LuaSkillRunner>();
builder.Services.AddSingleton<TurnBasedCombatEngine>();

// 8. Game Modules (Chặng 1 + 2 + 3 + 4A + 4B)
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<HeroService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<BattleService>();
builder.Services.AddScoped<GachaService>();
builder.Services.AddScoped<ShopService>();
builder.Services.AddScoped<QuestService>();
builder.Services.AddScoped<MailService>();
builder.Services.AddScoped<ArenaService>();
builder.Services.AddScoped<GuildService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Log rõ chế độ hạ tầng đang chạy
app.Logger.LogInformation(
    "🚀 Game Server starting | Database={Database} | ScaleMode={ScaleMode}",
    usePostgres ? "PostgreSQL" : "SQLite (Local)",
    useRedis ? "Redis Distributed (Lock + Idempotency + ZSET + SignalR Backplane)" : "In-Memory (Single Node)");

// Tự động tạo schema & seed 2 thư Global Mail mẫu
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    db.Database.EnsureCreated();

    if (!usePostgres)
    {
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Guilds';";
        var hasLatestSchema = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
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
                Title = "🎁 Quà Chào Mừng Mở Server (Chặng 4B)",
                Body = "Tặng bạn 1.000 Gems (đủ quay 10 lần Gacha kích hoạt bảo hiểm 5★ + phát loa SignalR!) và 1.000 Gold.",
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
                Title = "🛠️ Đền Bù Bảo Trì Định Kỳ",
                Body = "Server đã sẵn sàng scale lên 10k+ CCU với PostgreSQL, Redis Distributed Lock, ZSET Leaderboard và SignalR Backplane!",
                RewardGold = 500,
                RewardGems = 200,
                RewardItemId = "potion_exp_small",
                RewardItemQty = 5,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.AddDays(30)
            });
        db.SaveChanges();
    }
}

// 9. HTTP & WebSocket Pipeline (Thứ tự middleware cực kỳ quan trọng!)
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

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<PlayerLockMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
