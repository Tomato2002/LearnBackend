namespace LearnBackend.Modules.Auth.Entities;

/// <summary>
/// Bảng tài khoản đăng nhập (tách khỏi PlayerProfile để đảm bảo bảo mật và dễ mở rộng nhiều phương thức đăng nhập).
/// </summary>
public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
