using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kuromi.Controls;

/// <summary>A smooth area+line chart for a series of values (history graphs).</summary>
public class Sparkline : Control
{
    public static readonly StyledProperty<IEnumerable<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IEnumerable<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush>(nameof(Stroke),
            new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0xB6)));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 2.5);

    public static readonly StyledProperty<double> MaxProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Max), double.NaN);

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, StrokeThicknessProperty, MaxProperty);
    }

    public IEnumerable<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }

    public override void Render(DrawingContext context)
    {
        var data = Values?.ToArray();
        if (data == null || data.Length < 2) return;

        var b = Bounds;
        double w = b.Width, h = b.Height;
        if (w <= 0 || h <= 0) return;

        double max = double.IsNaN(Max) ? data.Max() : Max;
        if (max <= 0) max = 1;
        double pad = StrokeThickness;

        Point Map(int i, double v)
        {
            var x = pad + (w - 2 * pad) * i / (data.Length - 1);
            var y = (h - pad) - (h - 2 * pad) * (v / max);
            return new Point(x, y);
        }

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            var p0 = Map(0, data[0]);
            lc.BeginFigure(p0, false);
            ac.BeginFigure(new Point(p0.X, h), true);
            ac.LineTo(p0);
            for (int i = 1; i < data.Length; i++)
            {
                var p = Map(i, data[i]);
                lc.LineTo(p);
                ac.LineTo(p);
            }
            ac.LineTo(new Point(Map(data.Length - 1, data[^1]).X, h));
            ac.EndFigure(true);
            lc.EndFigure(false);
        }

        // Flat translucent area fill based on the stroke color (no gradient).
        var baseColor = (Stroke as SolidColorBrush)?.Color ?? Colors.HotPink;
        var fill = new SolidColorBrush(Color.FromArgb(48, baseColor.R, baseColor.G, baseColor.B));
        context.DrawGeometry(fill, null, area);
        context.DrawGeometry(null, new Pen(Stroke, StrokeThickness, lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round), line);
    }
}
