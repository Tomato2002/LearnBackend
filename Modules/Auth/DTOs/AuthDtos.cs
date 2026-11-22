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

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid PlayerId,
    string Nickname);
