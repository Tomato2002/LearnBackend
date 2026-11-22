using System.ComponentModel.DataAnnotations;

namespace LearnBackend.Modules.Guild.Entities;

/// <summary>
/// Bảng Bang Hội kèm trạng thái Guild Boss chung cho cả bang.
/// Dùng [ConcurrencyCheck] trên BossVersion (Optimistic Concurrency) để chống ghi đè sát thương
/// khi nhiều thành viên đánh Boss cùng lúc!
/// </summary>
public sealed class Guild
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LeaderPlayerId { get; set; }

    public int BossLevel { get; set; } = 1;
    public string BossName { get; set; } = "Hắc Long Cổ Đại (Guild Boss Lv.1)";
    public int BossMaxHp { get; set; } = 8000;
    public int BossCurrentHp { get; set; } = 8000;

    [ConcurrencyCheck]
    public int BossVersion { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class GuildMember
{
    public long Id { get; set; }
    public Guid GuildId { get; set; }
    public Guid PlayerId { get; set; }
    public string Role { get; set; } = "Member"; // "Leader" | "Member"
    public long TotalBossDamage { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}
