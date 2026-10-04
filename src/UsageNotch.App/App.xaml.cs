using CommunityToolkit.WinUI.Notifications;
using Microsoft.Win32;
using System.IO;
using System.Threading;
using System.Windows;
using UsageNotch.Providers;
using UsageNotch.Windows;

namespace UsageNotch.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private TrayService? _tray;
    private readonly NotificationService _notifications = new();
    private bool _exiting;
    private bool _isDemo;
    private SettingsWindow? _settings;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\UsageNotch.SingleInstance", out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\UsageNotch.Show");
        if (!first) { _showEvent.Set(); Shutdown(); return; }
        try
        {
            var demo = e.Args.Contains("--demo");
            _isDemo = demo;
            var inspect = e.Args.Contains("--inspect") || demo;
            var store = new SettingsStore(demo ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch", "Demo") : null);
            var settings = await store.LoadAsync();
            if (inspect) settings = settings with { AutoHideFullscreen = false, FullscreenMode = "Visible" };
            if (demo) settings = settings with { ClaudeEnabled = true, CodexEnabled = true, NotificationsEnabled = false,
                ResetNotificationsEnabled = false, TaskNotificationsEnabled = false, CodexTasksEnabled = true,
                LaunchOnStartup = false, ClickThrough = false, ExpandOnHover = true, TopOffset = UsageNotch.Core.AppSettings.DefaultTopOffset };
            if (!inspect && settings.LaunchOnStartup) new StartupService().SetEnabled(true);
            var cached = await store.LoadCacheAsync();
            var codex = demo ? null : new CodexUsageProvider();
            _viewModel = new MainViewModel(demo ? [new DemoProvider("codex", 90, 45), new DemoProvider("claude", 32, 68)] :
                [codex!, new ClaudeSnapshotProvider()], store, settings, cached, await store.LoadAlertsAsync(),
                demo ? new DemoTaskMonitor(e.Args.Contains("--demo-cycle")) : new CodexTaskMonitor(codex));
            _window = new MainWindow(_viewModel) { ShowInTaskbar = inspect };
            MainWindow = _window;
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(() => _window.Recover()), null, Timeout.Infinite, false);
            _tray = new TrayService();
            _tray.ToggleRequested += (_, _) => _window.ToggleVisibility();
            _tray.DetailsRequested += (_, _) => _window.Recover();
            _tray.RefreshRequested += async (_, _) => await _viewModel.RefreshAsync();
            _tray.SettingsRequested += (_, _) => ShowSettings();
            _tray.ExitRequested += async (_, _) => await ExitAsync();
            _window.ExitRequested += async (_, _) => await ExitAsync();
            _viewModel.SettingsRequested += (_, _) => ShowSettings();
            _viewModel.AlertRaised += (_, alert) => _notifications.Show(
                $"{alert.ProviderId} · {alert.WindowLabel}: {alert.UsedPercent:0}% used ({Math.Max(0, 100 - alert.UsedPercent):0}% remaining).");
            _viewModel.ResetRaised += (_, alert) => _notifications.Show($"{alert.ProviderId} · {alert.WindowLabel} reset confirmed.");
            _viewModel.TaskAttentionRaised += (_, text) => _notifications.Show(text);
            ToastNotificationManagerCompat.OnActivated += _ => Dispatcher.BeginInvoke(() => _window?.Recover());
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerChanged;
            SystemEvents.UserPreferenceChanged += OnPreferencesChanged;
            _window.Show();
            await _viewModel.StartAsync();
            if (e.Args.Contains("--settings")) ShowSettings();
        }
        catch (Exception exception)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "startup-error.txt"), exception.ToString());
            }
            catch { }
            MessageBox.Show("Peak could not start. Check that its local data folder and Codex CLI are accessible.",
                "Peak", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private async void ShowSettings()
    {
        if (_viewModel is null || _window is null) return;
        if (_settings is not null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(_viewModel.Settings, _isDemo, _viewModel, _window.RecoveryShortcutAvailable) { Topmost = true };
        _settings.PeekRequested += (_, _) => _window.Recover();
        try
        {
            if (_settings.ShowDialog() == true)
            {
                await _viewModel.ApplySettingsAsync(_settings.Result);
                if (_viewModel.Settings.HideFromCapture && !_window.CaptureExclusionApplied)
                    MessageBox.Show("Your settings were saved, but Windows could not exclude Peak from capture. Check your recording preview before sharing.", "Peak", MessageBoxButton.OK, MessageBoxImage.Information);
                if (!_window.RecoveryShortcutAvailable)
                    MessageBox.Show("Your settings were saved, but another app is using the reveal shortcut. Choose a different shortcut in Settings, or use Peak's tray menu.", "Peak", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch { MessageBox.Show("The settings could not be saved. Check file and startup permissions.", "Peak"); }
        finally { _settings = null; ThemeService.Apply(_viewModel.Settings); }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(async () =>
    {
        if (_viewModel is null) return;
        if (e.Reason == SessionSwitchReason.SessionLock) _viewModel.IsSuspended = true;
        if (e.Reason == SessionSwitchReason.SessionUnlock) { _viewModel.IsSuspended = false; await _viewModel.RefreshAsync(); }
    });
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(async () =>
    {
        if (_viewModel is null) return;
        if (e.Mode == PowerModes.Suspend) _viewModel.IsSuspended = true;
        if (e.Mode == PowerModes.Resume) { _viewModel.IsSuspended = false; await _viewModel.RefreshAsync(); }
    });
    private void OnPreferencesChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => { if (_settings is not null) _settings.RefreshPreview(); else if (_viewModel is not null) ThemeService.Apply(_viewModel.Settings); });

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _showWait?.Unregister(null);
        _window?.Stop();
        _tray?.Dispose();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        SystemEvents.UserPreferenceChanged -= OnPreferencesChanged;
        if (_viewModel is not null) await _viewModel.DisposeAsync();
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose(); _showEvent?.Dispose(); _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
