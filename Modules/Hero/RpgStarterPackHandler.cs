using LearnBackend.Common.Events;
using LearnBackend.Infrastructure.Persistence;
using LearnBackend.Modules.Hero.Entities;
using LearnBackend.Modules.Inventory.Entities;
using LearnBackend.Modules.MasterData;

namespace LearnBackend.Modules.Hero;

/// <summary>
/// Lắng nghe sự kiện PlayerRegisteredEvent để tặng đội hình tân thủ & trang bị riêng của thể loại RPG.
/// Nhờ tách ra đây, module Auth trở nên ĐỘC LẬP 100% và tái sử dụng được cho mọi thể loại game khác!
/// </summary>
public sealed class RpgStarterPackHandler(GameDbContext db, MasterDataStore masterData)
    : IGameEventHandler<PlayerRegisteredEvent>
{
    public Task HandleAsync(PlayerRegisteredEvent gameEvent, CancellationToken ct)
    {
        var playerId = gameEvent.PlayerId;
        var nowUtc = gameEvent.OccurredAtUtc;

        db.PlayerHeroes.AddRange(
            new PlayerHero
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                HeroTemplateId = "hero_arthur",
                Level = 1,
                TeamSlot = 1,
                AcquiredAtUtc = nowUtc
            },
            new PlayerHero
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                HeroTemplateId = "hero_lyra",
                Level = 1,
                TeamSlot = 2,
                AcquiredAtUtc = nowUtc
            },
            new PlayerHero
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                HeroTemplateId = "hero_ignis",
                Level = 1,
                TeamSlot = 3,
                AcquiredAtUtc = nowUtc
            });

        db.InventoryItems.AddRange(
            new InventoryItem { PlayerId = playerId, ItemId = "potion_exp_small", Quantity = 5 },
            new InventoryItem { PlayerId = playerId, ItemId = "stone_enhance", Quantity = 5 });

        if (masterData.Items.TryGetValue("sword_iron", out var starterSword))
        {
            db.Equipments.Add(new EquipmentInstance
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                TemplateId = starterSword.Id,
                EnhanceLevel = 0,
                BonusAtk = starterSword.BaseAtkBonus,
                BonusHp = starterSword.BaseHpBonus,
                BonusDef = starterSword.BaseDefBonus,
                EquippedHeroId = null,
                CreatedAtUtc = nowUtc
            });
        }

        return Task.CompletedTask;
    }
}
