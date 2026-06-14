using Avalonia;
using Kuromi.Glass.Rendering;

namespace Kuromi.Glass;

/// <summary>
/// A <see cref="GlassSurface"/> that deforms under touch/press — it scales up slightly, squishes toward
/// the pointer on drag and shows a radial highlight, then springs back on release.
/// </summary>
public class GlassInteractiveSurface : GlassSurface
{
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<GlassInteractiveSurface, bool>(nameof(IsInteractive), true);

    public static readonly StyledProperty<bool> InteractiveHighlightEnabledProperty =
        AvaloniaProperty.Register<GlassInteractiveSurface, bool>(nameof(InteractiveHighlightEnabled), true);

    public static readonly StyledProperty<double> InteractiveMaxScaleDipProperty =
        AvaloniaProperty.Register<GlassInteractiveSurface, double>(nameof(InteractiveMaxScaleDip), 4.0);

    private readonly GlassPressDriver _driver;

    public GlassInteractiveSurface()
    {
        _driver = new GlassPressDriver(this, () => IsInteractive, () => InteractiveMaxScaleDip);
    }

    public bool IsInteractive { get => GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }
    public bool InteractiveHighlightEnabled { get => GetValue(InteractiveHighlightEnabledProperty); set => SetValue(InteractiveHighlightEnabledProperty, value); }
    public double InteractiveMaxScaleDip { get => GetValue(InteractiveMaxScaleDipProperty); set => SetValue(InteractiveMaxScaleDipProperty, value); }

    protected override double GetInteractiveProgress() => _driver.Progress;
    protected override Point GetInteractivePosition() => _driver.Position;
    protected override bool GetInteractiveHighlightEnabled() => IsInteractive && InteractiveHighlightEnabled;
}
