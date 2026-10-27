using System.Text.Json;
using UsageNotch.Core;
using UsageNotch.Providers;

namespace UsageNotch.Core.Tests;

public sealed class CodexLocalWorkTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private static readonly DateTimeOffset At = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);

    private static string Event(string type, int second, string turn = "turn1", string? itemType = null) =>
        JsonSerializer.Serialize(new { timestamp = At.AddSeconds(second), type = "event_msg",
            payload = new { type, turn_id = turn, item = new { type = itemType, content = "PRIVATE-CONTENT" } } });

    private static CodexTask Parse(string[] lines, int now = 10) =>
        CodexTaskMonitor.ParseLocal(Id, "Chat", "notch", lines, At.AddSeconds(now))!;

    [Fact]
    public void ObservedTurnStartShowsWorkWithoutHooks()
    {
        var task = Parse([Event("task_started", 0), Event("token_count", 2)]);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.False(task.FromHooks);
        Assert.Equal(At, task.StartedAt);
        Assert.Equal(IslandActivityKind.Working, TaskActivitySummary.Select([task], At.AddSeconds(10)).Kind);
    }

    [Theory]
    [InlineData("Reasoning")]
    [InlineData("AgentMessage")]
    public void FreshModelActivityRecoversWorkWhenTurnStartIsOutsideTheTail(string itemType)
    {
        var task = Parse([Event("item_completed", 1, itemType: itemType)]);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.Null(task.StartedAt);
        Assert.False(task.NeedsAttention);
        Assert.False(task.FromHooks);
        Assert.Equal(itemType == "Reasoning" ? "Thinking · local log" : "Working on your request · local log", task.Activity);
    }

    [Fact]
    public void AnonymousModelActivityDoesNotInventAnActiveTurn()
    {
        var task = Parse([Event("item_completed", 1, turn: "", itemType: "Reasoning")]);
        Assert.Equal(CodexTaskState.RecentActivity, task.State);
    }

    [Theory]
    [InlineData("task_complete", CodexTaskState.Idle)]
    [InlineData("turn_aborted", CodexTaskState.Interrupted)]
    public void EndedTurnsStayEndedWhenLateItemsOrQuotaUpdatesArrive(string end, CodexTaskState state)
    {
        var task = Parse([Event("task_started", 0), Event(end, 2), Event("item_completed", 3, itemType: "CommandExecution"),
            Event("item_completed", 4, itemType: "Reasoning"), Event("token_count", 5)]);
        Assert.Equal(state, task.State);
        Assert.Equal(At.AddSeconds(2), task.ObservedAt);
    }

    [Fact]
    public void LatePreviousTurnModelActivityCannotReplaceCurrentTurn()
    {
        var task = Parse([Event("task_started", 0), Event("task_started", 2, "turn2"),
            Event("item_completed", 3, itemType: "Reasoning")]);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.Equal(At.AddSeconds(2), task.ObservedAt);
        Assert.Equal(At.AddSeconds(2), task.StartedAt);
    }

    [Fact]
    public void WorkingObservationExpiresWhenThereIsNoFreshEvidence()
    {
        var task = Parse([Event("item_completed", 1, itemType: "Reasoning")], now: 302);
        Assert.Equal(CodexTaskState.Stale, task.State);
        Assert.False(TaskActivitySummary.Select([task], At.AddSeconds(302)).IsVisible);
    }

    [Fact]
    public void SanitizedModelActivityKeepsOnlyLifecycleMetadata()
    {
        var raw = Event("item_completed", 1, itemType: "Reasoning");
        var cached = CodexTaskMonitor.SanitizeLocalLine(raw)!;
        Assert.DoesNotContain("PRIVATE", cached);
        Assert.Equal(Parse([raw]), Parse([cached]));
    }

    [Theory]
    [InlineData(CodexTaskState.Stale)]
    [InlineData(CodexTaskState.Idle)]
    [InlineData(CodexTaskState.Working)]
    public void NewerLocalWorkReplacesOutdatedHooks(CodexTaskState state)
    {
        var local = Parse([Event("item_completed", 2, itemType: "Reasoning")]);
        var hook = local with { State = state, ObservedAt = At, FromHooks = true, StartedAt = At.AddDays(-1) };
        var task = CodexTaskMonitor.ReconcileLocal(hook, local);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.Equal(local.ObservedAt, task.ObservedAt);
        Assert.False(task.FromHooks);
        Assert.Null(task.StartedAt);
    }

    [Theory]
    [InlineData(CodexTaskState.ApprovalRequested)]
    [InlineData(CodexTaskState.InputRequested)]
    public void LocalActivityDoesNotDismissAnUnresolvedHookRequest(CodexTaskState state)
    {
        var local = Parse([Event("task_started", 0), Event("item_completed", 2, itemType: "Reasoning")]);
        var hook = local with { State = state, ObservedAt = At.AddSeconds(1), FromHooks = true, AttentionKey = "pending" };
        Assert.Equal(hook, CodexTaskMonitor.ReconcileLocal(hook, local));
    }

    [Fact]
    public async Task MonitorFindsResumedOldSessionsAndWorkBeyondTheTailWithOutdatedHooks()
    {
        var temp = Path.Combine(Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var sessions = Path.Combine(temp, "sessions", now.AddDays(-30).ToString("yyyy/MM/dd"));
            var events = Path.Combine(temp, "events");
            Directory.CreateDirectory(sessions);
            Directory.CreateDirectory(events);
            var path = Path.Combine(sessions, "rollout-" + Id + ".jsonl");
            string Current(string line) => line.Replace(At.AddSeconds(1).ToString("yyyy-MM-ddTHH:mm:sszzz"),
                now.AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:sszzz"));
            File.WriteAllLines(path, ["{\"privatePadding\":\"" + new string('x', 300 * 1024) + "\"}",
                Current(Event("item_completed", 1, itemType: "Reasoning"))]);
            File.WriteAllText(Path.Combine(events, Id + ".jsonl"), JsonSerializer.Serialize(
                new CodexTaskEvent(1, Id, "old-turn", "Stop", now.AddDays(-1), "notch", null, null)) + "\n");
            await using var monitor = new CodexTaskMonitor(null, temp, events);
            var task = Assert.Single((await monitor.ReadAsync(default)).Tasks);
            Assert.Equal(CodexTaskState.Working, task.State);
            Assert.False(task.FromHooks);
            Assert.Equal("Thinking · local log", task.Activity);
            Assert.Equal(task, Assert.Single((await monitor.ReadAsync(default)).Tasks));
        }
        finally
        {
            if (!Path.GetFullPath(temp).StartsWith(Path.Combine(Path.GetTempPath(), "UsageNotch.Tests") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test path.");
            Directory.Delete(temp, true);
        }
    }
}
