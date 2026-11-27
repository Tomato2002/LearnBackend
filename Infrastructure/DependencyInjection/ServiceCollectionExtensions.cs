using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using LearnBackend.Common.Events;
using LearnBackend.Common.Idempotency;
using LearnBackend.Common.Locking;
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
using LearnBackend.Modules.LiveOps;
using LearnBackend.Modules.Mail;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player;
using LearnBackend.Modules.Quest;
using LearnBackend.Modules.Shop;
using LearnBackend.Shared.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace LearnBackend.Infrastructure.DependencyInjection;

/// <summary>
/// Tách đăng ký DI thành 3 khối rõ ràng để tái sử dụng cho mọi dự án game mới:
/// 1) AddGamePlatformCore()  -> TÁI SỬ DỤNG 100% cho mọi thể loại game (Auth, Wallet, Lock, Idempotency, Redis, SignalR, Mail).
/// 2) AddLiveOpsModule()     -> TÁI SỬ DỤNG 100% cho mọi game Online (Bootstrap, Feature Flags, Maintenance, Daily/Hourly Bonus).
/// 3) AddRpgGameModules()    -> Riêng cho thể loại Turn-Based RPG (chỉ cần thay hàm này khi sang làm Card / Idle / Slot / Tower Defense!).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static (bool UsePostgres, bool UseRedis) AddGamePlatformCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dbProvider = configuration["Database:Provider"] ?? "Sqlite";
        var usePostgres = string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);

        services.AddDbContext<GameDbContext>(options =>
        {
            if (usePostgres)
            {
                var pgConn = configuration.GetConnectionString("Postgres")
                    ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Postgres khi bật PostgreSQL.");
                options.UseNpgsql(pgConn);
            }
            else
            {
                options.UseSqlite(configuration.GetConnectionString("GameDb"));
            }
        });

        // Security & JWT Authentication
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Thiếu cấu hình Jwt trong appsettings.");

        if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey) || jwtOptions.SecretKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey phải dài tối thiểu 32 ký tự. Ở môi trường Production, hãy cấu hình qua biến môi trường Jwt__SecretKey.");
        }

        services
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

        services.AddAuthorization();

        // Rate Limiting
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                var payload = JsonSerializer.Serialize(
                    ApiResponse.Fail(GameErrorCodes.RateLimited, "Bạn gửi yêu cầu quá nhanh, vui lòng thử lại sau giây lát."),
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

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        services.AddMemoryCache();

        // Core Platform Services
        services.AddSingleton<MasterDataStore>();
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<JwtTokenGenerator>();
        services.AddScoped<IGameEventBus, InProcessGameEventBus>();

        // Scale-Out Infrastructure (In-Memory vs Redis)
        var redisConnectionString = configuration.GetConnectionString("Redis");
        var useRedis = !string.IsNullOrWhiteSpace(redisConnectionString);
        var signalRBuilder = services.AddSignalR();

        if (useRedis)
        {
            var multiplexer = ConnectionMultiplexer.Connect(redisConnectionString!);
            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            services.AddSingleton<IPlayerLockManager, RedisPlayerLockManager>();
            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
            services.AddScoped<IArenaLeaderboardStore, RedisArenaLeaderboardStore>();

            signalRBuilder.AddStackExchangeRedis(redisConnectionString!, o =>
            {
                o.Configuration.ChannelPrefix = RedisChannel.Literal("RpgServer");
            });
        }
        else
        {
            services.AddSingleton<IPlayerLockManager, InMemoryPlayerLockManager>();
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
            services.AddScoped<IArenaLeaderboardStore, DbArenaLeaderboardStore>();
        }

        services.AddSingleton<IGameRealtimeBroadcaster, SignalRGameBroadcaster>();

        // Core Reusable Modules (dùng cho mọi thể loại game)
        services.AddScoped<AuthService>();
        services.AddScoped<PlayerService>();
        services.AddScoped<MailService>();

        return (usePostgres, useRedis);
    }

    public static IServiceCollection AddLiveOpsModule(this IServiceCollection services)
    {
        services.AddSingleton<LiveOpsFeatureStore>();
        services.AddScoped<LiveOpsService>();
        return services;
    }

    public static IServiceCollection AddRpgGameModules(this IServiceCollection services)
    {
        // Combat Engine + Sandboxed Lua Skill Runner (MoonSharp VM Object Pool)
        services.AddSingleton<LuaSkillRunner>();
        services.AddSingleton<TurnBasedCombatEngine>();

        // RPG Gameplay & Meta Modules
        services.AddScoped<HeroService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<BattleService>();
        services.AddScoped<GachaService>();
        services.AddScoped<ShopService>();
        services.AddScoped<QuestService>();
        services.AddScoped<ArenaService>();
        services.AddScoped<GuildService>();

        // Domain Event Handlers (Cắm vào Event Bus mà không làm bẩn Auth / Battle / Gacha / Shop!)
        services.AddScoped<IGameEventHandler<PlayerRegisteredEvent>, RpgStarterPackHandler>();
        services.AddScoped<IGameEventHandler<StageClearedEvent>, QuestEventHandlers>();
        services.AddScoped<IGameEventHandler<GachaSummonedEvent>, QuestEventHandlers>();
        services.AddScoped<IGameEventHandler<ShopPurchasedEvent>, QuestEventHandlers>();

        return services;
    }
}
