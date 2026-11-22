namespace LearnBackend.Modules.Auth.Entities;

/// <summary>
/// Refresh Token lưu dạng SHA-256 Hash trong DB (không bao giờ lưu chuỗi token gốc).
/// Cho phép gia hạn phiên chơi dài ngày (14 ngày) và thu hồi ngay lập tức khi Đăng xuất hoặc Ban tài khoản.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}
