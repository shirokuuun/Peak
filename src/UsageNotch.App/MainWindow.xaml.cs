using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UsageNotch.Windows;
using UsageNotch.Core;

namespace UsageNotch.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly WindowPlatformService _platform = new();
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _fullscreen = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _presence = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _expanded, _pinned, _manualHidden, _recovery, _foregroundFullscreen, _pointerWasOver;
    private DateTimeOffset _lastPointerAt = DateTimeOffset.MinValue;
    private double _targetOpacity = 1;
    private bool _presentationVisible = true;
    private int _detailsTransition;
    public bool CaptureExclusionApplied { get; private set; }
    public bool RecoveryShortcutAvailable => _platform.HotkeyAvailable;
    private double _targetHeight = 42;
    private double _targetWidth = 360;
    public event EventHandler? ExitRequested;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Surface.ShapeChanged += (_, _) => UpdateSurfaceBounds();
        ResizeIsland(false);
        ThemeService.Changed += OnThemeChanged;
        _collapse.Tick += (_, _) => { _collapse.Stop(); if (!_pinned) SetExpanded(false); };
        _hover.Tick += (_, _) =>
        {
            _hover.Stop();
            var pointer = _platform.IsPointerOver();
            var presentation = NotchPresence.Evaluate(_viewModel.Settings, _viewModel.IslandActivity, DateTimeOffset.UtcNow,
                pointer, _foregroundFullscreen, _pinned, _manualHidden, WindowPlatformService.AltHeld);
            if (pointer && presentation.Visible && !presentation.ClickThrough) SetExpanded(true);
        };
        _fullscreen.Tick += (_, _) => CheckFullscreen();
        _presence.Tick += (_, _) => CheckPresentation();
        viewModel.SettingsChanged += (_, _) => ApplySettings();
        viewModel.ContentChanged += (_, _) => ResizeIsland();
        SizeChanged += (_, _) => Reposition();
        Closing += (_, e) => { e.Cancel = true; ExitRequested?.Invoke(this, EventArgs.Empty); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _platform.Attach(this);
        _platform.HotkeyPressed += (_, _) => Recover();
        _platform.DisplayChanged += (_, _) => Reposition();
        ApplySettings();
        _fullscreen.Start();
        _presence.Start();
    }

    public void ToggleVisibility()
    {
        if (_manualHidden || !IsVisible) Recover();
        else { _manualHidden = true; Hide(); }
    }

    public void Recover()
    {
        _manualHidden = false;
        _presentationVisible = true;
        _recovery = _pinned = true;
        BeginAnimation(OpacityProperty, null); Opacity = _targetOpacity = 1;
        Show();
        _platform.SetClickThrough(false);
        SetExpanded(true);
        Activate();
    }

    public void ApplySettings()
    {
        ThemeService.Apply(_viewModel.Settings);
        _recovery = false;
        _platform.SetRecoveryShortcut(_viewModel.Settings.RecoveryShortcut);
        CaptureExclusionApplied = _platform.SetCaptureExclusion(_viewModel.Settings.HideFromCapture);
        // Tray recovery remains available even when another app owns Ctrl+Alt+U.
        _platform.SetClickThrough(_viewModel.Settings.ClickThrough);
        ResizeIsland();
        Reposition();
        CheckFullscreen();
    }
    private void Reposition() => _platform.PositionAtTopCenter(_viewModel.Settings.TopOffset,
        _viewModel.Settings.MonitorDeviceName, _viewModel.Settings.NotchStyle == "FixedNotch");
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _viewModel.RefreshPresentation();
        _viewModel.RefreshThemeIndicators();
    }
    private void CheckFullscreen()
    {
        if (_viewModel.IsSuspended) return;
        _foregroundFullscreen = _platform.IsForegroundFullscreenOnSelectedMonitor(_viewModel.Settings.MonitorDeviceName);
        CheckPresentation();
    }
    private void CheckPresentation()
    {
        if (!IsLoaded || _viewModel.IsSuspended) return;
        var now = DateTimeOffset.UtcNow;
        var pointer = _platform.IsPointerOver();
        if (pointer) _lastPointerAt = now;
        var settings = _viewModel.Settings;
        var altPeek = WindowPlatformService.AltHeld;
        var presentation = NotchPresence.Evaluate(settings, _viewModel.IslandActivity, now, pointer, _foregroundFullscreen,
            _pinned, _manualHidden, altPeek);
        // Keep hover details long enough to move out cleanly; fading itself never waits for this grace period.
        if (!presentation.Visible && settings.PresenceMode == "Hover" && _expanded && !_manualHidden &&
            (!_foregroundFullscreen || settings.EffectiveFullscreenMode == "Visible") && now - _lastPointerAt < TimeSpan.FromMilliseconds(350))
            presentation = presentation with { Visible = true };
        _presentationVisible = presentation.Visible;
        if (!presentation.Visible)
        {
            if (_expanded && !_pinned) SetExpanded(false);
        }
        else if (!IsVisible)
        {
            BeginAnimation(OpacityProperty, null); Opacity = _targetOpacity = 0;
            Show(); Reposition();
        }
        _platform.SetClickThrough(!presentation.Visible || presentation.ClickThrough);
        var targetOpacity = presentation.Visible ? presentation.Opacity : 0;
        if (Math.Abs(_targetOpacity - targetOpacity) > .001)
        {
            _targetOpacity = targetOpacity;
            var milliseconds = !SystemParameters.ClientAreaAnimation ? 0 : !presentation.Visible ? 140 :
                settings.FadeSpeed switch { "Instant" => 0, "Soft" => 300, _ => 160 };
            var fade = new DoubleAnimation(Opacity, targetOpacity, TimeSpan.FromMilliseconds(milliseconds))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) => { if (!_presentationVisible && _targetOpacity == 0 && IsVisible) Hide(); };
            BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
        else if (!presentation.Visible && Opacity < .001 && IsVisible) Hide();
        if (pointer && presentation.Visible && !presentation.ClickThrough && settings.ExpandOnHover &&
            (!_pointerWasOver || altPeek) && !_expanded && !_pinned && !_hover.IsEnabled) _hover.Start();
        if (!pointer && _pointerWasOver) { _hover.Stop(); if (!_pinned) _collapse.Start(); }
        _pointerWasOver = pointer;
    }
    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _collapse.Stop();
        var presentation = NotchPresence.Evaluate(_viewModel.Settings, _viewModel.IslandActivity, DateTimeOffset.UtcNow,
            true, _foregroundFullscreen, _pinned, _manualHidden, WindowPlatformService.AltHeld);
        if (_viewModel.Settings.ExpandOnHover && presentation.Visible && !presentation.ClickThrough) _hover.Start();
    }
    private void OnMouseLeave(object sender, MouseEventArgs e)
    { _hover.Stop(); if (!_pinned) _collapse.Start(); }
    private void OnHeaderClick(object sender, MouseButtonEventArgs e)
    {
        _pinned = !_pinned;
        SetExpanded(_pinned);
        if (_pinned) Activate();
        e.Handled = true;
    }
    private void OnCollapse(object sender, RoutedEventArgs e) { _pinned = false; SetExpanded(false); }
    private void OnKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { _pinned = false; SetExpanded(false); e.Handled = true; } }

    private void SetExpanded(bool expanded)
    {
        _hover.Stop(); _collapse.Stop();
        if (_expanded == expanded) return;
        _expanded = expanded;
        _viewModel.SetExpanded(expanded);
        var transition = ++_detailsTransition;
        ExpandedContent.IsHitTestVisible = false;
        if (expanded) { ExpandedContent.Visibility = Visibility.Visible; UsageScroll.ScrollToTop(); }
        ResizeIsland();
        if (!SystemParameters.ClientAreaAnimation)
        {
            ExpandedContent.BeginAnimation(OpacityProperty, null); ExpandedContent.Opacity = expanded ? 1 : 0;
            ExpandedContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ExpandedContent.IsHitTestVisible = expanded;
        }
        else
        {
            var opacity = new DoubleAnimationUsingKeyFrames();
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(ExpandedContent.Opacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (expanded) opacity.KeyFrames.Add(new LinearDoubleKeyFrame(ExpandedContent.Opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(expanded ? 1 : 0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(expanded ? 340 : 140))));
            opacity.Completed += (_, _) =>
            {
                if (transition != _detailsTransition) return;
                ExpandedContent.IsHitTestVisible = _expanded;
                if (!_expanded) ExpandedContent.Visibility = Visibility.Collapsed;
            };
            ExpandedContent.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
        }
        if (!expanded && _recovery) { _recovery = false; _platform.SetClickThrough(_viewModel.Settings.ClickThrough); }
    }

    private double ExpandedHeight() => _viewModel.IsTaskView ?
        Math.Clamp(140 + _viewModel.Tasks.Count * 140, 300, 520) :
        Math.Clamp(92 + _viewModel.Providers.Sum(p => 86 + p.Windows.Count * 70 +
            (p.NoticeText.Length > 0 ? 22 + Math.Max(0, (int)Math.Ceiling(p.NoticeText.Length / 68.0) - 1) * 14 : 0)), 260, 520);

    private void ResizeIsland(bool animate = true)
    {
        var width = _viewModel.IslandWidth;
        var fullHeight = Math.Min(560, ExpandedHeight() + _viewModel.IslandHeaderHeight - 42);
        SurfaceLayout.Height = fullHeight;
        Surface.AttachedToTop = _viewModel.Settings.NotchStyle == "FixedNotch";
        var height = _expanded ? fullHeight : _viewModel.IslandHeaderHeight;
        var duration = SystemParameters.ClientAreaAnimation && animate ? 360 : 0;
        if (Math.Abs(_targetWidth - width) >= 1)
        {
            _targetWidth = width;
            AnimateSurface(NotchSurface.SurfaceWidthProperty, width, duration);
        }
        if (Math.Abs(_targetHeight - height) >= 1)
        {
            _targetHeight = height;
            AnimateSurface(NotchSurface.SurfaceHeightProperty, height, duration);
        }
        UpdateSurfaceBounds();
    }

    private void UpdateSurfaceBounds()
    {
        if (CompactHeader is null || ActivityHeader is null) return;
        CompactHeader.Width = Math.Max(0, Surface.SurfaceWidth - 36);
        ActivityHeader.Width = Math.Max(0, Surface.SurfaceWidth - 48);
        _platform.SetInteractionBounds(Surface.SurfaceBounds);
    }

    private void AnimateSurface(DependencyProperty property, double target, int milliseconds)
    {
        var from = (double)Surface.GetValue(property);
        Surface.BeginAnimation(property, null);
        Surface.SetValue(property, target);
        if (milliseconds == 0) return;
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(target, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)),
            new KeySpline(.16, 1, .3, 1)));
        Surface.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public void Stop()
    { ThemeService.Changed -= OnThemeChanged; _hover.Stop(); _collapse.Stop(); _fullscreen.Stop(); _presence.Stop(); _platform.Dispose(); }
}
