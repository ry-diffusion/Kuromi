using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Kuromi.Logging;

namespace Kuromi.Glass.Rendering;

/// <summary>
/// Self-contained press-feedback driver: attach it to a control and it wires pointer tracking, runs the
/// spring animation timer, applies the deformation transform and invalidates the control. Shared by the
/// interactive surface and the glass buttons.
/// </summary>
internal sealed class GlassPressDriver
{
    private readonly Control _owner;
    private readonly Func<bool> _enabled;
    private readonly Func<double> _maxScaleDip;
    private readonly GlassPressInteraction _press = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _watchdog;

    private const int ElevatedZ = 1000; // draw above siblings while pressed so the scale-up isn't covered

    private readonly ILog _log = Log.For<GlassPressDriver>();
    private DateTime _lastTick;
    private int? _activePointerId;
    private TopLevel? _hooked;
    private FloatingLens? _lens;

    public GlassPressDriver(Control owner, Func<bool> enabled, Func<double> maxScaleDip)
    {
        _owner = owner;
        _enabled = enabled;
        _maxScaleDip = maxScaleDip;

        _owner.RenderTransformOrigin = RelativePoint.Center;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;

        // Backstop: if a release is ever missed (e.g. pressing a button that launches an app or suspends
        // steals focus before PointerReleased arrives), force the press to end so the lens never sticks.
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _watchdog.Tick += (_, _) =>
        {
            _watchdog.Stop();
            if (_activePointerId is null)
                return;
            _log.Warn($"press watchdog fired on {_owner.GetType().Name} — forcing release (missed pointer-up)");
            End();
        };

        _owner.AddHandler(InputElement.PointerPressedEvent, OnSelfPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _owner.DetachedFromVisualTree += OnOwnerDetached;
    }

    public double Progress => _press.Progress;
    public Point Position => _press.Position;

    private void OnTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastTick).TotalSeconds, 0.0, 0.05);
        _lastTick = now;

        bool animating = _press.Step(dt);
        Apply();
        _owner.InvalidateVisual();

        if (!animating)
        {
            // Only drop the elevation once the press has fully relaxed, so the shrink-back still draws on top.
            if (_press.Progress < 0.01)
                _owner.ZIndex = 0;
            _timer.Stop();
        }
    }

    private void Start()
    {
        if (_timer.IsEnabled)
            return;
        _lastTick = DateTime.UtcNow;
        _timer.Start();
    }

    private void OnSelfPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_enabled() || _activePointerId is not null)
            return;
        if (!e.GetCurrentPoint(_owner).Properties.IsLeftButtonPressed)
            return;

        _activePointerId = e.Pointer.Id;
        _press.Press(e.GetPosition(_owner));
        _owner.ZIndex = ElevatedZ; // pop above neighbours; the scale-up can now spill out of the container
        EnsureLens().Show();       // lift a glass tile into the overlay so it floats above every card
        Hook();
        Apply();
        _owner.InvalidateVisual();
        Start();
        _watchdog.Stop();
        _watchdog.Start();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_activePointerId is null || e.Pointer.Id != _activePointerId.Value)
            return;
        _press.MoveTo(e.GetPosition(_owner));
        Apply();
        _owner.InvalidateVisual();
        Start();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activePointerId is null || e.Pointer.Id != _activePointerId.Value)
            return;
        End();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_activePointerId is not null)
            End();
    }

    private void End()
    {
        _watchdog.Stop();
        _activePointerId = null;
        _lens?.Hide();
        Unhook();
        _press.Release();
        Apply();
        _owner.InvalidateVisual();
        Start();
    }

    private void Hook()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(_owner);
        if (topLevel is null || ReferenceEquals(topLevel, _hooked))
            return;
        Unhook();
        _hooked = topLevel;
        _hooked.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, true);
        _hooked.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, true);
        _hooked.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel, true);
        // Losing window focus (launched app / suspend) means the release will never arrive — end now.
        if (_hooked is Window win)
            win.Deactivated += OnHostDeactivated;
    }

    private void Unhook()
    {
        if (_hooked is null)
            return;
        _hooked.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _hooked.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _hooked.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        if (_hooked is Window win)
            win.Deactivated -= OnHostDeactivated;
        _hooked = null;
    }

    private void OnHostDeactivated(object? sender, EventArgs e)
    {
        if (_activePointerId is null)
            return;
        _log.Debug($"window lost focus while pressing {_owner.GetType().Name} — ending press");
        End();
    }

    private void Apply()
    {
        _owner.RenderTransform = _enabled() && _press.GetDeformation(_owner.Bounds.Width, _owner.Bounds.Height, _maxScaleDip()) is { } m
            ? new MatrixTransform(m)
            : null;
    }

    private void OnOwnerDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _activePointerId = null;
        _owner.ZIndex = 0;
        _lens?.Cancel();
        Unhook();
        _timer.Stop();
        _watchdog.Stop();
    }

    private FloatingLens EnsureLens()
    {
        if (_lens is not null)
            return _lens;

        // A glass RIM that lifts above every card, leaving the label/icon underneath completely untouched:
        //  - fully transparent surface + identity grade (Vibrancy 1.0) so the clear centre is the exact
        //    backdrop, pixel-for-pixel — no tint, no wash,
        //  - no blur, edge-only refraction (the SDF fast-path passes the centre through unchanged),
        //  - held at scale 1.0 (pure fade) so the tile samples the backdrop 1:1 — no magnified ghost over
        //    the text. The lift reads from the soft shadow + rim highlight; the press driver's in-place
        //    squish still provides the "pop".
        GlassSurface tile = new()
        {
            RefractionHeight = 9,
            RefractionAmount = 14,
            BlurRadius = 0,
            DepthEffect = false,
            Vibrancy = 1.0,
            SurfaceColor = Colors.Transparent,
            HighlightOpacity = 0.6,
            ShadowRadius = 20,
            ShadowOffset = new Vector(0, 7),
            ShadowColor = Color.FromArgb(75, 0, 0, 0),
        };

        _lens = new FloatingLens(
            _owner, tile,
            size: () => _owner.Bounds.Size,
            hiddenScale: 1.0,
            shownScale: 1.0,
            cornerRadius: () => (_owner as TemplatedControl)?.CornerRadius ?? new CornerRadius(12));
        return _lens;
    }
}
