using LearnBackend.Modules.Battle.Engine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace LearnBackend.Tests;

public class TurnBasedCombatEngineTests
{
    [Fact]
    public void Simulate_WithSameSeedAndLuaScripts_Is100PercentDeterministic()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var env = new TestWebHostEnvironment(repoRoot);
        var runner = new LuaSkillRunner(env);
        var engine = new TurnBasedCombatEngine(runner);

        const int seed = 20261122;

        var simA = engine.Simulate(CreateHeroes(), CreateEnemies(), seed);
        var simB = engine.Simulate(CreateHeroes(), CreateEnemies(), seed);

        Assert.Equal(simA.IsVictory, simB.IsVictory);
        Assert.Equal(simA.TotalRounds, simB.TotalRounds);
        Assert.Equal(simA.Logs.Count, simB.Logs.Count);
        Assert.True(simA.Logs.Count > 0);

        for (var i = 0; i < simA.Logs.Count; i++)
        {
            Assert.Equal(simA.Logs[i], simB.Logs[i]);
        }
    }

    [Fact]
    public async Task Simulate_ConcurrentBattlesWithLuaVmPool_RemainsThreadSafeAndDeterministic()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var env = new TestWebHostEnvironment(repoRoot);
        var runner = new LuaSkillRunner(env);
        var engine = new TurnBasedCombatEngine(runner);

        const int seed = 999888;
        var expected = engine.Simulate(CreateHeroes(), CreateEnemies(), seed);

        // Chạy 20 trận chiến đấu Lua song song cùng lúc để kiểm chứng VM Object Pool an toàn đa luồng
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => engine.Simulate(CreateHeroes(), CreateEnemies(), seed)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var actual in results)
        {
            Assert.Equal(expected.IsVictory, actual.IsVictory);
            Assert.Equal(expected.TotalRounds, actual.TotalRounds);
            Assert.Equal(expected.Logs.Count, actual.Logs.Count);
        }
    }

    private static List<CombatUnit> CreateHeroes() =>
    [
        new CombatUnit
        {
            Id = "h1", Name = "Arthur", Side = "Hero",
            MaxHp = 1200, Hp = 1200, Atk = 150, Def = 100, Speed = 105,
            SkillIds = ["skill_slash", "skill_holy_shield"]
        },
        new CombatUnit
        {
            Id = "h2", Name = "Ignis", Side = "Hero",
            MaxHp = 980, Hp = 980, Atk = 195, Def = 75, Speed = 118,
            SkillIds = ["skill_fireball", "skill_phoenix_rebirth"]
        }
    ];

    private static List<CombatUnit> CreateEnemies() =>
    [
        new CombatUnit
        {
            Id = "e1", Name = "Goblin", Side = "Enemy",
            MaxHp = 600, Hp = 600, Atk = 85, Def = 35, Speed = 95,
            SkillIds = ["enemy_bite"]
        }
    ];

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LearnBackend.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
