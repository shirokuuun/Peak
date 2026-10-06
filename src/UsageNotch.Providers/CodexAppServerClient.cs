using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace UsageNotch.Providers;

internal sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string _executable;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Process? _process;
    private Task? _readerTask;
    private long _nextId;
    private readonly CancellationTokenSource _lifetime = new();

    public CodexAppServerClient(string executable) => _executable = executable;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return;

        _process = Process.Start(CreateStartInfo(_executable)) ?? throw new InvalidOperationException("Codex app-server did not start.");
        _readerTask = ReadLoopAsync(_process, _lifetime.Token);
        _ = DrainErrorsAsync(_process, _lifetime.Token);

        await RequestAsync("initialize", new
        {
            clientInfo = new { name = "usage_notch", title = "Peak", version = "1.0.1" }
        }, cancellationToken).ConfigureAwait(false);
        await SendAsync(new { method = "initialized", @params = new { } }, cancellationToken).ConfigureAwait(false);
    }

    internal static ProcessStartInfo CreateStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");
        // Let the CLI use its configured login store. Peak never reads credentials itself.
        // Telemetry overrides apply only to this child process.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("analytics.enabled=false");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("otel.exporter=\"none\"");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("otel.trace_exporter=\"none\"");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("otel.metrics_exporter=\"none\"");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("otel.log_user_prompt=false");

        return startInfo;
    }

    public Task<JsonElement> ReadAccountAsync(CancellationToken cancellationToken) =>
        RequestAsync("account/read", new { refreshToken = false }, cancellationToken);

    public Task<JsonElement> ReadRateLimitsAsync(CancellationToken cancellationToken) =>
        RequestAsync("account/rateLimits/read", new { }, cancellationToken);

    public Task<JsonElement> ReadUsageAsync(CancellationToken cancellationToken) =>
        RequestAsync("account/usage/read", new { }, cancellationToken);

    public Task<JsonElement> ReadThreadsAsync(CancellationToken cancellationToken) =>
        RequestAsync("thread/list", new { limit = 50, sortKey = "updated_at", sortDirection = "desc",
            archived = false, useStateDbOnly = true,
            sourceKinds = new[] { "cli", "vscode", "appServer", "exec", "unknown" } }, cancellationToken);

    private async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate request id.");

        try
        {
            await SendAsync(new { method, @params = parameters, id }, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(object value, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("Codex app-server is not running.");
        var json = JsonSerializer.Serialize(value);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;

                JsonDocument document;
                try { document = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }
                using var parsed = document;
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id)) continue;
                if (!_pending.TryGetValue(id, out var completion)) continue;

                if (root.TryGetProperty("error", out var error))
                {
                    completion.TrySetException(new InvalidOperationException("Codex could not complete the usage request."));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            foreach (var completion in _pending.Values) completion.TrySetException(exception);
        }
        finally
        {
            foreach (var completion in _pending.Values)
                completion.TrySetException(new IOException("Codex app-server stopped unexpectedly."));
        }
    }

    private static async Task DrainErrorsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null) { }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_process is null) return;
        try
        {
            _process.StandardInput.Close();
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch { }
        _process.Dispose();
        _process = null;
        if (_readerTask is not null) try { await _readerTask.ConfigureAwait(false); } catch { }
        _lifetime.Dispose();
    }
}
