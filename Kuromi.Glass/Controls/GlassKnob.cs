using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Kuromi.Glass;

/// <summary>
/// Shared knob visual for the slider and switch: a plain opaque rounded handle at rest (no glass pass,
/// so no backdrop artefact), which on <see cref="Pressed"/> grows and turns into a clear glass lens that
/// refracts/distorts the backdrop, then animates back on release. Footprint = the pressed size, so the
/// glass always grows within bounds (no overflow/clamping).
/// </summary>
internal sealed class GlassKnob : Panel
{
    private static readonly Color ClearGlass = Color.FromArgb(22, 255, 255, 255);

    private readonly GlassSurface _glass;
    private readonly Border _cap;
    private readonly double _restW;
    private readonly double _restH;
    private readonly double _pressW;
    private readonly double _pressH;

    private DispatcherTimer? _restore;
    private bool _pressed;

    public GlassKnob(double restW, double restH, double pressW, double pressH)
    {
        _restW = restW;
        _restH = restH;
        _pressW = pressW;
        _pressH = pressH;

        Width = pressW;   // footprint = pressed size; the knob grows to fill it (never overflows)
        Height = pressH;

        _glass = new GlassSurface
        {
            IsVisible = false,
            Width = restW,
            Height = restH,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(100), // clamped to min(w,h)/2 → fully rounded
            RefractionHeight = 9,
            RefractionAmount = 28,
            BlurRadius = 1,
            DepthEffect = true,
            ChromaticAberration = true,
            Vibrancy = 1.3,
            SurfaceColor = ClearGlass,
            HighlightOpacity = 0.6,
            ShadowRadius = 6,
            ShadowOffset = new Vector(0, 1),
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = Layoutable.HeightProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut() },
            },
        };

        _cap = new Border
        {
            Width = restW,
            Height = restH,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(100),
            Background = Brushes.White,
            BoxShadow = BoxShadows.Parse("0 1 5 0 #50000000"),
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut() },
            },
        };

        Children.Add(_glass);
        Children.Add(_cap);
    }

    public bool Pressed
    {
        get => _pressed;
        set
        {
            if (_pressed == value)
                return;
            _pressed = value;
            Apply();
        }
    }

    private void Apply()
    {
        if (_pressed)
        {
            // Show + grow the clear glass; fade the opaque cap out.
            _restore?.Stop();
            _glass.IsVisible = true;
            _glass.Width = _pressW;
            _glass.Height = _pressH;
            _cap.Opacity = 0;
        }
        else
        {
            // Animate the glass back down while the cap fades in over it, then hide the glass (so it does
            // no backdrop snapshot at rest → no artefact).
            _glass.Width = _restW;
            _glass.Height = _restH;
            _cap.Opacity = 1;

            _restore ??= CreateRestoreTimer();
            _restore.Stop();
            _restore.Start();
        }
    }

    private DispatcherTimer CreateRestoreTimer()
    {
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _glass.IsVisible = false;
        };
        return timer;
    }
}
