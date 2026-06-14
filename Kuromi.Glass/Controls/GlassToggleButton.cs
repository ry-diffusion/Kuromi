using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Kuromi.Glass.Rendering;

namespace Kuromi.Glass;

/// <summary>A liquid-glass <see cref="ToggleButton"/>. The surface fill strengthens when checked.</summary>
public class GlassToggleButton : ToggleButton, IGlassVisual
{
    public static readonly StyledProperty<double> BlurRadiusProperty =
        AvaloniaProperty.Register<GlassToggleButton, double>(nameof(BlurRadius), 8.0);

    public static readonly StyledProperty<double> VibrancyProperty =
        AvaloniaProperty.Register<GlassToggleButton, double>(nameof(Vibrancy), 1.4);

    public static readonly StyledProperty<bool> ChromaticAberrationProperty =
        AvaloniaProperty.Register<GlassToggleButton, bool>(nameof(ChromaticAberration), false);

    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<GlassToggleButton, Color>(nameof(TintColor), Colors.Transparent);

    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<GlassToggleButton, Color>(nameof(SurfaceColor), Color.FromArgb(28, 255, 255, 255));

    public static readonly StyledProperty<Color> CheckedSurfaceColorProperty =
        AvaloniaProperty.Register<GlassToggleButton, Color>(nameof(CheckedSurfaceColor), Color.FromArgb(96, 255, 255, 255));

    public static readonly StyledProperty<bool> ShadowEnabledProperty =
        AvaloniaProperty.Register<GlassToggleButton, bool>(nameof(ShadowEnabled), true);

    public static readonly StyledProperty<bool> InteractiveHighlightEnabledProperty =
        AvaloniaProperty.Register<GlassToggleButton, bool>(nameof(InteractiveHighlightEnabled), true);

    public static readonly StyledProperty<double> InteractiveMaxScaleDipProperty =
        AvaloniaProperty.Register<GlassToggleButton, double>(nameof(InteractiveMaxScaleDip), 6.0);

    private readonly GlassPressDriver _driver;

    static GlassToggleButton()
    {
        ClipToBoundsProperty.OverrideDefaultValue<GlassToggleButton>(false);
        CornerRadiusProperty.OverrideDefaultValue<GlassToggleButton>(new CornerRadius(20));
        TemplateProperty.OverrideDefaultValue<GlassToggleButton>(CreateTemplate());

        AffectsRender<GlassToggleButton>(
            CornerRadiusProperty, BlurRadiusProperty, VibrancyProperty, ChromaticAberrationProperty,
            TintColorProperty, SurfaceColorProperty, CheckedSurfaceColorProperty, ShadowEnabledProperty,
            IsCheckedProperty);
    }

    public GlassToggleButton()
    {
        _driver = new GlassPressDriver(this, () => IsEffectivelyEnabled, () => InteractiveMaxScaleDip);
    }

    public double BlurRadius { get => GetValue(BlurRadiusProperty); set => SetValue(BlurRadiusProperty, value); }
    public double Vibrancy { get => GetValue(VibrancyProperty); set => SetValue(VibrancyProperty, value); }
    public bool ChromaticAberration { get => GetValue(ChromaticAberrationProperty); set => SetValue(ChromaticAberrationProperty, value); }
    public Color TintColor { get => GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
    public Color SurfaceColor { get => GetValue(SurfaceColorProperty); set => SetValue(SurfaceColorProperty, value); }
    public Color CheckedSurfaceColor { get => GetValue(CheckedSurfaceColorProperty); set => SetValue(CheckedSurfaceColorProperty, value); }
    public bool ShadowEnabled { get => GetValue(ShadowEnabledProperty); set => SetValue(ShadowEnabledProperty, value); }
    public bool InteractiveHighlightEnabled { get => GetValue(InteractiveHighlightEnabledProperty); set => SetValue(InteractiveHighlightEnabledProperty, value); }
    public double InteractiveMaxScaleDip { get => GetValue(InteractiveMaxScaleDipProperty); set => SetValue(InteractiveMaxScaleDipProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        GlassPainter.Paint(context, new Rect(0, 0, Bounds.Width, Bounds.Height), GlassParameters.Build(this));
    }

    Color IGlassVisual.SurfaceColor => IsChecked == true ? CheckedSurfaceColor : SurfaceColor;

    double IGlassVisual.RefractionHeight => 14.0;
    double IGlassVisual.RefractionAmount => 22.0;
    bool IGlassVisual.DepthEffect => false;
    double IGlassVisual.Brightness => 0.0;
    double IGlassVisual.Contrast => 1.0;
    bool IGlassVisual.HighlightEnabled => true;
    double IGlassVisual.HighlightWidth => 0.75;
    double IGlassVisual.HighlightBlurRadius => 0.5;
    double IGlassVisual.HighlightOpacity => IsChecked == true ? 0.8 : 0.6;
    double IGlassVisual.HighlightAngle => 45.0;
    double IGlassVisual.HighlightFalloff => 1.0;
    double IGlassVisual.ShadowRadius => 12.0;
    Vector IGlassVisual.ShadowOffset => new(0.0, 3.0);
    Color IGlassVisual.ShadowColor => Color.FromArgb(56, 0, 0, 0);
    double IGlassVisual.ShadowOpacity => 1.0;
    double IGlassVisual.InteractiveProgress => _driver.Progress;
    Point IGlassVisual.InteractivePosition => _driver.Position;
    bool IGlassVisual.InteractiveHighlightEnabled => InteractiveHighlightEnabled;

    private static FuncControlTemplate CreateTemplate() =>
        new FuncControlTemplate<GlassToggleButton>((_, ns) =>
        {
            ContentPresenter presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                RecognizesAccessKey = true,
                [~ContentPresenter.ContentProperty] = new TemplateBinding(ContentProperty),
                [~ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentTemplateProperty),
                [~ContentPresenter.PaddingProperty] = new TemplateBinding(PaddingProperty),
                [~ContentPresenter.ForegroundProperty] = new TemplateBinding(ForegroundProperty),
                [~ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(HorizontalContentAlignmentProperty),
                [~ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(VerticalContentAlignmentProperty),
            }.RegisterInNameScope(ns);

            return new Border
            {
                ClipToBounds = true,
                Background = Brushes.Transparent, // hit-testable across the whole button (glass is drawn in Render)
                [~Border.CornerRadiusProperty] = new TemplateBinding(CornerRadiusProperty),
                Child = presenter,
            };
        });
}
