using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public sealed class UsageSelectionTests
{
    [Fact]
    public void CriticalWindow_UsesHighestFreshPercentage()
    {
        var provider = Provider("codex", Window("short", 72), Window("week", 91));
        var result = UsageSelection.FindCritical([provider]);
        Assert.NotNull(result);
        Assert.Equal("week", result.Value.Window.Id);
    }

    [Fact]
    public void CriticalWindow_IgnoresStaleAndMissingValues()
    {
        var stale = Window("stale", 99) with { Freshness = Freshness.Stale };
        var missing = stale with { Id = "missing", UsedPercent = null, Freshness = Freshness.Fresh };
        Assert.Null(UsageSelection.FindCritical([Provider("codex", stale, missing)]));
    }

    [Fact]
    public void ThresholdEvaluator_SeedsSilently_ThenReportsHighestCrossing()
    {
        var evaluator = new ThresholdEvaluator();
        evaluator.Evaluate([Provider("codex", Window("short", 65))], 70, 90, seedOnly: true);
        var alert = Assert.Single(evaluator.Evaluate([Provider("codex", Window("short", 94))], 70, 90));
        Assert.Equal(90, alert.Threshold);
    }

    [Fact]
    public void Settings_NormalizeClampsValuesAndOrdersThresholds()
    {
        var settings = new AppSettings { RefreshIntervalSeconds = 1, WarningThreshold = 99,
            CriticalThreshold = 1, TopOffset = -8 }.Normalize();
        Assert.Equal(30, settings.RefreshIntervalSeconds);
        Assert.Equal(98, settings.WarningThreshold);
        Assert.Equal(99, settings.CriticalThreshold);
        Assert.Equal(0, settings.TopOffset);
    }

    private static ProviderUsage Provider(string id, params UsageWindow[] windows) => new(
        1, id, "account", id, null, ConnectionState.Ready,
        [new("test", SourceKind.DocumentedCli, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, DateTimeOffset.UtcNow.AddMinutes(3), true)], windows, [], DateTimeOffset.UtcNow, null);

    private static UsageWindow Window(string id, double percent) => new(
        id, id, id, "account", "subscription-quota", "rolling", TimeSpan.FromHours(5), null,
        Metric<DateTimeOffset>.Reported(DateTimeOffset.UtcNow.AddHours(1), "test"),
        Metric<double>.Reported(percent, "test"), Freshness.Fresh,
        Metric<bool>.Reported(percent >= 100, "test"), "test");
}
