using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Kuromi.Glass;

/// <summary>
/// A liquid-glass switch: a <see cref="GlassKnob"/> slides along a pill track that tints with the accent
/// colour when on. The knob shares the slider's behaviour — opaque rounded handle at rest, growing into
/// a clear refracting glass lens while pressed. Derives from <see cref="ToggleButton"/>.
/// </summary>
public class GlassSwitch : ToggleButton
{
    public static readonly StyledProperty<object?> OnContentProperty =
        AvaloniaProperty.Register<GlassSwitch, object?>(nameof(OnContent), "On");

    public static readonly StyledProperty<object?> OffContentProperty =
        AvaloniaProperty.Register<GlassSwitch, object?>(nameof(OffContent), "Off");

    private const double PillWidth = 76;
    private const double PillHeight = 34;
    private const double Pad = 3;
    private const double KnobRestW = 42;
    private const double KnobRestH = 28;
    private const double KnobPressW = 52;
    private const double KnobPressH = 34;

    // The GlassKnob footprint is the pressed size; the resting knob sits centred inside it.
    private static readonly double FootInset = (KnobPressW - KnobRestW) / 2;
    private static readonly double KnobOff = Pad - FootInset;
    private static readonly double KnobOn = PillWidth - Pad - KnobRestW - FootInset;
    private static readonly double FootTop = (PillHeight - KnobPressH) / 2;

    private Border? _track;
    private GlassKnob? _knob;
    private ContentPresenter? _content;

    static GlassSwitch()
    {
        ClipToBoundsProperty.OverrideDefaultValue<GlassSwitch>(false);
        TemplateProperty.OverrideDefaultValue<GlassSwitch>(CreateTemplate());
    }

    public GlassSwitch()
    {
        AddHandler(PointerPressedEvent, (_, _) => SetPressed(true), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => SetPressed(false), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public object? OnContent { get => GetValue(OnContentProperty); set => SetValue(OnContentProperty, value); }
    public object? OffContent { get => GetValue(OffContentProperty); set => SetValue(OffContentProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<Border>("PART_Track");
        _content = e.NameScope.Find<ContentPresenter>("PART_Content");

        if (_knob is not null)
            _knob.Transitions = new Transitions
            {
                new DoubleTransition { Property = Canvas.LeftProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
            };
        if (_track is not null)
            _track.Transitions = new Transitions
            {
                new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(180) },
            };

        UpdateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        SetPressed(false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty
            || change.Property == OnContentProperty
            || change.Property == OffContentProperty)
            UpdateVisual();
    }

    private void SetPressed(bool pressed)
    {
        if (_knob is not null)
            _knob.Pressed = pressed;
    }

    private void UpdateVisual()
    {
        bool on = IsChecked == true;

        if (_knob is not null)
            Canvas.SetLeft(_knob, on ? KnobOn : KnobOff);

        // DynamicResource so the track tracks the live accent (updated from the wallpaper at runtime).
        if (_track is not null)
            _track[!Border.BackgroundProperty] = new DynamicResourceExtension(on ? "AccentBrush" : "ControlFill");

        if (_content is not null)
            _content.Content = on ? OnContent : OffContent;
    }

    private static FuncControlTemplate CreateTemplate() =>
        new FuncControlTemplate<GlassSwitch>((sw, ns) =>
        {
            Border track = new Border
            {
                Name = "PART_Track",
                Width = PillWidth,
                Height = PillHeight,
                CornerRadius = new CornerRadius(PillHeight / 2), // full pill
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            }.RegisterInNameScope(ns);

            GlassKnob knob = new GlassKnob(KnobRestW, KnobRestH, KnobPressW, KnobPressH);
            sw._knob = knob;
            knob.ElevationTarget = sw; // raise the whole switch so the popped knob clears its neighbours
            knob.PopScale = 1.18;      // gentler than the slider — the pill track is short
            Canvas.SetLeft(knob, KnobOff);
            Canvas.SetTop(knob, FootTop);

            Canvas pill = new Canvas { Width = PillWidth, Height = PillHeight, VerticalAlignment = VerticalAlignment.Center };
            pill.Children.Add(track);
            pill.Children.Add(knob);

            ContentPresenter content = new ContentPresenter
            {
                Name = "PART_Content",
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                [!ContentPresenter.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
            }.RegisterInNameScope(ns);

            Grid root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = Brushes.Transparent };
            Grid.SetColumn(content, 1);
            root.Children.Add(pill);
            root.Children.Add(content);
            return root;
        });
}
