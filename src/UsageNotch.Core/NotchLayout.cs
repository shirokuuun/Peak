namespace UsageNotch.Core;

public readonly record struct NotchLayout(double Width, double HeaderHeight, bool ShowActivity, bool AttachedToTop)
{
    public const double HostWidth = 600;
    public const double HostHeight = 560;

    public static NotchLayout Calculate(AppSettings settings, bool expanded, bool hasActivity)
    {
        var attached = settings.NotchStyle == "FixedNotch";
        var showActivity = hasActivity && (expanded || !attached);
        return new(expanded || hasActivity && !attached ? HostWidth : 360, showActivity ? 78 : 42, showActivity, attached);
    }
}
