using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UsageNotch.Core;

namespace UsageNotch.App;

public sealed class CodexTaskViewModel : ObservableObject
{
    private readonly CodexTask _task;
    private string? _openError;
    public string Title => _task.Title;
    public string Project => MainViewModel.DisplayProject(_task.Project);
    public string StateText => _task.State switch
    {
        CodexTaskState.ApprovalRequested => "Approval requested · review in Codex",
        CodexTaskState.InputRequested => "Question for you · open Codex",
        CodexTaskState.Working => "Working · hook observed",
        CodexTaskState.RecentActivity => "Recent activity · local log",
        CodexTaskState.Idle => "Finished / idle · last observed",
        CodexTaskState.Interrupted => "Interrupted",
        CodexTaskState.Stale => "Last activity · current state unknown",
        _ => "Current state unavailable"
    };
    public string Activity => _task.Activity;
    public string FooterLabel => Project == "Local project" ? Title : Project;
    public string FooterState => _task.State switch
    {
        CodexTaskState.ApprovalRequested => "review",
        CodexTaskState.InputRequested => "question",
        CodexTaskState.Working when _task.Activity == "Command execution" => "shell",
        CodexTaskState.Working when _task.Activity == "Editing files" => "editing",
        CodexTaskState.Working => "working",
        CodexTaskState.Idle => "Idle",
        CodexTaskState.RecentActivity => "recent",
        CodexTaskState.Interrupted => "paused",
        _ => "unknown"
    };
    public string AgeText => "Observed " + MainViewModel.FormatAge(DateTimeOffset.UtcNow - _task.ObservedAt);
    public string ElapsedText => _task.StartedAt is { } start ? "Elapsed " + MainViewModel.FormatDuration(
        (_task.State is CodexTaskState.Idle or CodexTaskState.Interrupted or CodexTaskState.Stale ? _task.ObservedAt : DateTimeOffset.UtcNow) - start) : "";
    public Brush FooterBrush => _task.State == CodexTaskState.Working && _task.Activity == "Command execution" ? ThemeService.Brush("ActivityBrush") : Brush;
    public string OpenError => _openError ?? "";
    public Brush Brush => _task.NeedsAttention ? MainViewModel.BrushFor(75, new()) : _task.State == CodexTaskState.Working
        ? MainViewModel.BrushFor(0, new()) : MainViewModel.Muted;
    public ICommand OpenChatCommand { get; }
    public CodexTaskViewModel(CodexTask task)
    {
        _task = task;
        OpenChatCommand = new RelayCommand(OpenChat, () => !task.IsDemo && CodexTaskEvents.ValidThreadId(task.ThreadId));
    }
    private void OpenChat()
    {
        try { Process.Start(new ProcessStartInfo("codex://threads/" + _task.ThreadId) { UseShellExecute = true }); }
        catch { _openError = "Could not open Codex. Open this chat in the desktop app."; Raise(nameof(OpenError)); }
    }
    public void RefreshClock() { Raise(nameof(AgeText)); Raise(nameof(ElapsedText)); Raise(nameof(Brush)); Raise(nameof(FooterBrush)); }
}
