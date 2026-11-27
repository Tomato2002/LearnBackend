namespace LearnBackend.Modules.Battle.Engine;

/// <summary>
/// Dependency Inversion Interface cho nguồn tải script Lua Skill.
/// Giúp lõi Battle Engine / LuaSkillRunner độc lập 100% khỏi ASP.NET Core (IWebHostEnvironment) và ổ đĩa!
/// </summary>
public interface ISkillScriptProvider
{
    IReadOnlyDictionary<string, string> LoadAllScripts();
}
