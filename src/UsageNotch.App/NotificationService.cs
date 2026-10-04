using CommunityToolkit.WinUI.Notifications;

namespace UsageNotch.App;

internal sealed class NotificationService
{
    public void Show(string message)
    {
        try
        {
            new ToastContentBuilder().AddText("Peak").AddText(message).AddArgument("action", "show")
                .Show(toast => { toast.ExpirationTime = DateTimeOffset.Now.AddHours(1); });
        }
        catch { /* Notifications are optional; never terminate the overlay when Windows suppresses them. */ }
    }
}
