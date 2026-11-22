using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using LearnBackend.Common.Idempotency;
using LearnBackend.Common.Locking;
using LearnBackend.Common.Middleware;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Auth;
using LearnBackend.Modules.Battle;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Hero;
using LearnBackend.Modules.Inventory;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        var payload = JsonSerializer.Serialize(new
        {
            success = false,
            errorCode = "RATE_LIMITED",
            message = "Bạn gửi yêu cầu quá nhanh, vui lòng thử lại sau giây lát.",
            serverTimeUtc = DateTime.UtcNow
        });
        await context.HttpContext.Response.WriteAsync(payload, ct);
    };

    // Chặt hơn cho Auth (15 req / phút / IP)
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Toàn cục cho các endpoint khác (120 req / phút / IP)
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

// 6. Combat Engine + Sandboxed Lua Skill Runner (MoonSharp)
builder.Services.AddSingleton<LuaSkillRunner>();
builder.Services.AddSingleton<TurnBasedCombatEngine>();

// 7. Game Modules
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<HeroService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<BattleService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Tự động tạo file SQLite & làm mới schema dev nếu DB cũ chưa có bảng mới nhất (RefreshTokens)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    db.Database.EnsureCreated();

    var conn = db.Database.GetDbConnection();
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RefreshTokens';";
    var hasLatestSchema = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    conn.Close();

    if (!hasLatestSchema)
    {
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }
}

// 8. HTTP Pipeline (Thứ tự middleware cực kỳ quan trọng!)
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

// Phục vụ giao diện game test HTML trong thư mục wwwroot/ (mở tại http://localhost:5098/)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Thứ tự vàng: Khóa theo PlayerId trước -> rồi mới kiểm tra Idempotency Key!
app.UseMiddleware<PlayerLockMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
