namespace LearnBackend.Infrastructure.Outbox;

/// <summary>
/// Transactional Outbox Pattern (học từ kgrzybek/modular-monolith-with-ddd):
/// Lưu sự kiện tích hợp (Loa thông báo, Kafka, Push Notification) trong CÙNG 1 DB Transaction với nghiệp vụ.
/// Đảm bảo At-Least-Once Delivery: kể cả server sập ngay sau Commit, Worker khởi động lại vẫn gửi bù đủ 100%!
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;        // VD: "BroadcastAnnouncement"
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}

public sealed record BroadcastAnnouncementPayload(string Category, string Message);
