using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using UsageNotch.Providers;

namespace UsageNotch.App;

public partial class ProviderSetupWindow : Window
{
    private readonly string _provider;
    private readonly ProviderConnectionSetup _setup;
    private readonly CancellationTokenSource _closed = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private ProviderSetupStatus? _status;
    private bool _busy;
    public bool Connected => _status?.Step == ProviderSetupStep.Ready;
    public ProviderSetupStatus? Status => _status;

    public ProviderSetupWindow(string provider)
    {
        _provider = provider; _setup = new(provider);
        InitializeComponent();
        Heading.Text = provider == "codex" ? "Connect Codex" : "Connect Claude Code";
        Loaded += async (_, _) => await CheckAsync();
        _poll.Tick += async (_, _) => { if (!_busy) await CheckAsync(); };
        Closed += (_, _) => { _poll.Stop(); _closed.Cancel(); };
    }

    private async Task CheckAsync()
    {
        if (_busy || _closed.IsCancellationRequested) return;
        SetBusy(true);
        try
        {
            _status = await _setup.ProbeAsync(_closed.Token);
            if (_closed.IsCancellationRequested) return;
            StatusText.Text = _status.Message;
            StepTitle.Text = _status.Step switch
            {
                ProviderSetupStep.MissingCli => "Install the provider",
                ProviderSetupStep.SignIn => "Sign in to your account",
                ProviderSetupStep.AwaitingUsage => "Waiting for Claude Code",
                ProviderSetupStep.Ready => "Connected",
                ProviderSetupStep.Unsupported => "ChatGPT login required",
                _ => "Connection needs attention"
            };
            Primary.Content = _status.Step switch
            {
                ProviderSetupStep.MissingCli => "Install & connect",
                ProviderSetupStep.Ready => "Done",
                ProviderSetupStep.Unavailable => "Retry",
                _ => _provider == "codex" ? "Sign in & connect" : "Open Claude Code"
            };
            if (Connected) { _poll.Stop(); DialogResult = true; }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Primary.IsEnabled = Retry.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await CheckAsync();

    private async void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (Connected) { DialogResult = true; return; }
        if (_status?.Step == ProviderSetupStep.Unavailable) { await CheckAsync(); return; }
        SetBusy(true);
        try
        {
            if (_status?.Step == ProviderSetupStep.MissingCli)
            {
                StepTitle.Text = "Installing the provider";
                StatusText.Text = "Follow the official installer in the terminal. Peak will check the installation when it finishes.";
                using var installer = Process.Start(_setup.CreateInstallStartInfo()) ?? throw new InvalidOperationException();
                await installer.WaitForExitAsync(_closed.Token);
                _status = await _setup.ProbeAsync(_closed.Token);
                if (_status.Step == ProviderSetupStep.MissingCli) throw new InvalidOperationException();
                if (_status.Step == ProviderSetupStep.Ready) { SetBusy(false); await CheckAsync(); return; }
            }
            if (_closed.IsCancellationRequested) return;
            StepTitle.Text = "Finish in the provider window";
            using var login = Process.Start(_setup.CreateSignInStartInfo()) ?? throw new InvalidOperationException();
            if (_provider == "codex")
            {
                StatusText.Text = "Complete ChatGPT sign-in in your browser. Peak will check your login afterward.";
                await login.WaitForExitAsync(_closed.Token);
            }
            else
            {
                StatusText.Text = "Sign in to Claude Code with Pro or Max, send a prompt and wait for its response. Peak is watching for usage.";
                _poll.Start();
            }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch
        {
            if (!_closed.IsCancellationRequested)
            {
                _status = new(ProviderSetupStep.Unavailable, "Setup did not finish. Check the provider terminal, then choose Check again.");
                StepTitle.Text = "Setup needs attention"; StatusText.Text = _status.Message; Primary.Content = "Retry";
            }
        }
        finally { SetBusy(false); }
        if (!_closed.IsCancellationRequested && _status?.Step != ProviderSetupStep.Unavailable) await CheckAsync();
    }
}
