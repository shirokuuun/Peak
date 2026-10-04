using UsageNotch.Core;

namespace UsageNotch.App;

internal sealed class DemoTaskMonitor : ICodexTaskMonitor
{
    private readonly DateTimeOffset _at = DateTimeOffset.UtcNow;
    private readonly bool _cycle;
    public DemoTaskMonitor(bool cycle = false) => _cycle = cycle;
    public event EventHandler? Changed { add { } remove { } }
    public Task<CodexTaskSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        if (_cycle)
        {
            var now = DateTimeOffset.UtcNow;
            var seconds = (now - _at).TotalSeconds;
            var phase = seconds % 48;
            var start = _at.AddSeconds(Math.Floor(seconds / 48) * 48 + 8);
            var state = phase < 8 ? CodexTaskState.Idle : phase < 32 ? CodexTaskState.Working :
                phase < 42 ? CodexTaskState.ApprovalRequested : CodexTaskState.Idle;
            var activity = phase < 8 ? "Session opened" : phase < 20 ? "Editing files" : phase < 32 ? "Running checks" :
                phase < 42 ? "Command execution" : "Response finished";
            var observed = phase < 8 ? now.AddMinutes(-10) : phase < 42 ? now : start.AddSeconds(34);
            return Task.FromResult(new CodexTaskSnapshot([
                new("00000000-0000-0000-0000-000000000002", "Build the dynamic usage island", "notch", state,
                    activity, observed, true, IsDemo: true, StartedAt: phase < 8 ? null : start)
            ], "DEMO · repeating idle, working, approval and finished states", now));
        }
        return Task.FromResult(new CodexTaskSnapshot([
        new("00000000-0000-0000-0000-000000000001", "Build the usage notch", "notch", CodexTaskState.ApprovalRequested, "Command execution", _at, true, IsDemo: true),
        new("00000000-0000-0000-0000-000000000002", "Fix the checkout flow", "storefront", CodexTaskState.Working, "Editing files", _at, true, IsDemo: true, StartedAt: _at),
        new("00000000-0000-0000-0000-000000000003", "Review account settings", "dashboard", CodexTaskState.Idle, "Response finished", _at, true, IsDemo: true)
    ], "DEMO · sample tasks and approval · no live Codex activity", _at));
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
