using System.Text.Json;
using UsageNotch.Core;
using UsageNotch.Windows;

namespace UsageNotch.Core.Tests;

public sealed class PrivacyAndRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));
    public PrivacyAndRetentionTests() => Directory.CreateDirectory(_root);
    [Fact] public void TaskRetentionDropsOldRecordsEvenInAnActiveFileAndPreservesProviderLogs()
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid().ToString();
        var file = Path.Combine(_root, id + ".jsonl");
        var observation = new CodexTaskEvent(1, id, "turn", "Stop", now, "project", null, null);
        File.WriteAllLines(file, [JsonSerializer.Serialize(observation with { ObservedAt = now.AddDays(-8) }),
            "broken-json", JsonSerializer.Serialize(observation)]);
        var providerLog = Path.Combine(_root, "provider-transcript.jsonl");
        File.WriteAllText(providerLog, "untouched"); File.SetLastWriteTimeUtc(providerLog, now.AddDays(-90).UtcDateTime);
        ObservationRetention.Prune(_root, true, now);
        Assert.Single(File.ReadAllLines(file)); Assert.Equal("untouched", File.ReadAllText(providerLog));
    }
    [Fact] public void TaskRetentionBoundsLargeFiles()
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid().ToString(); var file = Path.Combine(_root, id + ".jsonl");
        var row = JsonSerializer.Serialize(new CodexTaskEvent(1, id, "turn", "Stop", now, "project", null, null));
        File.WriteAllLines(file, Enumerable.Repeat(row, 5000));
        ObservationRetention.Prune(_root, true, now);
        Assert.InRange(new FileInfo(file).Length, 1, ObservationRetention.MaximumTaskFileBytes);
        Assert.All(File.ReadAllLines(file), line => Assert.NotNull(JsonSerializer.Deserialize<CodexTaskEvent>(line)));
    }
    [Fact] public void ClaudeRetentionDeletesOldestForAgeAndSize()
    {
        var now = DateTimeOffset.UtcNow;
        var old = Path.Combine(_root, "aaaaaaaaaaaaaaaa.json");
        var middle = Path.Combine(_root, "bbbbbbbbbbbbbbbb.json");
        var recent = Path.Combine(_root, "cccccccccccccccc.json");
        foreach (var path in new[] { old, middle, recent }) File.WriteAllText(path, new string('a', 90));
        File.SetLastWriteTimeUtc(old, now.AddDays(-8).UtcDateTime);
        File.SetLastWriteTimeUtc(middle, now.AddHours(-1).UtcDateTime);
        ObservationRetention.Prune(_root, false, now, 100);
        Assert.False(File.Exists(old)); Assert.False(File.Exists(middle)); Assert.True(File.Exists(recent));
    }
    [Fact] public void ClearObservationsPreservesLegacyLicensePreferencesAndRestorationBackups()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bridge")); Directory.CreateDirectory(Path.Combine(_root, "codex-tasks"));
        foreach (var file in new[] { "settings.json", "license.dat", "bridge/setup.json", "usage-cache.json", "codex-tasks/task.jsonl" })
            File.WriteAllText(Path.Combine(_root, file), "test");
        new LocalDataCleanup(_root).ClearObservations();
        foreach (var file in new[] { "settings.json", "license.dat", "bridge/setup.json" }) Assert.True(File.Exists(Path.Combine(_root, file)));
        Assert.False(File.Exists(Path.Combine(_root, "usage-cache.json"))); Assert.False(Directory.Exists(Path.Combine(_root, "codex-tasks")));
    }
    [Fact] public void UninstallRestoresRegisteredCustomPathsAndPreservesUnrelatedSettings()
    {
        var data = Path.Combine(_root, "data"); var source = Path.Combine(_root, "source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "UsageNotch.ClaudeBridge.exe"), "test executable");
        var claude = Path.Combine(_root, "custom-claude.json"); var codex = Path.Combine(_root, "custom-codex.json");
        File.WriteAllText(claude, """{"model":"keep-me","statusLine":{"type":"command","command":"echo before"}}""");
        File.WriteAllText(codex, """{"metadata":"keep-me","hooks":{"Stop":[{"hooks":[{"command":"echo unrelated"}]}]}}""");
        new ClaudeBridgeSetup(claude, Path.Combine(data, "bridge")).Install(source);
        new CodexTaskBridgeSetup(codex, Path.Combine(data, "task-bridge")).Install(source);
        var cleanup = new LocalDataCleanup(data);
        Assert.Empty(cleanup.DisconnectIntegrations(false)); cleanup.RemoveBridgeFiles();
        Assert.Contains("echo before", File.ReadAllText(claude)); Assert.Contains("keep-me", File.ReadAllText(claude));
        Assert.Contains("echo unrelated", File.ReadAllText(codex)); Assert.DoesNotContain("usage-notch-task-observer-v1", File.ReadAllText(codex));
    }
    [Fact] public void FailedDisconnectPreservesRestorationBackup()
    {
        var data = Path.Combine(_root, "data"); var source = Path.Combine(_root, "source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "UsageNotch.ClaudeBridge.exe"), "test executable");
        var claude = Path.Combine(_root, "claude.json");
        new ClaudeBridgeSetup(claude, Path.Combine(data, "bridge")).Install(source);
        File.WriteAllText(claude, "malformed");
        var errors = new LocalDataCleanup(data).DisconnectIntegrations(false);
        Assert.NotEmpty(errors); Assert.True(File.Exists(Path.Combine(data, "bridge", "setup.json")));
        Assert.Equal("malformed", File.ReadAllText(claude));
    }
    [Fact] public void ProvidersDefaultToNoAccessUntilEnabled()
    { var settings = new AppSettings(); Assert.False(settings.CodexEnabled); Assert.False(settings.ClaudeEnabled); Assert.False(settings.CodexTasksEnabled); }
    public void Dispose() => Directory.Delete(_root, true);
}
