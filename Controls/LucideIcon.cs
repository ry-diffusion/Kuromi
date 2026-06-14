using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kuromi.Controls;

/// <summary>
/// Renders a Lucide icon (stroke-based, 24×24 viewbox) as a vector, scaled to the
/// control bounds and stroked with <see cref="Brush"/>. Geometries come from the
/// merged <c>Icons.axaml</c> resource dictionary, keyed <c>icon_&lt;kind&gt;</c>.
/// </summary>
public class LucideIcon : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<LucideIcon, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<LucideIcon, IBrush?>(nameof(Brush), Brushes.White);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(StrokeThickness), 1.8);

    private Geometry? _data;

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(KindProperty, BrushProperty, StrokeThicknessProperty);
    }

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
        {
            _data = null; // re-resolved lazily on next render (when attached to the tree)
            InvalidateVisual();
        }
    }

    private void Resolve()
    {
        var kind = Kind;
        if (string.IsNullOrEmpty(kind)) return;
        var key = "icon_" + kind.Replace('-', '_');
        if (this.TryFindResource(key, out var res) && res is Geometry g)
            _data = g;
    }

    public override void Render(DrawingContext context)
    {
        if (_data == null) Resolve();
        var data = _data;
        if (data == null) return;

        var b = Bounds;
        var size = System.Math.Min(b.Width, b.Height);
        if (size <= 0) return;

        double scale = size / 24.0;
        double offX = (b.Width - 24 * scale) / 2;
        double offY = (b.Height - 24 * scale) / 2;

        using (context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offX, offY)))
        {
            // Keep the stroke a constant ~2px on screen regardless of icon size,
            // so icons look crisp and consistent (the context scale is undone here).
            var thickness = StrokeThickness / scale;
            var pen = new Pen(Brush ?? Brushes.White, thickness,
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, data);
        }
    }
}
