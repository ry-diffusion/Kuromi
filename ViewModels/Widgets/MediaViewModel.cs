using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Services;

namespace Kuromi.ViewModels.Widgets;

public partial class MediaViewModel : ViewModelBase, IDisposable
{
    private readonly MediaService _service;
    private readonly IDisposable? _watcher;
    private readonly DispatcherTimer _debounce;

    [ObservableProperty] private bool _available;
    [ObservableProperty] private bool _hasPlayer;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private string? _artPath;

    public bool HasArt => !string.IsNullOrEmpty(ArtPath);
    partial void OnArtPathChanged(string? value) => OnPropertyChanged(nameof(HasArt));

    /// <summary>Accent + on-accent sampled from the album art (drive the play button).</summary>
    [ObservableProperty] private IBrush _accent = new SolidColorBrush(Color.Parse("#FF7AB6"));
    [ObservableProperty] private IBrush _accentForeground = new SolidColorBrush(Color.Parse("#1A1622"));

    public MediaViewModel(MediaService service)
    {
        _service = service;
        Available = _service.Available;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RefreshAsync(); };

        if (Available)
        {
            _ = RefreshAsync();
            _watcher = _service.Watch(() => { _debounce.Stop(); _debounce.Start(); });
        }
    }

    private async Task RefreshAsync()
    {
        var m = await _service.GetAsync();
        HasPlayer = m.HasPlayer;
        Title = m.Title;
        Artist = m.Artist;
        Playing = m.Playing;
        ArtPath = m.ArtPath;

        // Pull a Material You accent from the album art.
        if (m.ArtPath != null)
        {
            var dark = Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            var p = await Task.Run(() => Services.MaterialPalette.FromImage(m.ArtPath, dark));
            if (p is { } pal)
            {
                Accent = new SolidColorBrush(pal.Accent);
                AccentForeground = new SolidColorBrush(pal.OnAccent);
            }
        }
    }

    [RelayCommand] private Task PlayPause() => _service.PlayPauseAsync();
    [RelayCommand] private Task Next() => _service.NextAsync();
    [RelayCommand] private Task Previous() => _service.PreviousAsync();

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
    }
}
