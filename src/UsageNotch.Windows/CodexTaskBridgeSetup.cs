using System.IO;
using System.Text.Json.Nodes;
using UsageNotch.Core;

namespace UsageNotch.Windows;

public sealed class CodexTaskBridgeSetup
{
    private readonly string _config, _bridge;
    private const string Marker = "usage-notch-task-observer-v1";

    public CodexTaskBridgeSetup(string? configPath = null, string? bridgeDirectory = null)
    {
        _config = configPath ?? Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "hooks.json");
        _bridge = bridgeDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch", "task-bridge");
    }

    public bool IsInstalled()
    {
        try
        {
            var hooks = Read()["hooks"] as JsonObject;
            return hooks is not null && CodexTaskEvents.HookNames.All(name => hooks[name] is JsonArray array && array.Any(IsOurs));
        }
        catch { return false; }
    }

    public void Install(string sourceDirectory)
    {
        if (!File.Exists(Path.Combine(sourceDirectory, "UsageNotch.ClaudeBridge.exe"))) throw new IOException("Publish the notch before connecting task activity.");
        var config = Read();
        if (config["hooks"] is not null && config["hooks"] is not JsonObject) throw new IOException("Codex hooks.json has an unsupported hooks value; it was preserved.");
        var hooks = config["hooks"] as JsonObject ?? new JsonObject();
        // Check every affected entry before modifying anything; preserve unrelated hooks and metadata.
        foreach (var name in CodexTaskEvents.HookNames)
            if (hooks[name] is not null && hooks[name] is not JsonArray) throw new IOException("Codex hook entries must be arrays; the existing file was preserved.");
        Directory.CreateDirectory(_bridge);
        IntegrationRegistry.Record(Path.GetDirectoryName(_bridge)!, "codex", _config);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory)) File.Copy(file, Path.Combine(_bridge, Path.GetFileName(file)), true);
        var exe = Path.Combine(_bridge, "UsageNotch.ClaudeBridge.exe").Replace('\\', '/');
        var command = $"\"{exe}\" --codex-event --observer {Marker}";
        // No execution-policy change. A command string forwards UTF-8 stdin through either outer shell.
        var windowsExe = exe.Replace("'", "''");
        var windowsCommand = "powershell.exe -NoProfile -NonInteractive -Command \"" +
            "Set-Variable -Name OutputEncoding -Value ([Text.UTF8Encoding]::new()); " +
            "[Console]::InputEncoding=[Text.UTF8Encoding]::new(); " +
            $"[Console]::In.ReadToEnd() | & '{windowsExe}' --codex-event --observer {Marker}\"";
        foreach (var name in CodexTaskEvents.HookNames)
        {
            var array = hooks[name] as JsonArray ?? new JsonArray();
            for (var i = array.Count - 1; i >= 0; i--) if (IsOurs(array[i])) array.RemoveAt(i);
            array.Add(new JsonObject { ["matcher"] = "*", ["hooks"] = new JsonArray(new JsonObject
                { ["type"] = "command", ["command"] = command, ["commandWindows"] = windowsCommand, ["timeout"] = 3 }) });
            hooks[name] = array.Parent is null ? array : array.DeepClone();
        }
        if (hooks.Parent is null) config["hooks"] = hooks;
        Write(config);
    }

    public void Uninstall()
    {
        var config = Read();
        if (config["hooks"] is not JsonObject hooks) return;
        foreach (var name in CodexTaskEvents.HookNames)
            if (hooks[name] is JsonArray array)
            {
                for (var i = array.Count - 1; i >= 0; i--) if (IsOurs(array[i])) array.RemoveAt(i);
                if (array.Count == 0) hooks.Remove(name);
            }
        Write(config);
    }

    private static bool IsOurs(JsonNode? entry) => entry is JsonObject obj && obj["hooks"] is JsonArray handlers &&
        handlers.Count == 1 && handlers[0] is JsonObject handler && handler["command"] is JsonValue value &&
        value.TryGetValue<string>(out var command) && command.EndsWith("--observer " + Marker, StringComparison.Ordinal) &&
        command.Contains("--codex-event", StringComparison.Ordinal);

    private JsonObject Read() => File.Exists(_config) ? JsonNode.Parse(File.ReadAllText(_config)) as JsonObject ??
        throw new IOException("Codex hooks.json must contain an object.") : new JsonObject();

    private void Write(JsonObject config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_config)!);
        var temporary = _config + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, config.ToJsonString(new() { WriteIndented = true })); File.Move(temporary, _config, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
