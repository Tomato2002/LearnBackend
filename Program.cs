using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using LearnBackend.Common.DTOs;
using LearnBackend.Common.Events;
using LearnBackend.Common.Idempotency;
using LearnBackend.Common.Locking;
using LearnBackend.Common.Middleware;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Auth;
using LearnBackend.Modules.Battle;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Gacha;
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

var builder = WebApplication.CreateBuilder(args);

// 1. Database (SQLite - sau này đổi sang PostgreSQL chỉ cần thay UseSqlite -> UseNpgsql)
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GameDb")));

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

// 5. Common & Infrastructure Services
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<MasterDataStore>();
builder.Services.AddSingleton<IPlayerLockManager, InMemoryPlayerLockManager>();
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JwtTokenGenerator>();

// 6. Internal Domain Event Bus + Event Handlers (Decoupled Architecture)
builder.Services.AddScoped<IGameEventBus, InProcessGameEventBus>();
builder.Services.AddScoped<IGameEventHandler<StageClearedEvent>, QuestEventHandlers>();
builder.Services.AddScoped<IGameEventHandler<GachaSummonedEvent>, QuestEventHandlers>();
builder.Services.AddScoped<IGameEventHandler<ShopPurchasedEvent>, QuestEventHandlers>();

// 7. Combat Engine + Sandboxed Lua Skill Runner (MoonSharp)
builder.Services.AddSingleton<LuaSkillRunner>();
builder.Services.AddSingleton<TurnBasedCombatEngine>();

// 8. Game Modules (Chặng 1 + 2 + 3)
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<HeroService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<BattleService>();
builder.Services.AddScoped<GachaService>();
builder.Services.AddScoped<ShopService>();
builder.Services.AddScoped<QuestService>();
builder.Services.AddScoped<MailService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Tự động tạo file SQLite, nâng cấp schema dev nếu cần, và seed 1 thư Global Mail đền bù mẫu
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    db.Database.EnsureCreated();

    var conn = db.Database.GetDbConnection();
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='GlobalMails';";
    var hasLatestSchema = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    conn.Close();

    if (!hasLatestSchema)
    {
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    if (!db.GlobalMails.Any())
    {
        var nowUtc = DateTime.UtcNow;
        db.GlobalMails.AddRange(
            new GlobalMail
            {
                Id = Guid.NewGuid(),
                Title = "🎁 Quà Chào Mừng Mở Server (Chặng 3)",
                Body = "Cảm ơn bạn đã tham gia thử nghiệm! Tặng bạn 1.000 Gems (đủ quay 10 lần Gacha kích hoạt bảo hiểm 5★) và 1.000 Gold.",
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
                Body = "Server vừa nâng cấp hệ thống Event Bus, Gacha Pity và Cửa Hàng ngày. Chúc bạn chơi game vui vẻ!",
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

// 9. HTTP Pipeline (Thứ tự middleware cực kỳ quan trọng!)
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

app.Run();
