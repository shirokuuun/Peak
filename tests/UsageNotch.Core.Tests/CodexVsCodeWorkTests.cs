using System.Text.Json;
using UsageNotch.Core;
using UsageNotch.Providers;

namespace UsageNotch.Core.Tests;

public sealed class CodexVsCodeWorkTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string OtherId = "22222222-2222-2222-2222-222222222222";
    private static readonly DateTimeOffset At = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static string Record(string type, int second, object payload) =>
        JsonSerializer.Serialize(new { timestamp = At.AddSeconds(second), type, payload });
    private static string Usage(int second, string turn = "turn1", string thread = Id) =>
        Record("token_usage_record", second, new { thread_id = thread, turn_id = turn, response_id = "PRIVATE-RESPONSE",
            usage = new { input_tokens = 4321 }, turn_token_usage = new { total_tokens = 9876 } });
    private static string Event(string type, int second, string? turn = "turn1") =>
        Record("event_msg", second, new { type, turn_id = turn, message = "PRIVATE-MESSAGE" });
    private static string Question(int second) => Record("response_item", second,
        new { type = "function_call", name = "request_user_input", call_id = "PRIVATE-QUESTION-ID", arguments = "PRIVATE-QUESTION" });
    private static CodexTask Parse(IEnumerable<string> lines, int now = 10) =>
        CodexTaskMonitor.ParseLocal(Id, "Synthetic VS Code chat", "project", lines, At.AddSeconds(now))!;

    [Fact]
    public void VsCodeTailRecoversWorkingWithoutTheOriginalStartRecord()
    {
        var task = Parse([Event("token_count", 0, null),
            Record("response_item", 1, new { type = "reasoning", summary = "PRIVATE-REASONING" }),
            Usage(2), Event("agent_message", 3, null)]);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.Null(task.StartedAt);
        Assert.Equal(At.AddSeconds(3), task.ObservedAt);
        Assert.Equal(IslandActivityKind.Working, TaskActivitySummary.Select([task], At.AddSeconds(10)).Kind);
    }

    [Theory]
    [InlineData("agent_message", "Working on your request · local log")]
    [InlineData("agent_reasoning", "Thinking · local log")]
    public void LegacyModelEventsUseTheKnownTurn(string type, string activity)
    {
        var task = Parse([Record("turn_context", 0, new { turn_id = "turn1", cwd = "PRIVATE-PATH" }), Event(type, 1, null)]);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.Equal(activity, task.Activity);
        Assert.Null(task.StartedAt);
    }

    [Theory]
    [InlineData("reasoning", "assistant")]
    [InlineData("message", "assistant")]
    public void AssistantResponseItemsRefreshAKnownTurn(string type, string role)
    {
        var task = Parse([Usage(0), Record("response_item", 1, new { type, role, content = "PRIVATE-CONTENT" })]);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.Equal(At.AddSeconds(1), task.ObservedAt);
    }

    [Fact]
    public void ContextOrAnonymousMessagesDoNotInventRunningWork()
    {
        var context = Record("turn_context", 0, new { turn_id = "turn1" });
        Assert.Null(CodexTaskMonitor.ParseLocal(Id, "Chat", "project", [context], At.AddSeconds(10)));
        Assert.Equal(CodexTaskState.RecentActivity, Parse([Event("agent_message", 1, null)]).State);
        Assert.Equal(CodexTaskState.RecentActivity, Parse([Event("token_count", 0, null), Usage(1, turn: "")]).State);
    }

    [Fact]
    public void UsageFromAnotherChatDoesNotMakeThisChatRun()
    {
        var task = Parse([Event("token_count", 0, null), Usage(1, thread: OtherId)]);
        Assert.Equal(CodexTaskState.RecentActivity, task.State);
        Assert.Equal(At, task.ObservedAt);
        var normalized = CodexTaskMonitor.SanitizeLocalLine(Usage(1, thread: OtherId))!;
        Assert.Equal(task, Parse([Event("token_count", 0, null), normalized]));
    }

    [Theory]
    [InlineData("task_complete", CodexTaskState.Idle)]
    [InlineData("turn_aborted", CodexTaskState.Interrupted)]
    public void FinishedTurnsIgnoreLateLegacyMessagesAndUsage(string end, CodexTaskState state)
    {
        var task = Parse([Usage(0), Event(end, 1), Usage(2), Event("agent_message", 3, null)]);
        Assert.Equal(state, task.State);
        Assert.Equal(At.AddSeconds(1), task.ObservedAt);
    }

    [Fact]
    public void PreviousTurnUsageCannotOverwriteNewWorkOrAQuestion()
    {
        var current = Parse([Event("task_started", 0), Event("task_complete", 1), Event("task_started", 2, "turn2"),
            Usage(3), Usage(4, "turn2"), Question(5), Usage(6, "turn2"), Event("agent_message", 7, null)]);
        Assert.Equal(CodexTaskState.InputRequested, current.State);
        Assert.Equal(At.AddSeconds(5), current.ObservedAt);
        Assert.Equal(At.AddSeconds(2), current.StartedAt);
    }

    [Fact]
    public void UserMessagesCannotRefreshAWorkingTurn()
    {
        var task = Parse([Usage(0), Record("response_item", 1, new { type = "message", role = "user", content = "PRIVATE-PROMPT" })]);
        Assert.Equal(At, task.ObservedAt);
    }

    [Fact]
    public void StaleUsageDoesNotHoldTheIslandOpen()
    {
        var task = Parse([Usage(0)], now: 301);
        Assert.Equal(CodexTaskState.Stale, task.State);
        Assert.False(TaskActivitySummary.Select([task], At.AddSeconds(301)).IsVisible);
    }

    [Fact]
    public void NormalizedCacheOmitsTextTokenTotalsAndResponseIdentifiers()
    {
        var raw = new[] { Usage(0), Event("agent_message", 1, null),
            Record("response_item", 2, new { type = "reasoning", summary = "PRIVATE-REASONING" }) };
        var normalized = raw.Select(CodexTaskMonitor.SanitizeLocalLine).Select(line => line!).ToArray();
        var cached = string.Join('\n', normalized);
        Assert.DoesNotContain("PRIVATE", cached);
        Assert.DoesNotContain("4321", cached);
        Assert.DoesNotContain("9876", cached);
        Assert.Equal(Parse(raw), Parse(normalized));
    }

    [Fact]
    public async Task MonitorKeepsWorkAcrossLogRolloverAndClearsItAfterCompletion()
    {
        using var fixture = new LogFixture();
        await fixture.Append(Event("task_started", -1));
        await fixture.Append(Usage(0));
        await using var monitor = fixture.Monitor();
        var working = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(CodexTaskState.Working, working.State);
        Assert.NotNull(working.StartedAt);
        await fixture.Append(Record("response_item", 1, new { type = "custom_tool_call_output", call_id = "tool", output = new string('x', 300 * 1024) }));
        Assert.Equal(working, Assert.Single((await monitor.ReadAsync(default)).Tasks));
        await fixture.Append(Event("task_complete", 2));
        var finished = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(CodexTaskState.Idle, finished.State);
        await fixture.Append(Event("agent_message", 3, null));
        Assert.Equal(finished, Assert.Single((await monitor.ReadAsync(default)).Tasks));
    }

    [Fact]
    public async Task MonitorRecoversAColdStartAndHandlesASplitCompletionRecord()
    {
        using var fixture = new LogFixture();
        await fixture.Append(Record("response_item", 0, new { type = "custom_tool_call_output", output = new string('x', 300 * 1024) }));
        await fixture.Append(Usage(1));
        await using var monitor = fixture.Monitor();
        Assert.Equal(CodexTaskState.Working, Assert.Single((await monitor.ReadAsync(default)).Tasks).State);
        var completion = fixture.Current(Event("task_complete", 2));
        await File.AppendAllTextAsync(fixture.Path, completion[..(completion.Length / 2)]);
        Assert.Equal(CodexTaskState.Working, Assert.Single((await monitor.ReadAsync(default)).Tasks).State);
        await File.AppendAllTextAsync(fixture.Path, completion[(completion.Length / 2)..] + "\n");
        Assert.Equal(CodexTaskState.Idle, Assert.Single((await monitor.ReadAsync(default)).Tasks).State);
    }

    [Fact]
    public async Task ReplacedOrTruncatedLogsDoNotKeepOldWork()
    {
        using var fixture = new LogFixture();
        await fixture.Append(Usage(0));
        await using var monitor = fixture.Monitor();
        Assert.Equal(CodexTaskState.Working, Assert.Single((await monitor.ReadAsync(default)).Tasks).State);
        await File.WriteAllTextAsync(fixture.Path, fixture.Current(Event("task_complete", 1)) + "\n");
        Assert.Equal(CodexTaskState.Idle, Assert.Single((await monitor.ReadAsync(default)).Tasks).State);
    }

    [Fact]
    public async Task PendingQuestionSurvivesLargeOutputUntilItsMatchingAnswer()
    {
        using var fixture = new LogFixture();
        await fixture.Append(Usage(0));
        await fixture.Append(Question(1));
        await using var monitor = fixture.Monitor();
        var question = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(CodexTaskState.InputRequested, question.State);
        await fixture.Append(Record("response_item", 2, new { type = "custom_tool_call_output", call_id = "other-tool", output = new string('x', 300 * 1024) }));
        Assert.Equal(question, Assert.Single((await monitor.ReadAsync(default)).Tasks));
        await fixture.Append(Usage(3));
        Assert.Equal(question, Assert.Single((await monitor.ReadAsync(default)).Tasks));
        await fixture.Append(Record("response_item", 4, new { type = "function_call_output", call_id = "PRIVATE-QUESTION-ID", output = "PRIVATE-ANSWER" }));
        var answered = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(CodexTaskState.Working, answered.State);
        Assert.Null(answered.AttentionKey);
    }

    [Fact]
    public async Task OldWorkDoesNotBecomeFreshWhenLargeUnrelatedOutputArrives()
    {
        using var fixture = new LogFixture();
        await fixture.Append(Usage(-400));
        await using var monitor = fixture.Monitor();
        var stale = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(CodexTaskState.Stale, stale.State);
        await fixture.Append(Record("response_item", 0, new { type = "custom_tool_call_output", call_id = "other-tool", output = new string('x', 300 * 1024) }));
        Assert.Equal(stale, Assert.Single((await monitor.ReadAsync(default)).Tasks));
    }

    [Fact]
    public async Task RewrittenLargerLogDiscardsThePreviousTurnCheckpoint()
    {
        using var fixture = new LogFixture();
        await fixture.Append(Usage(0));
        await using var monitor = fixture.Monitor();
        Assert.Equal(CodexTaskState.Working, Assert.Single((await monitor.ReadAsync(default)).Tasks).State);
        var replacement = Record("response_item", 1, new { type = "message", role = "user", content = new string('x', 1000) });
        await File.WriteAllTextAsync(fixture.Path, fixture.Current(replacement) + "\n" + fixture.Current(Event("task_complete", 2, "turn2")) + "\n");
        var finished = Assert.Single((await monitor.ReadAsync(default)).Tasks);
        Assert.Equal(CodexTaskState.Idle, finished.State);
        Assert.Null(finished.StartedAt);
    }

    private sealed class LogFixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow.AddSeconds(-20);
        public string Path { get; }
        public LogFixture()
        {
            var sessions = System.IO.Path.Combine(_root, "sessions", "2026", "01", "01");
            Directory.CreateDirectory(sessions);
            Path = System.IO.Path.Combine(sessions, "rollout-" + Id + ".jsonl");
        }
        public string Current(string line)
        {
            using var doc = JsonDocument.Parse(line);
            var original = DateTimeOffset.Parse(doc.RootElement.GetProperty("timestamp").GetString()!);
            return line.Replace(original.ToString("yyyy-MM-ddTHH:mm:sszzz"), _now.Add(original - At).ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"));
        }
        public Task Append(string line) => File.AppendAllTextAsync(Path, Current(line) + "\n");
        public CodexTaskMonitor Monitor() => new(null, _root, System.IO.Path.Combine(_root, "events"));
        public void Dispose()
        {
            if (!System.IO.Path.GetFullPath(_root).StartsWith(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UsageNotch.Tests") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid fixture path.");
            Directory.Delete(_root, true);
        }
    }
}
