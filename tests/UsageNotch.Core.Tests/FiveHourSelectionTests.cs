using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public sealed class FiveHourSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
    private static UsageWindow Window(string id, double used, TimeSpan? duration) => new(
        id, "subscription", id, "account", "subscription-quota", "provider-defined", duration, null,
        Metric<DateTimeOffset>.Reported(Now.AddHours(1), "test"), Metric<double>.Reported(used, "test"),
        Freshness.Fresh, null, "test");
    private static ProviderUsage Provider(params UsageWindow[] windows) => new(
        1, "claude", "account", "Test account", null, ConnectionState.Ready,
        [new("test", SourceKind.DocumentedCli, null, Now, Now, null, Now.AddMinutes(3), true)], windows, [], Now, null);

    [Fact]
    public void CompactReadingUsesFiveHourEvenWhenWeeklyUsageIsHigher()
    {
        var session = Window("fiveHour", 32, TimeSpan.FromHours(5));
        var weekly = Window("weekly", 68, TimeSpan.FromDays(7));
        var provider = Provider(weekly, session);
        Assert.Equal(session, UsageSelection.FindFiveHour([provider], Now)!.Value.Window);
        Assert.Equal(weekly, UsageSelection.FindCritical([provider], Now)!.Value.Window);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void SelectionUsesReportedDurationForEachProvider(string providerId)
    {
        var session = Window("unfamiliar-bucket:primary", 15, TimeSpan.FromHours(5)) with { Label = "Account quota" };
        var provider = Provider(Window("session", 90, TimeSpan.FromDays(7)), session) with { ProviderId = providerId };
        Assert.Equal(session, UsageSelection.FindFiveHour([provider], Now)!.Value.Window);
    }

    [Fact]
    public void MissingFiveHourDoesNotSubstituteWeeklyOrAnUnknownWindow()
    {
        Assert.Null(UsageSelection.FindFiveHour([Provider(
            Window("weekly", 90, TimeSpan.FromDays(7)), Window("primary", 20, null) with { Label = "5-hour" })], Now));
        Assert.Null(UsageSelection.FindFiveHour([], Now));
    }

    [Fact]
    public void MissingOrExpiredSessionDoesNotFallBackToWeekly()
    {
        var weekly = Window("weekly", 90, TimeSpan.FromDays(7));
        var session = Window("session", 20, TimeSpan.FromHours(5));
        foreach (var unavailable in new[] {
            session with { UsedPercent = null },
            session with { Freshness = Freshness.Stale },
            session with { ResetsAt = Metric<DateTimeOffset>.Reported(Now, "test") },
            session with { UsedPercent = Metric<double>.Reported(double.NaN, "test") } })
            Assert.Null(UsageSelection.FindFiveHour([Provider(unavailable, weekly)], Now));
    }

    [Fact]
    public void OldUnverifiedAndDisconnectedSourcesRemainUnavailable()
    {
        var provider = Provider(Window("session", 20, TimeSpan.FromHours(5)));
        Assert.Null(UsageSelection.FindFiveHour([provider], Now.AddMinutes(3)));
        Assert.Null(UsageSelection.FindFiveHour([provider with { Sources = [provider.Sources[0] with { AccountVerified = false }] }], Now));
        Assert.Null(UsageSelection.FindFiveHour([provider with { Connection = ConnectionState.Offline }], Now));
    }

    [Fact]
    public void MultipleFiveHourBucketsUseTheMostConsumedFreshSession()
    {
        var primary = Window("main:primary", 30, TimeSpan.FromHours(5));
        var second = Window("second:primary", 75, TimeSpan.FromHours(5));
        var stale = Window("stale:primary", 99, TimeSpan.FromHours(5)) with { Freshness = Freshness.Stale };
        Assert.Equal(second, UsageSelection.FindFiveHour([Provider(primary, stale, second)], Now)!.Value.Window);
    }
}
