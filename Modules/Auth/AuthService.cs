using System.Net;
using LearnBackend.Common.Events;
using LearnBackend.Common.Exceptions;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Infrastructure.Security;
using LearnBackend.Modules.Auth.DTOs;
using LearnBackend.Modules.Auth.Entities;
using LearnBackend.Modules.MasterData;
using LearnBackend.Modules.Player.Entities;
using LearnBackend.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Modules.Auth;

/// <summary>
/// Module Auth độc lập 100% — KHÔNG tham chiếu Hero, Inventory hay bất kỳ logic đặc thù thể loại game nào.
/// Khi đăng ký thành công, Auth phát sự kiện PlayerRegisteredEvent để từng game tự tặng quà tân thủ của riêng mình.
/// </summary>
public sealed class AuthService(
    GameDbContext db,
    PasswordHasher passwordHasher,
    JwtTokenGenerator jwtTokenGenerator,
    MasterDataStore masterData,
    IGameEventBus eventBus)
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
            throw new GameException(GameErrorCodes.DuplicateUsername, "Tên đăng nhập đã tồn tại.", HttpStatusCode.Conflict);
        }

        var nicknameTaken = await db.Players
            .AnyAsync(p => p.Nickname == nickname, ct);
        if (nicknameTaken)
        {
            throw new GameException(GameErrorCodes.DuplicateNickname, "Tên nhân vật đã có người sử dụng.", HttpStatusCode.Conflict);
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

        var (rawRefresh, refreshHash, refreshExpiresAtUtc) = jwtTokenGenerator.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        // Phát sự kiện nội bộ trong cùng Transaction -> từng game tự tặng gói tân thủ (RpgStarterPackHandler)
        await eventBus.PublishAsync(new PlayerRegisteredEvent(player.Id, nowUtc), ct);

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
                GameErrorCodes.InvalidCredentials,
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

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        var tokenHash = JwtTokenGenerator.HashToken(request.RefreshToken.Trim());
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (existing is null || !existing.IsActive)
        {
            throw new GameException(
                GameErrorCodes.InvalidRefreshToken,
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
