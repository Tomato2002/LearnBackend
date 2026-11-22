namespace LearnBackend.Modules.MasterData.Models;

public sealed class BannerConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long CostGemsPerPull { get; init; } = 100;
    public double BaseFiveStarRate { get; init; } = 0.05;
    public double BaseFourStarRate { get; init; } = 0.25;
    public int SoftPityStart { get; init; } = 7;
    public double SoftPityRateStep { get; init; } = 0.15;
    public int HardPity { get; init; } = 10;
    public List<string> FiveStarHeroIds { get; init; } = [];
    public List<string> FourStarHeroIds { get; init; } = [];
    public List<string> ThreeStarHeroIds { get; init; } = [];
}

public sealed class ShopItemConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CurrencyType { get; init; } = "Gold"; // "Gold" | "Gems"
    public long Price { get; init; }
    public string RewardItemId { get; init; } = string.Empty;
    public int RewardQuantity { get; init; } = 1;
    public int DailyLimit { get; init; } = 1;
}

public sealed class QuestConfig
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty; // "StageCleared" | "GachaSummoned" | "ShopPurchased"
    public int TargetCount { get; init; } = 1;
    public long RewardGold { get; init; }
    public long RewardGems { get; init; }
    public string? RewardItemId { get; init; }
    public int RewardItemQty { get; init; }
}
