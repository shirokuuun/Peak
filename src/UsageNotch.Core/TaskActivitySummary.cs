namespace UsageNotch.Core;

public enum IslandActivityKind { Idle, Working, Attention, Finished, Interrupted }

public sealed record TaskActivitySummary(CodexTask? Task, IslandActivityKind Kind, int OtherActiveCount)
{
    public static readonly TaskActivitySummary Empty = new(null, IslandActivityKind.Idle, 0);
    public bool IsVisible => Task is not null;

    public static TaskActivitySummary Select(IReadOnlyList<CodexTask> tasks, DateTimeOffset now, string? preferredThread = null)
    {
        // Unconfirmed local-log activity remains in Tasks; it must not hold the idle island open.
        var fresh = tasks.Where(t => t.ObservedAt <= now.AddMinutes(2) && now - t.ObservedAt <= TimeSpan.FromMinutes(5)).ToArray();
        var active = fresh.Where(t => t.NeedsAttention || t.State == CodexTaskState.Working).ToArray();
        var candidates = active.Where(t => t.NeedsAttention).ToArray();
        var kind = IslandActivityKind.Attention;
        if (candidates.Length == 0)
        {
            candidates = active.Where(t => t.State == CodexTaskState.Working).ToArray();
            kind = IslandActivityKind.Working;
        }
        if (candidates.Length > 0)
        {
            // Keep the current chat in view while another chat reports a new tool event.
            var task = candidates.FirstOrDefault(t => t.ThreadId == preferredThread) ?? candidates.MaxBy(t => t.ObservedAt)!;
            return new(task, kind, Math.Max(0, active.Length - 1));
        }
        var finished = fresh.Where(t => now >= t.ObservedAt && now - t.ObservedAt < TimeSpan.FromSeconds(8) &&
            (t.State == CodexTaskState.Interrupted || t.State == CodexTaskState.Idle &&
             t.Activity.StartsWith("Response finished", StringComparison.Ordinal))).MaxBy(t => t.ObservedAt);
        return finished is null ? Empty : new(finished,
            finished.State == CodexTaskState.Interrupted ? IslandActivityKind.Interrupted : IslandActivityKind.Finished, 0);
    }
}
