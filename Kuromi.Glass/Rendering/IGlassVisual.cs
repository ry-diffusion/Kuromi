using Avalonia;
using Avalonia.Media;

namespace Kuromi.Glass.Rendering;

/// <summary>The appearance + interaction surface shared by every glass control.</summary>
internal interface IGlassVisual
{
    CornerRadius CornerRadius { get; }

    double RefractionHeight { get; }
    double RefractionAmount { get; }
    bool DepthEffect { get; }
    bool ChromaticAberration { get; }

    double BlurRadius { get; }
    double Vibrancy { get; }
    double Brightness { get; }
    double Contrast { get; }

    Color TintColor { get; }
    Color SurfaceColor { get; }

    bool HighlightEnabled { get; }
    double HighlightWidth { get; }
    double HighlightBlurRadius { get; }
    double HighlightOpacity { get; }
    double HighlightAngle { get; }
    double HighlightFalloff { get; }

    bool ShadowEnabled { get; }
    double ShadowRadius { get; }
    Vector ShadowOffset { get; }
    Color ShadowColor { get; }
    double ShadowOpacity { get; }

    double InteractiveProgress { get; }
    Point InteractivePosition { get; }
    bool InteractiveHighlightEnabled { get; }
}

internal static class GlassParameters
{
    public static GlassDrawParameters Build(IGlassVisual v) => new()
    {
        CornerRadius = v.CornerRadius,
        RefractionHeight = v.RefractionHeight,
        RefractionAmount = v.RefractionAmount,
        DepthEffect = v.DepthEffect,
        ChromaticAberration = v.ChromaticAberration,
        BlurRadius = v.BlurRadius,
        Saturation = v.Vibrancy,
        Brightness = v.Brightness,
        Contrast = v.Contrast,
        TintColor = v.TintColor,
        SurfaceColor = v.SurfaceColor,
        HighlightEnabled = v.HighlightEnabled,
        HighlightWidth = v.HighlightWidth,
        HighlightBlurRadius = v.HighlightBlurRadius,
        HighlightOpacity = v.HighlightOpacity,
        HighlightAngleDegrees = v.HighlightAngle,
        HighlightFalloff = v.HighlightFalloff,
        ShadowEnabled = v.ShadowEnabled,
        ShadowRadius = v.ShadowRadius,
        ShadowOffset = v.ShadowOffset,
        ShadowColor = v.ShadowColor,
        ShadowOpacity = v.ShadowOpacity,
        InteractiveProgress = v.InteractiveProgress,
        InteractivePosition = v.InteractivePosition,
        InteractiveHighlightEnabled = v.InteractiveHighlightEnabled,
    };
}
