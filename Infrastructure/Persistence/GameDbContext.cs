using LearnBackend.Modules.Auth.Entities;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.Player.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnBackend.Infrastructure.Persistence;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<PlayerProfile> Players => Set<PlayerProfile>();
    public DbSet<CurrencyLedger> CurrencyLedgers => Set<CurrencyLedger>();
    public DbSet<PlayerHero> PlayerHeroes => Set<PlayerHero>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<EquipmentInstance> Equipments => Set<EquipmentInstance>();

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
    }
}
