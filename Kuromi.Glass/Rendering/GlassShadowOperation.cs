using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Kuromi.Glass.Rendering;

/// <summary>Soft drop shadow drawn behind the glass. Lives outside the control bounds.</summary>
internal sealed class GlassShadowOperation : ICustomDrawOperation
{
    private readonly Rect _controlBounds;
    private readonly Rect _bounds;
    private readonly GlassDrawParameters _p;

    public GlassShadowOperation(Rect controlBounds, GlassDrawParameters parameters)
    {
        _controlBounds = controlBounds;
        _p = parameters;

        double pad = parameters.ShadowRadius * 3.0
                     + Math.Max(Math.Abs(parameters.ShadowOffset.X), Math.Abs(parameters.ShadowOffset.Y))
                     + 4.0;
        _bounds = controlBounds.Inflate(pad);
    }

    public Rect Bounds => _bounds;
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        ISkiaSharpApiLeaseFeature? leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature is null)
            return;

        float w = (float)_controlBounds.Width;
        float h = (float)_controlBounds.Height;
        if (w <= 0 || h <= 0)
            return;

        double opacity = Math.Clamp(_p.ShadowOpacity, 0.0, 1.0);
        if (opacity <= 0.001 || _p.ShadowColor.A == 0 || _p.ShadowRadius <= 0.001)
            return;

        using ISkiaSharpApiLease lease = leaseFeature.Lease();
        SKCanvas canvas = lease.SkCanvas;

        float maxRadius = Math.Min(w, h) * 0.5f;
        float[] radii = GlassGeometry.GetCornerRadii(_p.CornerRadius, maxRadius);

        SKRect rect = SKRect.Create(0, 0, w, h);
        SKRect shadowRect = rect;
        shadowRect.Offset((float)_p.ShadowOffset.X, (float)_p.ShadowOffset.Y);

        Color sc = _p.ShadowColor;
        byte alpha = (byte)Math.Clamp(sc.A * opacity, 0, 255);
        float sigma = (float)Math.Max(0.0, _p.ShadowRadius) * 0.5f;

        using SKPaint paint = new()
        {
            Color = new SKColor(sc.R, sc.G, sc.B, alpha),
            IsAntialias = true,
            MaskFilter = sigma > 0.001f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
        };

        canvas.Save();
        // Cut out the card interior so the shadow only shows around the edges.
        using (SKPath cutout = GlassGeometry.CreateRoundRectPath(rect, radii))
            canvas.ClipPath(cutout, SKClipOperation.Difference, true);
        using (SKPath shadow = GlassGeometry.CreateRoundRectPath(shadowRect, radii))
            canvas.DrawPath(shadow, paint);
        canvas.Restore();
    }
}
