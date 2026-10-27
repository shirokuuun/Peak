using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageNotch.Core;

public enum CodexTaskState { Unknown, RecentActivity, Working, ApprovalRequested, InputRequested, Idle, Interrupted, Stale }

public sealed record CodexTaskEvent(int SchemaVersion, string ThreadId, string? TurnId, string EventName,
    DateTimeOffset ObservedAt, string Project, string? ToolName, string? OperationKey);

public sealed record CodexTask(string ThreadId, string Title, string Project, CodexTaskState State,
    string Activity, DateTimeOffset ObservedAt, bool FromHooks, string? AttentionKey = null, bool IsDemo = false,
    DateTimeOffset? StartedAt = null)
{
    public bool NeedsAttention => State is CodexTaskState.ApprovalRequested or CodexTaskState.InputRequested;
}

public sealed record CodexTaskSnapshot(IReadOnlyList<CodexTask> Tasks, string Status, DateTimeOffset ObservedAt);

public interface ICodexTaskMonitor : IAsyncDisposable
{
    event EventHandler? Changed;
    Task<CodexTaskSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public static class CodexTaskEvents
{
    public static readonly string[] HookNames = ["SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest",
        "PostToolUse", "Stop", "Interrupt", "SessionEnd"];

    public static bool ValidThreadId(string? value) => value is not null && Guid.TryParseExact(value, "D", out _);

    public static CodexTaskEvent? Parse(JsonElement input, DateTimeOffset now)
    {
        if (input.ValueKind != JsonValueKind.Object) return null;
        var name = String(input, "hook_event_name");
        if (name is null || !HookNames.Contains(name)) return null;
        var id = String(input, "session_id");
        // Subagent hooks can carry their parent's session_id. A transcript filename identifies the actual local chat.
        var transcript = String(input, "transcript_path");
        if (transcript is not null)
        {
            var match = Regex.Match(transcript, @"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\.jsonl$");
            if (match.Success) id = match.Groups[1].Value;
        }
        if (!ValidThreadId(id)) return null;
        var cwd = String(input, "cwd") ?? "";
        var project = cwd.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault() ?? "Local project";
        var tool = String(input, "tool_name");
        if (tool is not null)
        { tool = Regex.Replace(tool, @"[^a-zA-Z0-9_./:-]", ""); tool = tool[..Math.Min(80, tool.Length)]; }
        string? operation = null;
        if (tool is not null && input.TryGetProperty("tool_input", out var args))
        {
            // Inputs can contain secrets. Only a fingerprint used to match completion is retained.
            // Codex adds approval-only metadata (such as description) to shell/patch requests.
            // Their command is the shared identity across PreToolUse, PermissionRequest and PostToolUse.
            var identity = (tool is "Bash" or "apply_patch") && args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String ? command : args;
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer)) Canonical(writer, identity);
            operation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tool).Concat(buffer.ToArray()).ToArray()))[..24];
        }
        return new(1, id!, String(input, "turn_id"), name, now, project[..Math.Min(80, project.Length)], tool, operation);
    }

    public static CodexTask Reduce(IEnumerable<CodexTaskEvent> input, string title, DateTimeOffset now)
    {
        var events = input.Where(e => e.SchemaVersion == 1 && e.ObservedAt <= now.AddMinutes(2))
            .OrderBy(e => e.ObservedAt).ToArray();
        if (events.Length == 0) throw new ArgumentException("At least one valid event is required.");
        var last = events[0];
        string? turn = null;
        DateTimeOffset? startedAt = null;
        var state = CodexTaskState.Unknown;
        var pending = new Dictionary<string, (CodexTaskState State, DateTimeOffset RequestedAt)>();
        var running = new HashSet<string>();
        string activity = "";
        foreach (var e in events)
        {
            if (e.EventName == "UserPromptSubmit") { turn = e.TurnId; startedAt = e.ObservedAt; pending.Clear(); running.Clear(); }
            else if (turn is not null && e.TurnId is not null && turn != e.TurnId) continue;
            turn ??= e.TurnId;
            last = e;
            var key = e.OperationKey ?? e.ToolName ?? "unknown";
            switch (e.EventName)
            {
                case "SessionStart": startedAt = null; state = CodexTaskState.Idle; activity = "Session opened"; break;
                case "UserPromptSubmit": state = CodexTaskState.Working; activity = "Working on your request"; break;
                case "PreToolUse":
                    running.Add(key);
                    if (e.ToolName?.Contains("request_user_input", StringComparison.OrdinalIgnoreCase) == true)
                        pending[key] = (CodexTaskState.InputRequested, e.ObservedAt);
                    state = CodexTaskState.Working; activity = ToolLabel(e.ToolName); break;
                case "PermissionRequest":
                    pending[key] = (CodexTaskState.ApprovalRequested, e.ObservedAt);
                    state = CodexTaskState.Working; activity = ToolLabel(e.ToolName); break;
                case "PostToolUse":
                    pending.Remove(key); running.Remove(key);
                    state = CodexTaskState.Working; activity = running.Count > 0 ? "Tools running" : "Working on the next step"; break;
                case "Stop": state = CodexTaskState.Idle; pending.Clear(); running.Clear(); activity = "Response finished"; break;
                case "Interrupt": state = CodexTaskState.Interrupted; pending.Clear(); running.Clear(); activity = "Turn interrupted"; break;
                case "SessionEnd": state = CodexTaskState.Idle; pending.Clear(); running.Clear(); activity = "Session closed"; break;
            }
        }
        // Unrelated tool activity cannot extend the lifetime of an unanswered observation.
        foreach (var key in pending.Where(item => now - item.Value.RequestedAt > TimeSpan.FromMinutes(5)).Select(item => item.Key).ToArray())
            pending.Remove(key);
        if (pending.Count > 0) state = pending.Values.Any(item => item.State == CodexTaskState.ApprovalRequested)
            ? CodexTaskState.ApprovalRequested : CodexTaskState.InputRequested;
        if (now - last.ObservedAt > TimeSpan.FromMinutes(5) && state is CodexTaskState.Working or CodexTaskState.ApprovalRequested or CodexTaskState.InputRequested)
            state = CodexTaskState.Stale;
        var attentionKey = pending.Count == 0 ? null : string.Join(",", pending.Keys.Order(StringComparer.Ordinal).Select(key => turn + "|" + key));
        return new(last.ThreadId, title, last.Project, state, activity, last.ObservedAt, true, attentionKey, StartedAt: startedAt);
    }

    public static string ToolLabel(string? name) => name switch
    {
        "Bash" or "exec_command" or "write_stdin" => "Command execution",
        "apply_patch" or "Edit" or "Write" => "Editing files",
        "Read" or "read_file" => "Reading files",
        "exec" or "functions.exec" => "Running tools",
        null or "" => "Codex activity",
        _ when name.Contains("request_user_input", StringComparison.OrdinalIgnoreCase) => "Question for you",
        _ when name.Contains("imagegen", StringComparison.OrdinalIgnoreCase) => "Generating an image",
        _ when name.Contains("web__", StringComparison.OrdinalIgnoreCase) => "Researching",
        _ when name.Contains("node_repl", StringComparison.OrdinalIgnoreCase) || name.Contains("cua", StringComparison.OrdinalIgnoreCase) => "Checking the app",
        _ when name.StartsWith("mcp__", StringComparison.Ordinal) => "Using a connected tool",
        _ => name
    };

    private static string? String(JsonElement obj, string key) => obj.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Canonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(writer, item); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
}
