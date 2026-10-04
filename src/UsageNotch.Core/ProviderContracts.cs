namespace UsageNotch.Core;

public sealed record ProviderCapabilities(
    bool CanRefresh,
    bool RequiresCliActivity,
    bool ProvidesQuotaPercent,
    bool ProvidesActivityTotals);

public sealed record ProviderProbe(
    bool Available,
    string? ToolVersion,
    ProviderCapabilities Capabilities,
    string? Message = null);

public interface IUsageProvider : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    Task<ProviderProbe> ProbeAsync(CancellationToken cancellationToken);
    Task<ProviderUsage> RefreshAsync(CancellationToken cancellationToken);
}

public static class UsageSelection
{
    public static (ProviderUsage Provider, UsageWindow Window)? FindCritical(IEnumerable<ProviderUsage> providers,
        DateTimeOffset? at = null) => FindWindow(providers, _ => true, at);

    public static (ProviderUsage Provider, UsageWindow Window)? FindFiveHour(IEnumerable<ProviderUsage> providers,
        DateTimeOffset? at = null) => FindWindow(providers, window => window.Duration == TimeSpan.FromHours(5), at);

    private static (ProviderUsage Provider, UsageWindow Window)? FindWindow(IEnumerable<ProviderUsage> providers,
        Func<UsageWindow, bool> matchesWindow, DateTimeOffset? at)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        var matches = providers
            .Where(p => p.Connection == ConnectionState.Ready)
            .SelectMany(p => p.Windows
                .Where(w => matchesWindow(w) && IsAlertEligible(p, w, now))
                .Select(w => (Provider: p, Window: w)))
            .OrderByDescending(x => x.Window.UsedPercent!.Value)
            .ThenByDescending(x => x.Window.ResetsAt?.Value)
            .ThenBy(x => x.Provider.ProviderId, StringComparer.Ordinal)
            .ToList();

        return matches.Count == 0 ? null : matches[0];
    }

    public static Freshness GetFreshness(SourceObservation source, DateTimeOffset now) =>
        source.ObservedAt > now.AddMinutes(2) || source.StaleAfter is null || now >= source.StaleAfter
            ? Freshness.Stale : Freshness.Fresh;

    public static Freshness WindowFreshness(ProviderUsage provider, UsageWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt?.Value <= now) return Freshness.AwaitingUpdate;
        var source = provider.Sources.FirstOrDefault(s => s.Id == window.SourceId);
        if (source is null || GetFreshness(source, now) == Freshness.Stale) return Freshness.Stale;
        return window.Freshness;
    }

    public static bool IsAlertEligible(ProviderUsage provider, UsageWindow window, DateTimeOffset now) =>
        provider.Connection == ConnectionState.Ready &&
        provider.Sources.Any(s => s.Id == window.SourceId && s.AccountVerified) &&
        WindowFreshness(provider, window, now) == Freshness.Fresh &&
        window.UsedPercent is { Value: >= 0 } metric && double.IsFinite(metric.Value);
}
