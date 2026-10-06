using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UsageNotch.Core;
using UsageNotch.Windows;

namespace UsageNotch.App;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    internal static Brush Muted => ThemeService.Brush("MutedBrush");
    private static Brush Green => ThemeService.Brush("SuccessBrush");
    private static Brush Amber => ThemeService.Brush("WarningBrush");
    private static Brush Red => ThemeService.Brush("CriticalBrush");
    private readonly IReadOnlyList<IUsageProvider> _services;
    private readonly ICodexTaskMonitor? _taskMonitor;
    private readonly SemaphoreSlim _taskLock = new(1, 1);
    private readonly DispatcherTimer _taskDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Task? _taskPoll;
    private CodexTaskSnapshot _taskSnapshot = new([], "Waiting for task activity", DateTimeOffset.MinValue);
    private bool _tasksSelected;
    private bool _expanded;
    private TaskActivitySummary _island = TaskActivitySummary.Empty;
    private string _taskStatus = "Waiting for task activity";
    private readonly HashSet<string> _notifiedTaskRequests = [];
    private readonly SettingsStore _store;
    private readonly ThresholdEvaluator _thresholds;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _fileDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Task? _pollTask;
    private IReadOnlyList<ProviderUsage> _usage = [];
    private bool _firstRead = true;
    private int _failures;
    private bool _isRefreshing;
    private double _criticalRemaining;
    private string _criticalText = "Waiting…";
    private string _criticalInitial = "·";
    private string _updatedText = "Waiting for data";
    private string _footer = "Local only";
    private Brush _statusBrush = Muted;

    public ObservableCollection<ProviderCardViewModel> Providers { get; } = [];
    public ObservableCollection<CodexTaskViewModel> Tasks { get; } = [];
    public IEnumerable<CodexTaskViewModel> FooterTasks => Tasks.Take(2);
    public Visibility UsageVisibility => _tasksSelected ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TasksVisibility => _tasksSelected ? Visibility.Visible : Visibility.Collapsed;
    public string TasksTabText => AttentionCount > 0 ? $"Tasks · {AttentionCount} review" : "Tasks";
    public string TaskSwitchText => _tasksSelected ? "Limits" : AttentionCount > 0 ? $"Review {AttentionCount}" : "Tasks";
    public bool IsTaskView => _tasksSelected;
    public int AttentionCount => _taskSnapshot.Tasks.Count(t => t.NeedsAttention);
    public string TaskStatus { get => _taskStatus; private set => Set(ref _taskStatus, value); }
    public string EmptyTaskText => Tasks.Count == 0 ? "No recent local chats found. Start a Codex chat, then refresh." : "";
    public bool HasIslandActivity => _island.IsVisible;
    public TaskActivitySummary IslandActivity => _island;
    public string RecoveryHint => "Click to pin details. " + NotchPresence.ShortcutLabel(Settings.RecoveryShortcut) + " reveals Peak.";
    public double IslandHeaderHeight => HasIslandActivity ? 78 : 42;
    public double IslandWidth => HasIslandActivity ? 600 : 360;
    public Visibility IslandActivityVisibility => HasIslandActivity ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IslandProgressVisibility => _island.Kind == IslandActivityKind.Working ? Visibility.Visible : Visibility.Hidden;
    public bool IsIslandAnimating => _island.Kind == IslandActivityKind.Working && !IsSuspended && SystemParameters.ClientAreaAnimation;
    public string IslandOtherTasks => _island.OtherActiveCount > 0 ? $"+{_island.OtherActiveCount}" : "";
    public Brush IslandActivityBrush => _island.Kind switch
    {
        IslandActivityKind.Attention => Amber,
        IslandActivityKind.Working or IslandActivityKind.Finished => Green,
        _ => Muted
    };
    public string IslandActivityText => _island.Task is not { } task ? "" : (_island.Kind switch
    {
        IslandActivityKind.Attention => task.State == CodexTaskState.InputRequested ? "Question for you · open Codex" : "Approval requested · review in Codex",
        IslandActivityKind.Working => task.Activity,
        IslandActivityKind.Finished => "Finished",
        IslandActivityKind.Interrupted => "Interrupted",
        _ => ""
    }) + (DisplayProject(task.Project) == "Local project" ? "" : " · " + DisplayProject(task.Project));
    public string IslandElapsedText
    {
        get
        {
            if (_island.Task is not { } task) return "";
            if (task.StartedAt is not { } start) return "Seen " + FormatAge(DateTimeOffset.UtcNow - task.ObservedAt);
            var end = _island.Kind is IslandActivityKind.Finished or IslandActivityKind.Interrupted ? task.ObservedAt : DateTimeOffset.UtcNow;
            return "Elapsed " + FormatDuration(end - start);
        }
    }
    public Brush CompactActivityBrush => AttentionCount > 0 ? Amber : _taskSnapshot.Tasks.Any(t => t.State == CodexTaskState.Working) ? Green : Muted;
    public string CompactTooltip => _taskSnapshot.Tasks.FirstOrDefault(t => t.NeedsAttention || t.State == CodexTaskState.Working ||
        t.State == CodexTaskState.RecentActivity && DateTimeOffset.UtcNow - t.ObservedAt < TimeSpan.FromMinutes(5)) is { } task
        ? task.Title + " · " + task.Activity + (task.NeedsAttention ? " · open Tasks to review in Codex" : "") : "Peak · local subscription usage";
    public AppSettings Settings { get; private set; }
    public bool IsSuspended { get; set; }
    public bool IsRefreshing { get => _isRefreshing; private set => Set(ref _isRefreshing, value); }
    public double CriticalPercent { get => _criticalRemaining; private set => Set(ref _criticalRemaining, value); }
    public string CriticalRemainingText { get => _criticalText; private set => Set(ref _criticalText, value); }
    public string CriticalProviderInitial { get => _criticalInitial; private set => Set(ref _criticalInitial, value); }
    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }
    public string FooterText { get => _footer; private set => Set(ref _footer, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public double CodexRemaining => RemainingFor("codex") ?? 0;
    public double ClaudeRemaining => RemainingFor("claude") ?? 0;
    public string CodexRemainingText => RemainingFor("codex") is { } value ? $"{value:0}%" : "—";
    public string ClaudeRemainingText => RemainingFor("claude") is { } value ? $"{value:0}%" : "—";
    public Brush CodexBrush => RemainingFor("codex") is { } value ? BrushFor(100 - value, Settings) : Muted;
    public Brush ClaudeBrush => RemainingFor("claude") is { } value ? BrushFor(100 - value, Settings) : Muted;
    public Geometry CodexRing => Ring(CodexRemaining);
    public Geometry ClaudeRing => Ring(ClaudeRemaining);
    public string CompactTitle => (_taskSnapshot.Tasks.Any(t => t.IsDemo) ? "Demo · " : "") +
        (_island.Task?.Title ?? "Peak");
    public string CodexTooltip => "Codex · " + (Settings.CodexEnabled ? "5-hour · " + CodexRemainingText + " remaining" : "disabled");
    public string ClaudeTooltip => "Claude · " + (Settings.ClaudeEnabled ? "5-hour · " + ClaudeRemainingText + " remaining" : "disabled");
    public ICommand RefreshCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand ShowUsageCommand { get; }
    public ICommand ShowTasksCommand { get; }
    public ICommand ToggleTasksCommand { get; }
    public event EventHandler? SettingsRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler<UsageAlert>? AlertRaised;
    public event EventHandler<ResetAlert>? ResetRaised;
    public event EventHandler<string>? TaskAttentionRaised;
    public event EventHandler? ContentChanged;

    public MainViewModel(IReadOnlyList<IUsageProvider> services, SettingsStore store,
        AppSettings settings, IReadOnlyList<ProviderUsage> cached, IReadOnlyDictionary<string, AlertCheckpoint>? alerts = null,
        ICodexTaskMonitor? taskMonitor = null)
    {
        _services = services;
        _taskMonitor = taskMonitor;
        _store = store;
        _thresholds = new(alerts);
        Settings = settings.Normalize();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        ShowUsageCommand = new RelayCommand(() => SelectTasks(false));
        ShowTasksCommand = new RelayCommand(() => SelectTasks(true));
        ToggleTasksCommand = new RelayCommand(() => SelectTasks(!_tasksSelected));
        _clock.Tick += (_, _) => RefreshPresentation();
        _fileDebounce.Tick += async (_, _) => { _fileDebounce.Stop(); await RefreshAsync(); };
        _taskDebounce.Tick += async (_, _) => { _taskDebounce.Stop(); await RefreshTasksAsync(); };
        if (_taskMonitor is not null) _taskMonitor.Changed += OnTasksChanged;
        foreach (var source in services.OfType<IUsageChangeSource>()) source.Changed += OnSourceChanged;
        UpdateUsage(cached.Where(p => Enabled(p.ProviderId)).ToArray(), true);
    }

    public async Task StartAsync()
    {
        _clock.Start();
        await Task.WhenAll(RefreshAsync(), RefreshTasksAsync());
        _pollTask = RunRefreshLoopAsync();
        _taskPoll = RunTaskLoopAsync();
    }

    public void SetExpanded(bool expanded)
    { _expanded = expanded; UpdateClockInterval(); }
    private void UpdateClockInterval() => _clock.Interval = TimeSpan.FromSeconds(_expanded || HasIslandActivity ? 1 : 15);
    public void SelectTasks(bool selected)
    {
        _tasksSelected = selected;
        Raise(nameof(UsageVisibility)); Raise(nameof(TasksVisibility));
        Raise(nameof(TaskSwitchText));
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SelectAttentionIfNeeded() { if (AttentionCount > 0) SelectTasks(true); }
    private void OnTasksChanged(object? sender, EventArgs e)
    {
        if (_shutdown.IsCancellationRequested) return;
        Application.Current.Dispatcher.BeginInvoke(() => { _taskDebounce.Stop(); _taskDebounce.Start(); });
    }
    private async Task RunTaskLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), _shutdown.Token); await RefreshTasksAsync(); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
        }
    }
    private async Task RefreshTasksAsync()
    {
        if (_shutdown.IsCancellationRequested || IsSuspended || !await _taskLock.WaitAsync(0)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            if (!Settings.CodexTasksEnabled || _taskMonitor is null)
                _taskSnapshot = new([], "Task monitoring is disabled in Settings", DateTimeOffset.UtcNow);
            else _taskSnapshot = await _taskMonitor.ReadAsync(timeout.Token);
            TaskStatus = _taskSnapshot.Status;
            Tasks.Clear();
            foreach (var task in _taskSnapshot.Tasks)
            {
                Tasks.Add(new(task));
                if (task.NeedsAttention)
                {
                    var newRequest = false;
                    foreach (var key in (task.AttentionKey ?? "preview").Split(','))
                        newRequest |= _notifiedTaskRequests.Add(task.ThreadId + "|" + key);
                    if (newRequest && Settings.TaskNotificationsEnabled)
                        TaskAttentionRaised?.Invoke(this, "Codex requested review · " + task.Title);
                }
            }
            Raise(nameof(EmptyTaskText)); Raise(nameof(TasksTabText)); Raise(nameof(AttentionCount));
            Raise(nameof(FooterTasks)); Raise(nameof(TaskSwitchText));
            Raise(nameof(CompactActivityBrush)); Raise(nameof(CompactTooltip)); Raise(nameof(CompactTitle));
            RefreshIslandPresentation();
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch
        {
            _taskSnapshot = new([], "Task activity unavailable. Refresh or reconnect in Settings.", DateTimeOffset.UtcNow);
            Tasks.Clear(); TaskStatus = _taskSnapshot.Status;
            Raise(nameof(EmptyTaskText)); Raise(nameof(TasksTabText)); Raise(nameof(CompactTitle));
            Raise(nameof(FooterTasks)); Raise(nameof(TaskSwitchText));
            Raise(nameof(CompactActivityBrush)); Raise(nameof(CompactTooltip));
            RefreshIslandPresentation();
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _taskLock.Release(); }
    }
    private bool Enabled(string id) => id switch { "codex" => Settings.CodexEnabled, "claude" => Settings.ClaudeEnabled, _ => true };
    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (sender is IUsageProvider provider && !Enabled(provider.Id)) return;
        if (_shutdown.IsCancellationRequested) return;
        Application.Current.Dispatcher.BeginInvoke(() => { _fileDebounce.Stop(); _fileDebounce.Start(); });
    }

    public async Task RefreshAsync()
    {
        if (_shutdown.IsCancellationRequested || IsSuspended || !await _refreshLock.WaitAsync(0)) return;
        try
        {
            IsRefreshing = true;
            FooterText = "Refreshing…";
            var results = await Task.WhenAll(_services.Where(p => Enabled(p.Id)).Select(ReadSafeAsync));
            // Retain a confirmed reading on a transport failure, but mark it unavailable for summary/alerts.
            results = results.Select(current =>
            {
                var prior = _usage.FirstOrDefault(p => p.ProviderId == current.ProviderId && p.Sources.Any(s => s.AccountVerified));
                if (prior is not null && (current.Connection is ConnectionState.Error or ConnectionState.Offline ||
                    current.Sources.Any(s => s.Kind == SourceKind.LocalLog)))
                    return prior with { Connection = ConnectionState.Offline, Error = current.Error ?? new("offline", "Unable to refresh. Showing the last reading.", true) };
                return current;
            }).ToArray();
            _failures = results.Any(p => p.Connection is ConnectionState.Error or ConnectionState.Offline) ? Math.Min(4, _failures + 1) : 0;
            UpdateUsage(results, _firstRead);
            _firstRead = false;
            await _store.SaveCacheAsync(results, _shutdown.Token);
            await _store.SaveAlertsAsync(_thresholds.State, _shutdown.Token);
            _ = RefreshTasksAsync();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        { FooterText = "Could not save local data"; }
        finally { IsRefreshing = false; RefreshPresentation(); _refreshLock.Release(); }
    }

    private async Task<ProviderUsage> ReadSafeAsync(IUsageProvider provider)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await provider.RefreshAsync(timeout.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { throw; }
        catch
        {
            return ProviderUsage.Waiting(provider.Id, provider.DisplayName) with
            {
                Connection = ConnectionState.Error,
                Error = new("provider_unavailable", "Usage unavailable. Check the provider CLI, then refresh.", true)
            };
        }
    }

    private async Task RunRefreshLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                var seconds = Math.Min(600, Settings.RefreshIntervalSeconds * Math.Pow(2, _failures));
                await Task.Delay(TimeSpan.FromSeconds(seconds), _shutdown.Token);
                await RefreshAsync();
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
        }
    }

    private void UpdateUsage(IReadOnlyList<ProviderUsage> usage, bool seed)
    {
        _usage = usage;
        Providers.Clear();
        foreach (var provider in usage) Providers.Add(new(provider, Settings));
        RefreshPresentation();
        var alerts = _thresholds.Evaluate(usage, Settings.WarningThreshold, Settings.CriticalThreshold, seed);
        if (Settings.NotificationsEnabled) foreach (var alert in alerts) AlertRaised?.Invoke(this, alert);
        if (Settings.ResetNotificationsEnabled) foreach (var alert in _thresholds.Resets) ResetRaised?.Invoke(this, alert);
    }

    public void RefreshThemeIndicators() => Raise(nameof(CompactActivityBrush));
    public void RefreshPresentation()
    {
        RefreshIslandPresentation();
        foreach (var card in Providers) card.RefreshClock();
        foreach (var name in new[] { nameof(CodexRemainingText), nameof(ClaudeRemainingText), nameof(CodexBrush),
                     nameof(ClaudeBrush), nameof(CodexRing), nameof(ClaudeRing), nameof(CompactTitle), nameof(CodexTooltip), nameof(ClaudeTooltip) }) Raise(name);
        foreach (var task in Tasks) task.RefreshClock();
        var critical = UsageSelection.FindCritical(_usage);
        if (critical is null)
        {
            CriticalPercent = 0;
            CriticalRemainingText = _usage.Count == 0 ? "No providers" : "Unavailable";
            CriticalProviderInitial = "·";
            StatusBrush = Muted;
        }
        else
        {
            var used = critical.Value.Window.UsedPercent!.Value;
            CriticalPercent = Math.Clamp(100 - used, 0, 100);
            CriticalRemainingText = $"{Math.Max(0, 100 - used):0}% left";
            CriticalProviderInitial = critical.Value.Provider.ProviderId == "claude" ? "Cl" : "Co";
            StatusBrush = BrushFor(used, Settings);
        }
        var observed = _usage.Select(p => p.LastSuccessfulReadAt).Where(t => t.HasValue).ToArray();
        UpdatedText = observed.Length == 0 ? "Waiting for data" : $"Read {FormatAge(DateTimeOffset.UtcNow - observed.Max()!.Value)}";
        if (!IsRefreshing) FooterText = _usage.Any(p => p.Error is not null) ? "Check provider status" : "Local only";
    }

    private void RefreshIslandPresentation()
    {
        var wasVisible = HasIslandActivity;
        _island = TaskActivitySummary.Select(_taskSnapshot.Tasks, DateTimeOffset.UtcNow, _island.Task?.ThreadId);
        foreach (var name in new[] { nameof(HasIslandActivity), nameof(IslandHeaderHeight), nameof(IslandWidth),
                     nameof(IslandActivityVisibility), nameof(IslandProgressVisibility), nameof(IsIslandAnimating),
                     nameof(IslandActivityBrush), nameof(IslandActivityText), nameof(IslandElapsedText),
                     nameof(IslandOtherTasks), nameof(CompactTitle) }) Raise(name);
        UpdateClockInterval();
        if (wasVisible != HasIslandActivity) ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}" :
            $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    internal static string DisplayProject(string? project) => string.IsNullOrWhiteSpace(project) ||
        project.StartsWith("g-p-", StringComparison.OrdinalIgnoreCase) || Guid.TryParse(project, out _) ? "Local project" : project;

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        settings = settings.Normalize();
        if (settings.LaunchOnStartup != Settings.LaunchOnStartup) new StartupService().SetEnabled(settings.LaunchOnStartup);
        await _store.SaveAsync(settings);
        Settings = settings;
        Raise(nameof(RecoveryHint));
        _firstRead = true; // Changing notification thresholds must not generate a burst of alerts.
        UpdateUsage(_usage.Where(p => Enabled(p.ProviderId)).ToArray(), true);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        await RefreshAsync();
        await RefreshTasksAsync();
    }

    internal static Brush BrushFor(double used, AppSettings settings) => used >= settings.CriticalThreshold ? Red :
        used >= settings.WarningThreshold ? Amber : Green;
    private double? RemainingFor(string provider)
    {
        var session = UsageSelection.FindFiveHour(_usage.Where(p => p.ProviderId == provider && Enabled(p.ProviderId)));
        return session is null ? null : Math.Clamp(100 - session.Value.Window.UsedPercent!.Value, 0, 100);
    }
    private static Geometry Ring(double percent)
    {
        if (percent <= 0) return Geometry.Empty;
        if (percent >= 100) return new EllipseGeometry(new Point(11, 11), 9, 9);
        var angle = percent * Math.PI / 50 - Math.PI / 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(11, 2), false, false);
            context.ArcTo(new Point(11 + 9 * Math.Cos(angle), 11 + 9 * Math.Sin(angle)),
                new Size(9, 9), 0, percent > 50, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze(); return geometry;
    }
    internal static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 10) return "just now";
        if (age.TotalMinutes < 1) return $"{Math.Max(0, (int)age.TotalSeconds)}s ago";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }

    public async ValueTask DisposeAsync()
    {
        _clock.Stop(); _fileDebounce.Stop(); _taskDebounce.Stop();
        if (_taskMonitor is not null) _taskMonitor.Changed -= OnTasksChanged;
        foreach (var source in _services.OfType<IUsageChangeSource>()) source.Changed -= OnSourceChanged;
        _shutdown.Cancel();
        if (_pollTask is not null) try { await _pollTask; } catch (OperationCanceledException) { }
        if (_taskPoll is not null) try { await _taskPoll; } catch (OperationCanceledException) { }
        await _taskLock.WaitAsync();
        if (_taskMonitor is not null) await _taskMonitor.DisposeAsync();
        _taskLock.Release();
        await _refreshLock.WaitAsync();
        foreach (var provider in _services) await provider.DisposeAsync();
        _refreshLock.Release();
        _shutdown.Dispose();
    }
}

