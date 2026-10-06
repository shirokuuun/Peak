using System.Net.Mail;

namespace UsageNotch.Core;

/// <summary>Public contact details only. App access never depends on this configuration.</summary>
public sealed record PublishingConfiguration
{
    public string PublisherName { get; init; } = "";
    public string SupportEmail { get; init; } = "";
    public string WebsiteUrl { get; init; } = "";
    public string ReleasesUrl { get; init; } = "";
    public string PolicyVersion { get; init; } = "";

    public bool IsComplete => !string.IsNullOrWhiteSpace(PublisherName) &&
        IsSupportEmail(SupportEmail) && IsHttpsUrl(WebsiteUrl) && IsHttpsUrl(ReleasesUrl) &&
        !string.IsNullOrWhiteSpace(PolicyVersion);

    public static bool IsSupportEmail(string value) => !value.Contains('\r') && !value.Contains('\n') &&
        MailAddress.TryCreate(value, out var address) && address.Address == value;

    public static bool IsHttpsUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);
}

/// <summary>Sample data is selected only by an explicit demo flag, in every build configuration.</summary>
public static class SessionMode
{
    public static bool IsDemo(IEnumerable<string> arguments) => arguments.Contains("--demo", StringComparer.Ordinal);
}
