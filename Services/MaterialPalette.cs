using System.Linq;
using Avalonia.Media;
using MaterialColorUtilities.Palettes;
using MaterialColorUtilities.Schemes;
using MaterialColorUtilities.Utils;
using SkiaSharp;

namespace Kuromi.Services;

/// <summary>
/// A full Material You (Material Design 3) color scheme derived from an image.
/// Accent/secondary/tertiary drive the graphs &amp; controls; the on-surface tones
/// give text colors that harmonize with the wallpaper (theme-aware).
/// </summary>
public readonly record struct DynamicPalette(
    Color Accent, Color Secondary, Color Tertiary, Color OnAccent,
    Color TextPrimary, Color TextSecondary, Color TextMuted);

public static class MaterialPalette
{
    /// <summary>Extract a Material You scheme from an image, or null if it fails.</summary>
    public static DynamicPalette? FromImage(string path, bool dark)
    {
        try
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded == null) return null;

            // Downscale so quantization is fast (Material's own pipeline uses ~112px).
            using var small = decoded.Resize(new SKImageInfo(112, 112),
                new SKSamplingOptions(SKFilterMode.Linear)) ?? decoded;

            var pixels = System.Array.ConvertAll(small.Pixels, p => (uint)p);
            var seed = ImageUtils.ColorsFromImage(pixels).FirstOrDefault();
            if (seed == 0) return null;

            var core = CorePalette.Of(seed);
            Scheme<uint> s = dark
                ? new DarkSchemeMapper().Map(core)
                : new LightSchemeMapper().Map(core);

            return new DynamicPalette(
                Accent: C(s.Primary),
                Secondary: C(s.Secondary),
                Tertiary: C(s.Tertiary),
                OnAccent: C(s.OnPrimary),
                TextPrimary: C(s.OnSurface),
                TextSecondary: C(s.OnSurfaceVariant),
                TextMuted: C(core.NeutralVariant[(uint)(dark ? 65 : 45)]));
        }
        catch
        {
            return null;
        }
    }

    private static Color C(uint argb) => Color.FromUInt32(argb);
}
