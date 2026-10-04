using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using UsageNotch.Core;

namespace UsageNotch.App;

internal static class ThemeService
{
    public static event EventHandler? Changed;
    public static void Apply(AppSettings settings)
    {
        var light = settings.Theme == "Light";
        if (settings.Theme == "System")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        RgbColor Parse(string value) { RgbColor.TryParse(value, out var color); return color; }
        var palette = settings.Theme == "Custom" ? NotchPalette.Create(Parse(settings.Normalize().CustomColor)) :
            light ? NotchPalette.Create(Parse("#F6F6F6"), Parse("#ECECEC"), RgbColor.White, Parse("#D9D9D9")) :
            NotchPalette.Create(Parse("#090909"), Parse("#181818"), Parse("#222222"), Parse("#3B3B3B"));
        Set("SurfaceBrush", palette.Surface); Set("PanelBrush", palette.Panel); Set("CardBrush", palette.Card);
        Set("CardBorderBrush", palette.Border); Set("TextBrush", palette.Text); Set("MutedBrush", palette.Muted);
        Set("TrackBrush", palette.Track); Set("LimitFillBrush", palette.Text);
        Set("SuccessBrush", palette.Success); Set("WarningBrush", palette.Warning); Set("CriticalBrush", palette.Critical);
        Set("AccentBrush", palette.Accent); Set("ActivityBrush", palette.Activity);
        Changed?.Invoke(null, EventArgs.Empty);
    }
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static SolidColorBrush Brush(RgbColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze(); return brush;
    }
    private static void Set(string key, RgbColor color) => Application.Current.Resources[key] = Brush(color);
}
