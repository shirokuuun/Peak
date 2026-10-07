using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsageNotch.Core;

namespace UsageNotch.Windows;

public sealed class ClaudeBridgeSetup
{
    private readonly string _config;
    private readonly string _bridge;
    private string StatePath => Path.Combine(_bridge, "setup.json");
    public ClaudeBridgeSetup(string? configPath = null, string? bridgeDirectory = null)
    {
        _config = configPath ?? Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "settings.json");
        _bridge = bridgeDirectory ?? Path.Combine(PeakDataPaths.Root, "bridge");
    }

    public bool IsInstalled()
    {
        try
        {
            var config = Read(_config);
            var state = Read(StatePath);
            return state["installed"] is not null && JsonNode.DeepEquals(config["statusLine"], state["installed"]);
        }
        catch { return false; }
    }

    public void Install(string sourceDirectory)
    {
        if (!File.Exists(Path.Combine(sourceDirectory, "UsageNotch.ClaudeBridge.exe")))
            throw new IOException("The Claude bridge is missing. Publish the app with scripts/publish.ps1 first.");
        var config = Read(_config);
        if (File.Exists(StatePath) && !IsInstalled())
            throw new IOException("Claude's status line changed after setup. Disconnect the old bridge before reconnecting.");
        var previous = IsInstalled() ? ReadPrevious(Read(StatePath)) : config["statusLine"]?.DeepClone();
        IntegrationRegistry.Record(Path.GetDirectoryName(_bridge)!, "claude", _config);
        Directory.CreateDirectory(_bridge);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(file, Path.Combine(_bridge, Path.GetFileName(file)), overwrite: true);
        var exe = Path.Combine(_bridge, "UsageNotch.ClaudeBridge.exe").Replace('\\', '/').Replace("'", "''");
        var chain = StatePath.Replace('\\', '/').Replace("'", "''");
        var installed = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"powershell.exe -NoProfile -NonInteractive -Command \"& '{exe}' --chain-file '{chain}'\""
        };
        // Save only the old status-line entry, never a copy of the whole CLI config.
        WriteAtomic(StatePath, new JsonObject
        {
            ["previousProtected"] = WindowsSecretProtector.Protect(previous?.ToJsonString() ?? "null"),
            ["installed"] = installed.DeepClone()
        });
        config["statusLine"] = installed;
        WriteAtomic(_config, config);
    }

    public bool Uninstall()
    {
        if (!File.Exists(StatePath)) return false;
        var state = Read(StatePath);
        var config = Read(_config);
        var restored = JsonNode.DeepEquals(config["statusLine"], state["installed"]);
        if (restored)
        {
            if (ReadPrevious(state) is { } previous) config["statusLine"] = previous;
            else config.Remove("statusLine");
            WriteAtomic(_config, config);
        }
        File.Delete(StatePath);
        return restored;
    }

    private static JsonNode? ReadPrevious(JsonObject state) => state["previousProtected"] is { } encrypted
        ? JsonNode.Parse(WindowsSecretProtector.Unprotect(encrypted.GetValue<string>())) : state["previous"]?.DeepClone();

    private static JsonObject Read(string path) => File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject() ?? new()
        : new();

    private static void WriteAtomic(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
