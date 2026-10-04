using System.Text.Json.Serialization;

namespace UsageNotch.Core;

[JsonConverter(typeof(JsonStringEnumConverter<MetricBasis>))]
public enum MetricBasis { Reported, Derived, Estimated }

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionState>))]
public enum ConnectionState { Ready, Offline, AuthRequired, Unsupported, Error, Waiting }

[JsonConverter(typeof(JsonStringEnumConverter<Freshness>))]
public enum Freshness { Fresh, Stale, AwaitingUpdate }

[JsonConverter(typeof(JsonStringEnumConverter<SourceKind>))]
public enum SourceKind { DocumentedCli, StatusLine, LocalLog, OfficialApi, Manual, Unofficial }

public sealed record Metric<T>(T Value, MetricBasis Basis, IReadOnlyList<string> SourceIds)
{
    public static Metric<T> Reported(T value, string sourceId) => new(value, MetricBasis.Reported, [sourceId]);
    public static Metric<T> Derived(T value, params string[] sourceIds) => new(value, MetricBasis.Derived, sourceIds);
}

public sealed record SourceObservation(
    string Id,
    SourceKind Kind,
    string? ToolVersion,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? ProviderAsOf,
    DateTimeOffset? StaleAfter,
    bool AccountVerified);

public sealed record UsageWindow(
    string Id,
    string BucketId,
    string Label,
    string Coverage,
    string Purpose,
    string WindowKind,
    TimeSpan? Duration,
    Metric<DateTimeOffset>? StartsAt,
    Metric<DateTimeOffset>? ResetsAt,
    Metric<double>? UsedPercent,
    Freshness Freshness,
    Metric<bool>? LimitReached,
    string SourceId,
    Metric<double>? QuantityUsed = null,
    Metric<double>? QuantityLimit = null,
    string? QuantityUnit = null);

public sealed record ActivityTotal(
    string Id,
    string Label,
    string Unit,
    Metric<long> Value,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    string Coverage);

public sealed record ProviderError(string Code, string Message, bool Retryable);

public sealed record ProviderUsage(
    int SchemaVersion,
    string ProviderId,
    string AccountKey,
    string AccountLabel,
    Metric<string>? PlanName,
    ConnectionState Connection,
    IReadOnlyList<SourceObservation> Sources,
    IReadOnlyList<UsageWindow> Windows,
    IReadOnlyList<ActivityTotal> Activity,
    DateTimeOffset? LastSuccessfulReadAt,
    ProviderError? Error)
{
    public static ProviderUsage Waiting(string providerId, string label) => new(
        1, providerId, providerId, label, null, ConnectionState.Waiting, [], [], [], null, null);
}

public sealed record UsageAlert(
    string ProviderId,
    string WindowId,
    string WindowLabel,
    int Threshold,
    double UsedPercent,
    DateTimeOffset? ResetsAt);
