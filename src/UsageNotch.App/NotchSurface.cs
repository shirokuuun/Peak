using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UsageNotch.App;

// Animate the visible shape inside a stable host. The details retain their final
// layout throughout opening, avoiding HWND resizing and text reflow on each frame.
public sealed class NotchSurface : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background),
        typeof(Brush), typeof(NotchSurface), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public static readonly DependencyProperty SurfaceWidthProperty = DependencyProperty.Register(nameof(SurfaceWidth),
        typeof(double), typeof(NotchSurface), new PropertyMetadata(360d, OnShapeChanged));
    public static readonly DependencyProperty SurfaceHeightProperty = DependencyProperty.Register(nameof(SurfaceHeight),
        typeof(double), typeof(NotchSurface), new PropertyMetadata(42d, OnShapeChanged));
    public static readonly DependencyProperty AttachedToTopProperty = DependencyProperty.Register(nameof(AttachedToTop),
        typeof(bool), typeof(NotchSurface), new PropertyMetadata(false, OnShapeChanged));
    public double SurfaceWidth { get => (double)GetValue(SurfaceWidthProperty); set => SetValue(SurfaceWidthProperty, value); }
    public double SurfaceHeight { get => (double)GetValue(SurfaceHeightProperty); set => SetValue(SurfaceHeightProperty, value); }
    public bool AttachedToTop { get => (bool)GetValue(AttachedToTopProperty); set => SetValue(AttachedToTopProperty, value); }
    public Rect SurfaceBounds => new(Math.Max(0, (600 - SurfaceWidth) / 2), 0, Math.Max(0, SurfaceWidth), Math.Max(0, SurfaceHeight));
    public event EventHandler? ShapeChanged;

    public NotchSurface() => UpdateShape();
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(0, 0, 600, 560));
    }
    private static void OnShapeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((NotchSurface)target).UpdateShape();
    private void UpdateShape()
    {
        var bounds = SurfaceBounds;
        var radius = Math.Min(24, bounds.Height / 2);
        Geometry geometry;
        if (!AttachedToTop) geometry = new RectangleGeometry(bounds, radius, radius);
        else
        {
            var path = new StreamGeometry();
            using (var draw = path.Open())
            {
                draw.BeginFigure(bounds.TopLeft, true, true);
                draw.LineTo(bounds.TopRight, true, false);
                draw.LineTo(new(bounds.Right, bounds.Bottom - radius), true, false);
                draw.QuadraticBezierTo(bounds.BottomRight, new(bounds.Right - radius, bounds.Bottom), true, false);
                draw.LineTo(new(bounds.Left + radius, bounds.Bottom), true, false);
                draw.QuadraticBezierTo(bounds.BottomLeft, new(bounds.Left, bounds.Bottom - radius), true, false);
            }
            geometry = path;
        }
        geometry.Freeze();
        Clip = geometry;
        ShapeChanged?.Invoke(this, EventArgs.Empty);
    }
}
