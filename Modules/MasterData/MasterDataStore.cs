using System.Collections.Frozen;
using System.Text.Json;
using LearnBackend.Modules.MasterData.Models;

namespace LearnBackend.Modules.MasterData;

/// <summary>
/// Singleton giữ toàn bộ dữ liệu tĩnh của game (Config Hero, Stage, Luật game...) trên RAM.
/// Dùng FrozenDictionary để đọc cực nhanh O(1), không bao giờ phải query DB cho dữ liệu tĩnh.
/// </summary>
public sealed class MasterDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GameRulesConfig GameRules { get; }
    public FrozenDictionary<string, HeroConfig> Heroes { get; }
    public FrozenDictionary<string, StageConfig> Stages { get; }

    public MasterDataStore(IWebHostEnvironment env)
    {
        var baseDir = Path.Combine(env.ContentRootPath, "Data", "MasterData");

        GameRules = LoadJson<GameRulesConfig>(Path.Combine(baseDir, "game_rules.json"));

        var heroes = LoadJson<List<HeroConfig>>(Path.Combine(baseDir, "heroes.json"));
        Heroes = heroes.ToFrozenDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);

        var stages = LoadJson<List<StageConfig>>(Path.Combine(baseDir, "stages.json"));
        Stages = stages.ToFrozenDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
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
