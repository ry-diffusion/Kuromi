using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kuromi.Glass;

/// <summary>
/// A liquid-glass <see cref="Slider"/> (horizontal): a slim track line with a coloured fill, and a
/// <see cref="GlassKnob"/> handle (opaque oval at rest, clear glass pill that refracts the backdrop while
/// dragged). Built on Avalonia's <see cref="Track"/> for drag / click-to-seek.
/// </summary>
public class GlassSlider : Slider
{
    private const double TrackThickness = 6;
    private const double KnobRestWidth = 32;
    private const double KnobRestHeight = 18;
    private const double PopWidth = 46;
    private const double PopHeight = 28;

    private Border? _fill;
    private GlassKnob? _knob;

    static GlassSlider()
    {
        TemplateProperty.OverrideDefaultValue<GlassSlider>(CreateTemplate());
    }

    public GlassSlider()
    {
        // Tunnel handlers fire before the thumb captures the pointer, so the knob reacts to ANY press.
        AddHandler(PointerPressedEvent, (_, _) => SetGrabbed(true), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => SetGrabbed(false), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _fill = e.NameScope.Find<Border>("PART_Fill");
        UpdateFill();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        SetGrabbed(false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);
        UpdateFill(size.Width);
        return size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty
            || change.Property == MaximumProperty || change.Property == ForegroundProperty)
            UpdateFill();
    }

    private void SetGrabbed(bool grabbed)
    {
        if (_knob is not null)
            _knob.Pressed = grabbed;
    }

    private void UpdateFill(double? width = null)
    {
        if (_fill is null)
            return;

        _fill.Background = Foreground;

        double w = width ?? Bounds.Width;
        double range = Maximum - Minimum;
        double f = range > 0 ? Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
        if (w <= 0)
            return;

        // Fill from the left up to the knob centre (the thumb is PopWidth wide).
        _fill.Width = Math.Max(0, f * (w - PopWidth) + PopWidth / 2);
    }

    private static FuncControlTemplate CreateTemplate() =>
        new FuncControlTemplate<GlassSlider>((slider, ns) =>
        {
            Border groove = new Border
            {
                Height = TrackThickness,
                CornerRadius = new CornerRadius(TrackThickness / 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            };

            Border fill = new Border
            {
                Name = "PART_Fill",
                Height = TrackThickness,
                CornerRadius = new CornerRadius(TrackThickness / 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            }.RegisterInNameScope(ns);

            Thumb thumb = new Thumb
            {
                Name = "PART_Thumb",
                Width = PopWidth,
                Height = PopHeight,
                Template = new FuncControlTemplate<Thumb>((_, _) =>
                {
                    GlassKnob knob = new GlassKnob(KnobRestWidth, KnobRestHeight, PopWidth, PopHeight);
                    slider._knob = knob;
                    knob.FloatLens = true; // the dragged lens floats above all cards (iOS magnifier), escaping the card
                    return new Border { Background = Brushes.Transparent, Child = knob };
                }),
            }.RegisterInNameScope(ns);

            Track track = new Track
            {
                Name = "PART_Track",
                DecreaseButton = TransparentButton("PART_DecreaseButton", ns),
                IncreaseButton = TransparentButton("PART_IncreaseButton", ns),
                Thumb = thumb,
                [!Track.MinimumProperty] = new TemplateBinding(RangeBase.MinimumProperty),
                [!Track.MaximumProperty] = new TemplateBinding(RangeBase.MaximumProperty),
                [!Track.ValueProperty] = new TemplateBinding(RangeBase.ValueProperty) { Mode = BindingMode.TwoWay },
                [!Track.OrientationProperty] = new TemplateBinding(Slider.OrientationProperty),
                [!Track.IsDirectionReversedProperty] = new TemplateBinding(Slider.IsDirectionReversedProperty),
            }.RegisterInNameScope(ns);

            Grid root = new Grid { MinHeight = PopHeight };
            root.Children.Add(groove);
            root.Children.Add(fill);
            root.Children.Add(track);
            return root;
        });

    private static RepeatButton TransparentButton(string name, INameScope ns) =>
        new RepeatButton
        {
            Name = name,
            Focusable = false,
            Background = Brushes.Transparent,
            Template = new FuncControlTemplate<RepeatButton>((_, _) => new Border { Background = Brushes.Transparent }),
        }.RegisterInNameScope(ns);
}
