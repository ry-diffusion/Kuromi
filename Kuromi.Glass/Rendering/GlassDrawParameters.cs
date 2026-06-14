using Avalonia;
using Avalonia.Media;

namespace Kuromi.Glass.Rendering;

/// <summary>Immutable snapshot of a glass control's visual parameters, handed to the render thread.</summary>
internal struct GlassDrawParameters
{
    public CornerRadius CornerRadius { get; set; }

    // Lens
    public double RefractionHeight { get; set; }
    public double RefractionAmount { get; set; }
    public bool DepthEffect { get; set; }
    public bool ChromaticAberration { get; set; }

    // Backdrop grade
    public double BlurRadius { get; set; }
    public double Saturation { get; set; }
    public double Brightness { get; set; }
    public double Contrast { get; set; }

    // Tint / fill
    public Color TintColor { get; set; }
    public Color SurfaceColor { get; set; }

    // Edge highlight
    public bool HighlightEnabled { get; set; }
    public double HighlightWidth { get; set; }
    public double HighlightBlurRadius { get; set; }
    public double HighlightOpacity { get; set; }
    public double HighlightAngleDegrees { get; set; }
    public double HighlightFalloff { get; set; }

    // Drop shadow
    public bool ShadowEnabled { get; set; }
    public double ShadowRadius { get; set; }
    public Vector ShadowOffset { get; set; }
    public Color ShadowColor { get; set; }
    public double ShadowOpacity { get; set; }

    // Interactive press feedback
    public double InteractiveProgress { get; set; }
    public Point InteractivePosition { get; set; }
    public bool InteractiveHighlightEnabled { get; set; }
}
