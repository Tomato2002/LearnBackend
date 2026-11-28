using System.Reflection;
using LearnBackend.Modules.Auth;
using LearnBackend.Modules.Battle.Engine;
using LearnBackend.Modules.Gacha.Entities;
using LearnBackend.Modules.Player.Entities;
using Microsoft.AspNetCore.Mvc;

namespace LearnBackend.Tests;

/// <summary>
/// Architecture Fitness Tests (học từ kgrzybek/modular-monolith-with-ddd):
/// Tự động kiểm tra ranh giới giữa các module bằng Reflection mỗi lần chạy `dotnet test`.
/// Nếu dev nào vô tình kéo EF Core vào Combat Engine, kéo Hero vào Auth, hoặc mở `public set` trên Rich Domain Entity -> CI báo đỏ ngay!
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly BackendAssembly = typeof(TurnBasedCombatEngine).Assembly;

    [Fact]
    public void BattleEngine_MustBePureDomain_NoEfCoreOrAspNetCoreDependencies()
    {
        var engineTypes = BackendAssembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("LearnBackend.Modules.Battle.Engine", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(engineTypes);

        foreach (var type in engineTypes)
        {
            var referencedTypes = GetReferencedMemberTypes(type);
            var forbidden = referencedTypes
                .Where(rt => rt.Namespace is not null && (
                    rt.Namespace.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                    rt.Namespace.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                    rt.Namespace.StartsWith("LearnBackend.Infrastructure.Persistence", StringComparison.Ordinal)))
                .Select(rt => $"{type.Name} -> {rt.FullName}")
                .ToList();

            Assert.Empty(forbidden);
        }
    }

    [Fact]
    public void AuthModule_MustNotDependDirectlyOnRpgGameModules()
    {
        var authTypes = BackendAssembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("LearnBackend.Modules.Auth", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(authTypes);

        string[] forbiddenModulePrefixes =
        [
            "LearnBackend.Modules.Hero",
            "LearnBackend.Modules.Inventory",
            "LearnBackend.Modules.Battle",
            "LearnBackend.Modules.Gacha",
            "LearnBackend.Modules.Shop",
            "LearnBackend.Modules.Quest",
            "LearnBackend.Modules.Arena",
            "LearnBackend.Modules.Guild"
        ];

        foreach (var type in authTypes)
        {
            var referencedTypes = GetReferencedMemberTypes(type);
            var violations = referencedTypes
                .Where(rt => rt.Namespace is not null &&
                             forbiddenModulePrefixes.Any(p => rt.Namespace.StartsWith(p, StringComparison.Ordinal)))
                .Select(rt => $"{type.Name} -> {rt.FullName}")
                .ToList();

            Assert.Empty(violations);
        }
    }

    [Fact]
    public void RichDomainEntities_MustProtectStateWithNonPublicSetters()
    {
        Type[] richDomainTypes = [typeof(PlayerProfile), typeof(CombatUnit), typeof(PlayerGachaPity)];

        foreach (var entityType in richDomainTypes)
        {
            var mutablePublicProps = entityType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is not null
                            && p.SetMethod.IsPublic
                            && !p.SetMethod.ReturnParameter
                                .GetRequiredCustomModifiers()
                                .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)))
                .Select(p => $"{entityType.Name}.{p.Name}")
                .ToList();

            Assert.Empty(mutablePublicProps);
        }
    }

    [Fact]
    public void AllControllers_MustInheritControllerBaseAndHaveApiControllerAttribute()
    {
        var controllerTypes = BackendAssembly.GetTypes()
            .Where(t => !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(controllerTypes);

        foreach (var ctrl in controllerTypes)
        {
            Assert.True(typeof(ControllerBase).IsAssignableFrom(ctrl), $"{ctrl.Name} phải kế thừa ControllerBase.");
            Assert.True(ctrl.GetCustomAttribute<ApiControllerAttribute>() is not null, $"{ctrl.Name} thiếu [ApiController].");
        }
    }

    private static IEnumerable<Type> GetReferencedMemberTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var f in type.GetFields(flags))
            yield return f.FieldType;

        foreach (var p in type.GetProperties(flags))
            yield return p.PropertyType;

        foreach (var c in type.GetConstructors(flags))
            foreach (var param in c.GetParameters())
                yield return param.ParameterType;

        foreach (var m in type.GetMethods(flags))
        {
            yield return m.ReturnType;
            foreach (var param in m.GetParameters())
                yield return param.ParameterType;
        }
    }
}
