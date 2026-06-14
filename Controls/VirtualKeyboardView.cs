using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Kuromi.Glass;

namespace Kuromi.Controls;

/// <summary>
/// A self-contained on-screen QWERTY keyboard for touch input (no external OSK
/// needed). Keys are non-focusable so the target <see cref="TextBox"/> keeps focus
/// while typing. Inserts/edits text at the target's caret.
/// </summary>
public class VirtualKeyboardView : UserControl
{
    private TextBox? _target;
    private bool _shift;
    private readonly List<Button> _letterKeys = new();
    private readonly List<LucideIcon> _icons = new();
    private readonly List<GlassButton> _keys = new();

    public event Action? CloseRequested;

    public VirtualKeyboardView()
    {
        Focusable = false;

        var rows = new StackPanel { Spacing = 6 };
        rows.Children.Add(CharRow("1234567890"));
        rows.Children.Add(CharRow("qwertyuiop"));
        rows.Children.Add(CharRow("asdfghjkl"));
        rows.Children.Add(BottomLetterRow());
        rows.Children.Add(ActionRow());

        var border = new Border
        {
            CornerRadius = new CornerRadius(22, 22, 0, 0),
            Padding = new Thickness(12),
            Child = rows,
        };
        border.Classes.Add("card");
        Content = border;

        // Resolve theme-dependent icon colors + key frosting once we're in the tree.
        AttachedToVisualTree += (_, _) =>
        {
            if (this.TryFindResource("TextPrimary", out var v) && v is IBrush b)
                foreach (var i in _icons) i.Brush = b;
            ApplyKeyFrost();
        };
        ActualThemeVariantChanged += (_, _) => ApplyKeyFrost();
    }

    // Frost the glass keys so they read as solid keys over the busy dashboard behind them
    // (dark, light-text keys in dark mode; light, dark-text keys in light mode).
    private void ApplyKeyFrost()
    {
        Color frost = ActualThemeVariant == ThemeVariant.Dark
            ? Color.FromArgb(150, 18, 14, 26)
            : Color.FromArgb(165, 255, 255, 255);
        foreach (var k in _keys)
            k.SurfaceColor = frost;
    }

    public void SetTarget(TextBox? tb) => _target = tb;

    // ---------------- rows ----------------

    private Control CharRow(string chars)
    {
        var grid = new UniformGrid { Columns = chars.Length, Height = 50 };
        foreach (var c in chars)
        {
            var b = Key(c.ToString());
            if (char.IsLetter(c)) _letterKeys.Add(b);
            b.Click += (_, _) => Insert((string)b.Content!);
            grid.Children.Add(b);
        }
        return grid;
    }

    private Control BottomLetterRow()
    {
        var grid = new UniformGrid { Columns = 9, Height = 50 };

        var shift = IconKey("arrow-up");
        shift.Click += (_, _) => ToggleShift();
        grid.Children.Add(shift);

        foreach (var c in "zxcvbnm")
        {
            var b = Key(c.ToString());
            _letterKeys.Add(b);
            b.Click += (_, _) => Insert((string)b.Content!);
            grid.Children.Add(b);
        }

        var back = IconKey("delete");
        back.Click += (_, _) => Backspace();
        grid.Children.Add(back);

        return grid;
    }

    private Control ActionRow()
    {
        var grid = new Grid
        {
            Height = 50,
            ColumnDefinitions = new ColumnDefinitions("58,58,58,*,72,72"),
        };

        void Add(Control c, int col) { Grid.SetColumn(c, col); grid.Children.Add(c); }

        var dot = Key("."); dot.Click += (_, _) => Insert(".");
        var at = Key("@"); at.Click += (_, _) => Insert("@");
        var dash = Key("-"); dash.Click += (_, _) => Insert("-");
        var space = Key("espaço"); space.FontSize = 14; space.Click += (_, _) => Insert(" ");
        var enter = IconKey("corner-down-left"); enter.Click += (_, _) => CloseRequested?.Invoke();
        var close = IconKey("x"); close.Click += (_, _) => CloseRequested?.Invoke();

        Add(dot, 0); Add(at, 1); Add(dash, 2); Add(space, 3); Add(enter, 4); Add(close, 5);
        return grid;
    }

    // ---------------- keys ----------------

    private Button Key(string label)
    {
        var b = new GlassButton
        {
            Content = label,
            Classes = { "key" },
            Focusable = false,
            ShadowEnabled = false,
            BlurRadius = 6,
            InteractiveMaxScaleDip = 2,
        };
        _keys.Add(b);
        return b;
    }

    private Button IconKey(string icon)
    {
        var ic = new LucideIcon { Kind = icon, Width = 20, Height = 20, Brush = Brushes.White };
        _icons.Add(ic);
        var b = new GlassButton
        {
            Content = ic,
            Classes = { "key" },
            Focusable = false,
            ShadowEnabled = false,
            BlurRadius = 6,
            InteractiveMaxScaleDip = 2,
        };
        _keys.Add(b);
        return b;
    }

    // ---------------- editing ----------------

    private void ToggleShift()
    {
        _shift = !_shift;
        foreach (var b in _letterKeys)
        {
            var s = (string)b.Content!;
            b.Content = _shift ? s.ToUpperInvariant() : s.ToLowerInvariant();
        }
    }

    private void Insert(string s)
    {
        if (_target == null) return;
        var text = _target.Text ?? "";
        var i = Math.Clamp(_target.CaretIndex, 0, text.Length);
        _target.Text = text.Insert(i, s);
        _target.CaretIndex = i + s.Length;

        if (_shift && s.Length == 1 && char.IsLetter(s[0])) ToggleShift(); // one-shot shift
    }

    private void Backspace()
    {
        if (_target == null) return;
        var text = _target.Text ?? "";
        var i = Math.Clamp(_target.CaretIndex, 0, text.Length);
        if (i > 0)
        {
            _target.Text = text.Remove(i - 1, 1);
            _target.CaretIndex = i - 1;
        }
    }
}
