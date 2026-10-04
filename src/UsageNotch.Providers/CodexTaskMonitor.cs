using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsageNotch.Core;

namespace UsageNotch.Providers;

public sealed record CodexTaskMetadata(string Id, string Title, string Project)
{
    internal static IReadOnlyList<CodexTaskMetadata> Parse(JsonElement response)
    {
        var tasks = new List<CodexTaskMetadata>();
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return tasks;
        foreach (var thread in data.EnumerateArray())
        {
            if (thread.ValueKind != JsonValueKind.Object) continue;
            var id = CodexUsageMapper.GetString(thread, "id");
            if (!CodexTaskEvents.ValidThreadId(id)) continue;
            var title = CodexUsageMapper.GetString(thread, "name");
            if (string.IsNullOrWhiteSpace(title)) title = CodexUsageMapper.GetString(thread, "preview")?.Split('\n')[0];
            if (string.IsNullOrWhiteSpace(title)) title = "Codex chat · " + id![..8];
            var cwd = CodexUsageMapper.GetString(thread, "cwd") ?? "";
            var project = cwd.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault() ?? "Local project";
            tasks.Add(new(id!, title[..Math.Min(160, title.Length)], project));
        }
        return tasks;
    }
}

public sealed class CodexTaskMonitor : ICodexTaskMonitor
{
    private readonly CodexUsageProvider? _catalog;
    private readonly string _codexRoot, _eventsRoot;
    private readonly FileSystemWatcher _watcher;
    private readonly Dictionary<string, (long Size, DateTime Mtime, string[] Lines)> _tails = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CodexTaskMetadata> _metadata = [];
    private DateTimeOffset _catalogAt = DateTimeOffset.MinValue;
    public event EventHandler? Changed;

