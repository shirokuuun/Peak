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
    private sealed record LocalTail(long Size, DateTime Mtime, DateTime Created, long Offset, byte[] BoundaryHash, LocalTaskState State);
    private readonly Dictionary<string, LocalTail> _localTails = new(StringComparer.OrdinalIgnoreCase);
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
        // Resumed chats keep their original date folder. Select recent writes across all folders,
        // then read bounded tails from only the newest files.
        var sessionsRoot = Path.Combine(_codexRoot, "sessions");
        var localFiles = (Directory.Exists(sessionsRoot) ? Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories) : [])
            .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).Take(16);
        foreach (var file in localFiles)
        {
            var match = Regex.Match(file.Name, @"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\.jsonl$");
            if (!match.Success) continue;
            var id = match.Groups[1].Value;
            var metadata = _metadata.FirstOrDefault(t => t.Id == id);
            var task = await ReadLocalAsync(file, id, metadata?.Title ?? "Codex chat · " + id[..8],
                metadata?.Project ?? "Local project", readPaths, now, cancellationToken).ConfigureAwait(false);
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
        foreach (var key in _localTails.Keys.Where(key => !readPaths.Contains(key)).ToArray()) _localTails.Remove(key);
        var ordered = tasks.Values.OrderByDescending(t => t.NeedsAttention).ThenByDescending(t => t.State == CodexTaskState.Working)
            .ThenByDescending(t => t.ObservedAt).Take(8).ToArray();
        var status = hookTaskCount == 0 ? "Local work and questions · approval monitoring needs setup in Settings" :
            "Hook observations and local work · approvals are reviewed in Codex";
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
        var localWork = local.State == CodexTaskState.Working && local.ObservedAt > observed.ObservedAt &&
            (!observed.NeedsAttention || local.StartedAt > observed.ObservedAt);
        return localQuestion || localCompletion || localWork ? observed with { State = local.State, Activity = local.Activity,
            ObservedAt = local.ObservedAt, StartedAt = localWork ? local.StartedAt : local.StartedAt ?? observed.StartedAt,
            AttentionKey = local.AttentionKey, FromHooks = false } : observed;
    }

    internal static CodexTask? ParseLocal(string id, string title, string project, IEnumerable<string> lines, DateTimeOffset now)
    {
        var state = new LocalTaskState(id);
        foreach (var line in lines) state.Apply(ParseLocalEvent(line), now);
        return state.Snapshot(title, project, now);
    }

    // Retain lifecycle state, rather than raw log content, as the bounded read window
    // advances. Large tool results cannot discard the current turn or a pending question.
    private sealed class LocalTaskState(string threadId)
    {
        private DateTimeOffset? _lastAt, _startedAt;
        private string? _turn;
        private bool _working, _turnEnded;
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private CodexTaskState _state = CodexTaskState.RecentActivity;
        private string _activity = "Local activity · approval status unavailable";

        public void Apply(LocalTaskEvent? e, DateTimeOffset now)
        {
            if (e is null || e.ObservedAt > now.AddMinutes(2) || e.ThreadId is not null &&
                !string.Equals(e.ThreadId, threadId, StringComparison.OrdinalIgnoreCase)) return;
            if (e.Type == "turn_context")
            {
                // A context identifies a turn; it is not evidence that work is running.
                if (e.TurnId is null || _turn == e.TurnId || e.ObservedAt < _lastAt) return;
                _turn = e.TurnId; _startedAt = _lastAt = null; _working = _turnEnded = false; _pending.Clear();
                _state = CodexTaskState.RecentActivity; _activity = "Local activity · approval status unavailable";
                return;
            }
            if (e.Type == "task_started")
            {
                _turn = e.TurnId; _startedAt = e.ObservedAt; _pending.Clear(); _working = true; _turnEnded = false;
                _state = CodexTaskState.Working; _activity = "Working on your request · local log";
            }
            else if (_turn is not null && e.TurnId is not null && _turn != e.TurnId) return;
            else if (e.Type is "task_complete" or "turn_aborted")
            {
                _pending.Clear();
                _turn ??= e.TurnId; _working = false; _turnEnded = true;
                _state = e.Type == "task_complete" ? CodexTaskState.Idle : CodexTaskState.Interrupted;
                _activity = e.Type == "task_complete" ? "Response finished · local log" : "Interrupted · local log";
            }
            else if (e.Type == "input_requested")
            {
                if (_turnEnded) return;
                _pending.Add(e.OperationKey!); _state = CodexTaskState.InputRequested; _activity = "Question for you · local log";
            }
            else if (e.Type == "input_resolved")
            {
                // Other tool results must neither dismiss a question nor refresh its observation age.
                if (!_pending.Remove(e.OperationKey!)) return;
                _state = _pending.Count > 0 ? CodexTaskState.InputRequested : _working ? CodexTaskState.Working : CodexTaskState.RecentActivity;
                _activity = _pending.Count > 0 ? "Question for you · local log" : "Answer received · local log";
            }
            else
            {
                // Late tool results must not reopen an ended turn or renew a pending question.
                if (_turnEnded || _pending.Count > 0) return;
                if (e.Type == "turn_activity" || e.Type == "model_activity" && (_turn is not null || e.TurnId is not null))
                {
                    _turn ??= e.TurnId; _working = true; _state = CodexTaskState.Working;
                    _activity = e.WorkKind == "thinking" ? "Thinking · local log" : "Working on your request · local log";
                }
            }
            _lastAt = e.ObservedAt;
        }

        public CodexTask? Snapshot(string title, string project, DateTimeOffset now)
        {
            if (_lastAt is null) return null;
            var state = _state;
            if (state is CodexTaskState.RecentActivity or CodexTaskState.Working or CodexTaskState.InputRequested && now - _lastAt > TimeSpan.FromMinutes(5))
                state = CodexTaskState.Stale;
            var key = _pending.Count == 0 ? null : string.Join(",", _pending.Order(StringComparer.Ordinal).Select(key => _turn + "|" + key));
            return new(threadId, title, project, state, _activity, _lastAt.Value, false, key, StartedAt: _startedAt);
        }
    }

    private async Task<CodexTask?> ReadLocalAsync(FileInfo file, string id, string title, string project,
        HashSet<string> readPaths, DateTimeOffset now, CancellationToken cancellationToken)
    {
        readPaths.Add(file.FullName);
        _localTails.TryGetValue(file.FullName, out var cached);
        if (cached is not null && cached.Size == file.Length && cached.Mtime == file.LastWriteTimeUtc && cached.Created == file.CreationTimeUtc)
            return cached.State.Snapshot(title, project, now);
        try
        {
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous);
            var size = stream.Length;
            var append = cached is not null && cached.Created == file.CreationTimeUtc && size >= cached.Size &&
                (size > cached.Size || cached.Mtime == file.LastWriteTimeUtc);
            if (append)
            {
                // A file can be rewritten to a larger size between polls. Compare a
                // fingerprint at the previous boundary before reusing its lifecycle state.
                var boundary = await BoundaryHashAsync(stream, cached!.Offset, cancellationToken).ConfigureAwait(false);
                append = cached.BoundaryHash.AsSpan().SequenceEqual(boundary);
            }
            var state = append ? cached!.State : new LocalTaskState(id);
            var previousOffset = append ? cached!.Offset : 0;
            var offset = Math.Max(previousOffset, size - 256 * 1024);
            var skipFirst = offset > previousOffset;
            stream.Seek(offset, SeekOrigin.Begin);
            var bytes = new byte[(int)(size - offset)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
            var first = 0;
            if (skipFirst)
            {
                var newline = Array.IndexOf(bytes, (byte)'\n');
                first = newline >= 0 ? newline + 1 : bytes.Length;
            }
            if (lastNewline >= first)
                foreach (var line in Encoding.UTF8.GetString(bytes, first, lastNewline - first + 1).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    state.Apply(ParseLocalEvent(line), now);
            // Keep the offset before a partial record and reread it when its final bytes arrive.
            var nextOffset = lastNewline >= 0 ? offset + lastNewline + 1 : skipFirst ? size : offset;
            var boundaryHash = await BoundaryHashAsync(stream, nextOffset, cancellationToken).ConfigureAwait(false);
            _localTails[file.FullName] = new(size, file.LastWriteTimeUtc, file.CreationTimeUtc, nextOffset, boundaryHash, state);
            return state.Snapshot(title, project, now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return cached?.State.Snapshot(title, project, now); }
    }

    private static async Task<byte[]> BoundaryHashAsync(FileStream stream, long offset, CancellationToken cancellationToken)
    {
        var bytes = new byte[(int)Math.Min(64, offset)];
        stream.Seek(offset - bytes.Length, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return SHA256.HashData(bytes);
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
            // Hook files already contain sanitized bridge events. Local provider logs
            // use ReadLocalAsync, whose cache contains lifecycle state only.
            _tails[file.FullName] = (file.Length, file.LastWriteTimeUtc, lines);
            return lines;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    internal static string? SanitizeLocalLine(string line)
    {
        var e = ParseLocalEvent(line);
        return e is null ? null : JsonSerializer.Serialize(new { timestamp = e.ObservedAt,
            type = "event_msg", payload = new { type = e.Type, turn_id = e.TurnId, thread_id = e.ThreadId, operation_key = e.OperationKey, work_kind = e.WorkKind } });
    }

    private sealed record LocalTaskEvent(string Type, DateTimeOffset ObservedAt, string? TurnId = null, string? OperationKey = null,
        string? WorkKind = null, string? ThreadId = null);

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
            var recordType = CodexUsageMapper.GetString(root, "type");
            var turnId = CodexUsageMapper.GetString(payload, "turn_id");
            if (string.IsNullOrWhiteSpace(turnId) || turnId.Length > 256) turnId = null;
            var threadId = CodexUsageMapper.GetString(payload, "thread_id");
            if (recordType == "token_usage_record")
            {
                // VS Code emits these for model responses even when its legacy message
                // records have no turn id. No counts or response content enter the cache.
                return turnId is not null && CodexTaskEvents.ValidThreadId(threadId) &&
                    payload.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                    ? new("turn_activity", at, turnId, WorkKind: "working", ThreadId: threadId) : null;
            }
            if (recordType == "turn_context") return turnId is null ? null : new("turn_context", at, turnId, ThreadId: threadId);
            if (recordType == "event_msg")
            {
                if (type == "turn_context") return turnId is null ? null : new(type, at, turnId, ThreadId: threadId);
                if (type is "agent_message" or "agent_reasoning")
                    return new("model_activity", at, turnId, WorkKind: type == "agent_reasoning" ? "thinking" : "working", ThreadId: threadId);
                if (type == "model_activity" && CodexUsageMapper.GetString(payload, "work_kind") is "thinking" or "working")
                    return new(type, at, turnId, WorkKind: CodexUsageMapper.GetString(payload, "work_kind"), ThreadId: threadId);
                if (turnId is not null)
                {
                    if (type == "item_completed" && payload.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
                    {
                        var itemType = CodexUsageMapper.GetString(item, "type");
                        if (itemType is "Reasoning" or "AgentMessage")
                            return new("turn_activity", at, turnId, WorkKind: itemType == "Reasoning" ? "thinking" : "working");
                    }
                    if (type == "turn_activity" && CodexUsageMapper.GetString(payload, "work_kind") is "thinking" or "working")
                        return new(type, at, turnId, WorkKind: CodexUsageMapper.GetString(payload, "work_kind"), ThreadId: threadId);
                }
                if (type is "task_started" or "task_complete" or "turn_aborted" or "item_completed" or "token_count")
                    return new(type, at, turnId);
                // The cache stores only these normalized question events, never arguments or answers.
                if (type is "input_requested" or "input_resolved" &&
                    CodexUsageMapper.GetString(payload, "operation_key") is { Length: 24 } key && key.All(Uri.IsHexDigit))
                    return new(type, at, OperationKey: key);
                return null;
            }
            if (recordType != "response_item") return null;
            if (type == "reasoning" || type == "message" && CodexUsageMapper.GetString(payload, "role") == "assistant")
                return new("model_activity", at, turnId, WorkKind: type == "reasoning" ? "thinking" : "working", ThreadId: threadId);
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
