using LearnBackend.Modules.Battle.Engine;

namespace LearnBackend.Tests;

public class TurnBasedCombatEngineTests
{
    [Fact]
    public void Simulate_WithSameSeedAndLuaScripts_Is100PercentDeterministic()
    {
        var runner = new LuaSkillRunner(CreateDiskScriptProvider());
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
        var runner = new LuaSkillRunner(CreateDiskScriptProvider());
        var engine = new TurnBasedCombatEngine(runner);

        const int seed = 999888;
        var expected = engine.Simulate(CreateHeroes(), CreateEnemies(), seed);

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

    private static ISkillScriptProvider CreateDiskScriptProvider()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var scriptsDir = Path.Combine(repoRoot, "Data", "Scripts", "Skills");
        return new DirectorySkillScriptProvider(scriptsDir);
    }

    private static List<CombatUnit> CreateHeroes() =>
    [
        new CombatUnit("h1", "Arthur", "Hero", 1200, 1200, 150, 100, 105, ["skill_slash", "skill_holy_shield"]),
        new CombatUnit("h2", "Ignis", "Hero", 980, 980, 195, 75, 118, ["skill_fireball", "skill_phoenix_rebirth"])
    ];

    private static List<CombatUnit> CreateEnemies() =>
    [
        new CombatUnit("e1", "Goblin", "Enemy", 600, 600, 85, 35, 95, ["enemy_bite"])
    ];

    private sealed class DirectorySkillScriptProvider(string scriptsDir) : ISkillScriptProvider
    {
        public IReadOnlyDictionary<string, string> LoadAllScripts()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(scriptsDir, "*.lua"))
            {
                dict[Path.GetFileNameWithoutExtension(file)] = File.ReadAllText(file);
            }
            return dict;
        }
    }
}
