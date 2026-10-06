using System.IO;
using System.Text.Json;

namespace UsageNotch.Windows;

public static class IntegrationRegistry
{
    public static void Record(string root, string provider, string configPath)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "integrations.json");
        var entries = Read(root);
        entries[provider] = Path.GetFullPath(configPath);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(entries)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Dictionary<string, string> Read(string root)
    {
        var path = Path.Combine(root, "integrations.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new() : new();
    }
}
