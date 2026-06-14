using Avalonia;
using Avalonia.Rendering.Composition;

namespace Kuromi.Glass;

public static class GlassAppBuilderExtensions
{
    /// <summary>
    /// Render-tuning defaults that pair well with glass surfaces: region-based dirty-rect clipping (so
    /// only the changed area recomposites and re-snapshots) and a generous Skia GPU resource cache.
    /// </summary>
    public static AppBuilder UseKuromiGlass(
        this AppBuilder builder,
        long skiaMaxGpuResourceSizeBytes = 256L * 1024L * 1024L,
        int maxDirtyRects = 8)
    {
        return builder
            .With(new CompositionOptions
            {
                UseRegionDirtyRectClipping = true,
                MaxDirtyRects = maxDirtyRects,
            })
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = skiaMaxGpuResourceSizeBytes,
            });
    }
}
