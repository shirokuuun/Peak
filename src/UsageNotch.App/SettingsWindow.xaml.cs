using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UsageNotch.Core;
using UsageNotch.Windows;

namespace UsageNotch.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private string _customColor;
    private bool _initializing = true;
    private readonly ClaudeBridgeSetup _bridge = new();
    private readonly CodexTaskBridgeSetup _taskBridge = new();
    private readonly Dictionary<string, (FrameworkElement Page, string Title, string Description)> _sections = new();
    public event EventHandler? PeekRequested;
    public AppSettings Result { get; private set; }
    public SettingsWindow(AppSettings settings, bool demo = false, MainViewModel? liveViewModel = null, bool shortcutAvailable = true)
    {
        _original = Result = settings.Normalize();
        _customColor = _original.CustomColor;
        InitializeComponent();
        DataContext = liveViewModel;
        _sections.Add("Presence", (PresencePage, "Presence", "Choose when Peak stays in view, and when it needs your attention."));
        _sections.Add("OutOfWay", (OutOfWayPage, "Out of the way", "Keep your screen within reach. Peak can step aside under the pointer."));
        _sections.Add("Appearance", (AppearancePage, "Appearance", "Make the notch yours. Its text and status colors adapt to your choice."));
        _sections.Add("Providers", (ProvidersPage, "Providers", "Connect your usage and task activity to Peak."));
        _sections.Add("Alerts", (AlertsPage, "Alerts", "Choose which updates deserve a notification."));
        _sections.Add("Shortcuts", (ShortcutsPage, "Shortcuts", "Bring Peak back whenever you need it."));
        _sections.Add("System", (SystemPage, "System", "Manage startup, refresh timing, and local data."));
        CodexEnabled.IsChecked = settings.CodexEnabled; ClaudeEnabled.IsChecked = settings.ClaudeEnabled;
        CodexTasksEnabled.IsChecked = settings.CodexTasksEnabled; TaskNotificationsEnabled.IsChecked = settings.TaskNotificationsEnabled;
        TaskBridgeStatus.Text = _taskBridge.IsInstalled() ? "Installed. Review and trust Peak hooks in Codex /hooks; reconnect existing chats. Hook observations start after trust." :
            "Connect installs local observation hooks. Review and trust them in Codex /hooks, then reconnect existing chats. No approval decisions are made by the notch.";
        RefreshInterval.Text = settings.RefreshIntervalSeconds.ToString();
        WarningThreshold.Text = settings.WarningThreshold.ToString(); CriticalThreshold.Text = settings.CriticalThreshold.ToString();
        NotificationsEnabled.IsChecked = settings.NotificationsEnabled; ResetNotificationsEnabled.IsChecked = settings.ResetNotificationsEnabled;
        LaunchOnStartup.IsChecked = settings.LaunchOnStartup; ExpandOnHover.IsChecked = settings.ExpandOnHover;
        ClickThrough.IsChecked = settings.ClickThrough;
        FadeUnderPointer.IsChecked = settings.FadeUnderPointer;
        PointerOpacity.Value = settings.PointerOpacityPercent;
        HideFromCapture.IsChecked = settings.HideFromCapture;
        AttentionDuration.Value = settings.AttentionDurationSeconds;
        SelectChoice(PresenceChoices, _original.PresenceMode);
        SelectChoice(AttentionChoices, _original.AttentionMode);
        SelectChoice(FullscreenChoices, _original.EffectiveFullscreenMode);
        SelectChoice(FadeChoices, _original.FadeSpeed);
        RecoveryShortcut.ItemsSource = new[] { "Ctrl + Alt + U", "Ctrl + Alt + P", "Ctrl + Shift + Space" };
        RecoveryShortcut.SelectedItem = NotchPresence.ShortcutLabel(_original.RecoveryShortcut);
        ShortcutStatus.Text = shortcutAvailable ? "Works while another app is in front. Also available from Peak's tray menu." :
            "The current shortcut is used by another app. Choose a different shortcut, or reveal Peak from its tray menu.";
        Monitor.ItemsSource = WindowPlatformService.MonitorNames;
        Monitor.SelectedItem = WindowPlatformService.MonitorNames.Contains(settings.MonitorDeviceName) ? settings.MonitorDeviceName : "Primary";
        Theme.ItemsSource = new[] { "System", "Dark", "Light", "Custom" }; Theme.SelectedItem = _original.Theme;
        TopOffset.Value = settings.TopOffset;
        BridgeStatus.Text = _bridge.IsInstalled() ? "Connected. Restart Claude Code and use it to receive usage." :
            "Connect adds a local usage bridge and preserves your current status line. Claude Code 2.1.251+ is required.";
        if (demo)
        {
            ConnectClaude.IsEnabled = DisconnectClaude.IsEnabled = LaunchOnStartup.IsEnabled = false;
            ConnectTasks.IsEnabled = DisconnectTasks.IsEnabled = false;
            TaskNotificationsEnabled.IsEnabled = false;
            TaskBridgeStatus.Text = "Demo preview · sample tasks. Task setup is disabled.";
            BridgeStatus.Text = "Demo preview · sample data. Provider setup and startup changes are disabled.";
        }
        _initializing = false;
        UpdateColorDescription();
        UpdateBehaviorHints();
        ShowSection("Presence");
        Closed += (_, _) => { if (DialogResult != true) ThemeService.Apply(_original); };
    }
    private static void SelectChoice(Panel group, string value)
    {
        foreach (var choice in group.Children.OfType<RadioButton>()) choice.IsChecked = choice.Tag?.ToString() == value;
    }
    private static string Choice(Panel group, string fallback) => group.Children.OfType<RadioButton>()
        .FirstOrDefault(choice => choice.IsChecked == true)?.Tag?.ToString() ?? fallback;
    private void OnSectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_initializing && sender is RadioButton navigation) ShowSection(navigation.Tag?.ToString() ?? "Presence");
    }
    private void ShowSection(string name)
    {
        if (!_sections.TryGetValue(name, out var selected)) return;
        foreach (var section in _sections.Values) section.Page.Visibility = section.Page == selected.Page ? Visibility.Visible : Visibility.Collapsed;
        SectionTitle.Text = selected.Title;
        SectionDescription.Text = selected.Description;
        SectionScroll.ScrollToTop();
        ValidationMessage.Text = "";
    }
    private void OnBehaviorChoice(object sender, RoutedEventArgs e)
    {
        if (!_initializing) UpdateBehaviorHints();
    }
    private void UpdateBehaviorHints()
    {
        var attention = Choice(AttentionChoices, "UntilCleared");
        AttentionTiming.Visibility = attention == "UntilCleared" ? Visibility.Collapsed : Visibility.Visible;
        AttentionHint.Text = attention switch
        {
            "Brief" => "Show reported requests for the time below, then follow your presence setting.",
            "Remind" => "Show for the time below, then return for 8 seconds each minute while the request is still reported.",
            _ => "Keep reported requests visible until the task updates. Reports expire after five minutes without an update."
        };
        PointerOpacity.IsEnabled = FadeUnderPointer.IsChecked == true;
        FadeChoices.IsEnabled = FadeUnderPointer.IsChecked == true;
    }
    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
    private void OnPeek(object sender, RoutedEventArgs e) => PeekRequested?.Invoke(this, EventArgs.Empty);
    private void UpdateMiniPreview()
    {
        MiniNotch.Background = (System.Windows.Media.Brush)Application.Current.Resources["SurfaceBrush"];
        MiniNotch.BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["CardBorderBrush"];
        var text = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"];
        MiniTitle.Foreground = MiniCodex.Foreground = MiniClaude.Foreground = text;
    }
    private void UpdateColorDescription()
    {
        RgbColor.TryParse(_customColor, out var color);
        ColorSwatch.Background = ThemeService.Brush(color);
        ColorDescription.Text = Theme.SelectedItem?.ToString() == "Custom" ?
            _customColor + " · Text and status colors adjust for readability." : "Choose any color, or follow a light, dark, or system theme.";
        UpdateMiniPreview();
    }
    public void RefreshPreview()
    {
        ThemeService.Apply(_original with { Theme = Theme.SelectedItem?.ToString() ?? "System", CustomColor = _customColor });
        UpdateMiniPreview();
    }
    private void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        UpdateColorDescription(); RefreshPreview();
    }
    private void OnChooseColor(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow(_customColor) { Owner = this };
        picker.ColorChanged += color =>
        {
            ThemeService.Apply(_original with { Theme = "Custom", CustomColor = color.Hex });
            UpdateMiniPreview();
        };
        if (picker.ShowDialog() == true)
        {
            _customColor = picker.SelectedColor.Hex;
            Theme.SelectedItem = "Custom";
        }
        UpdateColorDescription(); RefreshPreview();
    }
    private void OnConnectTasks(object sender, RoutedEventArgs e)
    {
        try
        {
            _taskBridge.Install(Path.Combine(AppContext.BaseDirectory, "bridge"));
            CodexTasksEnabled.IsChecked = true;
            TaskBridgeStatus.Text = "Installed now. In Codex, use /hooks to review and trust Peak hooks, then reconnect existing chats. Cancel keeps this connection.";
        }
        catch { TaskBridgeStatus.Text = "Could not connect. Check that the published bridge and Codex hooks.json are available and writable."; }
    }
    private void OnDisconnectTasks(object sender, RoutedEventArgs e)
    {
        try
        {
            _taskBridge.Uninstall();
            TaskBridgeStatus.Text = "Peak hooks removed; other hooks preserved. Reconnect existing chats to stop their loaded hooks.";
        }
        catch { TaskBridgeStatus.Text = "Could not disconnect. Check Codex hooks.json permissions."; }
    }
    private void OnConnectClaude(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "bridge");
            if (!Directory.Exists(folder)) folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../UsageNotch.ClaudeBridge/bin/Debug/net10.0"));
            _bridge.Install(folder);
            ClaudeEnabled.IsChecked = true;
            BridgeStatus.Text = "Connected. Restart Claude Code; usage appears after its first response. This connection is saved now.";
        }
        catch (Exception exception) { BridgeStatus.Text = exception.Message; }
    }
    private void OnDisconnectClaude(object sender, RoutedEventArgs e)
    {
        try
        {
            var restored = _bridge.Uninstall();
            BridgeStatus.Text = restored ? "Disconnected; your previous status line was restored." : "Disconnected. Your current status line was preserved.";
            ClaudeEnabled.IsChecked = false;
        }
        catch { BridgeStatus.Text = "Could not disconnect. Check that the Claude settings file is writable."; }
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshInterval.Text, out var refresh) || refresh is < 30 or > 600)
        {
            NavSystem.IsChecked = true;
            ValidationMessage.Text = "Refresh interval must be between 30 and 600 seconds.";
            RefreshInterval.Focus(); return;
        }
        if (!int.TryParse(WarningThreshold.Text, out var warning) || warning is < 1 or > 98 ||
            !int.TryParse(CriticalThreshold.Text, out var critical) || critical <= warning || critical > 100)
        {
            NavAlerts.IsChecked = true;
            ValidationMessage.Text = "Critical must be above warning, up to 100%.";
            WarningThreshold.Focus(); return;
        }
        Result = (_original with
        {
            CodexEnabled = CodexEnabled.IsChecked == true, ClaudeEnabled = ClaudeEnabled.IsChecked == true,
            CodexTasksEnabled = CodexTasksEnabled.IsChecked == true, TaskNotificationsEnabled = TaskNotificationsEnabled.IsChecked == true,
            RefreshIntervalSeconds = refresh, WarningThreshold = warning, CriticalThreshold = critical,
            NotificationsEnabled = NotificationsEnabled.IsChecked == true,
            ResetNotificationsEnabled = ResetNotificationsEnabled.IsChecked == true,
            LaunchOnStartup = LaunchOnStartup.IsChecked == true, ExpandOnHover = ExpandOnHover.IsChecked == true,
            ClickThrough = ClickThrough.IsChecked == true,
            PresenceMode = Choice(PresenceChoices, "Always"),
            FullscreenMode = Choice(FullscreenChoices, "Hidden"),
            AttentionMode = Choice(AttentionChoices, "UntilCleared"),
            AttentionDurationSeconds = (int)Math.Round(AttentionDuration.Value),
            FadeUnderPointer = FadeUnderPointer.IsChecked == true,
            PointerOpacityPercent = (int)Math.Round(PointerOpacity.Value),
            FadeSpeed = Choice(FadeChoices, "Quick"),
            RecoveryShortcut = RecoveryShortcut.SelectedIndex switch { 1 => "CtrlAltP", 2 => "CtrlShiftSpace", _ => "CtrlAltU" },
            HideFromCapture = HideFromCapture.IsChecked == true,
            Theme = Theme.SelectedItem?.ToString() ?? "System", MonitorDeviceName = Monitor.SelectedItem?.ToString() ?? "Primary",
            CustomColor = _customColor,
            TopOffset = (int)Math.Round(TopOffset.Value)
        }).Normalize();
        DialogResult = true;
    }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
