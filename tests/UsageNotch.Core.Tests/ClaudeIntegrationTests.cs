using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsageNotch.Providers;
using UsageNotch.Windows;

namespace UsageNotch.Core.Tests;

public sealed class ClaudeIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));
    public ClaudeIntegrationTests()
    {
        if (!Path.GetFullPath(_root).StartsWith(Path.Combine(Path.GetTempPath(), "UsageNotch.Tests") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test directory.");
        Directory.CreateDirectory(_root);
    }

    [Fact] public async Task BridgeSanitizesDataAndRedrawDoesNotRefreshObservation()
    {
        var input = """{"session_id":"private-session","cwd":"SECRET-PROJECT","version":"2.1.251","context_window":{"total_input_tokens":123},"rate_limits":{"five_hour":{"used_percentage":25,"resets_at":2000000000},"seven_day":{"used_percentage":62,"resets_at":2000500000}}}""";
        await RunBridge(input);
        var file = Assert.Single(Directory.GetFiles(_root, "*.json"));
        var first = await File.ReadAllTextAsync(file);
        var writtenAt = File.GetLastWriteTimeUtc(file);
        await RunBridge(input);
        Assert.Equal(first, await File.ReadAllTextAsync(file));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(file));
        Assert.DoesNotContain("SECRET", first); Assert.DoesNotContain("private-session", first);
        await using var provider = new ClaudeSnapshotProvider(_root);
        var reading = await provider.RefreshAsync(default);
        Assert.Equal(2, reading.Windows.Count);
        Assert.Equal(25, reading.Windows[0].UsedPercent!.Value);
    }
    [Fact] public void SetupPreservesOtherSettingsAndRestoresExistingStatusLine()
    {
        var config = Path.Combine(_root, "settings.json");
        File.WriteAllText(config, """{"model":"opus","statusLine":{"type":"command","command":"echo custom"}}""");
        var setup = new ClaudeBridgeSetup(config, Path.Combine(_root, "installed"));
        setup.Install(BridgeFolder());
        Assert.True(setup.IsInstalled());
        Assert.DoesNotContain("echo custom", File.ReadAllText(Path.Combine(_root, "installed", "setup.json")));
        Assert.Equal("opus", JsonNode.Parse(File.ReadAllText(config))!["model"]!.ToString());
        Assert.True(setup.Uninstall());
        Assert.Equal("echo custom", JsonNode.Parse(File.ReadAllText(config))!["statusLine"]!["command"]!.ToString());
    }
    [Fact] public void DisconnectPreservesAStatusLineChangedAfterInstall()
    {
        var config = Path.Combine(_root, "settings.json");
        var setup = new ClaudeBridgeSetup(config, Path.Combine(_root, "installed"));
        setup.Install(BridgeFolder());
        File.WriteAllText(config, """{"statusLine":{"type":"command","command":"echo new"}}""");
        Assert.False(setup.Uninstall());
        Assert.Contains("echo new", File.ReadAllText(config));
    }
    [Fact] public async Task BridgeKeepsTheExistingCommandsOutputUsingEncryptedBackup()
    {
        var config = Path.Combine(_root, "settings.json");
        var installed = Path.Combine(_root, "installed");
        File.WriteAllText(config, """{"statusLine":{"type":"command","command":"echo preserved-status"}}""");
        new ClaudeBridgeSetup(config, installed).Install(BridgeFolder());
        var output = await RunBridge("{}", Path.Combine(installed, "setup.json"));
        Assert.Equal("preserved-status", output.Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstalledStatusLineReceivesUsageThroughWindowsShells(bool gitBash)
    {
        var config = Path.Combine(_root, "settings.json");
        var installed = Path.Combine(_root, "bridge with spaces é");
        var snapshots = Path.Combine(_root, "snapshots");
        new ClaudeBridgeSetup(config, installed).Install(BridgeFolder());
        var command = JsonNode.Parse(File.ReadAllText(config))!["statusLine"]!["command"]!.GetValue<string>();
        // Redirect only the snapshot destination so the actual installed shell command runs without touching user data.
        var destination = snapshots.Replace('\\', '/').Replace("'", "''");
        command = command.Replace(" --chain-file ", $" --output '{destination}' --chain-file ");
        var shell = gitBash ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") : "powershell.exe";
        if (gitBash) Assert.True(File.Exists(shell), "Git for Windows is required for the status-line integration test.");
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (!gitBash) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); }
        info.ArgumentList.Add(gitBash ? "-c" : "-Command");
        info.ArgumentList.Add(command);
        using var child = Process.Start(info)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        await child.StandardInput.WriteAsync("""{"session_id":"fixture","rate_limits":{"five_hour":{"used_percentage":25,"resets_at":2000000000}}}""");
        child.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await child.WaitForExitAsync(timeout.Token); }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        Assert.True(child.ExitCode == 0, await errors);
        Assert.Contains("75% left", await output);
        await using var provider = new ClaudeSnapshotProvider(snapshots, () => "claude.exe");
        var reading = await provider.RefreshAsync(default);
        Assert.Equal(25, Assert.Single(reading.Windows).UsedPercent!.Value);
    }
    private async Task<string> RunBridge(string input, string? chainFile = null)
    {
        var info = new ProcessStartInfo(Path.Combine(BridgeFolder(), "UsageNotch.ClaudeBridge.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("--output"); info.ArgumentList.Add(_root);
        if (chainFile is not null) { info.ArgumentList.Add("--chain-file"); info.ArgumentList.Add(chainFile); }
        using var child = Process.Start(info)!;
        var output = child.StandardOutput.ReadToEndAsync();
        await child.StandardInput.WriteAsync(input); child.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await child.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, child.ExitCode);
        return await output;
    }
    private static string BridgeFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UsageNotch.slnx"))) directory = directory.Parent;
        var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "UsageNotch.ClaudeBridge", "bin", configuration, "net10.0");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
