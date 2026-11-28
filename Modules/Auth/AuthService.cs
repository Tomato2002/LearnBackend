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
/// Module Auth độc lập 100% — hỗ trợ Username/Password, Guest DeviceId Login, Account Linking và RBAC Role.
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
            Role = "Player",
            CreatedAtUtc = nowUtc,
            LastLoginAtUtc = nowUtc
        };

        var (player, starterLedgers) = PlayerProfile.CreateNew(account.Id, nickname, rules, nowUtc);

        db.UserAccounts.Add(account);
        db.Players.Add(player);
        db.CurrencyLedgers.AddRange(starterLedgers);

        var (rawRefresh, refreshHash, refreshExpiresAtUtc) = jwtTokenGenerator.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await eventBus.PublishAsync(new PlayerRegisteredEvent(player.Id, nowUtc), ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, account.Username!, account.Role);
        return new AuthResponse(accessToken, accessExpiresAtUtc, rawRefresh, refreshExpiresAtUtc, player.Id, player.Nickname, account.Role, account.IsGuest);
    }

    /// <summary>
    /// Đăng nhập nhanh bằng DeviceId (học từ heroiclabs/nakama):
    /// Nếu thiết bị chưa có tài khoản -> tự tạo Guest Account + Player + Starter Pack trong 1 Transaction!
    /// </summary>
    public async Task<AuthResponse> GuestLoginAsync(GuestLoginRequest request, CancellationToken ct)
    {
        var deviceId = request.DeviceId.Trim();
        var nowUtc = DateTime.UtcNow;

        var existingAccount = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.DeviceId == deviceId, ct);

        if (existingAccount is not null)
        {
            var existingPlayer = await db.Players
                .FirstOrDefaultAsync(p => p.UserAccountId == existingAccount.Id, ct)
                ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy hồ sơ người chơi.", HttpStatusCode.NotFound);

            existingAccount.LastLoginAtUtc = nowUtc;
            var (rawRef, refHash, refExp) = jwtTokenGenerator.GenerateRefreshToken();
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserAccountId = existingAccount.Id,
                TokenHash = refHash,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = refExp
            });

            await db.SaveChangesAsync(ct);

            var identity = existingAccount.Username ?? $"guest:{deviceId[..Math.Min(8, deviceId.Length)]}";
            var (tok, exp) = jwtTokenGenerator.GenerateAccessToken(existingAccount.Id, existingPlayer.Id, identity, existingAccount.Role);
            return new AuthResponse(tok, exp, rawRef, refExp, existingPlayer.Id, existingPlayer.Nickname, existingAccount.Role, existingAccount.IsGuest);
        }

        var nickname = string.IsNullOrWhiteSpace(request.Nickname)
            ? $"Guest_{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
            : request.Nickname.Trim();

        if (await db.Players.AnyAsync(p => p.Nickname == nickname, ct))
        {
            nickname = $"{nickname}_{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            Role = "Player",
            CreatedAtUtc = nowUtc,
            LastLoginAtUtc = nowUtc
        };

        var (player, starterLedgers) = PlayerProfile.CreateNew(account.Id, nickname, masterData.GameRules, nowUtc);
        db.UserAccounts.Add(account);
        db.Players.Add(player);
        db.CurrencyLedgers.AddRange(starterLedgers);

        var (rawRefresh, refreshHash, refreshExpiresAtUtc) = jwtTokenGenerator.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await eventBus.PublishAsync(new PlayerRegisteredEvent(player.Id, nowUtc), ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, $"guest:{deviceId[..Math.Min(8, deviceId.Length)]}", account.Role);
        return new AuthResponse(accessToken, accessExpiresAtUtc, rawRefresh, refreshExpiresAtUtc, player.Id, player.Nickname, account.Role, account.IsGuest);
    }

    /// <summary>
    /// Liên kết tài khoản Guest hiện tại với Username + Password để đồng bộ đa thiết bị (giữ nguyên toàn bộ Hero/Vàng/Gems).
    /// </summary>
    public async Task<AuthResponse> LinkGuestAccountAsync(Guid playerId, LinkAccountRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var normalizedUsername = username.ToUpperInvariant();

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy người chơi.", HttpStatusCode.NotFound);

        var account = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == player.UserAccountId, ct)
            ?? throw new GameException("ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản.", HttpStatusCode.NotFound);

        if (!account.IsGuest)
        {
            throw new GameException("ALREADY_LINKED", "Tài khoản này đã được liên kết Username rồi.");
        }

        var usernameTaken = await db.UserAccounts.AnyAsync(u => u.NormalizedUsername == normalizedUsername, ct);
        if (usernameTaken)
        {
            throw new GameException(GameErrorCodes.DuplicateUsername, "Tên đăng nhập đã tồn tại.", HttpStatusCode.Conflict);
        }

        var nowUtc = DateTime.UtcNow;
        account.Username = username;
        account.NormalizedUsername = normalizedUsername;
        account.PasswordHash = passwordHasher.Hash(request.Password);

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

        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, account.Username, account.Role);
        return new AuthResponse(accessToken, accessExpiresAtUtc, rawRefresh, refreshExpiresAtUtc, player.Id, player.Nickname, account.Role, account.IsGuest);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalizedUsername = request.Username.Trim().ToUpperInvariant();

        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, ct);

        if (account is null || string.IsNullOrEmpty(account.PasswordHash) || !passwordHasher.Verify(request.Password, account.PasswordHash))
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

        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, account.Username!, account.Role);
        return new AuthResponse(accessToken, accessExpiresAtUtc, rawRefresh, refreshExpiresAtUtc, player.Id, player.Nickname, account.Role, account.IsGuest);
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

        var identity = account.Username ?? $"guest:{account.Id:N}";
        var (accessToken, accessExpiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(account.Id, player.Id, identity, account.Role);
        return new AuthResponse(accessToken, accessExpiresAtUtc, newRawRefresh, newRefreshExpiresAtUtc, player.Id, player.Nickname, account.Role, account.IsGuest);
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
