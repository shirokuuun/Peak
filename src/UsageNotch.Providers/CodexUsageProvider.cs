using System.Diagnostics;
using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.Providers;

public sealed class CodexUsageProvider : IUsageProvider
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CodexLocalSessionReader _fallback = new();
    private CodexAppServerClient? _client;
    private string? _executable;
    private string? _version;
    private bool _usageSupported = true;

    public string Id => "codex";
    public string DisplayName => "Codex";

    public async Task<IReadOnlyList<CodexTaskMetadata>> ReadTaskCatalogAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var probe = await ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (!probe.Available) return [];
            _client ??= new CodexAppServerClient(_executable!);
            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
            var response = await _client.ReadThreadsAsync(cancellationToken).ConfigureAwait(false);
            return CodexTaskMetadata.Parse(response);
        }
        finally { _refreshLock.Release(); }
    }

    public async Task<ProviderProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        _executable ??= ExecutableLocator.Find("codex");
        if (_executable is null)
            return new ProviderProbe(false, null, new(true, false, true, true), "Install the Windows Codex CLI, sign in with codex login, then restart Peak.");

        _version ??= await ReadVersionAsync(_executable, cancellationToken).ConfigureAwait(false);
        return new ProviderProbe(true, _version, new(true, false, true, true));
    }

    public async Task<ProviderUsage> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var probe = await ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (!probe.Available)
                return ProviderUsage.Waiting(Id, DisplayName) with
                {
                    Connection = ConnectionState.Unsupported,
                    Error = new ProviderError("cli_missing", probe.Message!, false)
                };

            try
            {
                _client ??= new CodexAppServerClient(_executable!);
                await _client.StartAsync(cancellationToken).ConfigureAwait(false);
                var account = await _client.ReadAccountAsync(cancellationToken).ConfigureAwait(false);
                if (!account.TryGetProperty("account", out var identity) || identity.ValueKind == JsonValueKind.Null)
                {
                    var unavailable = CodexUsageMapper.Map(account, default, null, _version, DateTimeOffset.UtcNow);
                    // A login completed outside this process must be picked up on the next refresh.
                    await _client.DisposeAsync().ConfigureAwait(false);
                    _client = null;
                    var local = await _fallback.ReadLatestAsync(_version, cancellationToken).ConfigureAwait(false);
                    return local is null ? unavailable : local with
                    {
                        Connection = unavailable.Connection,
                        Error = new("secure_login_required", "Run codex login with ChatGPT, then refresh. Local readings have an unverified account and are excluded from rings and alerts.", false)
                    };
                }
                var limits = await _client.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false);
                JsonElement? usage = null;
                if (_usageSupported)
                {
                    try { usage = await _client.ReadUsageAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception exception) when (exception is not OperationCanceledException) { _usageSupported = false; }
                }

                return CodexUsageMapper.Map(account, limits, usage, _version, DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
                _client = null;

                var fallback = await _fallback.ReadLatestAsync(_version, cancellationToken).ConfigureAwait(false);
                return fallback ?? ProviderUsage.Waiting(Id, DisplayName) with
                {
                    Connection = ConnectionState.Error,
                    Error = new ProviderError("codex_unavailable", "Live Codex usage is unavailable. Check codex --version and codex login status, then refresh.", true)
                };
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<string?> ReadVersionAsync(string executable, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable, "--version")
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = Process.Start(info);
        if (process is null) return null;
        try
        {
            var version = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return version.Trim();
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
        _refreshLock.Dispose();
    }
}
