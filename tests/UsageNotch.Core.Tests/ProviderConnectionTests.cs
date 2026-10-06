using UsageNotch.Core;
using UsageNotch.Providers;

namespace UsageNotch.Core.Tests;

public sealed class ProviderConnectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CodexChildUsesTheCliLoginStoreAndDisablesTelemetry()
    {
        var info = CodexAppServerClient.CreateStartInfo("codex.exe");
        Assert.Equal(new[] { "app-server", "--listen", "stdio://" }, info.ArgumentList.Take(3));
        Assert.DoesNotContain(info.ArgumentList, argument => argument.Contains("cli_auth_credentials_store"));
        Assert.Equal(Environment.GetEnvironmentVariable("CODEX_HOME"), info.Environment.TryGetValue("CODEX_HOME", out var home) ? home : null);
        Assert.Contains("analytics.enabled=false", info.ArgumentList);
        Assert.Contains("otel.exporter=\"none\"", info.ArgumentList);
        Assert.Contains("otel.trace_exporter=\"none\"", info.ArgumentList);
        Assert.Contains("otel.metrics_exporter=\"none\"", info.ArgumentList);
        Assert.Contains("otel.log_user_prompt=false", info.ArgumentList);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void LocalLauncherIsDetectedEvenWhenTheAppsPathIsStale(string name)
    {
        var expected = CreateFile($"profile/.local/bin/{name}.exe");
        Assert.Equal(expected, Find(name));
    }

    [Fact]
    public void CodexNpmWrapperResolvesToTheNativeAppServer()
    {
        CreateFile("roaming/npm/codex.cmd");
        var expected = CreateFile("roaming/npm/node_modules/@openai/codex-win32-x64/vendor/x86_64-pc-windows-msvc/codex/codex.exe");
        Assert.Equal(expected, Find("codex", Path.Combine(_root, "roaming/npm")));
    }

    [Fact]
    public void CodexOfficialWindowsInstallerIsDetectedBeforeTheBundledDesktopCli()
    {
        var expected = CreateFile("local/Programs/OpenAI/Codex/bin/codex.exe");
        CreateFile("local/OpenAI/Codex/bin/desktop-version/codex.exe");
        Assert.Equal(expected, Find("codex"));
    }

    [Fact]
    public void MissingCliIsDistinctFromAnInstalledBridgeAwaitingActivity()
    {
        Assert.Null(Find("claude"));
        Assert.Null(Find("codex"));
    }

    [Fact]
    public async Task ClaudeWithoutACliExplainsTheMissingInstallation()
    {
        await using var provider = new ClaudeSnapshotProvider(Path.Combine(_root, "snapshots"), () => null);
        var reading = await provider.RefreshAsync(default);
        Assert.Equal(ConnectionState.Unsupported, reading.Connection);
        Assert.Equal("cli_missing", reading.Error!.Code);
        Assert.Contains("Install", reading.Error.Message);
        Assert.Empty(reading.Windows);
    }

    [Fact]
    public async Task ClaudeWithACliButNoSnapshotExplainsTheFirstResponseRequirement()
    {
        await using var provider = new ClaudeSnapshotProvider(Path.Combine(_root, "snapshots"), () => "claude.exe");
        var reading = await provider.RefreshAsync(default);
        Assert.Equal(ConnectionState.Waiting, reading.Connection);
        Assert.Contains("first response", reading.Error!.Message);
        Assert.Empty(reading.Windows);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public async Task SetupRequiresInstallationBeforeReadingAccountOrUsage(string name)
    {
        var setup = new ProviderConnectionSetup(name, _ => null,
            (_, _) => throw new InvalidOperationException("Account must not be read"),
            _ => throw new InvalidOperationException("Usage must not be read"));
        Assert.Equal(ProviderSetupStep.MissingCli, (await setup.ProbeAsync(default)).Step);
        Assert.Throws<FileNotFoundException>(() => setup.CreateSignInStartInfo());
        var installer = setup.CreateInstallStartInfo();
        Assert.True(installer.UseShellExecute);
        Assert.Contains(name == "codex" ? "https://chatgpt.com/codex/install.ps1" : "https://claude.ai/install.ps1", installer.ArgumentList.Last());
        Assert.False(installer.RedirectStandardOutput);
        Assert.False(installer.RedirectStandardError);
    }

    [Theory]
    [InlineData(ConnectionState.Ready, ProviderSetupStep.Ready)]
    [InlineData(ConnectionState.AuthRequired, ProviderSetupStep.SignIn)]
    [InlineData(ConnectionState.Unsupported, ProviderSetupStep.Unsupported)]
    public async Task CodexSetupReusesExistingLoginAndDistinguishesApiAccounts(ConnectionState account, ProviderSetupStep expected)
    {
        var setup = new ProviderConnectionSetup("codex", _ => "codex.exe", (_, _) => Task.FromResult(account),
            _ => throw new InvalidOperationException("Claude must not be read"));
        Assert.Equal(expected, (await setup.ProbeAsync(default)).Step);
        var signIn = setup.CreateSignInStartInfo();
        Assert.Equal(new[] { "login" }, signIn.ArgumentList);
        Assert.DoesNotContain(signIn.ArgumentList, argument => argument.Contains("keyring"));
    }

    [Fact]
    public async Task ClaudeSetupWaitsForReportedUsageRatherThanAssumingBridgeInstallationConnectedAnAccount()
    {
        var setup = new ProviderConnectionSetup("claude", _ => "claude.exe",
            (_, _) => throw new InvalidOperationException("Codex must not be read"),
            _ => Task.FromResult(ProviderUsage.Waiting("claude", "Claude")));
        Assert.Equal(ProviderSetupStep.AwaitingUsage, (await setup.ProbeAsync(default)).Step);
    }

    [Fact]
    public async Task SetupTransportFailureIsRecoverableAndCancellationIsPreserved()
    {
        var setup = new ProviderConnectionSetup("codex", _ => "codex.exe", (_, _) => throw new IOException("fixture"),
            _ => throw new InvalidOperationException());
        Assert.Equal(ProviderSetupStep.Unavailable, (await setup.ProbeAsync(default)).Step);
        setup = new ProviderConnectionSetup("codex", _ => "codex.exe", (_, _) => throw new OperationCanceledException(),
            _ => throw new InvalidOperationException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => setup.ProbeAsync(default));
    }

    [Theory]
    [InlineData(true, ProviderSetupStep.Ready)]
    [InlineData(false, ProviderSetupStep.AwaitingUsage)]
    public async Task ClaudeSetupReusesOnlyFreshReportedLimits(bool fresh, ProviderSetupStep expected)
    {
        var folder = Path.Combine(_root, "snapshots");
        Directory.CreateDirectory(folder);
        var observation = DateTimeOffset.UtcNow.AddMinutes(fresh ? 0 : -10);
        var snapshot = System.Text.Json.JsonSerializer.Serialize(new
        {
            observedAt = observation,
            rateLimits = new { fiveHour = new { usedPercentage = 25, resetsAt = 2000000000 } }
        });
        File.WriteAllText(Path.Combine(folder, "fixture.json"), snapshot);
        await using var reader = new ClaudeSnapshotProvider(folder, () => "claude.exe");
        var setup = new ProviderConnectionSetup("claude", _ => "claude.exe",
            (_, _) => throw new InvalidOperationException("Codex must not be read"), reader.RefreshAsync);
        Assert.Equal(expected, (await setup.ProbeAsync(default)).Step);
    }

    private string? Find(string name, string path = "") => ExecutableLocator.Find(name, path,
        Path.Combine(_root, "profile"), Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));

    private string CreateFile(string relative)
    {
        var file = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "fixture");
        return file;
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(_root);
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "UsageNotch.Tests")) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test directory.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
