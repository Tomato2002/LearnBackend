using System.Text;
using LearnBackend.Common.Locking;
using LearnBackend.Common.Middleware;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Auth;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Database (SQLite - sau này đổi sang PostgreSQL chỉ cần thay UseSqlite -> UseNpgsql)
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GameDb")));

// 2. Security & JWT Authentication
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Thiếu cấu hình Jwt trong appsettings.json.");

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

// 3. Common & Infrastructure Services
builder.Services.AddSingleton<MasterDataStore>();
builder.Services.AddSingleton<IPlayerLockManager, InMemoryPlayerLockManager>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JwtTokenGenerator>();

// 4. Game Modules
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Tự động tạo file SQLite & các bảng nếu chưa tồn tại (tiện cho giai đoạn dev)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    db.Database.EnsureCreated();
}

// 5. HTTP Pipeline (Thứ tự middleware cực kỳ quan trọng!)
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Đặt PlayerLockMiddleware SAU Authentication để đã đọc được PlayerId từ JWT
app.UseMiddleware<PlayerLockMiddleware>();

app.MapControllers();

app.Run();
