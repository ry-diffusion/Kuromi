using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace Kuromi.Glass.Rendering;

/// <summary>
/// Lifts a glass "lens" into the window's <see cref="OverlayLayer"/> so it floats ABOVE every card while a
/// source control is pressed / dragged (iOS magnifier feel). It tracks the source's centre each frame,
/// animates in on <see cref="Show"/> and out on <see cref="Hide"/>. Because the overlay shares the window's
/// render surface, the lens refracts the real backdrop. Shared by the slider knob and the glass press driver.
/// </summary>
internal sealed class FloatingLens
{
    private readonly Control _source;
    private readonly GlassSurface _lens;
    private readonly ScaleTransform _scale;
    private readonly Func<Size> _size;
    private readonly Func<CornerRadius>? _cornerRadius;
    private readonly double _hiddenScale;
    private readonly double _shownScale;
    private readonly DispatcherTimer _follow;
    private readonly DispatcherTimer _remove;

    private OverlayLayer? _overlay;
    private bool _active;

    public FloatingLens(
        Control source,
        GlassSurface lens,
        Func<Size> size,
        double hiddenScale,
        double shownScale,
        Func<CornerRadius>? cornerRadius = null)
    {
        _source = source;
        _lens = lens;
        _size = size;
        _cornerRadius = cornerRadius;
        _hiddenScale = hiddenScale;
        _shownScale = shownScale;

        _scale = new ScaleTransform(hiddenScale, hiddenScale)
        {
            Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
            },
        };
        _lens.RenderTransform = _scale;
        _lens.RenderTransformOrigin = RelativePoint.Center;
        _lens.IsHitTestVisible = false; // never steal the drag/press from the source
        _lens.Opacity = 0;
        _lens.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() },
        };

        _follow = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _follow.Tick += (_, _) => UpdatePosition();
        _remove = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        _remove.Tick += (_, _) => { _remove.Stop(); Detach(); };
    }

    /// <summary>Lift the lens into the overlay over the source and animate it in.</summary>
    public void Show()
    {
        _remove.Stop();
        _overlay = OverlayLayer.GetOverlayLayer(_source);
        if (_overlay is null)
            return;

        if (_cornerRadius is not null)
            _lens.CornerRadius = _cornerRadius();
        if (!_overlay.Children.Contains(_lens))
            _overlay.Children.Add(_lens);

        _active = true;
        UpdatePosition();
        _lens.Opacity = 1;
        _scale.ScaleX = _scale.ScaleY = _shownScale;
        if (!_follow.IsEnabled)
            _follow.Start();
    }

    /// <summary>Animate the lens out, then remove it from the overlay once the fade finishes.</summary>
    public void Hide()
    {
        if (!_active)
            return;
        _active = false;
        _follow.Stop();
        _lens.Opacity = 0;
        _scale.ScaleX = _scale.ScaleY = _hiddenScale;
        _remove.Stop();
        _remove.Start();
    }

    /// <summary>Immediately tear the lens down (e.g. the source left the tree).</summary>
    public void Cancel()
    {
        _active = false;
        _follow.Stop();
        _remove.Stop();
        Detach();
    }

    private void UpdatePosition()
    {
        if (_overlay is null)
            return;

        Size sz = _size();
        if (sz.Width > 0 && sz.Height > 0)
        {
            _lens.Width = sz.Width;
            _lens.Height = sz.Height;
        }

        Point? centre = _source.TranslatePoint(new Point(_source.Bounds.Width / 2, _source.Bounds.Height / 2), _overlay);
        if (centre is null)
            return;

        Canvas.SetLeft(_lens, centre.Value.X - sz.Width / 2);
        Canvas.SetTop(_lens, centre.Value.Y - sz.Height / 2);
    }

    private void Detach()
    {
        if (_overlay is not null && _overlay.Children.Contains(_lens))
            _overlay.Children.Remove(_lens);
    }
}