public sealed class ProviderCardViewModel : ObservableObject
{
    private readonly ProviderUsage _usage;
    public string Name => _usage.ProviderId == "claude" ? "Claude" : "Codex";
    public string HeadingText => _usage.ProviderId == "codex" ? "Codex limits" : "Claude limits";
    public string PlanText => _usage.PlanName?.Value is { } value ? System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value) : "Plan unknown";
    public string NoticeText => _usage.Error?.Message ?? (_usage.Sources.Any(s => s.ToolVersion == "demo") ? "DEMO · sample data" : "");
    public string StatusText => _usage.Error?.Message ?? (_usage.ProviderId == "claude" ? "Personal CLI · " : "CLI · ") +
        (_usage.LastSuccessfulReadAt is { } at ? "observed " + MainViewModel.FormatAge(DateTimeOffset.UtcNow - at) : "waiting for data");
    public Brush StatusBrush => MainViewModel.Muted;
    public string ActivityText => string.Join(" · ", _usage.Activity.Select(a => $"{a.Label}: {a.Value.Value:N0} {a.Unit}"));
    public ObservableCollection<WindowViewModel> Windows { get; } = [];
    public ProviderCardViewModel(ProviderUsage usage, AppSettings settings)
    {
        _usage = usage;
        foreach (var window in usage.Windows) Windows.Add(new(usage, window, settings));
    }
    public void RefreshClock() { Raise(nameof(StatusText)); foreach (var window in Windows) window.RefreshClock(); }
}

