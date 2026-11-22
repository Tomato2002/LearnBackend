using System.Collections.Concurrent;
using System.Collections.Frozen;
using LearnBackend.Modules.Battle.DTOs;
using MoonSharp.Interpreter;

namespace LearnBackend.Modules.Battle.Engine;

/// <summary>
/// Proxy chỉ đọc bọc quanh CombatUnit để truyền an toàn vào môi trường Lua.
/// Lua chỉ được đọc chỉ số, mọi thay đổi HP đều phải đi qua hàm của LuaBattleContext (C# kiểm soát).
/// </summary>
public sealed class LuaUnitProxy(CombatUnit unit)
{
    [MoonSharpHidden]
    public CombatUnit Inner => unit;

    public string name => unit.Name;
    public int hp => unit.Hp;
    public int max_hp => unit.MaxHp;
    public int atk => unit.Atk;
    public int def => unit.Def;
    public int speed => unit.Speed;
}

/// <summary>
/// Bộ API Sandbox mà C# cung cấp cho file script .lua gọi trong hàm on_cast(ctx).
/// Mọi phép tính phòng thủ, giới hạn máu và RNG theo Seed đều do C# kiểm soát 100%.
/// </summary>
public sealed class LuaBattleContext(
    int round,
    string skillId,
    CombatUnit caster,
    CombatUnit primaryTarget,
    List<CombatUnit> allies,
    Random seededRng,
    List<BattleTurnLogDto> logs)
{
    public LuaUnitProxy get_caster() => new(caster);

    public LuaUnitProxy get_target() => new(primaryTarget);

    public LuaUnitProxy get_lowest_hp_ally()
    {
        var lowest = allies
            .Where(a => a.IsAlive)
            .OrderBy(a => (double)a.Hp / Math.Max(1, a.MaxHp))
            .FirstOrDefault() ?? caster;
        return new LuaUnitProxy(lowest);
    }

    public int random_int(int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive) (minInclusive, maxInclusive) = (maxInclusive, minInclusive);
        return seededRng.Next(minInclusive, maxInclusive + 1);
    }

    public int deal_damage(LuaUnitProxy targetProxy, int rawPower, string label)
    {
        var target = targetProxy.Inner;
        var mitigation = 100.0 / (100.0 + Math.Max(0, target.Def));
        var finalDamage = Math.Max(1, (int)Math.Round(rawPower * mitigation));

        target.Hp = Math.Max(0, target.Hp - finalDamage);

        logs.Add(new BattleTurnLogDto(
            Round: round,
            ActorName: caster.Name,
            ActorSide: caster.Side,
            SkillId: skillId,
            SkillLabel: label,
            ActionType: "Damage",
            TargetName: target.Name,
            Amount: finalDamage,
            TargetHpAfter: target.Hp,
            TargetMaxHp: target.MaxHp,
            TargetDefeated: !target.IsAlive));

        return finalDamage;
    }

    public int heal(LuaUnitProxy targetProxy, int amount, string label)
    {
        var target = targetProxy.Inner;
        var safeAmount = Math.Max(1, amount);
        var before = target.Hp;
        target.Hp = Math.Min(target.MaxHp, target.Hp + safeAmount);
        var actualHeal = target.Hp - before;

        logs.Add(new BattleTurnLogDto(
            Round: round,
            ActorName: caster.Name,
            ActorSide: caster.Side,
            SkillId: skillId,
            SkillLabel: label,
            ActionType: "Heal",
            TargetName: target.Name,
            Amount: actualHeal,
            TargetHpAfter: target.Hp,
            TargetMaxHp: target.MaxHp,
            TargetDefeated: false));

        return actualHeal;
    }
}

/// <summary>
/// Singleton quản lý và thực thi Skill Lua trong Sandbox (MoonSharp) có OBJECT POOL:
/// - Không còn new Script() + DoString() lại ở mỗi lượt đánh (giảm mạnh CPU & GC ở 10k CCU).
/// - Mỗi skillId có 1 pool các VM đã biên dịch sẵn hàm on_cast(ctx); mượn chạy xong trả lại pool an toàn đa luồng.
/// </summary>
public sealed class LuaSkillRunner
{
    private const int MaxPoolSizePerSkill = 64;

    private readonly FrozenDictionary<string, string> _skillScripts;
    private readonly FrozenDictionary<string, ConcurrentBag<CompiledSkillVm>> _vmPools;

    static LuaSkillRunner()
    {
        UserData.RegisterType<LuaUnitProxy>();
        UserData.RegisterType<LuaBattleContext>();
    }

    public LuaSkillRunner(IWebHostEnvironment env)
    {
        var scriptsDir = Path.Combine(env.ContentRootPath, "Data", "Scripts", "Skills");
        var sourceDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var poolDict = new Dictionary<string, ConcurrentBag<CompiledSkillVm>>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(scriptsDir))
        {
            foreach (var file in Directory.GetFiles(scriptsDir, "*.lua"))
            {
                var skillId = Path.GetFileNameWithoutExtension(file);
                var code = File.ReadAllText(file);
                sourceDict[skillId] = code;

                // Khởi tạo sẵn 2 VM đã biên dịch trong pool để ấm máy (warm-up) ngay lúc startup
                var bag = new ConcurrentBag<CompiledSkillVm>();
                bag.Add(CompileVm(skillId, code));
                bag.Add(CompileVm(skillId, code));
                poolDict[skillId] = bag;
            }
        }

        _skillScripts = sourceDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _vmPools = poolDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> GetLoadedScripts() => _skillScripts;

    public void ExecuteSkill(string skillId, LuaBattleContext context)
    {
        if (!_skillScripts.TryGetValue(skillId, out var luaCode) ||
            !_vmPools.TryGetValue(skillId, out var pool))
        {
            var caster = context.get_caster();
            context.deal_damage(context.get_target(), caster.atk, "Đòn Đánh Thường");
            return;
        }

        // Mượn VM đã biên dịch sẵn từ Pool (không cần parse/compile lại Lua!)
        var vm = pool.TryTake(out var pooled) ? pooled : CompileVm(skillId, luaCode);
        var healthy = true;

        try
        {
            vm.Script.Call(vm.OnCastFunc, context);
        }
        catch
        {
            healthy = false;
            throw;
        }
        finally
        {
            // Chỉ trả VM sạch về pool và giữ giới hạn MaxPoolSizePerSkill để không phình RAM
            if (healthy && pool.Count < MaxPoolSizePerSkill)
            {
                pool.Add(vm);
            }
        }
    }

    private static CompiledSkillVm CompileVm(string skillId, string luaCode)
    {
        // Preset_SoftSandbox: chỉ cho phép toán học, chuỗi, bảng cơ bản — CẤM os/io/debug!
        var script = new Script(CoreModules.Preset_SoftSandbox);
        script.DoString(luaCode, codeFriendlyName: $"{skillId}.lua");

        var onCast = script.Globals.Get("on_cast");
        if (onCast.Type != DataType.Function)
        {
            throw new InvalidOperationException($"File {skillId}.lua thiếu hàm on_cast(ctx).");
        }

        return new CompiledSkillVm(script, onCast);
    }

    private sealed record CompiledSkillVm(Script Script, DynValue OnCastFunc);
}
