using System.Text;
using System.Security.Cryptography;
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
    private readonly FileSystemWatcher? _sessionWatcher;
    private readonly Dictionary<string, (long Size, DateTime Mtime, string[] Lines)> _tails = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CodexTaskMetadata> _metadata = [];
    private DateTimeOffset _catalogAt = DateTimeOffset.MinValue;
    public event EventHandler? Changed;

    public CodexTaskMonitor(CodexUsageProvider? catalog, string? codexRoot = null, string? eventsRoot = null)
    {
        _catalog = catalog;
        _codexRoot = codexRoot ?? Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _eventsRoot = eventsRoot ?? Path.Combine(PeakDataPaths.Root, "codex-tasks");
        Directory.CreateDirectory(_eventsRoot);
        _watcher = new(_eventsRoot, "*.jsonl") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += OnChanged; _watcher.Created += OnChanged; _watcher.Deleted += OnChanged;
        _watcher.EnableRaisingEvents = true;
        var sessionsRoot = Path.Combine(_codexRoot, "sessions");
        if (Directory.Exists(sessionsRoot))
        {
            _sessionWatcher = new(sessionsRoot, "*.jsonl") { IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            _sessionWatcher.Changed += OnChanged; _sessionWatcher.Created += OnChanged; _sessionWatcher.Deleted += OnChanged;
            _sessionWatcher.EnableRaisingEvents = true;
        }
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
            if (tasks.TryGetValue(id, out var local)) observed = ReconcileLocal(observed, local);
            tasks[id] = observed;
        }
        foreach (var key in _tails.Keys.Where(key => !readPaths.Contains(key)).ToArray()) _tails.Remove(key);
        var ordered = tasks.Values.OrderByDescending(t => t.NeedsAttention).ThenByDescending(t => t.State == CodexTaskState.Working)
            .ThenByDescending(t => t.ObservedAt).Take(8).ToArray();
        var status = hookTaskCount == 0 ? "Local questions and activity · approval monitoring needs setup in Settings" :
            "Hook observations and local questions · approvals are reviewed in Codex";
        return new(ordered, status, now);
    }

    internal static CodexTask ReconcileLocal(CodexTask observed, CodexTask local)
    {
        // A pending local question remains visible through unrelated tool hooks. Newer terminal hooks still win.
        var localQuestion = local.State == CodexTaskState.InputRequested && observed.State != CodexTaskState.ApprovalRequested &&
            (local.ObservedAt >= observed.ObservedAt || observed.State is CodexTaskState.Working or CodexTaskState.Stale or CodexTaskState.Unknown);
        var localCompletion = local.ObservedAt > observed.ObservedAt &&
            (local.State is CodexTaskState.Idle or CodexTaskState.Interrupted ||
             observed.State == CodexTaskState.InputRequested && local.Activity == "Answer received · local log");
        return localQuestion || localCompletion ? observed with { State = local.State, Activity = local.Activity,
            ObservedAt = local.ObservedAt, StartedAt = local.StartedAt ?? observed.StartedAt,
            AttentionKey = local.AttentionKey, FromHooks = false } : observed;
    }

    internal static CodexTask? ParseLocal(string id, string title, string project, IEnumerable<string> lines, DateTimeOffset now)
    {
        DateTimeOffset? lastAt = null;
        DateTimeOffset? startedAt = null;
        string? turn = null;
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var state = CodexTaskState.RecentActivity;
        var activity = "Local activity · approval status unavailable";
        foreach (var line in lines)
        {
            var e = ParseLocalEvent(line);
            if (e is null || e.ObservedAt > now.AddMinutes(2)) continue;
            if (e.Type == "task_started")
            {
                turn = e.TurnId; startedAt = e.ObservedAt; pending.Clear();
                state = CodexTaskState.RecentActivity; activity = "Turn started · local log";
            }
            else if (turn is not null && e.TurnId is not null && turn != e.TurnId) continue;
            else if (e.Type is "task_complete" or "turn_aborted")
            {
                pending.Clear();
                state = e.Type == "task_complete" ? CodexTaskState.Idle : CodexTaskState.Interrupted;
                activity = e.Type == "task_complete" ? "Response finished · local log" : "Interrupted · local log";
            }
            else if (e.Type == "input_requested")
            {
                pending.Add(e.OperationKey!); state = CodexTaskState.InputRequested; activity = "Question for you · local log";
            }
            else if (e.Type == "input_resolved")
            {
                // Other tool results must neither dismiss a question nor refresh its observation age.
                if (!pending.Remove(e.OperationKey!)) continue;
                state = pending.Count > 0 ? CodexTaskState.InputRequested : CodexTaskState.RecentActivity;
                activity = pending.Count > 0 ? "Question for you · local log" : "Answer received · local log";
            }
            lastAt = e.ObservedAt;
        }
        if (lastAt is null) return null;
        if (state is CodexTaskState.RecentActivity or CodexTaskState.InputRequested && now - lastAt > TimeSpan.FromMinutes(5))
            state = CodexTaskState.Stale;
        var attentionKey = pending.Count == 0 ? null : string.Join(",", pending.Order(StringComparer.Ordinal).Select(key => turn + "|" + key));
        return new(id, title, project, state, activity, lastAt.Value, false, attentionKey, StartedAt: startedAt);
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

    internal static string? SanitizeLocalLine(string line)
    {
        var e = ParseLocalEvent(line);
        return e is null ? null : JsonSerializer.Serialize(new { timestamp = e.ObservedAt,
            type = "event_msg", payload = new { type = e.Type, turn_id = e.TurnId, operation_key = e.OperationKey } });
    }

    private sealed record LocalTaskEvent(string Type, DateTimeOffset ObservedAt, string? TurnId = null, string? OperationKey = null);

    private static LocalTaskEvent? ParseLocalEvent(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object ||
                !DateTimeOffset.TryParse(CodexUsageMapper.GetString(root, "timestamp"), out var at)) return null;
            var type = CodexUsageMapper.GetString(payload, "type");
            if (CodexUsageMapper.GetString(root, "type") == "event_msg")
            {
                if (type is "task_started" or "task_complete" or "turn_aborted" or "item_completed" or "token_count")
                    return new(type, at, CodexUsageMapper.GetString(payload, "turn_id"));
                // The cache stores only these normalized question events, never arguments or answers.
                if (type is "input_requested" or "input_resolved" &&
                    CodexUsageMapper.GetString(payload, "operation_key") is { Length: 24 } key && key.All(Uri.IsHexDigit))
                    return new(type, at, OperationKey: key);
                return null;
            }
            if (CodexUsageMapper.GetString(root, "type") != "response_item") return null;
            var name = CodexUsageMapper.GetString(payload, "name");
            var question = (type is "function_call" or "custom_tool_call") && (name is "request_user_input" or "functions.request_user_input");
            var output = type is "function_call_output" or "custom_tool_call_output";
            if ((!question && !output) || CodexUsageMapper.GetString(payload, "call_id") is not { Length: > 0 and <= 256 } callId) return null;
            var operationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(callId)))[..24];
            return new(question ? "input_requested" : "input_resolved", at, OperationKey: operationKey);
        }
        catch (JsonException) { return null; }
    }

    public ValueTask DisposeAsync() { _watcher.Dispose(); _sessionWatcher?.Dispose(); return ValueTask.CompletedTask; }
}