public sealed class WindowViewModel : ObservableObject
{
    private readonly ProviderUsage _provider;
    private readonly UsageWindow _window;
    private readonly AppSettings _settings;
    private string _resetText = "";
    public string Label => _window.Label;
    public string LimitLabel => _window.Label switch { "5-hour" => "5-hour limit", "Weekly" => "Weekly limit", _ => _window.Label };
    public double Percent => Math.Clamp(100 - (_window.UsedPercent?.Value ?? 100), 0, 100);
    public string RemainingText => _window.UsedPercent is null ? "Unavailable" : $"{Math.Max(0, 100 - _window.UsedPercent.Value):0}% remaining";
    public string LeftText => _window.UsedPercent is null ? "Unavailable" : $"{Math.Max(0, 100 - _window.UsedPercent.Value):0}% left";
    public string ResetSummary => _window.ResetsAt is not null && ResetText.LastIndexOf(" · ", StringComparison.Ordinal) is var index && index >= 0
        ? ResetText[..index] : ResetText;
    public string QuantityText => _window.QuantityUsed is { } used && _window.QuantityLimit is { } limit ?
        $"{used.Value:N0} / {limit.Value:N0} {_window.QuantityUnit}" : "";
    public Brush Brush => UsageSelection.IsAlertEligible(_provider, _window, DateTimeOffset.UtcNow)
        ? MainViewModel.BrushFor(_window.UsedPercent!.Value, _settings) : MainViewModel.Muted;
    public string ResetText { get => _resetText; private set => Set(ref _resetText, value); }
    public WindowViewModel(ProviderUsage provider, UsageWindow window, AppSettings settings)
    { _provider = provider; _window = window; _settings = settings; RefreshClock(); }
    public void RefreshClock()
    {
        Raise(nameof(Brush));
        var freshness = UsageSelection.WindowFreshness(_provider, _window, DateTimeOffset.UtcNow);
        var status = freshness == Freshness.Stale ? "Stale · " : _provider.Connection != ConnectionState.Ready ? "Last reading · " : "";
        if (freshness == Freshness.AwaitingUpdate) { ResetText = "Awaiting provider update"; Raise(nameof(ResetSummary)); return; }
        if (_window.ResetsAt is not { } reset) { ResetText = status + "Reset time unavailable"; Raise(nameof(ResetSummary)); return; }
        var remaining = reset.Value - DateTimeOffset.UtcNow;
        var countdown = remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}d {remaining.Hours}h" :
            remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m" :
            $"{remaining.Minutes}m {remaining.Seconds}s";
        ResetText = $"{status}Resets in {countdown} · {reset.Value.ToLocalTime():MMM d, h:mm tt}";
        Raise(nameof(ResetSummary));
    }
}
