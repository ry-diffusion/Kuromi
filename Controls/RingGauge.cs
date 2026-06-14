using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kuromi.Controls;

/// <summary>Circular progress gauge (0-100) with a centered label.</summary>
public class RingGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RingGauge, double>(nameof(Value));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<RingGauge, double>(nameof(Thickness), 12);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<RingGauge, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)));

    public static readonly StyledProperty<IBrush> ArcBrushProperty =
        AvaloniaProperty.Register<RingGauge, IBrush>(nameof(ArcBrush),
            new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0xB6)));

    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<RingGauge, string?>(nameof(Caption));

    static RingGauge()
    {
        AffectsRender<RingGauge>(ValueProperty, ThicknessProperty, TrackBrushProperty,
            ArcBrushProperty, CaptionProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public string? Caption { get => GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        var size = System.Math.Min(b.Width, b.Height);
        if (size <= 0) return;

        var t = Thickness;
        var radius = (size - t) / 2;
        var center = new Point(b.Width / 2, b.Height / 2);

        // Track ring
        context.DrawEllipse(null, new Pen(TrackBrush, t), center, radius, radius);

        // Value arc
        var pct = System.Math.Clamp(Value, 0, 100) / 100.0;
        if (pct > 0)
        {
            var start = -90.0;
            var sweep = 360.0 * pct;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var startPt = PointOnCircle(center, radius, start);
                ctx.BeginFigure(startPt, false);
                var endAngle = start + sweep;
                var endPt = PointOnCircle(center, radius, endAngle);
                ctx.ArcTo(endPt, new Size(radius, radius), 0,
                    sweep > 180, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(ArcBrush, t, lineCap: PenLineCap.Round), geo);
        }

        // Centered label: big value + small caption
        var valueText = new FormattedText(
            $"{Value:0}%", System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, size * 0.22,
            Foreground());
        context.DrawText(valueText,
            new Point(center.X - valueText.Width / 2, center.Y - valueText.Height / 2 - (Caption != null ? 8 : 0)));

        if (!string.IsNullOrEmpty(Caption))
        {
            var capText = new FormattedText(
                Caption, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, size * 0.10,
                new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)));
            context.DrawText(capText,
                new Point(center.X - capText.Width / 2, center.Y + valueText.Height / 2 - 2));
        }
    }

    private IBrush Foreground() => Brushes.White;

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var rad = angleDeg * System.Math.PI / 180.0;
        return new Point(center.X + radius * System.Math.Cos(rad),
                         center.Y + radius * System.Math.Sin(rad));
    }
}
