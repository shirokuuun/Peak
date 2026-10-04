using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.Providers;

public sealed class ClaudeSnapshotProvider : IUsageProvider, IUsageChangeSource
{
    private readonly string _snapshotDirectory;
    private FileSystemWatcher? _watcher;
    public event EventHandler? Changed;

    public ClaudeSnapshotProvider(string? snapshotDirectory = null)
    {
        _snapshotDirectory = snapshotDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch", "claude");
        Directory.CreateDirectory(_snapshotDirectory);
        _watcher = new FileSystemWatcher(_snapshotDirectory, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Renamed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Error += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.EnableRaisingEvents = true;
    }

    public string Id => "claude";
    public string DisplayName => "Claude";

    public Task<ProviderProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        var available = ExecutableLocator.Find("claude") is not null;
        return Task.FromResult(new ProviderProbe(available, null,
            new(false, true, true, true), available ? null : "Claude Code was not found."));
    }

    public async Task<ProviderUsage> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_snapshotDirectory)) return Waiting("Run Claude Code with the Peak bridge configured.");
        var file = Directory.EnumerateFiles(_snapshotDirectory, "*.json")
            .Select(path => new FileInfo(path)).OrderByDescending(item => item.LastWriteTimeUtc).FirstOrDefault();
        if (file is null) return Waiting("Waiting for Claude Code usage data.");

        try
        {
            await using var stream = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var observedAt = root.TryGetProperty("observedAt", out var observed) &&
                             DateTimeOffset.TryParse(observed.GetString(), out var parsed) ? parsed :
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            var now = DateTimeOffset.UtcNow;
            var source = new SourceObservation("claude-statusline", SourceKind.StatusLine,
                CodexUsageMapper.GetString(root, "version"), now, observedAt, null,
                observedAt.AddMinutes(5), true);
            var windows = new List<UsageWindow>();
            if (root.TryGetProperty("rateLimits", out var limits))
            {
                AddWindow(limits, "fiveHour", "5-hour", TimeSpan.FromHours(5), source, now, windows);
                AddWindow(limits, "sevenDay", "Weekly", TimeSpan.FromDays(7), source, now, windows);
            }

            return new ProviderUsage(1, "claude", "claude-personal", "Claude account", null,
                windows.Count > 0 ? ConnectionState.Ready : ConnectionState.Waiting, [source], windows, [], observedAt,
                windows.Count > 0 ? null : new ProviderError("quota_missing", "No quota fields yet. Claude Code 2.1.251+ and a supported subscription are required; use Claude Code once.", true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Waiting("Claude usage snapshot could not be read.") with
            {
                Connection = ConnectionState.Error,
                Error = new ProviderError("snapshot_invalid", "Claude usage snapshot could not be read. It will be retried.", true)
            };
        }
    }

    private static void AddWindow(JsonElement limits, string key, string label, TimeSpan duration,
        SourceObservation source, DateTimeOffset now, List<UsageWindow> windows)
    {
        if (!limits.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object ||
            !CodexUsageMapper.TryGetDouble(value, out var percent, "usedPercentage") || !double.IsFinite(percent) || percent < 0) return;
        DateTimeOffset? reset = null;
        if (CodexUsageMapper.TryGetInt64(value, out var seconds, "resetsAt"))
            reset = CodexUsageMapper.SafeReset(seconds);
        var freshness = UsageSelection.GetFreshness(source, now);
        if (reset <= now) freshness = Freshness.AwaitingUpdate;

        windows.Add(new UsageWindow($"claude:{key}", "claude", label, "account",
            "subscription-quota", "provider-defined", duration, null,
            reset is null ? null : Metric<DateTimeOffset>.Reported(reset.Value, source.Id),
            Metric<double>.Reported(percent, source.Id), freshness,
            Metric<bool>.Reported(percent >= 100, source.Id), source.Id));
    }

    private ProviderUsage Waiting(string message) => ProviderUsage.Waiting(Id, DisplayName) with
    {
        Error = new ProviderError("waiting_for_cli", message, true)
    };

    public ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        _watcher = null;
        return ValueTask.CompletedTask;
    }
}
