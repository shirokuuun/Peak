namespace UsageNotch.Core;

public sealed record AlertCheckpoint(double LastPercent, DateTimeOffset? ResetAt, int HighestThreshold);
public sealed record ResetAlert(string ProviderId, string WindowLabel);

public sealed class ThresholdEvaluator
{
    private readonly Dictionary<string, AlertCheckpoint> _state;
    public IReadOnlyDictionary<string, AlertCheckpoint> State => _state;
    public IReadOnlyList<ResetAlert> Resets { get; private set; } = [];

    public ThresholdEvaluator(IReadOnlyDictionary<string, AlertCheckpoint>? state = null) =>
        _state = state is null ? new(StringComparer.Ordinal) : new(state, StringComparer.Ordinal);

    public IReadOnlyList<UsageAlert> Evaluate(IEnumerable<ProviderUsage> providers,
        int warningThreshold, int criticalThreshold, bool seedOnly = false, DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        var alerts = new List<UsageAlert>();
        var resets = new List<ResetAlert>();
        foreach (var provider in providers)
        foreach (var window in provider.Windows.Where(w => UsageSelection.IsAlertEligible(provider, w, now)))
        {
            var key = $"{provider.ProviderId}:{provider.AccountKey}:{window.Id}";
            var current = window.UsedPercent!.Value;
            var reset = window.ResetsAt?.Value;
            var exists = _state.TryGetValue(key, out var previous);
            // Timer expiry alone is insufficient. Require a new provider reset and a lower usage value.
            var newCycle = exists && previous!.ResetAt is { } oldReset && oldReset <= now &&
                           reset > oldReset && current < previous.LastPercent;
            var highest = newCycle ? 0 : previous?.HighestThreshold ?? 0;
            if (newCycle && !seedOnly) resets.Add(new(provider.ProviderId, window.Label));
            if (exists && !seedOnly)
            {
                var threshold = current >= criticalThreshold && highest < criticalThreshold &&
                                (newCycle || previous!.LastPercent < criticalThreshold)
                    ? criticalThreshold
                    : current >= warningThreshold && highest < warningThreshold &&
                      (newCycle || previous!.LastPercent < warningThreshold) ? warningThreshold : 0;
                if (threshold > 0)
                {
                    alerts.Add(new(provider.ProviderId, window.Id, window.Label, threshold, current, reset));
                    highest = threshold;
                }
            }
            else
            {
                highest = Math.Max(highest, current >= criticalThreshold ? criticalThreshold :
                    current >= warningThreshold ? warningThreshold : 0);
            }
            _state[key] = new(current, reset, highest);
        }
        Resets = resets;
        return alerts;
    }
}
