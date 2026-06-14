using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Kuromi.Glass.Rendering;

namespace Kuromi.Glass;

/// <summary>
/// A liquid-glass <see cref="ComboBox"/>: the closed box is a glass surface (refracts the backdrop),
/// with a chevron and the selected item; the drop-down is a frosted rounded panel. Drop-in for a
/// <see cref="ComboBox"/>.
/// </summary>
public class GlassComboBox : ComboBox, IGlassVisual
{
    private readonly GlassPressDriver _driver;

    static GlassComboBox()
    {
        ClipToBoundsProperty.OverrideDefaultValue<GlassComboBox>(false);
        CornerRadiusProperty.OverrideDefaultValue<GlassComboBox>(new CornerRadius(14));
        TemplateProperty.OverrideDefaultValue<GlassComboBox>(CreateTemplate());
        AffectsRender<GlassComboBox>(CornerRadiusProperty);
    }

    public GlassComboBox()
    {
        // No in-place squish (maxScaleDip 0): this is a wide text box, and a scale/shift on press would
        // offset the label a few px under the floating glass tile, producing a faded double-image. With no
        // deform the tile stays aligned 1:1, so the text stays sharp; the floating lift is the feedback.
        _driver = new GlassPressDriver(this, () => IsEffectivelyEnabled, () => 0.0);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        GlassPainter.Paint(context, new Rect(0, 0, Bounds.Width, Bounds.Height), GlassParameters.Build(this));
    }

    // Glass appearance (fixed, container-tuned).
    double IGlassVisual.RefractionHeight => 16.0;
    double IGlassVisual.RefractionAmount => 28.0;
    bool IGlassVisual.DepthEffect => false;
    bool IGlassVisual.ChromaticAberration => false;
    double IGlassVisual.BlurRadius => 6.0;
    double IGlassVisual.Vibrancy => 1.3;
    double IGlassVisual.Brightness => 0.0;
    double IGlassVisual.Contrast => 1.0;
    Color IGlassVisual.TintColor => Colors.Transparent;
    Color IGlassVisual.SurfaceColor => Color.FromArgb(30, 255, 255, 255);
    bool IGlassVisual.HighlightEnabled => true;
    double IGlassVisual.HighlightWidth => 0.75;
    double IGlassVisual.HighlightBlurRadius => 0.5;
    double IGlassVisual.HighlightOpacity => 0.5;
    double IGlassVisual.HighlightAngle => 45.0;
    double IGlassVisual.HighlightFalloff => 1.0;
    bool IGlassVisual.ShadowEnabled => true;
    double IGlassVisual.ShadowRadius => 12.0;
    Vector IGlassVisual.ShadowOffset => new(0.0, 3.0);
    Color IGlassVisual.ShadowColor => Color.FromArgb(56, 0, 0, 0);
    double IGlassVisual.ShadowOpacity => 1.0;
    double IGlassVisual.InteractiveProgress => _driver.Progress;
    Point IGlassVisual.InteractivePosition => _driver.Position;
    bool IGlassVisual.InteractiveHighlightEnabled => true;

    private static FuncControlTemplate CreateTemplate() =>
        new FuncControlTemplate<GlassComboBox>((combo, ns) =>
        {
            // --- Closed box: selected item + chevron over the glass ---
            ContentControl selected = new ContentControl
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(14, 0, 6, 0),
                [!ContentControl.ContentProperty] = new TemplateBinding(SelectionBoxItemProperty),
                [!ContentControl.ContentTemplateProperty] = new TemplateBinding(SelectionBoxItemTemplateProperty),
                [!ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
            };

            Path chevron = new Path
            {
                Data = Geometry.Parse("M 0 0 L 5 5 L 10 0"),
                StrokeThickness = 1.6,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 12,
                Height = 7,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
                [!Shape.StrokeProperty] = new DynamicResourceExtension("TextMuted"),
            };
            Grid.SetColumn(chevron, 1);

            Grid boxContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            boxContent.Children.Add(selected);
            boxContent.Children.Add(chevron);

            Border box = new Border
            {
                Background = Brushes.Transparent, // hit-testable; glass is drawn in Render
                ClipToBounds = true,
                [!Border.CornerRadiusProperty] = new TemplateBinding(CornerRadiusProperty),
                Child = boxContent,
            };

            // --- Drop-down: frosted rounded panel with the item list ---
            ItemsPresenter itemsPresenter = new ItemsPresenter { Name = "PART_ItemsPresenter" }.RegisterInNameScope(ns);

            ScrollViewer scroll = new ScrollViewer
            {
                MaxHeight = 320,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = itemsPresenter,
            };

            GlassSurface popupRoot = new GlassSurface
            {
                CornerRadius = new CornerRadius(14),
                RefractionHeight = 22,
                RefractionAmount = 50,
                BlurRadius = 3,
                Vibrancy = 1.3,
                // Lighter frost + low blur + strong refraction so it reads as real glass (was flat/dark before).
                SurfaceColor = Color.FromArgb(0x88, 0x1B, 0x17, 0x26),
                HighlightOpacity = 0.5,
                ShadowRadius = 16,
                ShadowOffset = new Vector(0, 8),
                Padding = new Thickness(4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = scroll,
                [!Layoutable.MinWidthProperty] = new Binding("Bounds.Width") { Source = combo },
            };

            Popup popup = new Popup
            {
                Name = "PART_Popup",
                PlacementTarget = combo,
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                VerticalOffset = 6,
                // Render in the window's overlay layer so the glass popup can sample the dashboard
                // behind it (real refraction), instead of opening as a separate window.
                ShouldUseOverlayLayer = true,
                Child = popupRoot,
                [!Popup.IsOpenProperty] = new TemplateBinding(IsDropDownOpenProperty),
            }.RegisterInNameScope(ns);

            Panel root = new Panel();
            root.Children.Add(box);
            root.Children.Add(popup);
            return root;
        });
}
