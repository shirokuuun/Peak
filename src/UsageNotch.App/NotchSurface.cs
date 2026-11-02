using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsageNotch.Core;

namespace UsageNotch.App;

// Animate the visible shape inside a stable host. The details retain their final
// layout throughout opening, avoiding HWND resizing and text reflow on each frame.
public sealed class NotchSurface : Decorator
{
    public const double ContentWidth = NotchLayout.HostWidth;
    public const double AttachmentRadius = 14;
    public const double HostWidth = ContentWidth + AttachmentRadius * 2;
    public const double HostHeight = NotchLayout.HostHeight;

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
    private double LayoutWidth => double.IsNaN(Width) ? ActualWidth > 0 ? ActualWidth : HostWidth : Width;
    public Rect BodyBounds => new(Math.Max(0, (LayoutWidth - SurfaceWidth) / 2), 0, Math.Max(0, SurfaceWidth), Math.Max(0, SurfaceHeight));
    private double ShoulderRadius => AttachedToTop ? Math.Min(AttachmentRadius, Math.Min(BodyBounds.Left, SurfaceHeight / 2)) : 0;
    public Rect SurfaceBounds
    {
        get
        {
            var body = BodyBounds;
            var shoulder = ShoulderRadius;
            return new(body.Left - shoulder, 0, body.Width + shoulder * 2, body.Height);
        }
    }
    public event EventHandler? ShapeChanged;

    public NotchSurface() => UpdateShape();
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateShape();
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
    }
    private static void OnShapeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((NotchSurface)target).UpdateShape();
    private void UpdateShape()
    {
        var bounds = BodyBounds;
        var radius = Math.Min(24, bounds.Height / 2);
        Geometry geometry;
        if (!AttachedToTop) geometry = new RectangleGeometry(bounds, radius, radius);
        else
        {
            var path = new StreamGeometry();
            using (var draw = path.Open())
            {
                // Concave shoulders flare out along the screen edge and turn into
                // the straight sides without narrowing the notch's content area.
                var shoulder = ShoulderRadius;
                var tangent = shoulder * .5522847498307936;
                draw.BeginFigure(new(bounds.Left - shoulder, 0), true, true);
                draw.LineTo(new(bounds.Right + shoulder, 0), true, false);
                draw.BezierTo(new(bounds.Right + shoulder - tangent, 0),
                    new(bounds.Right, shoulder - tangent), new(bounds.Right, shoulder), true, false);
                draw.LineTo(new(bounds.Right, bounds.Bottom - radius), true, false);
                draw.QuadraticBezierTo(bounds.BottomRight, new(bounds.Right - radius, bounds.Bottom), true, false);
                draw.LineTo(new(bounds.Left + radius, bounds.Bottom), true, false);
                draw.QuadraticBezierTo(bounds.BottomLeft, new(bounds.Left, bounds.Bottom - radius), true, false);
                draw.LineTo(new(bounds.Left, shoulder), true, false);
                draw.BezierTo(new(bounds.Left, shoulder - tangent),
                    new(bounds.Left - shoulder + tangent, 0), new(bounds.Left - shoulder, 0), true, false);
            }
            geometry = path;
        }
        geometry.Freeze();
        Clip = geometry;
        ShapeChanged?.Invoke(this, EventArgs.Empty);
    }
}
