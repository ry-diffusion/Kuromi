using Avalonia;
using Avalonia.Media;

namespace Kuromi.Glass.Rendering;

/// <summary>Issues the glass custom draw operations. Shared by every glass control.</summary>
internal static class GlassPainter
{
    public static void Paint(DrawingContext context, Rect bounds, in GlassDrawParameters p)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (p.ShadowEnabled && p.ShadowOpacity > 0.001 && p.ShadowColor.A > 0 && p.ShadowRadius > 0.001)
            context.Custom(new GlassShadowOperation(bounds, p));

        context.Custom(new GlassDrawOperation(bounds, p));
    }
}
