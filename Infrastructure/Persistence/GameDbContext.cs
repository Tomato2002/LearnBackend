using LearnBackend.Infrastructure.Outbox;
using LearnBackend.Modules.Arena.Entities;
using LearnBackend.Modules.Auth.Entities;
using LearnBackend.Modules.Gacha.Entities;
using LearnBackend.Modules.Guild.Entities;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.LiveOps.Entities;
using LearnBackend.Modules.Mail.Entities;
using LearnBackend.Modules.Player.Entities;
using LearnBackend.Modules.Quest.Entities;
using LearnBackend.Modules.Shop.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Infrastructure.Persistence;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PlayerProfile> Players => Set<PlayerProfile>();
    public DbSet<CurrencyLedger> CurrencyLedgers => Set<CurrencyLedger>();
    public DbSet<PlayerHero> PlayerHeroes => Set<PlayerHero>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<EquipmentInstance> Equipments => Set<EquipmentInstance>();

    // Stage 3 Meta Tables
    public DbSet<PlayerGachaPity> PlayerGachaPities => Set<PlayerGachaPity>();
    public DbSet<GachaHistory> GachaHistories => Set<GachaHistory>();
    public DbSet<PlayerShopPurchase> PlayerShopPurchases => Set<PlayerShopPurchase>();
    public DbSet<PlayerQuestProgress> PlayerQuestProgresses => Set<PlayerQuestProgress>();
    public DbSet<GlobalMail> GlobalMails => Set<GlobalMail>();
    public DbSet<PlayerGlobalMailClaim> PlayerGlobalMailClaims => Set<PlayerGlobalMailClaim>();

    // Stage 4A Social & PvP Tables
    public DbSet<ArenaMatchHistory> ArenaMatchHistories => Set<ArenaMatchHistory>();
    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildMember> GuildMembers => Set<GuildMember>();

    // Stage 5 LiveOps Retention & Transactional Outbox Tables
    public DbSet<PlayerDailyBonus> PlayerDailyBonuses => Set<PlayerDailyBonus>();
    public DbSet<PlayerHourlyBonus> PlayerHourlyBonuses => Set<PlayerHourlyBonus>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(32);
            entity.Property(x => x.NormalizedUsername).HasMaxLength(32);
            entity.Property(x => x.PasswordHash).HasMaxLength(256);
            entity.Property(x => x.DeviceId).HasMaxLength(128);
            entity.Property(x => x.Role).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
            entity.HasIndex(x => x.DeviceId).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(256);
            entity.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.UserAccountId);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerProfile>(entity =>
        {
            entity.ToTable("Players", t =>
                t.HasCheckConstraint(
                    "CK_Players_NonNegativeBalances",
                    "\"Gold\" >= 0 AND \"Gems\" >= 0 AND \"StoredStamina\" >= 0 AND \"Level\" >= 1"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Nickname).HasMaxLength(32).IsRequired();
            entity.Property(x => x.WalletVersion).IsConcurrencyToken();
            entity.HasIndex(x => x.UserAccountId).IsUnique();
            entity.HasIndex(x => x.Nickname).IsUnique();
            entity.HasIndex(x => x.ArenaRating);
            entity.HasOne<UserAccount>()
                .WithOne()
                .HasForeignKey<PlayerProfile>(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Bảng Append-Only Audit Log (cố tình KHÔNG gắn Foreign Key để đạt tốc độ ghi cao & dễ Partition/Archive sau này)
        modelBuilder.Entity<CurrencyLedger>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CurrencyType).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ReferenceId).HasMaxLength(64);
            entity.HasIndex(x => new { x.PlayerId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<PlayerHero>(entity =>
        {
            entity.ToTable("PlayerHeroes", t =>
                t.HasCheckConstraint("CK_PlayerHeroes_ValidLevel", "\"Level\" >= 1"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.HeroTemplateId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.HeroTemplateId }).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("InventoryItems", t =>
                t.HasCheckConstraint("CK_InventoryItems_NonNegativeQty", "\"Quantity\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.ItemId }).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EquipmentInstance>(entity =>
        {
            entity.ToTable("Equipments", t =>
                t.HasCheckConstraint("CK_Equipments_NonNegativeEnhance", "\"EnhanceLevel\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TemplateId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.PlayerId);
            entity.HasIndex(x => x.EquippedHeroId);
            entity.HasOne<PlayerProfile>()
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PlayerHero>()
                .WithMany()
                .HasForeignKey(x => x.EquippedHeroId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlayerGachaPity>(entity =>
        {
            entity.ToTable("PlayerGachaPities", t =>
                t.HasCheckConstraint("CK_PlayerGachaPities_NonNegative", "\"PityCounter\" >= 0 AND \"TotalPulls\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BannerId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.BannerId }).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Append-Only Audit Log (không FK để tối ưu ghi & Partition theo CreatedAtUtc)
        modelBuilder.Entity<GachaHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BannerId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.HeroTemplateId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<PlayerShopPurchase>(entity =>
        {
            entity.ToTable("PlayerShopPurchases", t =>
                t.HasCheckConstraint("CK_PlayerShopPurchases_NonNegative", "\"PurchasedCount\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ShopItemId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.ShopItemId, x.PeriodKey }).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerQuestProgress>(entity =>
        {
            entity.ToTable("PlayerQuestProgresses", t =>
                t.HasCheckConstraint("CK_PlayerQuestProgresses_NonNegative", "\"CurrentProgress\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuestId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.QuestId, x.PeriodKey }).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GlobalMail>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(512).IsRequired();
            entity.Property(x => x.RewardItemId).HasMaxLength(64);
            entity.HasIndex(x => x.ExpiresAtUtc);
        });

        modelBuilder.Entity<PlayerGlobalMailClaim>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PlayerId, x.GlobalMailId }).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GlobalMail>()
                .WithMany()
                .HasForeignKey(x => x.GlobalMailId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Append-Only Audit Log (không FK để tối ưu ghi & Partition theo CreatedAtUtc)
        modelBuilder.Entity<ArenaMatchHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DefenderName).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.AttackerPlayerId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<Guild>(entity =>
        {
            entity.ToTable("Guilds", t =>
                t.HasCheckConstraint(
                    "CK_Guilds_ValidBossHp",
                    "\"BossCurrentHp\" >= 0 AND \"BossMaxHp\" > 0 AND \"BossLevel\" >= 1"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BossName).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<GuildMember>(entity =>
        {
            entity.ToTable("GuildMembers", t =>
                t.HasCheckConstraint("CK_GuildMembers_NonNegativeDmg", "\"TotalBossDamage\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.PlayerId).IsUnique(); // Mỗi người chơi chỉ vào 1 Bang
            entity.HasIndex(x => x.GuildId);
            entity.HasOne<Guild>()
                .WithMany()
                .HasForeignKey(x => x.GuildId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PlayerProfile>()
                .WithOne()
                .HasForeignKey<GuildMember>(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerDailyBonus>(entity =>
        {
            entity.ToTable("PlayerDailyBonuses", t =>
                t.HasCheckConstraint("CK_PlayerDailyBonuses_ValidStreak", "\"CurrentStreakDay\" >= 0 AND \"CurrentStreakDay\" <= 7"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LastClaimedDateUtc).HasMaxLength(16);
            entity.HasIndex(x => x.PlayerId).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithOne()
                .HasForeignKey<PlayerDailyBonus>(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerHourlyBonus>(entity =>
        {
            entity.ToTable("PlayerHourlyBonuses", t =>
                t.HasCheckConstraint("CK_PlayerHourlyBonuses_NonNegative", "\"TotalClaims\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PlayerId).IsUnique();
            entity.HasOne<PlayerProfile>()
                .WithOne()
                .HasForeignKey<PlayerHourlyBonus>(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
