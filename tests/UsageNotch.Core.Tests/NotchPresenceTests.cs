using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public sealed class NotchPresenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static TaskActivitySummary Activity(IslandActivityKind kind, int age = 0) => new(
        new CodexTask("chat", "Test chat", "Peak", kind == IslandActivityKind.Attention ? CodexTaskState.ApprovalRequested :
            CodexTaskState.Working, "Reported activity", Now.AddSeconds(-age), true), kind, 0);
    private static NotchPresentation Show(AppSettings settings, TaskActivitySummary? activity = null,
        bool pointer = false, bool fullscreen = false, bool pinned = false, bool hidden = false, bool alt = false) =>
        NotchPresence.Evaluate(settings, activity ?? TaskActivitySummary.Empty, Now, pointer, fullscreen, pinned, hidden, alt);

    [Theory]
    [InlineData("Always", false, true)]
    [InlineData("Always", true, true)]
    [InlineData("Active", false, false)]
    [InlineData("Active", true, false)]
    [InlineData("Hover", false, false)]
    [InlineData("Hover", true, true)]
    public void IdlePresenceFollowsTheChosenMode(string mode, bool pointer, bool visible) =>
        Assert.Equal(visible, Show(new() { PresenceMode = mode }, pointer: pointer).Visible);

    [Theory]
    [InlineData(IslandActivityKind.Working)]
    [InlineData(IslandActivityKind.Finished)]
    [InlineData(IslandActivityKind.Interrupted)]
    public void ConfirmedWorkAppearsInActiveMode(IslandActivityKind kind) =>
        Assert.True(Show(new() { PresenceMode = "Active" }, Activity(kind)).Visible);

    [Fact]
    public void AttentionReturnsInHoverModeButAnExpiredReportDoesNot()
    {
        var settings = new AppSettings { PresenceMode = "Hover", FadeUnderPointer = true };
        Assert.True(Show(settings, Activity(IslandActivityKind.Attention)).Visible);
        Assert.Equal(1, Show(settings, Activity(IslandActivityKind.Attention), pointer: true).Opacity);
        Assert.False(Show(settings, Activity(IslandActivityKind.Attention, 301)).Visible);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(60, false)]
    public void BriefAttentionEndsAtTheSelectedDuration(int age, bool visible) =>
        Assert.Equal(visible, Show(new() { PresenceMode = "Hover", AttentionMode = "Brief", AttentionDurationSeconds = 30 },
            Activity(IslandActivityKind.Attention, age)).Visible);

    [Theory]
    [InlineData(20, true)]
    [InlineData(30, false)]
    [InlineData(60, true)]
    [InlineData(67, true)]
    [InlineData(68, false)]
    [InlineData(120, true)]
    [InlineData(301, false)]
    public void RemindersReturnBrieflyWhileTheReportIsFresh(int age, bool visible) =>
        Assert.Equal(visible, Show(new() { PresenceMode = "Hover", AttentionMode = "Remind", AttentionDurationSeconds = 30 },
            Activity(IslandActivityKind.Attention, age)).Visible);

    [Fact]
    public void FullscreenHiddenWinsOverAttentionButManualRevealStillWorks()
    {
        var settings = new AppSettings { FullscreenMode = "Hidden" };
        Assert.False(Show(settings, Activity(IslandActivityKind.Attention), fullscreen: true).Visible);
        var revealed = Show(settings with { ClickThrough = true }, fullscreen: true, pinned: true);
        Assert.True(revealed.Visible);
        Assert.Equal(1, revealed.Opacity);
        Assert.False(revealed.ClickThrough);
        Assert.False(Show(settings, pinned: true, hidden: true).Visible);
    }

    [Fact]
    public void FullscreenAlertsShowAttentionAndOnlyBriefCompletion()
    {
        var settings = new AppSettings { FullscreenMode = "Alerts" };
        Assert.False(Show(settings, fullscreen: true).Visible);
        Assert.False(Show(settings, Activity(IslandActivityKind.Working), fullscreen: true).Visible);
        Assert.True(Show(settings, Activity(IslandActivityKind.Attention), fullscreen: true).Visible);
        Assert.True(Show(settings, Activity(IslandActivityKind.Finished, 7), fullscreen: true).Visible);
        Assert.False(Show(settings, Activity(IslandActivityKind.Finished, 8), fullscreen: true).Visible);
        Assert.True(Show(settings, Activity(IslandActivityKind.Interrupted, 7), fullscreen: true).Visible);
        Assert.False(Show(settings, Activity(IslandActivityKind.Interrupted, 8), fullscreen: true).Visible);
    }

    [Fact]
    public void FullscreenVisibleStillFollowsPresence()
    {
        Assert.True(Show(new() { FullscreenMode = "Visible" }, fullscreen: true).Visible);
        Assert.False(Show(new() { FullscreenMode = "Visible", PresenceMode = "Active" }, fullscreen: true).Visible);
    }

    [Fact]
    public void FadingPassesClicksThroughAndRestoresOpacityWhenPointerLeaves()
    {
        var settings = new AppSettings { FadeUnderPointer = true, PointerOpacityPercent = 25 };
        var faded = Show(settings, pointer: true);
        Assert.Equal(.25, faded.Opacity);
        Assert.True(faded.ClickThrough);
        var restored = Show(settings);
        Assert.Equal(1, restored.Opacity);
        Assert.False(restored.ClickThrough);
        Assert.Equal(0, Show(settings with { PointerOpacityPercent = 0 }, pointer: true).Opacity);
    }

    [Fact]
    public void AltPeekAndHoverModeRemainReadableAndInteractive()
    {
        var settings = new AppSettings { PresenceMode = "Active", FadeUnderPointer = true, ClickThrough = true };
        var peek = Show(settings, pointer: true, alt: true);
        Assert.True(peek.Visible);
        Assert.Equal(1, peek.Opacity);
        Assert.False(peek.ClickThrough);
        var hover = Show(settings with { PresenceMode = "Hover", ClickThrough = false }, pointer: true);
        Assert.Equal(1, hover.Opacity);
        Assert.False(hover.ClickThrough);
    }

    [Theory]
    [InlineData(true, "Hidden")]
    [InlineData(false, "Visible")]
    public void LegacyFullscreenPreferencesMigrateWithoutChangingBehavior(bool hide, string mode)
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"AutoHideFullscreen\":" + hide.ToString().ToLowerInvariant() + "}")!.Normalize();
        Assert.Equal(mode, legacy.FullscreenMode);
        Assert.Equal(hide, legacy.AutoHideFullscreen);
    }

    [Fact]
    public void ExplicitFullscreenPreferenceOverridesLegacyValue()
    {
        Assert.Equal("Visible", new AppSettings { FullscreenMode = "Visible", AutoHideFullscreen = true }.Normalize().EffectiveFullscreenMode);
        Assert.True(new AppSettings { FullscreenMode = "Alerts", AutoHideFullscreen = false }.Normalize().AutoHideFullscreen);
    }

    [Fact]
    public void InvalidSettingsCannotMakeTheNotchUnrecoverable()
    {
        var settings = new AppSettings { PresenceMode = "bad", FullscreenMode = "bad", AttentionMode = "bad",
            FadeSpeed = "bad", RecoveryShortcut = "bad", PointerOpacityPercent = -10, AttentionDurationSeconds = 999 }.Normalize();
        Assert.Equal("Always", settings.PresenceMode);
        Assert.Equal("Hidden", settings.FullscreenMode);
        Assert.Equal("UntilCleared", settings.AttentionMode);
        Assert.Equal("Quick", settings.FadeSpeed);
        Assert.Equal("CtrlAltU", settings.RecoveryShortcut);
        Assert.Equal(0, settings.PointerOpacityPercent);
        Assert.Equal(300, settings.AttentionDurationSeconds);
    }

    [Fact]
    public void NewPreferencesRoundTripWithoutSavingDerivedProperties()
    {
        var settings = new AppSettings { PresenceMode = "Hover", FullscreenMode = "Alerts", AttentionMode = "Remind",
            AttentionDurationSeconds = 60, FadeUnderPointer = true, PointerOpacityPercent = 15, FadeSpeed = "Soft",
            RecoveryShortcut = "CtrlAltP", HideFromCapture = true, Theme = "Custom", CustomColor = "#FFF0AA" }.Normalize();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(settings, options);
        Assert.DoesNotContain("effectiveFullscreenMode", json);
        Assert.Equal(settings, JsonSerializer.Deserialize<AppSettings>(json, options)!.Normalize());
    }
}
