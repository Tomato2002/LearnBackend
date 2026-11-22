using System.Collections.Frozen;
using System.Text.Json;
using LearnBackend.Modules.MasterData.Models;

namespace LearnBackend.Modules.MasterData;

/// <summary>
/// Singleton giữ toàn bộ dữ liệu tĩnh của game trên RAM bằng FrozenDictionary O(1).
/// </summary>
public sealed class MasterDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GameRulesConfig GameRules { get; }
    public FrozenDictionary<string, HeroConfig> Heroes { get; }
    public FrozenDictionary<string, EnemyConfig> Enemies { get; }
    public FrozenDictionary<string, ItemConfig> Items { get; }
    public FrozenDictionary<string, StageConfig> Stages { get; }
    public FrozenDictionary<string, BannerConfig> Banners { get; }
    public FrozenDictionary<string, ShopItemConfig> ShopItems { get; }
    public FrozenDictionary<string, QuestConfig> Quests { get; }

    public MasterDataStore(IWebHostEnvironment env)
    {
        var baseDir = Path.Combine(env.ContentRootPath, "Data", "MasterData");

        GameRules = LoadJson<GameRulesConfig>(Path.Combine(baseDir, "game_rules.json"));
        Heroes = LoadJson<List<HeroConfig>>(Path.Combine(baseDir, "heroes.json"))
            .ToFrozenDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        Enemies = LoadJson<List<EnemyConfig>>(Path.Combine(baseDir, "enemies.json"))
            .ToFrozenDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        Items = LoadJson<List<ItemConfig>>(Path.Combine(baseDir, "items.json"))
            .ToFrozenDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        Stages = LoadJson<List<StageConfig>>(Path.Combine(baseDir, "stages.json"))
            .ToFrozenDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        Banners = LoadJson<List<BannerConfig>>(Path.Combine(baseDir, "banners.json"))
            .ToFrozenDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
        ShopItems = LoadJson<List<ShopItemConfig>>(Path.Combine(baseDir, "shop_items.json"))
            .ToFrozenDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        Quests = LoadJson<List<QuestConfig>>(Path.Combine(baseDir, "quests.json"))
            .ToFrozenDictionary(q => q.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static T LoadJson<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Không tìm thấy file MasterData: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"File MasterData bị rỗng hoặc sai định dạng JSON: {filePath}");
    }
}
