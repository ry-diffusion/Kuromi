using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kuromi.Glass;
using Kuromi.ViewModels;

namespace Kuromi.Views;

public partial class SpotifyView : UserControl
{
    private SpotifyViewModel? _vm;
    private ScrollViewer? _scroll;
    private ItemsControl? _items;

    // Eased auto-scroll so the active lyric stays centred (only runs while following, not on manual scroll).
    private readonly DispatcherTimer _scrollAnim;
    private double _from, _to, _t;

    public SpotifyView()
    {
        InitializeComponent();
        _scrollAnim = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollAnim.Tick += OnScrollTick;
        DataContextChanged += (_, _) => Hook();

        // Pause progress polling while the user scrubs the seek bar; seek on release.
        var seek = this.FindControl<GlassSlider>("SeekSlider");
        if (seek is not null)
        {
            seek.AddHandler(PointerPressedEvent, (_, _) => _vm?.BeginScrub(), RoutingStrategies.Tunnel, handledEventsToo: true);
            seek.AddHandler(PointerReleasedEvent, (_, _) => _vm?.EndScrub(), RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    private void Hook()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as SpotifyViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpotifyViewModel.ActiveLyric))
            Dispatcher.UIThread.Post(ScrollToActive, DispatcherPriority.Background);
    }

    private void ScrollToActive()
    {
        _scroll ??= this.FindControl<ScrollViewer>("LyricsScroll");
        _items ??= this.FindControl<ItemsControl>("LyricsItems");
        var item = _vm?.ActiveLyric;
        if (_scroll is null || _items is null || item is null)
            return;

        int idx = _vm!.Lyrics.IndexOf(item);
        if (idx < 0)
            return;
        if (_items.ContainerFromIndex(idx) is not Control container)
            return;

        double target = container.Bounds.Y + container.Bounds.Height / 2 - _scroll.Viewport.Height / 2;
        double max = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
        AnimateTo(Math.Clamp(target, 0, max));
    }

    private void AnimateTo(double target)
    {
        if (_scroll is null)
            return;
        _from = _scroll.Offset.Y;
        _to = target;
        _t = 0;
        if (!_scrollAnim.IsEnabled)
            _scrollAnim.Start();
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (_scroll is null)
        {
            _scrollAnim.Stop();
            return;
        }
        _t = Math.Min(1, _t + 0.07);
        double eased = 1 - Math.Pow(1 - _t, 3); // cubic ease-out
        _scroll.Offset = new Vector(_scroll.Offset.X, _from + (_to - _from) * eased);
        if (_t >= 1)
            _scrollAnim.Stop();
    }
}
