using System.ComponentModel.DataAnnotations;

namespace LearnBackend.Modules.Auth.DTOs;

public sealed class RegisterRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(32)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [MinLength(6)]
    [MaxLength(64)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [MinLength(2)]
    [MaxLength(32)]
    public string Nickname { get; init; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Đăng nhập nhanh không cần mật khẩu bằng DeviceId (chuẩn Mobile Game như Nakama).
/// </summary>
public sealed class GuestLoginRequest
{
    [Required]
    [MinLength(6)]
    [MaxLength(128)]
    public string DeviceId { get; init; } = string.Empty;

    [MaxLength(32)]
    public string? Nickname { get; init; }
}

/// <summary>
/// Liên kết tài khoản Guest với Username/Password để đồng bộ đa thiết bị giữ nguyên tiến độ.
/// </summary>
public sealed class LinkAccountRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(32)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [MinLength(6)]
    [MaxLength(64)]
    public string Password { get; init; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Guid PlayerId,
    string Nickname,
    string Role,
    bool IsGuest);
