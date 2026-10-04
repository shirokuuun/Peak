using System.Text.Json;
using UsageNotch.Core;
using UsageNotch.Providers;

namespace UsageNotch.Core.Tests;

public sealed class TaskActivitySummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static CodexTask Task(string id, CodexTaskState state, int age = 0, string activity = "Editing files") =>
        new(id, "Chat " + id, "notch", state, activity, Now.AddSeconds(-age), true);

    [Fact]
    public void ReviewPreemptsWorkingAndCountsOtherActiveChats()
    {
        var result = TaskActivitySummary.Select([Task("work", CodexTaskState.Working),
            Task("review", CodexTaskState.ApprovalRequested, 2), Task("idle", CodexTaskState.Idle)], Now, "work");
        Assert.Equal("review", result.Task!.ThreadId);
        Assert.Equal(IslandActivityKind.Attention, result.Kind);
        Assert.Equal(1, result.OtherActiveCount);
    }

    [Fact]
    public void ParallelToolEventsDoNotSwitchAwayFromTheVisibleWorkingChat()
    {
        var result = TaskActivitySummary.Select([Task("new", CodexTaskState.Working),
            Task("visible", CodexTaskState.Working, 10)], Now, "visible");
        Assert.Equal("visible", result.Task!.ThreadId);
        Assert.Equal(1, result.OtherActiveCount);
    }

    [Fact]
    public void LocalActivityAloneKeepsTheIslandCompact()
    {
        var result = TaskActivitySummary.Select([Task("recent", CodexTaskState.RecentActivity)], Now);
        Assert.False(result.IsVisible);
        Assert.Equal(IslandActivityKind.Idle, result.Kind);
        Assert.Null(result.Task);
    }

    [Fact]
    public void LocalActivityDoesNotCountAsAnotherRunningChat()
    {
        var result = TaskActivitySummary.Select([Task("recent", CodexTaskState.RecentActivity),
            Task("working", CodexTaskState.Working, 2)], Now, "recent");
        Assert.Equal("working", result.Task!.ThreadId);
        Assert.Equal(IslandActivityKind.Working, result.Kind);
        Assert.Equal(0, result.OtherActiveCount);
    }

    [Theory]
    [InlineData(CodexTaskState.ApprovalRequested)]
    [InlineData(CodexTaskState.InputRequested)]
    public void AttentionStillExpandsWithoutCountingUnconfirmedActivity(CodexTaskState state)
    {
        var result = TaskActivitySummary.Select([Task("recent", CodexTaskState.RecentActivity),
            Task("review", state, 2)], Now);
        Assert.True(result.IsVisible);
        Assert.Equal("review", result.Task!.ThreadId);
        Assert.Equal(IslandActivityKind.Attention, result.Kind);
        Assert.Equal(0, result.OtherActiveCount);
    }

    [Fact]
    public void CompletionExpiresEvenWhenOtherLogsKeepUpdating()
    {
        var finish = Task("done", CodexTaskState.Idle, 2, "Response finished · local log");
        Assert.Equal(IslandActivityKind.Finished, TaskActivitySummary.Select(
            [finish, Task("recent", CodexTaskState.RecentActivity)], Now).Kind);
        Assert.False(TaskActivitySummary.Select([finish, Task("recent", CodexTaskState.RecentActivity)
            with { ObservedAt = Now.AddSeconds(6) }], Now.AddSeconds(6)).IsVisible);
    }

    [Fact]
    public void MissingStaleAndExpiredObservationsReturnToTheCompactIsland()
    {
        var result = TaskActivitySummary.Select([Task("expired", CodexTaskState.Working, 301),
            Task("stale", CodexTaskState.Stale), Task("unknown", CodexTaskState.Unknown),
            Task("future", CodexTaskState.ApprovalRequested, -121)], Now);
        Assert.False(result.IsVisible);
        Assert.Equal(IslandActivityKind.Idle, result.Kind);
        Assert.False(TaskActivitySummary.Select([], Now).IsVisible);
    }

    [Fact]
    public void FinishIsBriefAndNeverDisplacesAnotherRunningChat()
    {
        var finish = Task("done", CodexTaskState.Idle, 2, "Response finished · local log");
        Assert.Equal(IslandActivityKind.Finished, TaskActivitySummary.Select([finish], Now).Kind);
        Assert.False(TaskActivitySummary.Select([finish], Now.AddSeconds(6)).IsVisible);
        Assert.Equal("busy", TaskActivitySummary.Select([finish, Task("busy", CodexTaskState.Working)], Now).Task!.ThreadId);
        Assert.False(TaskActivitySummary.Select([Task("idle", CodexTaskState.Idle, 1, "Session opened")], Now).IsVisible);
    }

    [Fact]
    public void InterruptedTaskHasABriefStatusWithoutRunningProgress()
    {
        Assert.Equal(IslandActivityKind.Interrupted,
            TaskActivitySummary.Select([Task("paused", CodexTaskState.Interrupted)], Now).Kind);
    }

    [Fact]
    public void ToolUpdatesPreserveTheTurnStartAndTheNextTurnResetsIt()
    {
        CodexTaskEvent Event(string name, int second, string turn = "turn1") =>
            new(1, "thread", turn, name, Now.AddSeconds(second), "notch", "Bash", "a");
        var events = new[] { Event("UserPromptSubmit", 0), Event("PreToolUse", 2), Event("PostToolUse", 3) };
        Assert.Equal(Now, CodexTaskEvents.Reduce(events, "Chat", Now.AddSeconds(4)).StartedAt);
        var next = CodexTaskEvents.Reduce(events.Concat([Event("UserPromptSubmit", 5, "turn2"),
            Event("Stop", 6, "turn1"), Event("PreToolUse", 7, "turn2")]), "Chat", Now.AddSeconds(8));
        Assert.Equal(Now.AddSeconds(5), next.StartedAt);
        Assert.Equal(CodexTaskState.Working, next.State);
    }

    [Fact]
    public void MissingStartDoesNotInventElapsedTime()
    {
        var task = CodexTaskEvents.Reduce([new(1, "thread", "turn", "PreToolUse", Now, "notch", "Bash", "a")], "Chat", Now);
        Assert.Null(task.StartedAt);
    }

    [Fact]
    public void LocalLogRetainsObservedStartThroughCompletion()
    {
        string Event(string name, int second) => JsonSerializer.Serialize(new
        { timestamp = Now.AddSeconds(second), type = "event_msg", payload = new { type = name } });
        var task = CodexTaskMonitor.ParseLocal("thread", "Chat", "notch",
            [Event("task_started", 0), Event("token_count", 3), Event("task_complete", 8)], Now.AddSeconds(9));
        Assert.Equal(Now, task!.StartedAt);
        Assert.Equal(Now.AddSeconds(8), task.ObservedAt);
        Assert.Equal(CodexTaskState.Idle, task.State);
    }
}
