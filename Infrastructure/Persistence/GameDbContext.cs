using LearnBackend.Modules.Auth.Entities;
using LearnBackend.Modules.Gacha.Entities;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(32).IsRequired();
            entity.Property(x => x.NormalizedUsername).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.UserAccountId);
        });

        modelBuilder.Entity<PlayerProfile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Nickname).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.UserAccountId).IsUnique();
            entity.HasIndex(x => x.Nickname).IsUnique();
        });

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
            entity.HasKey(x => x.Id);
            entity.Property(x => x.HeroTemplateId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.HeroTemplateId }).IsUnique();
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.ItemId }).IsUnique();
        });

        modelBuilder.Entity<EquipmentInstance>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TemplateId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.PlayerId);
            entity.HasIndex(x => x.EquippedHeroId);
        });

        modelBuilder.Entity<PlayerGachaPity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BannerId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.BannerId }).IsUnique();
        });

        modelBuilder.Entity<GachaHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BannerId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.HeroTemplateId).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<PlayerShopPurchase>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ShopItemId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.ShopItemId, x.PeriodKey }).IsUnique();
        });

        modelBuilder.Entity<PlayerQuestProgress>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuestId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.PlayerId, x.QuestId, x.PeriodKey }).IsUnique();
        });

        modelBuilder.Entity<GlobalMail>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(512).IsRequired();
            entity.Property(x => x.RewardItemId).HasMaxLength(64);
        });

        modelBuilder.Entity<PlayerGlobalMailClaim>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PlayerId, x.GlobalMailId }).IsUnique();
        });
    }
}
