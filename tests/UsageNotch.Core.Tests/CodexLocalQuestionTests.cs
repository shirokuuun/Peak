using System.Text.Json;
using System.Text.Json.Nodes;
using UsageNotch.Core;
using UsageNotch.Providers;

namespace UsageNotch.Core.Tests;

public sealed class CodexLocalQuestionTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private static readonly DateTimeOffset At = new(2026, 10, 6, 7, 0, 0, TimeSpan.Zero);

    private static string Lifecycle(string type, int second, string turn = "turn1") => JsonSerializer.Serialize(new
    { timestamp = At.AddSeconds(second), type = "event_msg", payload = new { type, turn_id = turn, privatePrompt = "PRIVATE-PROMPT" } });

    private static string Question(int second, string callId = "question1", string type = "function_call", string name = "request_user_input") =>
        JsonSerializer.Serialize(new { timestamp = At.AddSeconds(second), type = "response_item",
            payload = new { type, name, call_id = callId, arguments = "PRIVATE-QUESTION", input = "PRIVATE-INPUT" } });

    private static string Output(int second, string callId = "question1", string type = "function_call_output") =>
        JsonSerializer.Serialize(new { timestamp = At.AddSeconds(second), type = "response_item",
            payload = new { type, call_id = callId, output = "PRIVATE-ANSWER" } });

    private static CodexTask Parse(string[] lines, int now = 10) =>
        CodexTaskMonitor.ParseLocal(Id, "Chat", "notch", lines, At.AddSeconds(now))!;

    [Theory]
    [InlineData("function_call", "request_user_input")]
    [InlineData("function_call", "functions.request_user_input")]
    [InlineData("custom_tool_call", "request_user_input")]
    public void PendingQuestionAppearsAsAttentionWithoutHooks(string type, string name)
    {
        var task = Parse([Lifecycle("task_started", 0), Question(1, type: type, name: name)]);
        Assert.Equal(CodexTaskState.InputRequested, task.State);
        Assert.True(task.NeedsAttention);
        Assert.False(task.FromHooks);
        Assert.Equal(At, task.StartedAt);
        Assert.Equal("Question for you · local log", task.Activity);
        Assert.NotNull(task.AttentionKey);
        var summary = TaskActivitySummary.Select([task], At.AddSeconds(10));
        Assert.Equal(IslandActivityKind.Attention, summary.Kind);
        Assert.True(NotchPresence.Evaluate(new() { PresenceMode = "Active" }, summary,
            At.AddSeconds(10), pointerOver: false, fullscreen: false).Visible);
    }

    [Fact]
    public void UnrelatedToolOutputDoesNotDismissOrRefreshQuestion()
    {
        var before = Parse([Lifecycle("task_started", 0), Question(1)]);
        var after = Parse([Lifecycle("task_started", 0), Question(1), Output(2, "other-tool")]);
        Assert.Equal(CodexTaskState.InputRequested, after.State);
        Assert.Equal(before.AttentionKey, after.AttentionKey);
        Assert.Equal(before.ObservedAt, after.ObservedAt);
    }

    [Theory]
    [InlineData("function_call_output")]
    [InlineData("custom_tool_call_output")]
    public void MatchingAnswerClearsQuestion(string outputType)
    {
        var task = Parse([Lifecycle("task_started", 0), Question(1), Output(2, type: outputType)]);
        Assert.Equal(CodexTaskState.RecentActivity, task.State);
        Assert.False(task.NeedsAttention);
        Assert.Null(task.AttentionKey);
        Assert.Equal("Answer received · local log", task.Activity);
        Assert.False(TaskActivitySummary.Select([task], At.AddSeconds(10)).IsVisible);
    }

    [Fact]
    public void AnswerOnlyClearsMatchingQuestion()
    {
        var task = Parse([Lifecycle("task_started", 0), Question(1), Question(2, "question2"), Output(3)]);
        var remaining = Parse([Lifecycle("task_started", 0), Question(2, "question2")]);
        Assert.Equal(CodexTaskState.InputRequested, task.State);
        Assert.Equal(remaining.AttentionKey, task.AttentionKey);
    }

    [Theory]
    [InlineData("task_complete", CodexTaskState.Idle)]
    [InlineData("turn_aborted", CodexTaskState.Interrupted)]
    [InlineData("task_started", CodexTaskState.RecentActivity)]
    public void TurnLifecycleClearsQuestion(string type, CodexTaskState state)
    {
        var task = Parse([Lifecycle("task_started", 0), Question(1), Lifecycle(type, 2)]);
        Assert.Equal(state, task.State);
        Assert.False(task.NeedsAttention);
        Assert.Null(task.AttentionKey);
    }

    [Fact]
    public void LatePreviousTurnCompletionCannotDismissNewQuestion()
    {
        var task = Parse([Lifecycle("task_started", 0), Question(1), Lifecycle("task_started", 2, "turn2"),
            Question(3, "question2"), Lifecycle("task_complete", 4), Output(5)]);
        Assert.Equal(CodexTaskState.InputRequested, task.State);
        Assert.Equal(At.AddSeconds(3), task.ObservedAt);
        Assert.Equal(At.AddSeconds(2), task.StartedAt);
    }

    [Fact]
    public void StaleQuestionDoesNotRemainAnActiveAlert()
    {
        var task = Parse([Lifecycle("task_started", 0), Question(1)], now: 302);
        Assert.Equal(CodexTaskState.Stale, task.State);
        Assert.False(task.NeedsAttention);
    }

    [Fact]
    public void InvalidOrUnrelatedCallsDoNotInventQuestions()
    {
        var task = Parse(["partial-json", Lifecycle("task_started", 0),
            Question(1, name: "request_permissions"), Question(2, name: "request_user_input_async"),
            Question(3, callId: ""), Question(200)]);
        Assert.Equal(CodexTaskState.RecentActivity, task.State);
        Assert.False(task.NeedsAttention);
    }

    [Fact]
    public void CacheKeepsOnlyQuestionLifecycleAndFingerprints()
    {
        var raw = new[] { Lifecycle("task_started", 0), Question(1, "PRIVATE-CALL-ID"), Output(2, "PRIVATE-CALL-ID") };
        var sanitized = raw.Select(CodexTaskMonitor.SanitizeLocalLine).Select(line => line!).ToArray();
        var cached = string.Join('\n', sanitized);
        foreach (var secret in new[] { "PRIVATE-PROMPT", "PRIVATE-QUESTION", "PRIVATE-INPUT", "PRIVATE-ANSWER", "PRIVATE-CALL-ID" })
            Assert.DoesNotContain(secret, cached);
        Assert.Equal(Parse(raw), Parse(sanitized));
        Assert.Equal(Parse(raw[..2]).AttentionKey, Parse(sanitized[..2]).AttentionKey);
    }

    [Fact]
    public async Task MonitorDetectsQuestionFromSanitizedTailThenClearsItAfterAnswer()
    {
        using var temp = new TaskTemp();
        var now = DateTimeOffset.UtcNow;
        var sessions = Path.Combine(temp.Path, "sessions", now.ToString("yyyy/MM/dd"));
        Directory.CreateDirectory(sessions);
        var path = Path.Combine(sessions, "rollout-" + Id + ".jsonl");
        string Current(string line, int second)
        {
            var item = JsonNode.Parse(line)!;
            item["timestamp"] = now.AddSeconds(second - 10);
            return item.ToJsonString();
        }
        File.WriteAllLines(path, [Current(Lifecycle("task_started", 0), 0), Current(Question(1), 1)]);
        await using var monitor = new CodexTaskMonitor(null, temp.Path, Path.Combine(temp.Path, "events"));
        var before = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(CodexTaskState.InputRequested, before.State);
        var cached = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(before.AttentionKey, cached.AttentionKey);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, _) => changed.TrySetResult();
        File.AppendAllLines(path, [Current(Output(2), 2)]);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var after = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.False(after.NeedsAttention);
        Assert.Equal(CodexTaskState.RecentActivity, after.State);
    }

    [Theory]
    [InlineData(CodexTaskState.Idle, -1, CodexTaskState.InputRequested)]
    [InlineData(CodexTaskState.Working, 1, CodexTaskState.InputRequested)]
    [InlineData(CodexTaskState.ApprovalRequested, 1, CodexTaskState.ApprovalRequested)]
    [InlineData(CodexTaskState.Idle, 1, CodexTaskState.Idle)]
    [InlineData(CodexTaskState.Interrupted, 1, CodexTaskState.Interrupted)]
    public void HookReconciliationPreservesPendingQuestionsAndNewerTerminalEvents(CodexTaskState hookState, int offset, CodexTaskState expected)
    {
        var local = Parse([Lifecycle("task_started", 0), Question(1)]);
        var hook = local with { State = hookState, ObservedAt = local.ObservedAt.AddSeconds(offset), FromHooks = true, AttentionKey = "hook-key" };
        Assert.Equal(expected, CodexTaskMonitor.ReconcileLocal(hook, local).State);
    }

    [Fact]
    public void LocalAnswerClearsMissedHookQuestionCompletion()
    {
        var local = Parse([Lifecycle("task_started", 0), Question(1), Output(2)]);
        var hook = local with { State = CodexTaskState.InputRequested, ObservedAt = At.AddSeconds(1), FromHooks = true, AttentionKey = "hook-key" };
        var reconciled = CodexTaskMonitor.ReconcileLocal(hook, local);
        Assert.False(reconciled.NeedsAttention);
        Assert.Null(reconciled.AttentionKey);
    }

    private sealed class TaskTemp : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));
        public TaskTemp() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            if (!full.StartsWith(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UsageNotch.Tests") + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test path.");
            Directory.Delete(full, true);
        }
    }
}
