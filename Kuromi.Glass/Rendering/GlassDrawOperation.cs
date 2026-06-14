using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Kuromi.Glass.Rendering;

/// <summary>
/// The main glass pass. Runs on the render thread inside the control's transform. It grabs the live
/// backdrop straight off the GPU surface (no CPU readback, no scene re-render), grades + blurs it,
/// refracts it through the lens shader, then paints the tint, surface fill and rim highlight.
/// </summary>
internal sealed class GlassDrawOperation : ICustomDrawOperation
{
    private readonly Rect _bounds;
    private readonly GlassDrawParameters _p;

    public GlassDrawOperation(Rect bounds, GlassDrawParameters parameters)
    {
        _bounds = bounds;
        _p = parameters;
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

        using ISkiaSharpApiLease lease = leaseFeature.Lease();
        RenderGlass(lease.SkCanvas, lease.SkSurface, lease.GrContext);
    }

    private void RenderGlass(SKCanvas canvas, SKSurface? surface, GRContext? grContext)
    {
        float w = (float)_bounds.Width;
        float h = (float)_bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        float maxRadius = Math.Min(w, h) * 0.5f;
        float[] radii = GlassGeometry.GetCornerRadii(_p.CornerRadius, maxRadius);
        SKRect rect = SKRect.Create(0, 0, w, h);

        // --- Grab the live backdrop straight from the GPU surface (no readback). ---
        SKImage? backdrop = null;
        int originX = 0, originY = 0;
        float deviceScale = 1f;
        SKMatrix inverted = SKMatrix.Identity;

        if (surface is not null)
        {
            SKMatrix total = canvas.TotalMatrix;
            if (total.TryInvert(out inverted))
            {
                deviceScale = MathF.Max(0.05f,
                    (Hypot(total.ScaleX, total.SkewY) + Hypot(total.SkewX, total.ScaleY)) * 0.5f);

                float marginDip = ComputeMarginDip();
                SKRect local = new(-marginDip, -marginDip, w + marginDip, h + marginDip);
                SKRect device = GlassGeometry.MapRect(total, local);

                // Snapshot intersects with the surface bounds internally; clamping left/top to >= 0
                // keeps the returned image's origin equal to (left, top).
                int left = Math.Max(0, (int)MathF.Floor(device.Left));
                int top = Math.Max(0, (int)MathF.Floor(device.Top));
                int right = (int)MathF.Ceiling(device.Right);
                int bottom = (int)MathF.Ceiling(device.Bottom);

                if (right > left && bottom > top)
                {
                    backdrop = surface.Snapshot(new SKRectI(left, top, right, bottom));
                    if (backdrop is not null)
                    {
                        originX = left;
                        originY = top;
                    }
                }
            }
        }

        if (backdrop is null)
        {
            FallbackFill(canvas, rect, radii);
            DrawEdgeHighlight(canvas, rect, radii, w, h);
            return;
        }

        SKImage filtered = ApplyBackdropGrade(backdrop, grContext, deviceScale, out bool filteredOwned);

        SKShader? imageShader = null;
        SKShader? lensShader = null;
        try
        {
            SKMatrix localMatrix = GlassGeometry.WithPixelOrigin(inverted, originX, originY);
            imageShader = SKShader.CreateImage(filtered, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, localMatrix);

            float refractionHeight = (float)Math.Clamp(_p.RefractionHeight, 0.0, Math.Min(w, h) * 0.5);
            float refractionAmount = (float)_p.RefractionAmount;
            bool applyLens = GlassShaders.Lens is not null
                             && refractionHeight > 0.001f
                             && MathF.Abs(refractionAmount) > 0.001f;

            SKShader baseShader = imageShader;
            if (applyLens)
            {
                SKRuntimeEffect effect = GlassShaders.Lens!;
                using SKRuntimeEffectUniforms uniforms = new(effect);
                uniforms["size"] = new[] { w, h };
                uniforms["cornerRadii"] = radii;
                uniforms["refractionHeight"] = refractionHeight;
                uniforms["refractionAmount"] = -refractionAmount; // positive public value pulls inward
                uniforms["depthEffect"] = _p.DepthEffect ? 1.0f : 0.0f;
                uniforms["dispersion"] = _p.ChromaticAberration ? 1.0f : 0.0f;

                using SKRuntimeEffectChildren children = new(effect);
                children["content"] = imageShader;

                lensShader = effect.ToShader(uniforms, children);
                if (lensShader is not null)
                    baseShader = lensShader;
            }

            canvas.Save();
            using (SKPath clip = GlassGeometry.CreateRoundRectPath(rect, radii))
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);

            using (SKPaint paint = new() { Shader = baseShader, IsAntialias = true })
                canvas.DrawRect(rect, paint);

            DrawTintAndSurface(canvas, rect);
            DrawInteractiveHighlight(canvas, w, h);
            canvas.Restore();

            DrawEdgeHighlight(canvas, rect, radii, w, h);
        }
        finally
        {
            lensShader?.Dispose();
            imageShader?.Dispose();
            if (filteredOwned)
                filtered.Dispose();
            backdrop.Dispose();
        }
    }

    private float ComputeMarginDip()
    {
        // The lens refracts *inward* (it pulls the backdrop toward the centre), so refraction never
        // samples outside the control — only the blur kernel reaches out (~3 sigma). Keeping the margin
        // to just the blur stops neighbouring glass surfaces from bleeding into the snapshot (which
        // showed up as faint edge halos on full repaints), and lets refraction be as strong as we like.
        double blur = _p.BlurRadius * 3.0;
        return (float)Math.Max(8.0, blur + 8.0);
    }

    private SKImage ApplyBackdropGrade(SKImage src, GRContext? grContext, float deviceScale, out bool owned)
    {
        float brightness = (float)Math.Clamp(_p.Brightness, -1.0, 1.0);
        float contrast = (float)Math.Clamp(_p.Contrast, 0.0, 4.0);
        float saturation = (float)Math.Clamp(_p.Saturation, 0.0, 4.0);

        // Blur radius is authored in DIPs; the snapshot is in device pixels.
        float blurSigmaPx = (float)Math.Clamp(_p.BlurRadius, 0.0, 256.0) * deviceScale;

        bool needsColor = MathF.Abs(brightness) > 0.0005f
                          || MathF.Abs(contrast - 1f) > 0.0005f
                          || MathF.Abs(saturation - 1f) > 0.0005f;
        bool needsBlur = blurSigmaPx > 0.0005f;

        if (!needsColor && !needsBlur)
        {
            owned = false;
            return src;
        }

        SKImageFilter? filter = null;
        if (needsColor)
        {
            using SKColorFilter cf = SKColorFilter.CreateColorMatrix(ColorControlsMatrix(brightness, contrast, saturation));
            filter = SKImageFilter.CreateColorFilter(cf, filter);
        }
        if (needsBlur)
        {
            SKRect crop = new(0, 0, src.Width, src.Height);
            filter = SKImageFilter.CreateBlur(blurSigmaPx, blurSigmaPx, SKShaderTileMode.Clamp, filter, crop);
        }

        SKImageInfo info = new(src.Width, src.Height, src.ColorType, src.AlphaType, src.ColorSpace);
        SKSurface? tmp = GlassSurfacePool.Rent(info, grContext);
        if (tmp is null)
        {
            filter?.Dispose();
            owned = false;
            return src;
        }

        SKImage result;
        using (filter)
        using (SKPaint paint = new() { ImageFilter = filter, BlendMode = SKBlendMode.Src })
        {
            // Rent() hands back a cleared surface; Src blend overwrites every covered pixel.
            tmp.Canvas.DrawImage(src, 0, 0, paint);
            tmp.Canvas.Flush();
            result = tmp.Snapshot();
        }

        GlassSurfacePool.Return(tmp, info);
        owned = true;
        return result;
    }

    private void DrawTintAndSurface(SKCanvas canvas, SKRect rect)
    {
        if (_p.TintColor.A > 0)
        {
            Color t = _p.TintColor;
            using (SKPaint hue = new() { Color = new SKColor(t.R, t.G, t.B, 255), IsAntialias = true, BlendMode = SKBlendMode.Hue })
                canvas.DrawRect(rect, hue);
            using (SKPaint fill = new() { Color = new SKColor(t.R, t.G, t.B, (byte)Math.Clamp(t.A * 0.75, 0, 255)), IsAntialias = true })
                canvas.DrawRect(rect, fill);
        }

        if (_p.SurfaceColor.A > 0)
        {
            Color s = _p.SurfaceColor;
            using SKPaint paint = new() { Color = new SKColor(s.R, s.G, s.B, s.A), IsAntialias = true };
            canvas.DrawRect(rect, paint);
        }
    }

    private void DrawInteractiveHighlight(SKCanvas canvas, float w, float h)
    {
        float progress = (float)Math.Clamp(_p.InteractiveProgress, 0.0, 1.0);
        if (!_p.InteractiveHighlightEnabled || progress <= 0.001f)
            return;

        SKRect rect = SKRect.Create(0, 0, w, h);

        using (SKPaint baseFill = new()
        {
            Color = new SKColor(255, 255, 255, (byte)Math.Clamp(0.08f * progress * 255f, 0f, 255f)),
            BlendMode = SKBlendMode.Plus,
            IsAntialias = true,
        })
            canvas.DrawRect(rect, baseFill);

        SKPoint center = new(
            (float)Math.Clamp(_p.InteractivePosition.X, 0, w),
            (float)Math.Clamp(_p.InteractivePosition.Y, 0, h));
        float radius = Math.Min(w, h) * 0.9f;
        byte peak = (byte)Math.Clamp(0.15f * progress * 255f, 0f, 255f);

        using SKShader radial = SKShader.CreateRadialGradient(
            center, radius,
            new[] { new SKColor(255, 255, 255, peak), new SKColor(255, 255, 255, 0) },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);
        using SKPaint paint = new() { Shader = radial, BlendMode = SKBlendMode.Plus, IsAntialias = true };
        canvas.DrawRect(rect, paint);
    }

    private void DrawEdgeHighlight(SKCanvas canvas, SKRect rect, float[] radii, float w, float h)
    {
        SKRuntimeEffect? effect = GlassShaders.Highlight;
        if (effect is null || !_p.HighlightEnabled || _p.HighlightOpacity <= 0.001 || _p.HighlightWidth <= 0.001)
            return;

        using SKRuntimeEffectUniforms uniforms = new(effect);
        uniforms["size"] = new[] { w, h };
        uniforms["cornerRadii"] = radii;
        uniforms["color"] = new[] { 1f, 1f, 1f, (float)Math.Clamp(_p.HighlightOpacity, 0.0, 1.0) };
        uniforms["angle"] = (float)(_p.HighlightAngleDegrees * Math.PI / 180.0);
        uniforms["falloff"] = (float)Math.Clamp(_p.HighlightFalloff, 0.0, 8.0);

        using SKRuntimeEffectChildren children = new(effect);
        using SKShader? shader = effect.ToShader(uniforms, children);
        if (shader is null)
            return;

        float blur = (float)Math.Clamp(_p.HighlightBlurRadius, 0.0, 20.0);
        using SKMaskFilter? mask = blur > 0.001f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur) : null;
        float stroke = MathF.Max(0.5f, (float)Math.Ceiling(Math.Clamp(_p.HighlightWidth, 0.0, 100.0)) * 2f);

        using SKPaint paint = new()
        {
            Shader = shader,
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            MaskFilter = mask,
        };

        canvas.Save();
        using (SKPath path = GlassGeometry.CreateRoundRectPath(rect, radii))
        {
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            canvas.DrawPath(path, paint);
        }
        canvas.Restore();
    }

    private void FallbackFill(SKCanvas canvas, SKRect rect, float[] radii)
    {
        // No GPU backdrop available — draw a translucent frosted fill so the surface still reads as glass.
        byte alpha = _p.SurfaceColor.A > 0 ? _p.SurfaceColor.A : (byte)28;
        SKColor color = _p.SurfaceColor.A > 0
            ? new SKColor(_p.SurfaceColor.R, _p.SurfaceColor.G, _p.SurfaceColor.B, alpha)
            : new SKColor(255, 255, 255, alpha);

        canvas.Save();
        using (SKPath clip = GlassGeometry.CreateRoundRectPath(rect, radii))
            canvas.ClipPath(clip, SKClipOperation.Intersect, true);
        using (SKPaint paint = new() { Color = color, IsAntialias = true })
            canvas.DrawRect(rect, paint);
        canvas.Restore();
    }

    // Color-controls matrix (brightness/contrast/saturation) operating on normalized (0..1) colors.
    private static float[] ColorControlsMatrix(float brightness, float contrast, float saturation)
    {
        float invSat = 1f - saturation;
        float r = 0.213f * invSat;
        float g = 0.715f * invSat;
        float b = 0.072f * invSat;
        float c = contrast;
        float t = 0.5f - c * 0.5f + brightness;
        float s = saturation;
        float cr = c * r, cg = c * g, cb = c * b, cs = c * s;
        return new[]
        {
            cr + cs, cg, cb, 0f, t,
            cr, cg + cs, cb, 0f, t,
            cr, cg, cb + cs, 0f, t,
            0f, 0f, 0f, 1f, 0f,
        };
    }

    private static float Hypot(float a, float b) => MathF.Sqrt(a * a + b * b);
}
