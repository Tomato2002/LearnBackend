namespace LearnBackend.Modules.Auth.Entities;

/// <summary>
/// Bảng tài khoản hỗ trợ chuẩn Mobile Game (học từ heroiclabs/nakama):
/// - Đăng nhập Guest bằng DeviceId (không cần nhập mật khẩu lúc mới tải game).
/// - Sau đó liên kết Username/Password (Account Linking) mà giữ nguyên 100% tiến độ.
/// - Phân quyền Role ("Player" | "Admin") cho cổng GM LiveOps.
/// </summary>
public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string? Username { get; set; }
    public string? NormalizedUsername { get; set; }
    public string? PasswordHash { get; set; }
    public string? DeviceId { get; set; }
    public string Role { get; set; } = "Player"; // "Player" | "Admin"
    public bool IsGuest => string.IsNullOrEmpty(NormalizedUsername);
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
