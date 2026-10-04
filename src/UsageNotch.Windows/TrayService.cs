using System.Drawing;
using System.Windows.Forms;

namespace UsageNotch.Windows;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _applicationIcon;

    public event EventHandler? ToggleRequested;
    public event EventHandler? DetailsRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show details", null, (_, _) => DetailsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Show / hide", null, (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Refresh now", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _applicationIcon = Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null;
        _icon = new NotifyIcon
        {
            Icon = _applicationIcon ?? SystemIcons.Application,
            Text = "Peak",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => DetailsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowUsageAlert(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = ToolTipIcon.Warning;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _applicationIcon?.Dispose();
    }
    private bool _disposed;
}
