using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Services;
using SpotifyAPI.Web;

namespace Kuromi.ViewModels;

public class SpotifyTrackViewModel(string title, string artist) : ViewModelBase
{
    public string Title { get; } = title;
    public string Artist { get; } = artist;
}

public partial class SpotifyPlaylistViewModel : ViewModelBase
{
    public string Name { get; }
    public string Owner { get; }
    public string Uri { get; }
    [ObservableProperty] private Bitmap? _art;

    public SpotifyPlaylistViewModel(string name, string owner, string uri, string? artUrl)
    {
        Name = name;
        Owner = owner;
        Uri = uri;
        if (!string.IsNullOrEmpty(artUrl))
            _ = LoadAsync(artUrl);
    }

    private async Task LoadAsync(string url)
    {
        var b = await ImageCache.GetAsync(url);
        if (b is not null)
            Dispatcher.UIThread.Post(() => Art = b);
    }
}

public partial class SpotifyDeviceViewModel(string id, string name, string type, bool isActive) : ViewModelBase
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string TypeText { get; } = type;
    [ObservableProperty] private bool _isActive = isActive;
}

public partial class LyricLineViewModel(int timeMs, string text) : ViewModelBase
{
    public int TimeMs { get; } = timeMs;
    public string Text { get; } = text;
    [ObservableProperty] private bool _active;
}

/// <summary>
/// The Spotify panel: PKCE sign-in, now-playing (art/title/artist/progress), transport controls, device
/// picker, queue, playlists and time-synced lyrics. Polls playback once a second while active.
/// </summary>
public partial class SpotifyViewModel : ViewModelBase, IDisposable
{
    private readonly SpotifyService _spotify;
    private readonly LyricsService _lyrics;
    private readonly DispatcherTimer _timer;

    private string? _currentTrackId;
    private int _durationMs;
    private bool _loadingLyrics;

    [ObservableProperty] private bool _hasClientId;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private bool _connecting;

    [ObservableProperty] private bool _hasTrack;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private Bitmap? _art;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private string _deviceName = "";

    [ObservableProperty] private bool _hasLyrics;
    [ObservableProperty] private bool _lyricsSynced;
    [ObservableProperty] private LyricLineViewModel? _activeLyric;

    public ObservableCollection<SpotifyTrackViewModel> Queue { get; } = new();
    public ObservableCollection<SpotifyPlaylistViewModel> Playlists { get; } = new();
    public ObservableCollection<SpotifyDeviceViewModel> Devices { get; } = new();
    public ObservableCollection<LyricLineViewModel> Lyrics { get; } = new();

    public SpotifyViewModel(SpotifyService spotify, LyricsService lyrics)
    {
        _spotify = spotify;
        _lyrics = lyrics;
        _spotify.StateChanged += OnStateChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await PollAsync();
        HasClientId = _spotify.HasClientId;
        IsAuthenticated = _spotify.IsAuthenticated;
    }

    /// <summary>Called when the Spotify tab becomes visible.</summary>
    public void Activate()
    {
        HasClientId = _spotify.HasClientId;
        IsAuthenticated = _spotify.IsAuthenticated;
        if (IsAuthenticated)
        {
            _timer.Start();
            _ = RefreshLibraryAsync();
            _ = PollAsync();
        }
    }

    public void Deactivate() => _timer.Stop();

    /// <summary>Re-read whether a Client ID is configured (after it changes in Ajustes).</summary>
    public void RefreshClientId()
    {
        HasClientId = _spotify.HasClientId;
        if (!HasClientId)
            IsAuthenticated = false;
    }

    private void OnStateChanged() => Dispatcher.UIThread.Post(() =>
    {
        IsAuthenticated = _spotify.IsAuthenticated;
        HasClientId = _spotify.HasClientId;
        if (IsAuthenticated)
        {
            _timer.Start();
            _ = RefreshLibraryAsync();
            _ = PollAsync();
        }
        else
        {
            _timer.Stop();
            HasTrack = false;
        }
    });

    [RelayCommand]
    private async Task Connect()
    {
        if (Connecting || !_spotify.HasClientId)
            return;
        Connecting = true;
        try { await _spotify.LoginAsync(); }
        catch { /* cancelled / failed */ }
        finally { Connecting = false; }
    }

    [RelayCommand]
    private void Disconnect() => _spotify.Logout();

    [RelayCommand]
    private async Task PlayPause()
    {
        await _spotify.PlayPauseAsync(IsPlaying);
        IsPlaying = !IsPlaying;
        await DelayedPoll();
    }

    [RelayCommand]
    private async Task Next()
    {
        await _spotify.NextAsync();
        await DelayedPoll();
    }

    [RelayCommand]
    private async Task Previous()
    {
        await _spotify.PreviousAsync();
        await DelayedPoll();
    }

