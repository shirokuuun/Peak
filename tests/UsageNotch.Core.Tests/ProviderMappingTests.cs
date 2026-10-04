using System.Text.Json;
using UsageNotch.Core;
using UsageNotch.Providers;

namespace UsageNotch.Core.Tests;

public class ProviderMappingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T10:00:00Z");
    private static JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    private static readonly JsonElement Account = Json("""{"account":{"type":"chatgpt","email":"someone@example.test","planType":"pro"}}""");

    [Fact] public void MultipleBucketsTakePrecedenceOverLegacyAndRetainIndependentWindows()
    {
        var rates = Json("""
            {"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{
              "codex":{"limitId":"codex","primary":{"usedPercent":90,"windowDurationMins":300,"resetsAt":1790766000},
              "secondary":{"usedPercent":45,"windowDurationMins":10080,"resetsAt":1791000000}},
              "review":{"limitId":"review","limitName":"Review","primary":{"usedPercent":12,"windowDurationMins":1440}}
            }}
            """);
        var provider = CodexUsageMapper.Map(Account, rates, null, "test", Now);
        Assert.Equal(3, provider.Windows.Count);
        Assert.Equal("pro", provider.PlanName!.Value);
        Assert.Equal(90, provider.Windows[0].UsedPercent!.Value);
        Assert.Null(provider.Windows[2].ResetsAt);
        Assert.Null(provider.Sources[0].ProviderAsOf);
        Assert.DoesNotContain("someone", provider.AccountKey);
    }
    [Fact] public void MalformedOptionalNumbersAreMissingRatherThanZero()
    {
        var provider = CodexUsageMapper.Map(Account,
            Json("""{"rateLimits":{"primary":{"usedPercent":null},"secondary":{"usedPercent":"90"}}}"""), null, null, Now);
        Assert.Empty(provider.Windows);
    }
    [Fact] public void InvalidResetDoesNotCrashAndExpiredResetIsNotHealthy()
    {
        var invalid = CodexUsageMapper.Map(Account, Json("""{"rateLimits":{"primary":{"usedPercent":80,"resetsAt":9223372036854775807}}}"""), null, null, Now);
        Assert.Null(Assert.Single(invalid.Windows).ResetsAt);
        var expired = CodexUsageMapper.Map(Account, Json("""{"rateLimits":{"primary":{"usedPercent":100,"resetsAt":1}}}"""), null, null, Now);
        Assert.Null(UsageSelection.FindCritical([expired], Now));
    }
    [Fact] public void MissingAuthenticationAndApiLoginAreDistinct()
    {
        Assert.Equal(ConnectionState.AuthRequired, CodexUsageMapper.Map(Json("""{"account":null,"requiresOpenaiAuth":true}"""), default, null, null, Now).Connection);
        Assert.Equal(ConnectionState.Unsupported, CodexUsageMapper.Map(Json("""{"account":{"type":"apiKey"}}"""), Json("{}"), null, null, Now).Connection);
    }
    [Fact] public void UnknownAccountCannotAlertEvenWithReportedPercentages()
    {
        var provider = CodexUsageMapper.Map(Json("""{"account":{"type":"chatgpt","email":null}}"""),
            Json("""{"rateLimits":{"primary":{"usedPercent":99}}}"""), null, null, Now);
        Assert.False(provider.Sources[0].AccountVerified);
        Assert.Null(UsageSelection.FindCritical([provider], Now));
    }
}
