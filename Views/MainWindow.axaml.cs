using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Kuromi.Controls;
using Kuromi.ViewModels;

namespace Kuromi.Views;

public partial class MainWindow : Window
{
    private bool _fullscreen;
    private bool _lastTouch;

    // Widget drag/resize state (grid cells)
    private WidgetViewModel? _active;
    private bool _resizing;
    private Point _startPointer;
    private int _origCol, _origRow, _origColSpan, _origRowSpan;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnGlobalPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnGlobalPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        KeyDown += OnKeyDown;

        if (Vkbd != null)
            Vkbd.CloseRequested += () =>
            {
                if (Vm != null) Vm.KeyboardVisible = false;
                ClearFocus();
            };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.FullscreenToggleRequested -= ToggleFullscreen;
                vm.FullscreenToggleRequested += ToggleFullscreen;
                vm.ExitRequested -= OnExitRequested;
                vm.ExitRequested += OnExitRequested;
            }
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;
    private ItemsControl? Host => this.FindControl<ItemsControl>("WidgetHost");

    // Show the on-screen keyboard when a text field is focused via touch.
    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;

        if (e.Source is TextBox tb)
        {
            Vkbd?.SetTarget(tb);
            if (_lastTouch) vm.KeyboardVisible = true;
        }
        else
        {
            vm.KeyboardVisible = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_fullscreen) ToggleFullscreen();
                else Close();
                e.Handled = true;
                break;
        }
    }

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        WindowState = _fullscreen ? WindowState.FullScreen : WindowState.Normal;
    }

    private void OnExitRequested() => Close();

    // ---------------- Pointer handling ----------------

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _lastTouch = e.Pointer.Type == PointerType.Touch;
        if (e.Source is not Control src) return;

        // Tapping outside the text field and the keyboard dismisses + unfocuses it,
        // so the next tap lands on whatever you clicked (no lingering caret).
        if (Vm?.KeyboardVisible == true && !IsWithin<TextBox>(src) && !IsWithin<VirtualKeyboardView>(src))
        {
            Vm.KeyboardVisible = false;
            ClearFocus();
        }

        // The control strip doubles as the window-move handle.
        if (IsWithin(src, "TopBar") && !IsWithinButton(src))
        {
            BeginMoveDrag(e);
            return;
        }

        var vm = Vm;
        if (vm is null || !vm.EditMode) return;
        if (IsWithinButton(src)) return; // let the remove button click through

        var (mode, widget) = HitTest(src);
        if (mode is null || widget is null) return;

        _active = widget;
        _resizing = mode == "resize";
        _startPointer = e.GetPosition(Host);
        _origCol = widget.Col; _origRow = widget.Row;
        _origColSpan = widget.ColSpan; _origRowSpan = widget.RowSpan;
        e.Pointer.Capture((Control)e.Source);
        e.Handled = true;
    }

    private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        // Reveal the control strip when the pointer reaches the top edge.
        var vm = Vm;
        if (vm is not null && _active is null)
        {
            var y = e.GetPosition(this).Y;
            if (y <= 6) vm.ShowControls = true;
            else if (y > 110 && !vm.EditMode) vm.ShowControls = false;
        }

        if (_active is null || vm is null) return;
        var host = Host;
        if (host is null || host.Bounds.Width <= 0 || host.Bounds.Height <= 0) return;

        int cols = vm.Dashboard.GridColumns, rows = vm.Dashboard.GridRows;
        double cellW = host.Bounds.Width / cols;
        double cellH = host.Bounds.Height / rows;

        var p = e.GetPosition(host);
        int dCol = (int)Math.Round((p.X - _startPointer.X) / cellW);
        int dRow = (int)Math.Round((p.Y - _startPointer.Y) / cellH);

        if (_resizing)
        {
            _active.ColSpan = Math.Clamp(_origColSpan + dCol, 1, cols - _active.Col);
            _active.RowSpan = Math.Clamp(_origRowSpan + dRow, 1, rows - _active.Row);
        }
        else
        {
            _active.Col = Math.Clamp(_origCol + dCol, 0, cols - _active.ColSpan);
            _active.Row = Math.Clamp(_origRow + dRow, 0, rows - _active.RowSpan);
        }
        e.Handled = true;
    }

    private void OnGlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_active is null) return;
        _active = null;
        e.Pointer.Capture(null);
        Vm?.Dashboard.Persist();
        e.Handled = true;
    }

    // ---------------- helpers ----------------

    private static (string? mode, WidgetViewModel? widget) HitTest(Control src)
    {
        string? mode = null;
        WidgetViewModel? widget = null;
        Visual? v = src;
        while (v != null)
        {
            if (v is Control c)
            {
                if (mode is null && c.Tag is string tag && tag is "drag" or "resize")
                    mode = tag;
                if (widget is null && c.DataContext is WidgetViewModel w)
                    widget = w;
            }
            if (mode != null && widget != null) break;
            v = v.GetVisualParent();
        }
        return (mode, widget);
    }

    private void ClearFocus()
    {
        // Move focus to an invisible sink so the text field loses the caret.
        this.FindControl<Border>("FocusSink")?.Focus();
    }

    private static bool IsWithin<T>(Control src) where T : Control
    {
        Visual? v = src;
        while (v != null)
        {
            if (v is T) return true;
            v = v.GetVisualParent();
        }
        return false;
    }

    private static bool IsWithin(Control src, string name)
    {
        Visual? v = src;
        while (v != null)
        {
            if (v is Control c && c.Name == name) return true;
            v = v.GetVisualParent();
        }
        return false;
    }

    private static bool IsWithinButton(Control src)
    {
        Visual? v = src;
        while (v != null)
        {
            if (v is Button) return true;
            v = v.GetVisualParent();
        }
        return false;
    }
}