    [RelayCommand]
    private async Task PlayPlaylist(SpotifyPlaylistViewModel? p)
    {
        if (p is null) return;
        await _spotify.PlayContextAsync(p.Uri);
        await DelayedPoll();
    }

    [RelayCommand]
    private async Task SelectDevice(SpotifyDeviceViewModel? d)
    {
        if (d is null) return;
        await _spotify.TransferAsync(d.Id);
        await DelayedPoll();
        await RefreshDevicesAsync();
    }

    [RelayCommand]
    private Task Refresh() => RefreshLibraryAsync();

    private async Task DelayedPoll()
    {
        await Task.Delay(350);
        await PollAsync();
    }

    private async Task PollAsync()
    {
        if (!_spotify.IsAuthenticated)
            return;

        var pb = await _spotify.GetPlaybackAsync();
        if (pb?.Item is FullTrack t)
        {
            HasTrack = true;
            Title = t.Name;
            Artist = string.Join(", ", t.Artists.Select(a => a.Name));
            IsPlaying = pb.IsPlaying;
            DeviceName = pb.Device?.Name ?? "";
            _durationMs = t.DurationMs;
            UpdateProgress(pb.ProgressMs);

            if (t.Id != _currentTrackId)
            {
                _currentTrackId = t.Id;
                _ = LoadArtAsync(t.Album?.Images?.FirstOrDefault()?.Url);
                _ = LoadLyricsAsync(t);
                _ = RefreshQueueAsync();
            }
        }
        else
        {
            HasTrack = false;
        }
    }

    private void UpdateProgress(int posMs)
    {
        Progress = _durationMs > 0 ? Math.Clamp((double)posMs / _durationMs, 0, 1) : 0;
        PositionText = Fmt(posMs);
        DurationText = Fmt(_durationMs);
        SyncLyrics(posMs);
    }

    private void SyncLyrics(int posMs)
    {
        if (!LyricsSynced || Lyrics.Count == 0)
            return;
        LyricLineViewModel? active = null;
        foreach (var l in Lyrics)
        {
            if (l.TimeMs <= posMs) active = l;
            else break;
        }
        if (ReferenceEquals(active, ActiveLyric))
            return;
        if (ActiveLyric is not null) ActiveLyric.Active = false;
        ActiveLyric = active;
        if (active is not null) active.Active = true;
    }

    private async Task LoadArtAsync(string? url)
    {
        var b = await ImageCache.GetAsync(url);
        if (b is not null)
            Art = b;
    }

    private async Task LoadLyricsAsync(FullTrack t)
    {
        if (_loadingLyrics) return;
        _loadingLyrics = true;
        Lyrics.Clear();
        HasLyrics = false;
        LyricsSynced = false;
        ActiveLyric = null;
        try
        {
            var res = await _lyrics.GetAsync(
                t.Name, t.Artists.FirstOrDefault()?.Name ?? "", t.Album?.Name ?? "", t.DurationMs / 1000);
            if (res is not null && res.Lines.Count > 0)
            {
                foreach (var line in res.Lines)
                    Lyrics.Add(new LyricLineViewModel(line.TimeMs, line.Text));
                LyricsSynced = res.Synced;
                HasLyrics = true;
            }
        }
        catch { /* ignore */ }
        finally { _loadingLyrics = false; }
    }

    private async Task RefreshLibraryAsync()
    {
        await RefreshDevicesAsync();
        await RefreshPlaylistsAsync();
    }

    private async Task RefreshPlaylistsAsync()
    {
        var pls = await _spotify.GetPlaylistsAsync();
        Playlists.Clear();
        foreach (var p in pls)
            Playlists.Add(new SpotifyPlaylistViewModel(
                p.Name ?? "", p.Owner?.DisplayName ?? "", p.Uri ?? "", p.Images?.FirstOrDefault()?.Url));
    }

    private async Task RefreshDevicesAsync()
    {
        var ds = await _spotify.GetDevicesAsync();
        Devices.Clear();
        foreach (var d in ds)
            Devices.Add(new SpotifyDeviceViewModel(d.Id ?? "", d.Name ?? "", d.Type ?? "", d.IsActive));
    }

    private async Task RefreshQueueAsync()
    {
        var q = await _spotify.GetQueueAsync();
        Queue.Clear();
        if (q?.Queue is null) return;
        foreach (var item in q.Queue.Take(20))
            if (item is FullTrack ft)
                Queue.Add(new SpotifyTrackViewModel(ft.Name, string.Join(", ", ft.Artists.Select(a => a.Name))));
    }

    private static string Fmt(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }

    public void Dispose()
    {
        _spotify.StateChanged -= OnStateChanged;
        _timer.Stop();
    }
}
