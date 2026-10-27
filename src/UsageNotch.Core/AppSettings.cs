using System.Text.Json.Serialization;

namespace UsageNotch.Core;

public sealed record AppSettings
{
    public const int DefaultTopOffset = 12;
    public bool CodexEnabled { get; init; }
    public bool ClaudeEnabled { get; init; }
    public bool CodexTasksEnabled { get; init; }
    public int ProviderConsentVersion { get; init; }
    public bool TaskNotificationsEnabled { get; init; }
    public int RefreshIntervalSeconds { get; init; } = 60;
    public int WarningThreshold { get; init; } = 70;
    public int CriticalThreshold { get; init; } = 90;
    public bool NotificationsEnabled { get; init; }
    public bool ResetNotificationsEnabled { get; init; }
    public bool LaunchOnStartup { get; init; }
    public bool ClickThrough { get; init; }
    public bool AutoHideFullscreen { get; init; } = true;
    public bool ExpandOnHover { get; init; } = true;
    public string NotchStyle { get; init; } = "DynamicIsland";
    public string PresenceMode { get; init; } = "Always";
    public string? FullscreenMode { get; init; }
    public string AttentionMode { get; init; } = "UntilCleared";
    public int AttentionDurationSeconds { get; init; } = 30;
    public bool FadeUnderPointer { get; init; }
    public int PointerOpacityPercent { get; init; }
    public string FadeSpeed { get; init; } = "Quick";
    public string RecoveryShortcut { get; init; } = "CtrlAltU";
    public bool HideFromCapture { get; init; }
    [JsonIgnore]
    public string EffectiveFullscreenMode => FullscreenMode is "Alerts" or "Hidden" or "Visible" ? FullscreenMode :
        AutoHideFullscreen ? "Hidden" : "Visible";
    public string Theme { get; init; } = "System";
    public string CustomColor { get; init; } = NotchPalette.DefaultColor;
    public string MonitorDeviceName { get; init; } = "Primary";
    public int TopOffset { get; init; } = DefaultTopOffset;

    public AppSettings Normalize()
    {
        var warning = Math.Clamp(WarningThreshold, 1, 98);
        return this with
        {
            RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 30, 600),
            WarningThreshold = warning,
            CriticalThreshold = Math.Clamp(CriticalThreshold, warning + 1, 100),
            TopOffset = Math.Clamp(TopOffset, 0, 200),
            NotchStyle = NotchStyle == "FixedNotch" ? "FixedNotch" : "DynamicIsland",
            PresenceMode = PresenceMode is "Active" or "Hover" ? PresenceMode : "Always",
            FullscreenMode = EffectiveFullscreenMode,
            AutoHideFullscreen = EffectiveFullscreenMode != "Visible",
            AttentionMode = AttentionMode is "Brief" or "Remind" ? AttentionMode : "UntilCleared",
            AttentionDurationSeconds = Math.Clamp(AttentionDurationSeconds, 10, 300),
            PointerOpacityPercent = Math.Clamp(PointerOpacityPercent, 0, 100),
            FadeSpeed = FadeSpeed is "Instant" or "Soft" ? FadeSpeed : "Quick",
            RecoveryShortcut = RecoveryShortcut is "CtrlAltP" or "CtrlShiftSpace" ? RecoveryShortcut : "CtrlAltU",
            Theme = Theme is "Light" or "Dark" or "Custom" ? Theme : "System",
            CustomColor = RgbColor.TryParse(CustomColor, out var color) ? color.Hex : NotchPalette.DefaultColor
        };
    }
}
