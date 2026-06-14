using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kuromi.Controls;

/// <summary>Vertical rounded bars for a small series (e.g. daily Claude cost).</summary>
public class BarChart : Control
{
    public static readonly StyledProperty<IEnumerable<double>?> ValuesProperty =
        AvaloniaProperty.Register<BarChart, IEnumerable<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush> BarBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush>(nameof(BarBrush),
            new SolidColorBrush(Color.FromRgb(0x9B, 0x8C, 0xFF)));

    static BarChart() => AffectsRender<BarChart>(ValuesProperty, BarBrushProperty);

    public IEnumerable<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var data = Values?.ToArray();
        if (data == null || data.Length == 0) return;

        var b = Bounds;
        double w = b.Width, h = b.Height;
        if (w <= 0 || h <= 0) return;

        double max = data.Max();
        if (max <= 0) max = 1;

        double gap = 6;
        double barW = (w - gap * (data.Length - 1)) / data.Length;
        if (barW <= 0) barW = 1;

        var color = (BarBrush as SolidColorBrush)?.Color ?? Colors.MediumPurple;

        for (int i = 0; i < data.Length; i++)
        {
            double bh = (h - 4) * (data[i] / max);
            if (bh < 3 && data[i] > 0) bh = 3;
            var x = i * (barW + gap);
            var rect = new Rect(x, h - bh, barW, bh);
            var fill = new SolidColorBrush(color);
            context.DrawRectangle(fill, null, new RoundedRect(rect, 4));
        }
    }
}
