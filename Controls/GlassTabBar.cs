using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Kuromi.Glass;

namespace Kuromi.Controls;

/// <summary>One tab of a <see cref="GlassTabBar"/>: a Lucide icon kind + a label.</summary>
public class GlassTabItem : AvaloniaObject
{
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<GlassTabItem, string>(nameof(Icon), "");
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<GlassTabItem, string>(nameof(Label), "");

    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
}

/// <summary>
/// A liquid-glass bottom tab bar (port of Kyant0's LiquidBottomTabs): a floating glass pill holding N
/// tabs (icon + label), with an accent-tinted glass indicator that spring-slides to the selected tab and
/// scales up while pressed. <see cref="SelectedIndex"/> is two-way bindable. Declare tabs as content:
/// <code>
/// &lt;controls:GlassTabBar SelectedIndex="{Binding Tab}"&gt;
///   &lt;controls:GlassTabBar.Tabs&gt;
///     &lt;controls:GlassTabItem Icon="layout-grid" Label="Painel"/&gt;
///     &lt;controls:GlassTabItem Icon="settings"    Label="Ajustes"/&gt;
///   &lt;/controls:GlassTabBar.Tabs&gt;
/// &lt;/controls:GlassTabBar&gt;
/// </code>
/// </summary>
public class GlassTabBar : Decorator
{
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<GlassTabBar, int>(nameof(SelectedIndex), 0, defaultBindingMode: BindingMode.TwoWay);

    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    /// <summary>The tabs. Fill via property-element syntax (see the class summary).</summary>
    public AvaloniaList<GlassTabItem> Tabs { get; } = new();

    private const double PillHeight = 62;
    private const double TabWidth = 104;
    private const double HMargin = 5;
    private const double VMargin = 5;
    private const double IndicatorHeight = PillHeight - 2 * VMargin;

    private readonly Canvas _indicatorLayer = new();
    private readonly GlassSurface _indicator;
    private readonly ScaleTransform _indicatorScale = new(1, 1);
    private readonly Border _highlight;
    private readonly Grid _tabRow = new();
    private readonly List<LucideIcon> _icons = new();
    private readonly List<TextBlock> _labels = new();

    public GlassTabBar()
    {
        _indicator = new GlassSurface
        {
            Height = IndicatorHeight,
            CornerRadius = new CornerRadius(IndicatorHeight / 2),
            RefractionHeight = 16,
            RefractionAmount = 30,
            BlurRadius = 1,
            DepthEffect = true,
            ChromaticAberration = true,
            Vibrancy = 1.25,
            HighlightOpacity = 0.6,
            ShadowRadius = 10,
            ShadowOffset = new Vector(0, 2),
            ShadowColor = Color.FromArgb(70, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            RenderTransform = _indicatorScale,
            RenderTransformOrigin = RelativePoint.Center,
            Transitions = new Transitions
            {
                // Smooth eased slide between tabs (matches the switch's CubicEaseOut, no overshoot).
                new DoubleTransition { Property = Canvas.LeftProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
            },
        };
        _indicator[!GlassSurface.SurfaceColorProperty] = new DynamicResourceExtension("AccentColor");

        // White press flash layered over the accent indicator (the shared "press" feedback).
        _highlight = new Border
        {
            Background = Brushes.White,
            Opacity = 0,
            CornerRadius = new CornerRadius(IndicatorHeight / 2),
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut() },
            },
        };
        _indicator.Content = _highlight;

        _indicatorScale.Transitions = new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() },
        };
        _indicatorLayer.Children.Add(_indicator);

        GlassSurface pill = new()
        {
            Height = PillHeight,
            CornerRadius = new CornerRadius(PillHeight / 2),
            Padding = new Thickness(0),
            RefractionHeight = 22,
            RefractionAmount = 40,
            BlurRadius = 8,
            DepthEffect = true,
            ChromaticAberration = true,
            Vibrancy = 1.2,
            HighlightOpacity = 0.5,
            ShadowRadius = 24,
            ShadowOffset = new Vector(0, 10),
            ShadowColor = Color.FromArgb(90, 0, 0, 0),
        };
        pill[!GlassSurface.SurfaceColorProperty] = new DynamicResourceExtension("GlassSurfaceColor");

        Grid root = new();
        root.Children.Add(_indicatorLayer);
        root.Children.Add(_tabRow);
        pill.Content = root;

        Child = pill;

        Tabs.CollectionChanged += (_, _) => RebuildTabs();
        AddHandler(PointerReleasedEvent, (_, _) => ReleasePress(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => ReleasePress(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_tabRow.Children.Count != Tabs.Count)
            RebuildTabs();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty)
        {
            PositionIndicator();
            UpdateSelectionVisuals();
        }
    }

    private void Press(int idx)
    {
        SelectedIndex = idx; // react on press-down so the indicator starts sliding immediately
        _indicatorScale.ScaleX = _indicatorScale.ScaleY = 1.12;
        _highlight.Opacity = 0.22;
    }

    private void ReleasePress()
    {
        _indicatorScale.ScaleX = _indicatorScale.ScaleY = 1.0;
        _highlight.Opacity = 0.0;
    }

    private void RebuildTabs()
    {
        _tabRow.Children.Clear();
        _tabRow.ColumnDefinitions.Clear();
        _icons.Clear();
        _labels.Clear();

        for (int i = 0; i < Tabs.Count; i++)
        {
            _tabRow.ColumnDefinitions.Add(new ColumnDefinition(TabWidth, GridUnitType.Pixel));

            LucideIcon icon = new() { Kind = Tabs[i].Icon, Width = 22, Height = 22 };
            TextBlock label = new()
            {
                Text = Tabs[i].Label,
                FontSize = 11.5,
                FontWeight = FontWeight.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _icons.Add(icon);
            _labels.Add(label);

            StackPanel stack = new()
            {
                Orientation = Orientation.Vertical,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stack.Children.Add(icon);
            stack.Children.Add(label);

            Border button = new()
            {
                Background = Brushes.Transparent, // hit-testable
                Child = stack,
                Width = TabWidth,
                Height = PillHeight,
            };
            int idx = i;
            button.PointerPressed += (_, _) => Press(idx);
            Grid.SetColumn(button, i);
            _tabRow.Children.Add(button);
        }

        _indicator.Width = TabWidth - 2 * HMargin;
        Canvas.SetTop(_indicator, VMargin);
        PositionIndicator();
        UpdateSelectionVisuals();
    }

    private void PositionIndicator()
    {
        if (Tabs.Count == 0)
            return;
        int i = Math.Clamp(SelectedIndex, 0, Tabs.Count - 1);
        Canvas.SetLeft(_indicator, i * TabWidth + HMargin);
    }

    private void UpdateSelectionVisuals()
    {
        if (_icons.Count == 0)
            return;
        int sel = Math.Clamp(SelectedIndex, 0, _icons.Count - 1);
        for (int i = 0; i < _icons.Count; i++)
        {
            bool on = i == sel;
            _icons[i][!LucideIcon.BrushProperty] = new DynamicResourceExtension(on ? "OnAccentBrush" : "TextMuted");
            _labels[i][!TextBlock.ForegroundProperty] = new DynamicResourceExtension(on ? "OnAccentBrush" : "TextMuted");
            _labels[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium;
        }
    }
}
