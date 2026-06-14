using System;
using Avalonia;
using SkiaSharp;

namespace Kuromi.Glass.Rendering;

/// <summary>Rounded-rect helpers shared by the glass draw operations.</summary>
internal static class GlassGeometry
{
    public static float[] GetCornerRadii(CornerRadius cornerRadius, float maxRadius)
    {
        return new[]
        {
            (float)Math.Clamp(cornerRadius.TopLeft, 0.0, maxRadius),
            (float)Math.Clamp(cornerRadius.TopRight, 0.0, maxRadius),
            (float)Math.Clamp(cornerRadius.BottomRight, 0.0, maxRadius),
            (float)Math.Clamp(cornerRadius.BottomLeft, 0.0, maxRadius),
        };
    }

    public static SKPath CreateRoundRectPath(SKRect rect, float[] radii)
    {
        using SKRoundRect rr = new();
        rr.SetRectRadii(rect, new[]
        {
            new SKPoint(radii[0], radii[0]),
            new SKPoint(radii[1], radii[1]),
            new SKPoint(radii[2], radii[2]),
            new SKPoint(radii[3], radii[3]),
        });

        SKPath path = new();
        path.AddRoundRect(rr, SKPathDirection.Clockwise);
        return path;
    }

    /// <summary>Transform a rect by an SKMatrix and return the axis-aligned bounding box.</summary>
    public static SKRect MapRect(in SKMatrix m, SKRect r)
    {
        Span<float> xs = stackalloc float[4];
        Span<float> ys = stackalloc float[4];
        xs[0] = m.ScaleX * r.Left + m.SkewX * r.Top + m.TransX;
        ys[0] = m.SkewY * r.Left + m.ScaleY * r.Top + m.TransY;
        xs[1] = m.ScaleX * r.Right + m.SkewX * r.Top + m.TransX;
        ys[1] = m.SkewY * r.Right + m.ScaleY * r.Top + m.TransY;
        xs[2] = m.ScaleX * r.Right + m.SkewX * r.Bottom + m.TransX;
        ys[2] = m.SkewY * r.Right + m.ScaleY * r.Bottom + m.TransY;
        xs[3] = m.ScaleX * r.Left + m.SkewX * r.Bottom + m.TransX;
        ys[3] = m.SkewY * r.Left + m.ScaleY * r.Bottom + m.TransY;

        float left = Math.Min(Math.Min(xs[0], xs[1]), Math.Min(xs[2], xs[3]));
        float top = Math.Min(Math.Min(ys[0], ys[1]), Math.Min(ys[2], ys[3]));
        float right = Math.Max(Math.Max(xs[0], xs[1]), Math.Max(xs[2], xs[3]));
        float bottom = Math.Max(Math.Max(ys[0], ys[1]), Math.Max(ys[2], ys[3]));
        return new SKRect(left, top, right, bottom);
    }

    /// <summary>
    /// Build the local matrix for an image shader so that image pixel (0,0) lands at the device-pixel
    /// <paramref name="originInPixels"/>. We first cancel the canvas transform (device px → local), then
    /// shift into the snapshot's pixel origin.
    /// </summary>
    public static SKMatrix WithPixelOrigin(SKMatrix invertedTransform, int originX, int originY)
    {
        invertedTransform.TransX += invertedTransform.ScaleX * originX + invertedTransform.SkewX * originY;
        invertedTransform.TransY += invertedTransform.SkewY * originX + invertedTransform.ScaleY * originY;
        return invertedTransform;
    }
}
