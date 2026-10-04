using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.Providers;

internal static class CodexUsageMapper
{
    public static ProviderUsage Map(JsonElement accountResult, JsonElement rateResult, JsonElement? usageResult,
        string? toolVersion, DateTimeOffset now, SourceKind sourceKind = SourceKind.DocumentedCli,
        string sourceId = "codex-app-server")
    {
        var account = TryGetProperty(accountResult, "account", out var accountValue) ? accountValue : default;
        var accountType = GetString(account, "type");

        if (account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ProviderUsage.Waiting("codex", "Codex") with
            {
                Connection = TryGetProperty(accountResult, "requiresOpenaiAuth", out var required) && required.ValueKind == JsonValueKind.True
                    ? ConnectionState.AuthRequired : ConnectionState.Unsupported,
                Error = new ProviderError("auth_required", "Sign in with Codex's Windows credential store, then refresh. See README for setup.", false)
            };

        var email = GetString(account, "email");
        var plan = GetString(account, "planType");
        var identity = email ?? GetString(account, "accountId");
        var accountKey = HashAccount(identity ?? "unverified-codex");
        var observation = new SourceObservation(sourceId, sourceKind, toolVersion, now, now, null,
            now.AddMinutes(sourceKind == SourceKind.LocalLog ? 5 : 3), identity is not null && sourceKind != SourceKind.LocalLog);

        var windows = new List<UsageWindow>();
        if (TryGetProperty(rateResult, "rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byId.EnumerateObject()) MapBucket(property.Value, property.Name, sourceId, now, windows);
        }
        else if (TryGetProperty(rateResult, "rateLimits", out var rateLimits) && rateLimits.ValueKind == JsonValueKind.Object)
        {
            MapBucket(rateLimits, GetString(rateLimits, "limitId") ?? "codex", sourceId, now, windows);
        }

        var activity = new List<ActivityTotal>();
        if (usageResult is { ValueKind: JsonValueKind.Object } usage &&
            usage.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object &&
            TryGetInt64(summary, "lifetimeTokens", out var lifetimeTokens))
        {
            activity.Add(new ActivityTotal("lifetime-tokens", "Lifetime tokens", "tokens",
                Metric<long>.Reported(lifetimeTokens, sourceId), null, now, "account"));
        }

        return new ProviderUsage(1, "codex", accountKey, email ?? "Codex account",
            plan is null ? null : Metric<string>.Reported(plan, sourceId),
            accountType == "chatgpt" ? ConnectionState.Ready : ConnectionState.Unsupported,
            [observation], windows, activity, now, accountType == "chatgpt" ? null :
                new ProviderError("subscription_required", "A ChatGPT subscription login is required; API billing is separate.", false));
    }

    internal static void MapBucket(JsonElement bucket, string fallbackId, string sourceId,
        DateTimeOffset now, List<UsageWindow> windows)
    {
        var bucketId = GetString(bucket, "limitId", "limit_id") ?? fallbackId;
        var bucketLabel = GetString(bucket, "limitName", "limit_name");
        AddWindow(bucket, "primary", bucketId, bucketLabel, sourceId, now, windows);
        AddWindow(bucket, "secondary", bucketId, bucketLabel, sourceId, now, windows);
    }

    private static void AddWindow(JsonElement bucket, string propertyName, string bucketId, string? bucketLabel,
        string sourceId, DateTimeOffset now, List<UsageWindow> windows)
    {
        if (!TryGetProperty(bucket, propertyName, out var window) || window.ValueKind != JsonValueKind.Object) return;
        if (!TryGetDouble(window, out var used, "usedPercent", "used_percent") || !double.IsFinite(used) || used < 0) return;

        TryGetInt64(window, out var durationMinutes, "windowDurationMins", "window_minutes");
        var hasReset = TryGetInt64(window, out var resetSeconds, "resetsAt", "resets_at");
        var resetsAt = hasReset ? SafeReset(resetSeconds) : null;
        var label = DurationLabel(durationMinutes, propertyName);
        if (!string.IsNullOrWhiteSpace(bucketLabel)) label = $"{bucketLabel} · {label}";
        var freshness = resetsAt is { } reset && reset <= now ? Freshness.AwaitingUpdate : Freshness.Fresh;

        windows.Add(new UsageWindow(
            $"{bucketId}:{propertyName}", bucketId, label, "account", "subscription-quota",
            "provider-defined", durationMinutes is > 0 and < 5256000 ? TimeSpan.FromMinutes(durationMinutes) : null,
            null,
            resetsAt is null ? null : Metric<DateTimeOffset>.Reported(resetsAt.Value, sourceId),
            Metric<double>.Reported(used, sourceId), freshness,
            Metric<bool>.Reported(used >= 100, sourceId), sourceId));
    }

    private static string DurationLabel(long minutes, string fallback) => minutes switch
    {
        300 => "5-hour",
        10080 => "Weekly",
        1440 => "Daily",
        > 0 when minutes % 1440 == 0 => $"{minutes / 1440}-day",
        > 0 when minutes % 60 == 0 => $"{minutes / 60}-hour",
        > 0 => $"{minutes}-minute",
        _ => fallback == "primary" ? "Primary" : "Secondary"
    };

    private static string HashAccount(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    internal static DateTimeOffset? SafeReset(long seconds)
    {
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }

    internal static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    internal static bool TryGetDouble(JsonElement element, out double value, params string[] names)
    {
        foreach (var name in names)
            if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value)) return true;
        value = 0;
        return false;
    }

    internal static bool TryGetInt64(JsonElement element, string name, out long value) => TryGetInt64(element, out value, name);

    internal static bool TryGetInt64(JsonElement element, out long value, params string[] names)
    {
        foreach (var name in names)
            if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value)) return true;
        value = 0;
        return false;
    }
}
