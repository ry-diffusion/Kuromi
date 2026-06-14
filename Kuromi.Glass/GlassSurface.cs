using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Kuromi.Glass.Rendering;

namespace Kuromi.Glass;

/// <summary>
/// A liquid-glass surface: it samples the live backdrop behind it on the GPU, blurs and grades it,
/// refracts it through a rounded-rect lens and paints a rim highlight and soft shadow. Use it as a
/// content container (card / panel). See <see cref="GlassButton"/> for an interactive variant.
/// </summary>
public class GlassSurface : ContentControl, IGlassVisual
{
    // --- Lens -------------------------------------------------------------------------------------
    public static readonly StyledProperty<double> RefractionHeightProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(RefractionHeight), 20.0);

    public static readonly StyledProperty<double> RefractionAmountProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(RefractionAmount), 40.0);

    public static readonly StyledProperty<bool> DepthEffectProperty =
        AvaloniaProperty.Register<GlassSurface, bool>(nameof(DepthEffect), false);

    public static readonly StyledProperty<bool> ChromaticAberrationProperty =
        AvaloniaProperty.Register<GlassSurface, bool>(nameof(ChromaticAberration), false);

    // --- Backdrop grade ---------------------------------------------------------------------------
    public static readonly StyledProperty<double> BlurRadiusProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(BlurRadius), 2.0);

    public static readonly StyledProperty<double> VibrancyProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(Vibrancy), 1.5);

    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(Brightness), 0.0);

    public static readonly StyledProperty<double> ContrastProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(Contrast), 1.0);

    // --- Tint / fill ------------------------------------------------------------------------------
    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<GlassSurface, Color>(nameof(TintColor), Colors.Transparent);

    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<GlassSurface, Color>(nameof(SurfaceColor), Colors.Transparent);

    // --- Edge highlight ---------------------------------------------------------------------------
    public static readonly StyledProperty<bool> HighlightEnabledProperty =
        AvaloniaProperty.Register<GlassSurface, bool>(nameof(HighlightEnabled), true);

    public static readonly StyledProperty<double> HighlightWidthProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(HighlightWidth), 0.75);

    public static readonly StyledProperty<double> HighlightBlurRadiusProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(HighlightBlurRadius), 0.5);

    public static readonly StyledProperty<double> HighlightOpacityProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(HighlightOpacity), 0.5);

    public static readonly StyledProperty<double> HighlightAngleProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(HighlightAngle), 45.0);

    public static readonly StyledProperty<double> HighlightFalloffProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(HighlightFalloff), 1.0);

    // --- Drop shadow ------------------------------------------------------------------------------
    public static readonly StyledProperty<bool> ShadowEnabledProperty =
        AvaloniaProperty.Register<GlassSurface, bool>(nameof(ShadowEnabled), true);

    public static readonly StyledProperty<double> ShadowRadiusProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(ShadowRadius), 24.0);

    public static readonly StyledProperty<Vector> ShadowOffsetProperty =
        AvaloniaProperty.Register<GlassSurface, Vector>(nameof(ShadowOffset), new Vector(0.0, 6.0));

    public static readonly StyledProperty<Color> ShadowColorProperty =
        AvaloniaProperty.Register<GlassSurface, Color>(nameof(ShadowColor), Color.FromArgb(45, 0, 0, 0));

    public static readonly StyledProperty<double> ShadowOpacityProperty =
        AvaloniaProperty.Register<GlassSurface, double>(nameof(ShadowOpacity), 1.0);

    static GlassSurface()
    {
        // The shadow draws outside the bounds; don't clip the control itself (the template clips content).
        ClipToBoundsProperty.OverrideDefaultValue<GlassSurface>(false);

        AffectsRender<GlassSurface>(
            CornerRadiusProperty,
            RefractionHeightProperty, RefractionAmountProperty, DepthEffectProperty, ChromaticAberrationProperty,
            BlurRadiusProperty, VibrancyProperty, BrightnessProperty, ContrastProperty,
            TintColorProperty, SurfaceColorProperty,
            HighlightEnabledProperty, HighlightWidthProperty, HighlightBlurRadiusProperty,
            HighlightOpacityProperty, HighlightAngleProperty, HighlightFalloffProperty,
            ShadowEnabledProperty, ShadowRadiusProperty, ShadowOffsetProperty, ShadowColorProperty, ShadowOpacityProperty);

        TemplateProperty.OverrideDefaultValue<GlassSurface>(CreateDefaultTemplate());
    }

    public double RefractionHeight { get => GetValue(RefractionHeightProperty); set => SetValue(RefractionHeightProperty, value); }
    public double RefractionAmount { get => GetValue(RefractionAmountProperty); set => SetValue(RefractionAmountProperty, value); }
    public bool DepthEffect { get => GetValue(DepthEffectProperty); set => SetValue(DepthEffectProperty, value); }
    public bool ChromaticAberration { get => GetValue(ChromaticAberrationProperty); set => SetValue(ChromaticAberrationProperty, value); }

    public double BlurRadius { get => GetValue(BlurRadiusProperty); set => SetValue(BlurRadiusProperty, value); }
    public double Vibrancy { get => GetValue(VibrancyProperty); set => SetValue(VibrancyProperty, value); }
    public double Saturation { get => Vibrancy; set => Vibrancy = value; }
    public double Brightness { get => GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    public double Contrast { get => GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }

    public Color TintColor { get => GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
    public Color SurfaceColor { get => GetValue(SurfaceColorProperty); set => SetValue(SurfaceColorProperty, value); }

    public bool HighlightEnabled { get => GetValue(HighlightEnabledProperty); set => SetValue(HighlightEnabledProperty, value); }
    public double HighlightWidth { get => GetValue(HighlightWidthProperty); set => SetValue(HighlightWidthProperty, value); }
    public double HighlightBlurRadius { get => GetValue(HighlightBlurRadiusProperty); set => SetValue(HighlightBlurRadiusProperty, value); }
    public double HighlightOpacity { get => GetValue(HighlightOpacityProperty); set => SetValue(HighlightOpacityProperty, value); }
    public double HighlightAngle { get => GetValue(HighlightAngleProperty); set => SetValue(HighlightAngleProperty, value); }
    public double HighlightFalloff { get => GetValue(HighlightFalloffProperty); set => SetValue(HighlightFalloffProperty, value); }

    public bool ShadowEnabled { get => GetValue(ShadowEnabledProperty); set => SetValue(ShadowEnabledProperty, value); }
    public double ShadowRadius { get => GetValue(ShadowRadiusProperty); set => SetValue(ShadowRadiusProperty, value); }
    public Vector ShadowOffset { get => GetValue(ShadowOffsetProperty); set => SetValue(ShadowOffsetProperty, value); }
    public Color ShadowColor { get => GetValue(ShadowColorProperty); set => SetValue(ShadowColorProperty, value); }
    public double ShadowOpacity { get => GetValue(ShadowOpacityProperty); set => SetValue(ShadowOpacityProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        GlassPainter.Paint(context, new Rect(0, 0, Bounds.Width, Bounds.Height), GlassParameters.Build(this));
    }

    /// <summary>Interactive subclasses override these to feed press feedback into the lens pass.</summary>
    protected virtual double GetInteractiveProgress() => 0.0;
    protected virtual Point GetInteractivePosition() => default;
    protected virtual bool GetInteractiveHighlightEnabled() => false;

    double IGlassVisual.InteractiveProgress => GetInteractiveProgress();
    Point IGlassVisual.InteractivePosition => GetInteractivePosition();
    bool IGlassVisual.InteractiveHighlightEnabled => GetInteractiveHighlightEnabled();

    private static FuncControlTemplate CreateDefaultTemplate() =>
        new FuncControlTemplate<GlassSurface>((_, ns) =>
        {
            ContentPresenter presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [~ContentPresenter.ContentProperty] = new TemplateBinding(ContentProperty),
                [~ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentTemplateProperty),
                [~ContentPresenter.PaddingProperty] = new TemplateBinding(PaddingProperty),
                [~ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(VerticalContentAlignmentProperty),
                [~ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(HorizontalContentAlignmentProperty),
            }.RegisterInNameScope(ns);

            return new Border
            {
                // Don't clip content: a pressed interactive child (e.g. a knob/button that grows) can then
                // spill out past the card's rounded edge instead of being cut off. Normal content is inset
                // by its own margins, so the rounded corners stay clean.
                ClipToBounds = false,
                [~Border.CornerRadiusProperty] = new TemplateBinding(CornerRadiusProperty),
                Child = presenter,
            };
        });
}
