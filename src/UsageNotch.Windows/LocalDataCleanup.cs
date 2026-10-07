using System.IO;
using UsageNotch.Core;

namespace UsageNotch.Windows;

public sealed class LocalDataCleanup
{
    public string Root { get; }
    public LocalDataCleanup(string? root = null) => Root = Path.GetFullPath(root ?? PeakDataPaths.Root);

    public void PruneObservations()
    {
        AssertSafe(Root);
        ObservationRetention.Prune(Path.Combine(Root, "codex-tasks"), true, DateTimeOffset.UtcNow);
        ObservationRetention.Prune(Path.Combine(Root, "claude"), false, DateTimeOffset.UtcNow);
        var diagnostic = Path.Combine(Root, "startup-error.txt");
        if (File.Exists(diagnostic) && File.GetLastWriteTimeUtc(diagnostic) < DateTime.UtcNow.AddDays(-7)) DeleteFile(diagnostic);
    }

    public IReadOnlyList<string> DisconnectIntegrations(bool removeStartup = true)
    {
        AssertSafe(Root);
        var errors = new List<string>();
        AssertSafe(Path.Combine(Root, "integrations.json"));
        AssertSafe(Path.Combine(Root, "task-bridge"));
        AssertSafe(Path.Combine(Root, "bridge", "setup.json"));
        Dictionary<string, string> registry;
        try { registry = IntegrationRegistry.Read(Root); }
        catch { return ["Peak's integration record could not be read. Disconnect providers in Settings before uninstalling."]; }
        try { new CodexTaskBridgeSetup(registry.GetValueOrDefault("codex"), Path.Combine(Root, "task-bridge")).Uninstall(); }
        catch { errors.Add("Could not remove Peak's Codex hooks. Their configuration and local files were preserved."); }
        try { new ClaudeBridgeSetup(registry.GetValueOrDefault("claude"), Path.Combine(Root, "bridge")).Uninstall(); }
        catch { errors.Add("Could not restore Claude's status line. Its configuration and backup were preserved."); }
        if (removeStartup)
        {
            try { new StartupService().RemoveOwnedEntries(); }
            catch { errors.Add("Could not remove Peak's startup entry."); }
        }
        return errors;
    }

    public void ClearObservations()
    {
        // Keep preferences, ignored legacy license data and restoration backups.
        foreach (var name in new[] { "usage-cache.json", "alert-state.json", "startup-error.txt" }) DeleteFile(Path.Combine(Root, name));
        foreach (var name in new[] { "codex-tasks", "claude", "Demo" }) DeleteFolder(Path.Combine(Root, name));
    }

    public void RemoveBridgeFiles()
    {
        foreach (var name in new[] { "bridge", "task-bridge" }) DeleteFolder(Path.Combine(Root, name));
        DeleteFile(Path.Combine(Root, "integrations.json"));
    }

    public void DeleteAllLocalData()
    {
        ClearObservations(); RemoveBridgeFiles();
        foreach (var name in new[] { "settings.json", "license.dat" }) DeleteFile(Path.Combine(Root, name));
        if (Directory.Exists(Root) && !Directory.EnumerateFileSystemEntries(Root).Any()) Directory.Delete(Root);
    }

    private void DeleteFolder(string path)
    {
        AssertSafe(path);
        if (!Directory.Exists(path)) return;
        var entries = new List<string>();
        void Inspect(string parent)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
            {
                AssertSafe(entry); entries.Add(entry);
                if (Directory.Exists(entry)) Inspect(entry);
            }
        }
        Inspect(path);
        foreach (var entry in entries.Where(File.Exists)) File.Delete(entry);
        foreach (var entry in entries.Where(Directory.Exists).OrderByDescending(p => p.Length)) Directory.Delete(entry);
        Directory.Delete(path);
    }

    private void DeleteFile(string path)
    {
        AssertSafe(path);
        if (File.Exists(path)) File.Delete(path);
    }

    private void AssertSafe(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.Equals(Root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(Root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The cleanup target is outside Peak's local data folder.");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && ObservationRetention.IsLink(current))
                throw new IOException("A linked folder or file was preserved. Remove it manually before cleanup.");
    }
}
