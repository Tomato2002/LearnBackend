using LearnBackend.Modules.Battle.Engine;

namespace LearnBackend.Infrastructure.Scripts;

/// <summary>
/// Tầng Infrastructure chịu trách nhiệm đọc file .lua từ thư mục Data/Scripts/Skills/.
/// </summary>
public sealed class FileSkillScriptProvider(IWebHostEnvironment env) : ISkillScriptProvider
{
    public IReadOnlyDictionary<string, string> LoadAllScripts()
    {
        var scriptsDir = Path.Combine(env.ContentRootPath, "Data", "Scripts", "Skills");
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(scriptsDir))
        {
            foreach (var file in Directory.GetFiles(scriptsDir, "*.lua"))
            {
                var skillId = Path.GetFileNameWithoutExtension(file);
                dict[skillId] = File.ReadAllText(file);
            }
        }

        return dict;
    }
}
