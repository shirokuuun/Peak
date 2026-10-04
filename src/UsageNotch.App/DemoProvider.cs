using UsageNotch.Core;

namespace UsageNotch.App;

// Explicit --demo mode uses a separate data directory and never calls provider services.
internal sealed class DemoProvider(string id, double sessionUsed, double weeklyUsed) : IUsageProvider
{
    public string Id => id;
    public string DisplayName => id;
    public Task<ProviderProbe> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderProbe(true, "demo", new(true, false, true, false)));
    public Task<ProviderUsage> RefreshAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var source = new SourceObservation("demo", SourceKind.Manual, "demo", now, now, null, now.AddMinutes(3), true);
        UsageWindow Window(string key, string label, double percent, TimeSpan duration, TimeSpan untilReset) => new(key, id, label,
            "demo", "subscription-quota", "sample", duration, null, Metric<DateTimeOffset>.Reported(now + untilReset, "demo"),
            Metric<double>.Reported(percent, "demo"), Freshness.Fresh, null, "demo");
        return Task.FromResult(new ProviderUsage(1, id, "demo", "DEMO", Metric<string>.Reported("DEMO · sample data", "demo"),
            ConnectionState.Ready, [source], [Window("session", "5-hour", sessionUsed, TimeSpan.FromHours(5), TimeSpan.FromHours(2)),
                Window("weekly", "Weekly", weeklyUsed, TimeSpan.FromDays(7), TimeSpan.FromDays(4))], [], now, null));
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
