using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kuromi.Glass.Rendering;

namespace Kuromi.Glass;

/// <summary>
/// Shared knob visual for the slider and switch: a plain opaque rounded handle at rest (no glass pass, so
/// no backdrop artefact). On <see cref="Pressed"/> it turns into a clear glass lens that refracts/distorts
/// the backdrop, then animates back on release.
/// <para>
/// With <see cref="FloatLens"/> the lens detaches into the window overlay and floats ABOVE every card
/// (iOS magnifier — used by the slider so the dragged knob escapes its container). Otherwise the lens grows
/// in place within the knob footprint (used by the switch).
/// </para>
/// </summary>
internal sealed class GlassKnob : Panel
{
    private static readonly Color ClearGlass = Color.FromArgb(22, 255, 255, 255);
    private const int ElevatedZ = 1000; // draw above siblings while active so the in-place grow isn't covered

    /// <summary>Extra magnify applied on press so the knob balloons past its slot.</summary>
    public double PopScale { get; set; } = 1.3;

    /// <summary>A control whose ZIndex is raised while the knob is active (in-place mode), so it escapes siblings.</summary>
    public Control? ElevationTarget { get; set; }

    /// <summary>When true the pressed lens floats in the window overlay above all cards instead of growing in place.</summary>
    public bool FloatLens { get; set; }

    private readonly GlassSurface _glass;
    private readonly Border _cap;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly double _restW;
    private readonly double _restH;
    private readonly double _pressW;
    private readonly double _pressH;

    private DispatcherTimer? _restore;
    private FloatingLens? _floating;
    private bool _pressed;

    public GlassKnob(double restW, double restH, double pressW, double pressH)
    {
        _restW = restW;
        _restH = restH;
        _pressW = pressW;
        _pressH = pressH;

        Width = pressW;   // footprint = pressed size; the in-place glass grows to fill it (never overflows)
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

        _scale.Transitions = new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut() },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut() },
        };
        RenderTransform = _scale;
        RenderTransformOrigin = RelativePoint.Center;

        DetachedFromVisualTree += (_, _) => _floating?.Cancel();
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
            _restore?.Stop();
            _cap.Opacity = 0; // the lens (floating or in-place) takes over from the opaque handle

            if (FloatLens)
            {
                EnsureFloating().Show();
            }
            else
            {
                _glass.IsVisible = true;
                _glass.Width = _pressW;
                _glass.Height = _pressH;
                _scale.ScaleX = _scale.ScaleY = PopScale;
                Elevate(true);
            }
        }
        else
        {
            _cap.Opacity = 1;

            if (FloatLens)
            {
                _floating?.Hide();
            }
            else
            {
                // Animate the glass back down while the cap fades in over it, then hide the glass (so it does
                // no backdrop snapshot at rest → no artefact). ZIndex drops once the shrink-back finishes.
                _glass.Width = _restW;
                _glass.Height = _restH;
                _scale.ScaleX = _scale.ScaleY = 1.0;

                _restore ??= CreateRestoreTimer();
                _restore.Stop();
                _restore.Start();
            }
        }
    }

    private FloatingLens EnsureFloating()
    {
        if (_floating is not null)
            return _floating;

        double lensW = _pressW * PopScale;
        double lensH = _pressH * PopScale;

        GlassSurface lens = new()
        {
            Width = lensW,
            Height = lensH,
            CornerRadius = new CornerRadius(100),
            RefractionHeight = 11,
            RefractionAmount = 30,
            BlurRadius = 1,
            DepthEffect = true,
            ChromaticAberration = true,
            Vibrancy = 1.3,
            SurfaceColor = ClearGlass,
            HighlightOpacity = 0.6,
            ShadowRadius = 14,
            ShadowOffset = new Vector(0, 5),
            ShadowColor = Color.FromArgb(80, 0, 0, 0),
        };

        // Grows from roughly the resting handle size up to the full lens, floating above every card.
        _floating = new FloatingLens(this, lens, () => new Size(lensW, lensH), hiddenScale: _restW / lensW, shownScale: 1.0);
        return _floating;
    }

    private void Elevate(bool on)
    {
        ZIndex = on ? ElevatedZ : 0;
        if (ElevationTarget is { } target)
            target.ZIndex = on ? ElevatedZ : 0;
    }

    private DispatcherTimer CreateRestoreTimer()
    {
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _glass.IsVisible = false;
            Elevate(false);
        };
        return timer;
    }
}
