using System.Net;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Auth.DTOs;
using LearnBackend.Modules.Auth.Entities;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Auth;

public sealed class AuthService(
    GameDbContext db,
    PasswordHasher passwordHasher,
    JwtTokenGenerator jwtTokenGenerator,
    MasterDataStore masterData)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var normalizedUsername = username.ToUpperInvariant();
        var nickname = request.Nickname.Trim();

        var usernameTaken = await db.UserAccounts
            .AnyAsync(u => u.NormalizedUsername == normalizedUsername, ct);
        if (usernameTaken)
        {
            throw new GameException("DUPLICATE_USERNAME", "Tên đăng nhập đã tồn tại.", HttpStatusCode.Conflict);
        }

        var nicknameTaken = await db.Players
            .AnyAsync(p => p.Nickname == nickname, ct);
        if (nicknameTaken)
        {
            throw new GameException("DUPLICATE_NICKNAME", "Tên nhân vật đã có người sử dụng.", HttpStatusCode.Conflict);
        }

        var nowUtc = DateTime.UtcNow;
        var rules = masterData.GameRules;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = normalizedUsername,
            PasswordHash = passwordHasher.Hash(request.Password),
            CreatedAtUtc = nowUtc,
            LastLoginAtUtc = nowUtc
        };

        var player = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            Nickname = nickname,
            Level = 1,
            Exp = 0,
            Gold = rules.StarterGold,
            Gems = rules.StarterGems,
            StoredStamina = rules.GetMaxStaminaForLevel(1),
            LastStaminaUpdatedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc
        };

        db.UserAccounts.Add(account);
        db.Players.Add(player);

        db.CurrencyLedgers.AddRange(
            new CurrencyLedger
            {
                PlayerId = player.Id,
                CurrencyType = "Gold",
                Delta = rules.StarterGold,
                BalanceAfter = player.Gold,
                Reason = "StarterBonus",
                CreatedAtUtc = nowUtc
            },
            new CurrencyLedger
            {
                PlayerId = player.Id,
                CurrencyType = "Gems",
                Delta = rules.StarterGems,
                BalanceAfter = player.Gems,
                Reason = "StarterBonus",
                CreatedAtUtc = nowUtc
            });

        // Tặng đội hình tân thủ (3 Hero ra trận) + nguyên liệu + 1 vũ khí mẫu
        db.PlayerHeroes.AddRange(
            new PlayerHero
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                HeroTemplateId = "hero_arthur",
                Level = 1,
                TeamSlot = 1,
                AcquiredAtUtc = nowUtc
            },
            new PlayerHero
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                HeroTemplateId = "hero_lyra",
                Level = 1,
                TeamSlot = 2,
                AcquiredAtUtc = nowUtc
            },
            new PlayerHero
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                HeroTemplateId = "hero_ignis",
                Level = 1,
                TeamSlot = 3,
                AcquiredAtUtc = nowUtc
            });

        db.InventoryItems.AddRange(
            new InventoryItem { PlayerId = player.Id, ItemId = "potion_exp_small", Quantity = 5 },
            new InventoryItem { PlayerId = player.Id, ItemId = "stone_enhance", Quantity = 5 });

        if (masterData.Items.TryGetValue("sword_iron", out var starterSword))
        {
            db.Equipments.Add(new EquipmentInstance
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                TemplateId = starterSword.Id,
                EnhanceLevel = 0,
                BonusAtk = starterSword.BaseAtkBonus,
                BonusHp = starterSword.BaseHpBonus,
                BonusDef = starterSword.BaseDefBonus,
                EquippedHeroId = null,
                CreatedAtUtc = nowUtc
            });
        }

        var (rawRefresh, refreshHash, refreshExpiresAtUtc) = jwtTokenGenerator.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, account.Username);
        return new AuthResponse(accessToken, accessExpiresAtUtc, rawRefresh, refreshExpiresAtUtc, player.Id, player.Nickname);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalizedUsername = request.Username.Trim().ToUpperInvariant();

        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, ct);

        if (account is null || !passwordHasher.Verify(request.Password, account.PasswordHash))
        {
            throw new GameException(
                "INVALID_CREDENTIALS",
                "Sai tên đăng nhập hoặc mật khẩu.",
                HttpStatusCode.Unauthorized);
        }

        var player = await db.Players
            .FirstOrDefaultAsync(p => p.UserAccountId == account.Id, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy hồ sơ người chơi.", HttpStatusCode.NotFound);

        var nowUtc = DateTime.UtcNow;
        account.LastLoginAtUtc = nowUtc;

        var (rawRefresh, refreshHash, refreshExpiresAtUtc) = jwtTokenGenerator.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await db.SaveChangesAsync(ct);

        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, account.Username);
        return new AuthResponse(accessToken, accessExpiresAtUtc, rawRefresh, refreshExpiresAtUtc, player.Id, player.Nickname);
    }

    /// <summary>
    /// Refresh Token Rotation: đổi Refresh Token cũ lấy cặp (AccessToken + RefreshToken) mới và vô hiệu hóa token cũ.
    /// </summary>
    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        var tokenHash = JwtTokenGenerator.HashToken(request.RefreshToken.Trim());
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (existing is null || !existing.IsActive)
        {
            throw new GameException(
                "INVALID_REFRESH_TOKEN",
                "Refresh token không hợp lệ, đã hết hạn hoặc đã bị thu hồi.",
                HttpStatusCode.Unauthorized);
        }

        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == existing.UserAccountId, ct)
            ?? throw new GameException("ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại.", HttpStatusCode.Unauthorized);

        var player = await db.Players
            .FirstOrDefaultAsync(p => p.UserAccountId == account.Id, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy hồ sơ người chơi.", HttpStatusCode.NotFound);

        var nowUtc = DateTime.UtcNow;
        var (newRawRefresh, newRefreshHash, newRefreshExpiresAtUtc) = jwtTokenGenerator.GenerateRefreshToken();

        // Thu hồi token cũ và ghi vết token thay thế (Rotation)
        existing.RevokedAtUtc = nowUtc;
        existing.ReplacedByTokenHash = newRefreshHash;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            TokenHash = newRefreshHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = newRefreshExpiresAtUtc
        });

        await db.SaveChangesAsync(ct);

        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, account.Username);
        return new AuthResponse(accessToken, accessExpiresAtUtc, newRawRefresh, newRefreshExpiresAtUtc, player.Id, player.Nickname);
    }

    /// <summary>
    /// Thu hồi Refresh Token khi người chơi đăng xuất.
    /// </summary>
    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        var tokenHash = JwtTokenGenerator.HashToken(request.RefreshToken.Trim());
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (existing is not null && existing.RevokedAtUtc is null)
        {
            existing.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
