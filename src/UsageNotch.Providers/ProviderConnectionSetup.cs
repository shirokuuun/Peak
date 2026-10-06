using System.Diagnostics;
using UsageNotch.Core;

namespace UsageNotch.Providers;

public enum ProviderSetupStep { MissingCli, SignIn, AwaitingUsage, Ready, Unsupported, Unavailable }
public sealed record ProviderSetupStatus(ProviderSetupStep Step, string Message);

public sealed class ProviderConnectionSetup
{
    private readonly string _provider;
    private readonly Func<string, string?> _find;
    private readonly Func<string, CancellationToken, Task<ConnectionState>> _codexAccount;
    private readonly Func<CancellationToken, Task<ProviderUsage>> _claudeUsage;

    public ProviderConnectionSetup(string provider) : this(provider, name => ExecutableLocator.Find(name), ReadCodexAccountAsync,
        async token => { await using var reader = new ClaudeSnapshotProvider(); return await reader.RefreshAsync(token); }) { }

    internal ProviderConnectionSetup(string provider, Func<string, string?> find,
        Func<string, CancellationToken, Task<ConnectionState>> codexAccount,
        Func<CancellationToken, Task<ProviderUsage>> claudeUsage)
    {
        if (provider is not ("codex" or "claude")) throw new ArgumentException("Unsupported provider.", nameof(provider));
        _provider = provider; _find = find; _codexAccount = codexAccount; _claudeUsage = claudeUsage;
    }

    public async Task<ProviderSetupStatus> ProbeAsync(CancellationToken token)
    {
        var executable = _find(_provider);
        if (executable is null) return new(ProviderSetupStep.MissingCli,
            "Install the Windows CLI from the provider's official installer. Peak will open it in a terminal, then continue setup.");
        try
        {
            if (_provider == "codex")
            {
                var connection = await _codexAccount(executable, token).ConfigureAwait(false);
                return connection switch
                {
                    ConnectionState.Ready => new(ProviderSetupStep.Ready, "Your existing ChatGPT login is ready. Save changes to show usage in Peak."),
                    ConnectionState.Unsupported => new(ProviderSetupStep.Unsupported, "This CLI uses an API-key account. Subscription limits require signing in with ChatGPT."),
                    _ => new(ProviderSetupStep.SignIn, "Finish signing in with ChatGPT in the provider's browser window. Peak does not ask for your password.")
                };
            }
            var usage = await _claudeUsage(token).ConfigureAwait(false);
            if (usage.Connection == ConnectionState.Ready && usage.Windows.Any(window => window.Freshness == Freshness.Fresh))
                return new(ProviderSetupStep.Ready, "Claude Code is reporting usage. Save changes to show it in Peak.");
            return new(ProviderSetupStep.AwaitingUsage, "The usage bridge is installed. Open Claude Code, sign in with Pro or Max, and send a prompt. Usage appears after its first response.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(ProviderSetupStep.Unavailable, "The CLI could not be checked. Retry, or update it using the provider setup guide.");
        }
    }

    public ProcessStartInfo CreateInstallStartInfo()
    {
        var url = _provider == "codex" ? "https://chatgpt.com/codex/install.ps1" : "https://claude.ai/install.ps1";
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = true };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        // Fixed official URLs only. Errors remain visible; no credentials or diagnostic output are captured.
        info.ArgumentList.Add("$ErrorActionPreference='Stop'; try { & ([scriptblock]::Create((Invoke-RestMethod -Uri '" + url +
            "'))) } catch { Write-Host 'Provider installation failed. Check the error below:'; Write-Host $_.Exception.Message; Read-Host 'Press Enter to return to Peak'; exit 1 }");
        return info;
    }

    public ProcessStartInfo CreateSignInStartInfo()
    {
        var executable = _find(_provider) ?? throw new FileNotFoundException("Install the provider CLI first.");
        if (_provider == "codex")
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = true };
            info.ArgumentList.Add("login");
            return info;
        }
        // A terminal stays open after Claude exits so installation/login errors can be read.
        var terminal = new ProcessStartInfo("powershell.exe") { UseShellExecute = true };
        terminal.ArgumentList.Add("-NoProfile"); terminal.ArgumentList.Add("-NoExit"); terminal.ArgumentList.Add("-Command");
        terminal.ArgumentList.Add("& '" + executable.Replace("'", "''") + "'");
        return terminal;
    }

    private static async Task<ConnectionState> ReadCodexAccountAsync(string executable, CancellationToken token)
    {
        await using var client = new CodexAppServerClient(executable);
        await client.StartAsync(token).ConfigureAwait(false);
        var result = await client.ReadAccountAsync(token).ConfigureAwait(false);
        if (!result.TryGetProperty("account", out var account)) return ConnectionState.AuthRequired;
        return CodexUsageMapper.GetString(account, "type") switch
        {
            "chatgpt" => ConnectionState.Ready,
            "apiKey" => ConnectionState.Unsupported,
            _ => ConnectionState.AuthRequired
        };
    }
}
