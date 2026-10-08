using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using UsageNotch.App;
using UsageNotch.Core;
using UsageNotch.Windows;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Application = System.Windows.Application;

// Render the published WPF controls, without App.Run, real windows, provider
// services, hooks, global shortcuts, or the user's existing Peak instance.
internal static class Program
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static string Output = "";
    private static string Fixture = "";
    private static Brush Ink => Hex("#111111");
    private static Brush Muted => Hex("#595959");

    [STAThread]
    public static void Main(string[] args)
    {
        Output = Path.GetFullPath(args.Single());
        if (Directory.Exists(Output)) throw new IOException("Use a new output directory to preserve previous exports.");
        Directory.CreateDirectory(Output);
        Fixture = Path.Combine(Output, ".fixtures");
        Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(Fixture, "codex"));
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(Fixture, "claude"));
        var app = new UsageNotch.App.App();
        app.InitializeComponent(); // Intentionally never run the application's startup.
        var dark = Capture("Dark", "limits");
        var light = Capture("Light", "limits");
        var attention = Capture("Dark", "attention");
        var tasks = Capture("Dark", "tasks");
        var compact = Capture("Dark", "compact");
        var settings = SettingsCapture();
        Save(dark, "native-usage-dark.png"); Save(light, "native-usage-light.png");
        Save(attention, "native-codex-attention.png"); Save(tasks, "native-codex-tasks.png");
        Save(compact, "native-compact.png"); Save(settings, "native-settings-presence.png");

        var one = Poster("01 / USAGE LIMITS");
        Text(one, "Your limits.\nOne clear view.", 96, 304, 710, 86, FontWeights.Bold);
        Text(one, "Codex and Claude usage, together.\nRemaining limits and reset times,\nwithout the window switching.", 100, 536, 710, 30, brush: Muted);
        Tag(one, "5-HOUR + WEEKLY WINDOWS", 100, 752);
        Shot(one, dark, 976, 164, 842);
        Save(one, "01-usage-limits.png");

        var two = Poster("02 / CODEX ATTENTION");
        Text(two, "Know when it’s your turn.", 100, 206, 1720, 84, FontWeights.Bold, align: TextAlignment.Center);
        Text(two, "A reported Codex request brings the work into view.", 150, 324, 1620, 32, brush: Muted, align: TextAlignment.Center);
        var stage = new Border { Width = 1680, Height = 442, CornerRadius = new CornerRadius(28), Background = Hex("#E5E5E2"), BorderBrush = Hex("#D4D4D1"), BorderThickness = new Thickness(1) };
        Place(two, stage, 120, 440);
        Shot(two, attention, 210, 507, 1500);
        Text(two, "Notice in Peak. Review and respond in Codex.", 250, 758, 1420, 30, brush: Muted, align: TextAlignment.Center);
        Save(two, "02-codex-attention.png");

        var three = Poster("03 / CODEX ACTIVITY");
        Text(three, "Stay with\nyour work.", 96, 306, 710, 92, FontWeights.Bold);
        Text(three, "See local Codex task activity,\nproject names, and elapsed time.\nOpen the chat when you need more.", 100, 548, 710, 30, brush: Muted);
        Tag(three, "ACTIVITY, AT A GLANCE", 100, 766);
        Shot(three, tasks, 976, 164, 842);
        Save(three, "03-codex-activity.png");

        var four = Poster("04 / APPEARANCE");
        Text(four, "A look that fits your desktop.", 100, 146, 1720, 76, FontWeights.Bold, align: TextAlignment.Center);
        Text(four, "Light or dark. The same clear view.", 100, 244, 1720, 30, brush: Muted, align: TextAlignment.Center);
        Shot(four, dark, 190, 316, 720); Shot(four, light, 1010, 316, 720);
        Save(four, "04-appearance.png");

        var five = Poster("05 / PRESENCE");
        Text(five, "There when\nyou need it.", 96, 320, 650, 84, FontWeights.Bold);
        Text(five, "Always visible, active, or on hover.\nChoose how Peak fits\nyour workflow.", 100, 550, 640, 29, brush: Muted);
        Shot(five, settings, 800, 154, 1000);
        Save(five, "05-presence-settings.png");
        File.WriteAllText(Path.Combine(Output, "README.txt"), "Peak showcase exports · 1920 x 1080 PNG\n\n01–05 are Store/website/social feature images. native-* are transparent captures of actual published Peak WPF controls at 3x resolution, using fictional sample data. No real provider accounts, private paths, or sessions were read. Frames, headings, and shadows are presentation graphics, outside Peak's actual UI.\n\nRecommended Store order: 01 usage, 02 attention, 03 activity, 04 appearance, 05 presence. Use image 01 as the LinkedIn attachment. Captions are in captions.json. Upload through the existing Store listing submission; these images do not change the application version or package.\n");
        File.WriteAllText(Path.Combine(Output, "captions.json"), System.Text.Json.JsonSerializer.Serialize(new[] {
            new { file="01-usage-limits.png", caption="Codex and Claude subscription limits in Peak. Actual interface with sample data." },
            new { file="02-codex-attention.png", caption="Peak surfaces a reported Codex approval request. Review and respond in Codex. Sample data." },
            new { file="03-codex-activity.png", caption="Local Codex task activity, projects, and elapsed time in Peak. Sample data." },
            new { file="04-appearance.png", caption="Peak's dark and light appearances. Actual interface with sample usage data." },
            new { file="05-presence-settings.png", caption="Choose when Peak stays visible: always, when active, or on hover. Actual settings interface." }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine($"Created five 1920 x 1080 feature images and six native WPF captures in {Output}");
    }

    private static BitmapSource Capture(string theme, string state)
    {
        ApplyTheme(theme);
        var settings = new AppSettings { Theme = theme, ProviderConsentVersion = 1, CodexEnabled = true, ClaudeEnabled = true, CodexTasksEnabled = true, NotificationsEnabled = false, TaskNotificationsEnabled = false };
        var tasks = state == "compact" || state == "limits" ? Array.Empty<CodexTask>() : new[] {
            new CodexTask("00000000-0000-0000-0000-000000000001", "Build the checkout flow", "storefront", state == "attention" ? CodexTaskState.ApprovalRequested : CodexTaskState.Working, state == "attention" ? "Command execution" : "Editing files", Now, true, IsDemo: true, StartedAt: Now.AddMinutes(-2)),
            new CodexTask("00000000-0000-0000-0000-000000000002", "Review the account page", "dashboard", CodexTaskState.Idle, "Response finished", Now.AddMinutes(-10), true, IsDemo: true)
        };
        var vm = new MainViewModel([], new SettingsStore(Path.Combine(Fixture, state + theme)), settings, [Usage("codex", "Plus · sample", 12, 44), Usage("claude", "Pro · sample", 38, 61)], taskMonitor: new SampleTasks(tasks));
        var refresh = typeof(MainViewModel).GetMethod("RefreshTasksAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Task)refresh.Invoke(vm, null)!).GetAwaiter().GetResult();
        var window = new MainWindow(vm);
        int height = state is "attention" ? 78 : state is "compact" ? 42 : 552;
        int width = state is "compact" ? 360 : 600;
        if (state is "limits" or "tasks")
        {
            vm.SetExpanded(true); vm.SelectTasks(state == "tasks");
            var expanded = (FrameworkElement)window.FindName("ExpandedContent");
            expanded.Visibility = Visibility.Visible; expanded.Opacity = 1;
            ((Border)window.FindName("IslandHeader")).CornerRadius = new CornerRadius(24, 24, 0, 0);
        }
        var root = (FrameworkElement)window.Content; window.Content = null;
        root.Resources.MergedDictionaries.Add(window.Resources);
        root.DataContext = vm;
        var bitmap = Render(root, width, height, 3);
        window.Stop(); vm.DisposeAsync().GetAwaiter().GetResult();
        return bitmap;
    }

    private static BitmapSource SettingsCapture()
    {
        ApplyTheme("Dark");
        var window = new SettingsWindow(new AppSettings { Theme="Dark", ProviderConsentVersion=1 }, demo:true);
        ((RadioButton)window.FindName("NavPresence")).IsChecked = true;
        var root = (FrameworkElement)window.Content; window.Content = null;
        root.Resources.MergedDictionaries.Add(window.Resources);
        var canvas = new Border { Background=Hex("#151515"), Child=root };
        System.Windows.Documents.TextElement.SetForeground(canvas, Brushes.White);
        return Render(canvas, 880, 720, 3);
    }
    private static void ApplyTheme(string theme) => typeof(MainWindow).Assembly.GetType("UsageNotch.App.ThemeService")!.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [new AppSettings { Theme=theme }]);
    private static ProviderUsage Usage(string id, string plan, double used, double weekly)
    {
        var source = new SourceObservation("sample", SourceKind.Manual, "showcase", Now, Now, null, Now.AddMinutes(3), true);
        UsageWindow Limit(string key, string label, double value, TimeSpan duration, TimeSpan reset) => new(key, id, label, "sample", "subscription-quota", "sample", duration, null, Metric<DateTimeOffset>.Reported(Now+reset, "sample"), Metric<double>.Reported(value, "sample"), Freshness.Fresh, null, "sample");
        return new(1, id, "sample", "DEMO", Metric<string>.Reported(plan, "sample"), ConnectionState.Ready, [source], [Limit("session", "5-hour", used, TimeSpan.FromHours(5), TimeSpan.FromHours(2)), Limit("weekly", "Weekly", weekly, TimeSpan.FromDays(7), TimeSpan.FromDays(4))], [], Now, null);
    }
    private sealed class SampleTasks(CodexTask[] tasks) : ICodexTaskMonitor
    {
        public event EventHandler? Changed { add {} remove {} }
        public Task<CodexTaskSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(new CodexTaskSnapshot(tasks, "DEMO · sample tasks · no live Codex activity", Now));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static Canvas Poster(string chapter)
    {
        var canvas = new Canvas { Width=1920, Height=1080, Background=Hex("#F0F0ED") };
        Text(canvas, "PEAK", 96, 60, 230, 42, FontWeights.Bold);
        Text(canvas, chapter, 350, 77, 1000, 19, brush:Muted);
        Text(canvas, "FREE ON MICROSOFT STORE", 1280, 77, 544, 19, FontWeights.Bold, align:TextAlignment.Right);
        Place(canvas, new Border { Width=1720, Height=1, Background=Hex("#D4D4D1") }, 100, 132);
        Place(canvas, new Border { Width=1720, Height=1, Background=Hex("#D4D4D1") }, 100, 994);
        Text(canvas, "Peak for Windows · neorangel", 100, 1018, 620, 19, brush:Muted);
        Text(canvas, "Actual Peak interface · sample data", 1040, 1018, 780, 19, brush:Muted, align:TextAlignment.Right);
        return canvas;
    }
    private static void Shot(Canvas canvas, BitmapSource bitmap, double x, double y, double width)
    {
        var image = new Image { Source=bitmap, Width=width, Height=width*bitmap.PixelHeight/bitmap.PixelWidth, Stretch=Stretch.Fill, Effect=new DropShadowEffect { BlurRadius=30, ShadowDepth=15, Opacity=.16, Color=Colors.Black } };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); Place(canvas, image, x, y);
    }
    private static void Tag(Canvas canvas, string value, double x, double y) => Text(canvas, value, x, y, 700, 19, FontWeights.Bold, brush:Muted);
    private static void Text(Canvas canvas, string value, double x, double y, double width, double size, FontWeight? weight=null, Brush? brush=null, TextAlignment align=TextAlignment.Left)
    {
        var block=new TextBlock { Text=value, Width=width, FontFamily=new FontFamily("Bahnschrift"), FontSize=size, FontWeight=weight??FontWeights.Normal, Foreground=brush??Ink, TextWrapping=TextWrapping.Wrap, TextAlignment=align, LineHeight=size*1.18, LineStackingStrategy=LineStackingStrategy.BlockLineHeight };
        Place(canvas,block,x,y);
    }
    private static void Place(Canvas canvas, UIElement element, double x, double y) { Canvas.SetLeft(element,x); Canvas.SetTop(element,y); canvas.Children.Add(element); }
    private static Brush Hex(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static BitmapSource Render(FrameworkElement element, int width, int height, int scale)
    {
        element.Measure(new Size(width,height)); element.Arrange(new Rect(0,0,width,height)); element.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width*scale,height*scale,96*scale,96*scale,PixelFormats.Pbgra32); bitmap.Render(element); bitmap.Freeze(); return bitmap;
    }
    private static void Save(Canvas canvas, string name) => Save(Render(canvas,1920,1080,1),name);
    private static void Save(BitmapSource bitmap, string name)
    {
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(Output,name)); encoder.Save(file);
    }
}
