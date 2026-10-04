using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UsageNotch.Core;

namespace UsageNotch.App;

public partial class ColorPickerWindow : Window
{
    private bool _updating = true;
    public RgbColor SelectedColor { get; private set; }
    public event Action<RgbColor>? ColorChanged;

    public ColorPickerWindow(string initialColor)
    {
        InitializeComponent();
        foreach (var hex in new[] { "#090909", "#FFFFFF", "#FFF2B3", "#F2A8C7", "#AFA4F6", "#3879DE", "#247B62", "#CE573D" })
        {
            RgbColor.TryParse(hex, out var color);
            var button = new Button { Background = ThemeService.Brush(color), Margin = new Thickness(2), Padding = new Thickness(0),
                Height = 28, ToolTip = hex, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                Content = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Width = 30, Height = 24, CornerRadius = new CornerRadius(5) } };
            System.Windows.Automation.AutomationProperties.SetName(button, "Choose " + hex);
            button.Click += (_, _) => SetColor(color);
            Presets.Children.Add(button);
        }
        RgbColor.TryParse(initialColor, out var initial);
        SetColor(initial);
        Loaded += (_, _) => UpdatePreview();
    }
    private void SetColor(RgbColor color, bool updateHex = true)
    {
        _updating = true;
        SelectedColor = color;
        var hsv = color.ToHsv();
        // Keep the selected hue for grayscale, so increasing saturation stays intuitive.
        if (hsv.Saturation > 0) Hue.Value = hsv.Hue;
        Saturation.Value = hsv.Saturation * 100; Brightness.Value = hsv.Brightness * 100;
        if (updateHex) HexInput.Text = color.Hex;
        ColorError.Text = ""; UseColor.IsEnabled = true;
        _updating = false;
        UpdatePreview();
        ColorChanged?.Invoke(color);
    }
    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        // Preserve hue and saturation even when brightness reaches zero.
        SelectedColor = RgbColor.FromHsv(Hue.Value, Saturation.Value / 100, Brightness.Value / 100);
        _updating = true; HexInput.Text = SelectedColor.Hex; _updating = false;
        ColorError.Text = ""; UseColor.IsEnabled = true;
        UpdatePreview(); ColorChanged?.Invoke(SelectedColor);
    }
    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (RgbColor.TryParse(HexInput.Text, out var color)) SetColor(color, updateHex: false);
        else { ColorError.Text = "Enter a hex color such as #AFA4F6 or #FFF."; UseColor.IsEnabled = false; }
    }
    private void OnPaletteDown(object sender, MouseButtonEventArgs e)
    {
        ColorField.CaptureMouse(); SelectPoint(e.GetPosition(ColorField)); e.Handled = true;
    }
    private void OnPaletteMove(object sender, MouseEventArgs e)
    {
        if (ColorField.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SelectPoint(e.GetPosition(ColorField));
    }
    private void OnPaletteUp(object sender, MouseButtonEventArgs e)
    {
        if (!ColorField.IsMouseCaptured) return;
        SelectPoint(e.GetPosition(ColorField)); ColorField.ReleaseMouseCapture(); e.Handled = true;
    }
    private void SelectPoint(Point point)
    {
        if (ColorField.ActualWidth <= 0 || ColorField.ActualHeight <= 0) return;
        _updating = true;
        Saturation.Value = Math.Clamp(point.X / ColorField.ActualWidth, 0, 1) * 100;
        Brightness.Value = (1 - Math.Clamp(point.Y / ColorField.ActualHeight, 0, 1)) * 100;
        _updating = false; OnSliderChanged(this, new(0, 0));
    }
    private void OnPaletteSizeChanged(object sender, SizeChangedEventArgs e) { if (!_updating) UpdatePreview(); }
    private void UpdatePreview()
    {
        HueField.Background = ThemeService.Brush(RgbColor.FromHsv(Hue.Value, 1, 1));
        var left = Math.Clamp(Saturation.Value / 100 * ColorField.ActualWidth - 8, 0, Math.Max(0, ColorField.ActualWidth - 16));
        var top = Math.Clamp((1 - Brightness.Value / 100) * ColorField.ActualHeight - 8, 0, Math.Max(0, ColorField.ActualHeight - 16));
        Canvas.SetLeft(SelectionOutline, left); Canvas.SetLeft(SelectionMarker, left);
        Canvas.SetTop(SelectionOutline, top); Canvas.SetTop(SelectionMarker, top);
        var palette = NotchPalette.Create(SelectedColor);
        PreviewSurface.Background = ThemeService.Brush(palette.Surface); PreviewSurface.BorderBrush = ThemeService.Brush(palette.Border);
        PreviewCard.Background = ThemeService.Brush(palette.Card); PreviewTrack.Background = ThemeService.Brush(palette.Track);
        PreviewFill.Background = PreviewTitle.Foreground = PreviewValue.Foreground = ThemeService.Brush(palette.Text);
        PreviewSecondary.Foreground = ThemeService.Brush(palette.Muted);
        PreviewSuccess.Foreground = ThemeService.Brush(palette.Success); PreviewWarning.Foreground = ThemeService.Brush(palette.Warning);
        PreviewCritical.Foreground = ThemeService.Brush(palette.Critical);
        RgbReadout.Text = $"RGB {SelectedColor.R}, {SelectedColor.G}, {SelectedColor.B}";
        ContrastNote.Text = (palette.Text == RgbColor.Black ? "Dark" : "Light") + " text · readability adjusted automatically";
    }
    private void OnUseColor(object sender, RoutedEventArgs e) { if (UseColor.IsEnabled) DialogResult = true; }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
