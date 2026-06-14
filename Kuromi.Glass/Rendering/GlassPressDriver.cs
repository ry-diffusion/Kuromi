using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

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

    private DateTime _lastTick;
    private int? _activePointerId;
    private TopLevel? _hooked;

    public GlassPressDriver(Control owner, Func<bool> enabled, Func<double> maxScaleDip)
    {
        _owner = owner;
        _enabled = enabled;
        _maxScaleDip = maxScaleDip;

        _owner.RenderTransformOrigin = RelativePoint.Center;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;

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
            _timer.Stop();
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
        Hook();
        Apply();
        _owner.InvalidateVisual();
        Start();
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
        _activePointerId = null;
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
    }

    private void Unhook()
    {
        if (_hooked is null)
            return;
        _hooked.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _hooked.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _hooked.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        _hooked = null;
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
        Unhook();
        _timer.Stop();
    }
}
