using System;
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
    /// <summary>Extract a Material You scheme from an image file, or null if it fails.</summary>
    public static DynamicPalette? FromImage(string path, bool dark)
    {
        try { using var b = SKBitmap.Decode(path); return FromBitmap(b, dark); }
        catch { return null; }
    }

    /// <summary>Extract a Material You scheme from encoded image bytes (e.g. a downloaded album cover).</summary>
    public static DynamicPalette? FromBytes(byte[] bytes, bool dark)
    {
        try { using var b = SKBitmap.Decode(bytes); return FromBitmap(b, dark); }
        catch { return null; }
    }

    private static DynamicPalette? FromBitmap(SKBitmap? decoded, bool dark)
    {
        if (decoded == null) return null;

        // Downscale so quantization is fast (Material's own pipeline uses ~112px).
        var small = decoded.Resize(new SKImageInfo(112, 112), new SKSamplingOptions(SKFilterMode.Linear));
        var src = small ?? decoded;

        var pixels = Array.ConvertAll(src.Pixels, p => (uint)p);
        small?.Dispose();

        var seed = ImageUtils.ColorsFromImage(pixels).FirstOrDefault();
        if (seed == 0) return null;

        var core = CorePalette.Of(seed);
        Scheme<uint> s = dark
            ? new DarkSchemeMapper().Map(core)
            : new LightSchemeMapper().Map(core);

        Color accent = Vibrant(s.Primary);
        return new DynamicPalette(
            Accent: accent,
            Secondary: Vibrant(s.Secondary),
            Tertiary: Vibrant(s.Tertiary),
            OnAccent: OnColor(accent),
            TextPrimary: C(s.OnSurface),
            TextSecondary: C(s.OnSurfaceVariant),
            TextMuted: C(core.NeutralVariant[(uint)(dark ? 65 : 45)]));
    }

    private static Color C(uint argb) => Color.FromUInt32(argb);

    /// <summary>Push a Material tone toward a punchier, more saturated accent (the M3 tones are muted).</summary>
    private static Color Vibrant(uint argb)
    {
        HslColor hsl = Color.FromUInt32(argb).ToHsl();
        double s = Math.Min(1.0, hsl.S * 1.55 + 0.22);
        double l = Math.Clamp(hsl.L, 0.52, 0.68);
        return new HslColor(1.0, hsl.H, s, l).ToRgb();
    }

    /// <summary>Readable text colour to sit on top of <paramref name="c"/>.</summary>
    private static Color OnColor(Color c)
    {
        double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        return lum > 0.6 ? Color.FromRgb(0x1A, 0x16, 0x22) : Colors.White;
    }
}
