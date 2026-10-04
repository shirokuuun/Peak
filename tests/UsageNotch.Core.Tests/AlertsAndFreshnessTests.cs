using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public class AlertsAndFreshnessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T10:00:00Z");
    private static ProviderUsage Reading(double percent, DateTimeOffset? reset = null, bool verified = true, string account = "one")
    {
        var source = new SourceObservation("source", SourceKind.DocumentedCli, null, Now, Now, null, Now.AddMinutes(3), verified);
        var window = new UsageWindow("session", "codex", "5-hour", "account", "quota", "provider-defined", TimeSpan.FromHours(5),
            null, Metric<DateTimeOffset>.Reported(reset ?? Now.AddHours(1), source.Id), Metric<double>.Reported(percent, source.Id),
            Freshness.Fresh, null, source.Id);
        return new(1, "codex", account, "account", null, ConnectionState.Ready, [source], [window], [], Now, null);
    }

    [Fact] public void UnverifiedLogsNeverEnterSummaryOrAlerts()
    {
        var evaluator = new ThresholdEvaluator();
        evaluator.Evaluate([Reading(20, verified: false)], 70, 90, at: Now);
        Assert.Null(UsageSelection.FindCritical([Reading(98, verified: false)], Now));
        Assert.Empty(evaluator.Evaluate([Reading(98, verified: false)], 70, 90, at: Now));
    }
    [Fact] public void AgeAndExpiryAreReevaluatedWithoutNewData()
    {
        var provider = Reading(90);
        Assert.NotNull(UsageSelection.FindCritical([provider], Now));
        Assert.Null(UsageSelection.FindCritical([provider], Now.AddMinutes(4)));
        Assert.Equal(Freshness.AwaitingUpdate, UsageSelection.WindowFreshness(provider, provider.Windows[0], Now.AddHours(2)));
    }
    [Fact] public void CorrectionsDoNotRepeatAnAlreadyDeliveredThreshold()
    {
        var evaluator = new ThresholdEvaluator();
        evaluator.Evaluate([Reading(20)], 70, 90, at: Now);
        Assert.Single(evaluator.Evaluate([Reading(91)], 70, 90, at: Now));
        evaluator.Evaluate([Reading(60)], 70, 90, at: Now);
        Assert.Empty(evaluator.Evaluate([Reading(95)], 70, 90, at: Now));
        var restarted = new ThresholdEvaluator(evaluator.State);
        Assert.Empty(restarted.Evaluate([Reading(99)], 70, 90, at: Now));
    }
    [Fact] public void TimerExpiryDoesNotEmitResetButNewProviderCycleDoes()
    {
        var evaluator = new ThresholdEvaluator();
        evaluator.Evaluate([Reading(99, Now.AddMinutes(1))], 70, 90, at: Now);
        evaluator.Evaluate([Reading(99, Now.AddMinutes(1))], 70, 90, at: Now.AddMinutes(2));
        Assert.Empty(evaluator.Resets);
        evaluator.Evaluate([Reading(5, Now.AddHours(5))], 70, 90, at: Now.AddMinutes(2));
        Assert.Single(evaluator.Resets);
        Assert.Single(evaluator.Evaluate([Reading(72, Now.AddHours(5))], 70, 90, at: Now.AddMinutes(2)));
    }
    [Fact] public void NewAccountIsSilentlySeeded()
    {
        var evaluator = new ThresholdEvaluator();
        evaluator.Evaluate([Reading(20)], 70, 90, at: Now);
        Assert.Empty(evaluator.Evaluate([Reading(98, account: "two")], 70, 90, at: Now));
    }
}