    public CodexTaskMonitor(CodexUsageProvider? catalog, string? codexRoot = null, string? eventsRoot = null)
    {
        _catalog = catalog;
        _codexRoot = codexRoot ?? Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _eventsRoot = eventsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch", "codex-tasks");
        Directory.CreateDirectory(_eventsRoot);
        _watcher = new(_eventsRoot, "*.jsonl") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += OnChanged; _watcher.Created += OnChanged; _watcher.Deleted += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    public async Task<CodexTaskSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_catalog is not null && now - _catalogAt > TimeSpan.FromSeconds(60))
        {
            _catalogAt = now;
            try { _metadata = await _catalog.ReadTaskCatalogAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { /* Local observations remain useful if the CLI catalog is unavailable. */ }
        }
        var tasks = new Dictionary<string, CodexTask>();
        var readPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localFiles = Enumerable.Range(-1, 8).Select(offset => Path.Combine(_codexRoot, "sessions",
                now.UtcDateTime.AddDays(-offset).ToString("yyyy/MM/dd"))).Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.jsonl"))
            .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).Take(16);
        foreach (var file in localFiles)
        {
            var match = Regex.Match(file.Name, @"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\.jsonl$");
            if (!match.Success) continue;
            var id = match.Groups[1].Value;
            var metadata = _metadata.FirstOrDefault(t => t.Id == id);
            var lines = await TailAsync(file, readPaths, cancellationToken).ConfigureAwait(false);
            var task = ParseLocal(id, metadata?.Title ?? "Codex chat · " + id[..8], metadata?.Project ?? "Local project", lines, now);
            if (task is not null) tasks[id] = task;
        }
        var hookFiles = Directory.EnumerateFiles(_eventsRoot, "*.jsonl").Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc).Take(32);
        var hookTaskCount = 0;
        foreach (var file in hookFiles)
        {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (!CodexTaskEvents.ValidThreadId(id)) continue;
            var events = new List<CodexTaskEvent>();
            foreach (var line in await TailAsync(file, readPaths, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var e = JsonSerializer.Deserialize<CodexTaskEvent>(line);
                    if (e is { SchemaVersion: 1 } && e.ThreadId == id && CodexTaskEvents.HookNames.Contains(e.EventName) &&
                        e.ObservedAt <= now.AddMinutes(2) && e.ObservedAt >= now.AddDays(-7)) events.Add(e);
                }
                catch (JsonException) { }
            }
            if (events.Count == 0) continue;
            hookTaskCount++;
            var metadata = _metadata.FirstOrDefault(t => t.Id == id);
            var observed = CodexTaskEvents.Reduce(events, metadata?.Title ?? tasks.GetValueOrDefault(id)?.Title ?? "Codex chat · " + id[..8], now);
            // A later completed/interrupted local turn clears a missed hook completion. It never invents an approval.
            if (tasks.TryGetValue(id, out var local) && local.ObservedAt > observed.ObservedAt &&
                local.State is CodexTaskState.Idle or CodexTaskState.Interrupted)
                observed = observed with { State = local.State, Activity = local.Activity, ObservedAt = local.ObservedAt, StartedAt = local.StartedAt };
            tasks[id] = observed;
        }
        foreach (var key in _tails.Keys.Where(key => !readPaths.Contains(key)).ToArray()) _tails.Remove(key);
        var ordered = tasks.Values.OrderByDescending(t => t.NeedsAttention).ThenByDescending(t => t.State == CodexTaskState.Working)
            .ThenByDescending(t => t.ObservedAt).Take(8).ToArray();
        var status = hookTaskCount == 0 ? "Approval monitoring needs setup · Settings → Connect task activity" :
            "Hook observations · other chats may need reconnecting · approvals are reviewed in Codex";
        return new(ordered, status, now);
    }

    internal static CodexTask? ParseLocal(string id, string title, string project, IEnumerable<string> lines, DateTimeOffset now)
    {
        DateTimeOffset? lastAt = null;
        DateTimeOffset? startedAt = null;
        var state = CodexTaskState.RecentActivity;
        var activity = "Local activity · approval status unavailable";
        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || CodexUsageMapper.GetString(root, "type") != "event_msg" ||
                    !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object ||
                    !DateTimeOffset.TryParse(CodexUsageMapper.GetString(root, "timestamp"), out var at) || at > now.AddMinutes(2)) continue;
                var type = CodexUsageMapper.GetString(payload, "type");
                if (type is not ("task_started" or "task_complete" or "turn_aborted" or "item_completed" or "token_count")) continue;
                lastAt = at;
                if (type == "task_started") { startedAt = at; state = CodexTaskState.RecentActivity; activity = "Turn started · local log"; }
                else if (type == "task_complete") { state = CodexTaskState.Idle; activity = "Response finished · local log"; }
                else if (type == "turn_aborted") { state = CodexTaskState.Interrupted; activity = "Interrupted · local log"; }
            }
            catch (JsonException) { }
        }
        if (lastAt is null) return null;
        if (state == CodexTaskState.RecentActivity && now - lastAt > TimeSpan.FromMinutes(5)) state = CodexTaskState.Stale;
        return new(id, title, project, state, activity, lastAt.Value, false, StartedAt: startedAt);
    }

    private async Task<string[]> TailAsync(FileInfo file, HashSet<string> readPaths, CancellationToken cancellationToken)
    {
        readPaths.Add(file.FullName);
        if (_tails.TryGetValue(file.FullName, out var cached) && cached.Size == file.Length && cached.Mtime == file.LastWriteTimeUtc)
            return cached.Lines;
        try
        {
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous);
            var offset = Math.Max(0, stream.Length - 256 * 1024);
            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            if (offset > 0) await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // Keep only lifecycle metadata in the cache; do not retain prompts, tool input/output, or token records.
            if (!Path.GetDirectoryName(file.FullName)!.Equals(_eventsRoot, StringComparison.OrdinalIgnoreCase))
                lines = lines.Select(SanitizeLocalLine).Where(line => line is not null).Select(line => line!).ToArray();
            _tails[file.FullName] = (file.Length, file.LastWriteTimeUtc, lines);
            return lines;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string? SanitizeLocalLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || CodexUsageMapper.GetString(root, "type") != "event_msg" ||
                !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;
            var type = CodexUsageMapper.GetString(payload, "type");
            if (type is not ("task_started" or "task_complete" or "turn_aborted" or "item_completed" or "token_count")) return null;
            return JsonSerializer.Serialize(new { timestamp = CodexUsageMapper.GetString(root, "timestamp"),
                type = "event_msg", payload = new { type } });
        }
        catch (JsonException) { return null; }
    }

    public ValueTask DisposeAsync() { _watcher.Dispose(); return ValueTask.CompletedTask; }
}
