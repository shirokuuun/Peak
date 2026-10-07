using CommunityToolkit.WinUI.Notifications;
using Microsoft.Win32;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Windows.Threading;
using UsageNotch.Core;
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
    private PublishingConfiguration _publishing = new();
    private readonly DispatcherTimer _maintenance = new() { Interval = TimeSpan.FromHours(1) };
    private bool _maintenanceRunning;
    private bool _explicitDemo;
    private bool _inspect;
    private bool _demoCycle;
    private bool _firstRun;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\UsageNotch.SingleInstance", out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\UsageNotch.Show");
        if (!first) { if (e.Args.Contains("--prepare-uninstall") || e.Args.Contains("--delete-local-data")) Shutdown(2); else { _showEvent.Set(); Shutdown(); } return; }
        if (e.Args.Contains("--prepare-uninstall") || e.Args.Contains("--delete-local-data"))
        {
            RunCleanup(e.Args.Contains("--delete-local-data"), e.Args.Contains("--quiet"));
            return;
        }
        try
        {
            _explicitDemo = SessionMode.IsDemo(e.Args);
            _inspect = e.Args.Contains("--inspect");
            _demoCycle = e.Args.Contains("--demo-cycle");
            _publishing = PublishingSetup.Load();
            await StartSessionAsync();
            _maintenance.Tick += OnMaintenance;
            _maintenance.Start();
            if (e.Args.Contains("--settings") || _firstRun) ShowSettings();
        }
        catch (Exception exception)
        {
            try
            {
                var folder = new SettingsStore().DataDirectory;
                Directory.CreateDirectory(folder);
                // Keep only a fault category; exception messages/stack traces can expose machine paths or private context.
                File.WriteAllText(Path.Combine(folder, "startup-error.txt"), DateTimeOffset.UtcNow.ToString("O") + " " + exception.GetType().Name);
            }
            catch { }
            MessageBox.Show("Peak could not start. Check that its local data folder is accessible.", "Peak", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private void RunCleanup(bool deleteData, bool quiet)
    {
        try
        {
            var cleanup = new LocalDataCleanup();
            var errors = cleanup.DisconnectIntegrations();
            if (errors.Count > 0) throw new IOException(string.Join("\n", errors));
            if (deleteData) cleanup.DeleteAllLocalData();
            else cleanup.RemoveBridgeFiles();
            Shutdown(0);
        }
        catch
        {
            if (!quiet) MessageBox.Show("Cleanup could not finish. Close Peak and the provider CLI sessions, then disconnect providers in Settings before trying again. Restoration backups were preserved when a provider could not be disconnected.",
                "Peak cleanup", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(2);
        }
    }

    private async Task StartSessionAsync()
    {
        var demo = _explicitDemo;
        _isDemo = demo;
        var inspect = _inspect || demo;
        var store = new SettingsStore(demo ? Path.Combine(new SettingsStore().DataDirectory, "Demo") : null);
        var settings = await store.LoadAsync();
        _firstRun = !demo && settings.ProviderConsentVersion < 1;
        if (_firstRun)
        {
            var keep = MessageBox.Show("Peak can read account labels, limits, local chat names and lifecycle logs from the providers you enable. It saves sanitized observations locally. Peak is free to use and has no telemetry. No prompts or provider credentials are saved. Keep your existing provider choices? Choose No to disable monitoring and review Privacy in Settings first.",
                "Peak · provider access", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.Yes;
            settings = settings with { ProviderConsentVersion = 1, CodexEnabled = keep && settings.CodexEnabled,
                CodexTasksEnabled = keep && settings.CodexTasksEnabled, ClaudeEnabled = keep && settings.ClaudeEnabled };
            await store.SaveAsync(settings);
        }
        if (inspect) settings = settings with { AutoHideFullscreen = false, FullscreenMode = "Visible" };
        if (demo) settings = settings with { ClaudeEnabled = true, CodexEnabled = true, NotificationsEnabled = false,
            ResetNotificationsEnabled = false, TaskNotificationsEnabled = false, CodexTasksEnabled = true,
            LaunchOnStartup = false, ClickThrough = false, ExpandOnHover = true, TopOffset = AppSettings.DefaultTopOffset };
        else new LocalDataCleanup().PruneObservations();
        if (!inspect)
        {
            var startup = new StartupService();
            if (await startup.GetPackagedEnabledAsync() is { } enabled)
            {
                if (enabled != settings.LaunchOnStartup)
                {
                    settings = settings with { LaunchOnStartup = enabled };
                    await store.SaveAsync(settings);
                }
            }
            else if (settings.LaunchOnStartup) await startup.SetEnabledAsync(true);
        }
        var codex = demo ? null : new CodexUsageProvider();
        _viewModel = new MainViewModel(demo ? [new DemoProvider("codex", 90, 45), new DemoProvider("claude", 32, 68)] :
            [codex!, new ClaudeSnapshotProvider()], store, settings, await store.LoadCacheAsync(), await store.LoadAlertsAsync(),
            demo ? new DemoTaskMonitor(_demoCycle) : new CodexTaskMonitor(codex));
        _window = new MainWindow(_viewModel) { ShowInTaskbar = inspect };
        MainWindow = _window;
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent!,
            (_, _) => Dispatcher.BeginInvoke(() => _window?.Recover()), null, Timeout.Infinite, false);
        _tray = new TrayService();
        _tray.ToggleRequested += (_, _) => _window?.ToggleVisibility();
        _tray.DetailsRequested += (_, _) => _window?.Recover();
        _tray.RefreshRequested += async (_, _) => { if (_viewModel is not null) await _viewModel.RefreshAsync(); };
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ExitRequested += async (_, _) => await ExitAsync();
        _window.ExitRequested += async (_, _) => await ExitAsync();
        _viewModel.SettingsRequested += (_, _) => ShowSettings();
        _viewModel.AlertRaised += (_, alert) => _notifications.Show($"{alert.ProviderId} · {alert.WindowLabel}: {alert.UsedPercent:0}% used ({Math.Max(0, 100 - alert.UsedPercent):0}% remaining).");
        _viewModel.ResetRaised += (_, alert) => _notifications.Show($"{alert.ProviderId} · {alert.WindowLabel} reset confirmed.");
        _viewModel.TaskAttentionRaised += (_, text) => _notifications.Show(text);
        if (!_eventsAttached)
        {
            ToastNotificationManagerCompat.OnActivated += _ => Dispatcher.BeginInvoke(() => _window?.Recover());
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerChanged;
            SystemEvents.UserPreferenceChanged += OnPreferencesChanged;
            _eventsAttached = true;
        }
        _window.Show();
        await _viewModel.StartAsync();
    }
    private bool _eventsAttached;

    private void OnMaintenance(object? sender, EventArgs e)
    {
        if (_maintenanceRunning || _exiting || _isDemo) return;
        _maintenanceRunning = true;
        try { new LocalDataCleanup().PruneObservations(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { /* Retry during the next maintenance cycle; provider access stays independent. */ }
        finally { _maintenanceRunning = false; }
    }

    private async void ShowSettings()
    {
        if (_viewModel is null || _window is null || _exiting) return;
        if (_settings is not null) { _settings.Activate(); return; }
        var dialog = new SettingsWindow(_viewModel.Settings, _isDemo, _viewModel, _window.RecoveryShortcutAvailable,
            _publishing, _firstRun) { Topmost = true };
        _settings = dialog; _firstRun = false;
        dialog.PeekRequested += (_, _) => _window?.Recover();
        try
        {
            if (dialog.ShowDialog() == true && _viewModel is not null)
            {
                await _viewModel.ApplySettingsAsync(dialog.Result);
                if (dialog.Result.LaunchOnStartup && !_viewModel.Settings.LaunchOnStartup)
                    MessageBox.Show("Windows has disabled Peak at sign-in. Enable Peak in Windows Settings → Apps → Startup, then reopen Peak’s settings.", "Peak");
                if (_viewModel.Settings.HideFromCapture && _window is { CaptureExclusionApplied: false })
                    MessageBox.Show("Your settings were saved, but Windows could not exclude Peak from capture. Check your recording preview before sharing.", "Peak");
                if (_window is { RecoveryShortcutAvailable: false })
                    MessageBox.Show("Your settings were saved, but another app uses the reveal shortcut. Choose another in Settings, or use Peak's tray menu.", "Peak");
            }
            if (dialog.ClearDataRequested || dialog.DisconnectRequested)
            {
                await StopSessionAsync();
                var cleanup = new LocalDataCleanup();
                var errors = cleanup.DisconnectIntegrations(removeStartup: false);
                if (errors.Count == 0 && dialog.ClearDataRequested) cleanup.ClearObservations();
                var store = new SettingsStore();
                var settings = await store.LoadAsync();
                await store.SaveAsync(settings with { CodexEnabled = false, CodexTasksEnabled = false, ClaudeEnabled = false });
                await StartSessionAsync();
                MessageBox.Show(errors.Count == 0 ? "Peak's providers are disconnected. Restart any existing CLI sessions to unload their old connections." : string.Join("\n", errors), "Peak privacy");
            }
            if (dialog.RestartRequested || dialog.DemoRequested)
            {
                _explicitDemo = dialog.DemoRequested;
                await StopSessionAsync(); await StartSessionAsync();
                if (_firstRun) _ = Dispatcher.BeginInvoke(ShowSettings);
            }
        }
        catch
        {
            if (_viewModel is null && !_exiting)
            {
                try { await StartSessionAsync(); }
                catch { await ExitAsync(); }
            }
            MessageBox.Show("The action could not finish. Check local data and provider configuration permissions. You can retry from Settings.", "Peak");
        }
        finally { _settings = null; if (_viewModel is not null) ThemeService.Apply(_viewModel.Settings); }
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
        _maintenance.Stop();
        await StopSessionAsync();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        SystemEvents.UserPreferenceChanged -= OnPreferencesChanged;
        Shutdown();
    }
    private async Task StopSessionAsync()
    {
        _showWait?.Unregister(null); _showWait = null;
        _window?.Stop(); _tray?.Dispose(); _tray = null;
        if (_viewModel is not null) await _viewModel.DisposeAsync();
        _window?.Close(); _window = null; _viewModel = null;
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose(); _showEvent?.Dispose(); _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
