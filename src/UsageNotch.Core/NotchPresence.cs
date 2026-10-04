namespace UsageNotch.Core;

public readonly record struct NotchPresentation(bool Visible, double Opacity, bool ClickThrough);

public static class NotchPresence
{
    public static NotchPresentation Evaluate(AppSettings settings, TaskActivitySummary activity, DateTimeOffset now,
        bool pointerOver, bool fullscreen, bool pinned = false, bool manuallyHidden = false, bool altPeek = false)
    {
        if (manuallyHidden) return new(false, 1, settings.ClickThrough);
        if (pinned) return new(true, 1, false);
        var age = Math.Max(0, (now - (activity.Task?.ObservedAt ?? now)).TotalSeconds);
        var attention = activity.Kind == IslandActivityKind.Attention && activity.Task is not null && age <= 300 &&
            (settings.AttentionMode switch
            {
                "Brief" => age < settings.AttentionDurationSeconds,
                "Remind" => age < settings.AttentionDurationSeconds || age % 60 < 8,
                _ => true
            });
        var active = activity.Task is not null && (activity.Kind is IslandActivityKind.Working or IslandActivityKind.Finished or IslandActivityKind.Interrupted);
        var visible = attention || (altPeek && pointerOver) || (settings.PresenceMode switch
        {
            "Active" => active,
            "Hover" => pointerOver,
            _ => true
        });
        if (fullscreen)
        {
            visible = settings.EffectiveFullscreenMode switch
            {
                "Hidden" => false,
                "Alerts" => attention || (active && (activity.Kind is IslandActivityKind.Finished or IslandActivityKind.Interrupted) && age < 8),
                _ => visible
            };
        }
        // Hover-only presence must remain usable; pinned details and attention requests take priority over fading.
        var faded = visible && settings.FadeUnderPointer && settings.PresenceMode != "Hover" && pointerOver && !altPeek && !attention;
        return new(visible, faded ? Math.Clamp(settings.PointerOpacityPercent, 0, 100) / 100d : 1,
            faded || settings.ClickThrough && !altPeek);
    }
    public static string ShortcutLabel(string shortcut) => shortcut switch
    {
        "CtrlAltP" => "Ctrl + Alt + P", "CtrlShiftSpace" => "Ctrl + Shift + Space", _ => "Ctrl + Alt + U"
    };
}
