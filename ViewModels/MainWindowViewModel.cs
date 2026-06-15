using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Models;
using Kuromi.Services;

namespace Kuromi.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly KuromiConfig _config;

    public DashboardViewModel Dashboard { get; }

    private readonly System.Collections.Generic.List<IDisposable> _watchers = new();
    private readonly DispatcherTimer _wallpaperDebounce;

    [ObservableProperty] private Bitmap? _wallpaper;
    [ObservableProperty] private double _blurRadius;
    [ObservableProperty] private double _overlayOpacity;
    [ObservableProperty] private bool _editMode;
    [ObservableProperty] private bool _showControls;
    [ObservableProperty] private bool _keyboardVisible;

    // --- Tabs / navigation (liquid-glass bottom tab bar) ---
    [ObservableProperty] private int _selectedTabIndex;
    public bool ShowDashboard => SelectedTabIndex == 0;
    public bool ShowSpotify => SelectedTabIndex == 1;
    public bool ShowSettings => SelectedTabIndex == 2;

    /// <summary>The Spotify panel view-model (PKCE auth, playback, lyrics).</summary>
    public SpotifyViewModel Spotify { get; }
    [ObservableProperty] private string _spotifyClientId = "";
    public string SpotifyRedirectUri => Services.SpotifyService.RedirectUri;

    // --- Background (selectable in Ajustes) ---
    [ObservableProperty] private BackgroundSource _backgroundSource = BackgroundSource.Wallpaper;
    [ObservableProperty] private Bitmap? _backgroundImage;
    [ObservableProperty] private double _backgroundBlur;
    public BackgroundSource[] BackgroundSources { get; } = Enum.GetValues<BackgroundSource>();

    // --- Settings (the "Ajustes" tab) ---
    private bool _settingsLoading = true;
    [ObservableProperty] private bool _darkMode = true;
    [ObservableProperty] private AccentSource _accentSource = AccentSource.Wallpaper;
    public AccentSource[] AccentSources { get; } = Enum.GetValues<AccentSource>();
    public string BackendName => _services.Desktop.Name;

    // --- Liquid-glass tuning (applied live to the cards via app resources) ---
    [ObservableProperty] private double _glassRefraction = 72;
    [ObservableProperty] private double _glassRefractionHeight = 32;
    [ObservableProperty] private bool _glassDepth = true;
    [ObservableProperty] private bool _glassChromatic = true;
    [ObservableProperty] private double _glassBlur = 2;
    [ObservableProperty] private double _glassVibrancy = 1.2;
    [ObservableProperty] private double _glassBrightness;
    [ObservableProperty] private double _glassContrast = 1;
    [ObservableProperty] private double _glassHighlight = 0.5;
    [ObservableProperty] private bool _glassShadow = true;
    [ObservableProperty] private double _glassShadowRadius = 24;

    public bool ControlsVisible => ShowControls || EditMode;
    public double ControlsOpacity => ControlsVisible ? 1.0 : 0.0;

    /// <summary>Raised so the window can toggle fullscreen / close.</summary>
    public event Action? FullscreenToggleRequested;
    public event Action? ExitRequested;

    public MainWindowViewModel() : this(new AppServices()) { }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        _config = services.Config.Load();
        BlurRadius = _config.BlurRadius;
        OverlayOpacity = _config.OverlayOpacity;

        Dashboard = new DashboardViewModel(_services, _config);
        Dashboard.WallpaperShouldRefresh += () => _ = LoadWallpaperAsync();

        _accentSource = _config.AccentSource; // field (no persist during init)
        _glassRefraction = _config.GlassRefraction;
        _glassRefractionHeight = _config.GlassRefractionHeight;
        _glassDepth = _config.GlassDepth;
        _glassChromatic = _config.GlassChromatic;
        _glassBlur = _config.GlassBlur;
        _glassVibrancy = _config.GlassVibrancy;
        _glassBrightness = _config.GlassBrightness;
        _glassContrast = _config.GlassContrast;
        _glassHighlight = _config.GlassHighlight;
        _glassShadow = _config.GlassShadow;
        _glassShadowRadius = _config.GlassShadowRadius;
        ApplyGlassResources();

        _ = LoadWallpaperAsync();
        _ = InitSettingsAsync();

        // Spotify panel: hand the configured Client ID to the service, build the VM, then try a silent
        // sign-in from the persisted token.
        _services.Spotify.SetClientId(_config.SpotifyClientId);
        Spotify = new SpotifyViewModel(_services.Spotify, _services.Lyrics);
        _spotifyClientId = _config.SpotifyClientId;
        _ = _services.Spotify.InitAsync();

        // Background: follow the configured source (wallpaper or a Spotify-derived image).
        _backgroundSource = _config.BackgroundSource;
        Spotify.PropertyChanged += OnSpotifyPropertyChanged;
        ApplyBackground();

        // Reload the wallpaper + accent when the system wallpaper/theme changes.
        _wallpaperDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _wallpaperDebounce.Tick += (_, _) => { _wallpaperDebounce.Stop(); _ = LoadWallpaperAsync(); };
        var w = _services.Desktop.WatchWallpaperAndTheme(
            () => { _wallpaperDebounce.Stop(); _wallpaperDebounce.Start(); });
        if (w != null) _watchers.Add(w);
    }

    partial void OnEditModeChanged(bool value)
    {
        Dashboard.EditMode = value; // keep the dashboard (drag/resize + QuickActions) in sync
        OnPropertyChanged(nameof(ControlsVisible));
        OnPropertyChanged(nameof(ControlsOpacity));
    }

    partial void OnShowControlsChanged(bool value)
    {
        OnPropertyChanged(nameof(ControlsVisible));
        OnPropertyChanged(nameof(ControlsOpacity));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowDashboard));
        OnPropertyChanged(nameof(ShowSpotify));
        OnPropertyChanged(nameof(ShowSettings));
        UpdateSpotifyPolling();
    }

    /// <summary>Poll Spotify while its tab is open OR the background follows the current track.</summary>
    private void UpdateSpotifyPolling()
    {
        if (ShowSpotify || BackgroundSource == BackgroundSource.CurrentTrack || AccentSource == AccentSource.CurrentTrack)
            Spotify.Activate();
        else
            Spotify.Deactivate();
    }

    partial void OnSpotifyClientIdChanged(string value)
    {
        if (_settingsLoading) return;
        _config.SpotifyClientId = value?.Trim() ?? "";
        _services.Spotify.SetClientId(_config.SpotifyClientId);
        Spotify.RefreshClientId();
        ScheduleSave();
        _ = _services.Spotify.InitAsync();
    }

    // --- Background ---
    partial void OnBackgroundSourceChanged(BackgroundSource value)
    {
        if (_settingsLoading) return;
        _config.BackgroundSource = value;
        ScheduleSave();
        ApplyBackground();
    }

    partial void OnWallpaperChanged(Bitmap? value)
    {
        if (BackgroundSource == BackgroundSource.Wallpaper)
            BackgroundImage = value;
    }

    private void OnSpotifyPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SpotifyViewModel.Art))
            return;
        if (BackgroundSource == BackgroundSource.CurrentTrack)
            BackgroundImage = Spotify.Art ?? Wallpaper;
        if (AccentSource == AccentSource.CurrentTrack)
            _ = ApplyMusicAccentAsync();
    }

    private void ApplyBackground()
    {
        switch (BackgroundSource)
        {
            case BackgroundSource.CurrentTrack:
                BackgroundBlur = 28; // blur-extend the single square cover to fill
                BackgroundImage = Spotify.Art ?? Wallpaper;
                break;
            case BackgroundSource.Playlists:
                BackgroundBlur = 6; // a tiled mosaic already fills the screen; keep it mostly visible
                _ = BuildMosaicAsync(_services.Spotify.GetPlaylistArtsAsync());
                break;
            case BackgroundSource.RecentTracks:
                BackgroundBlur = 6;
                _ = BuildMosaicAsync(_services.Spotify.GetRecentTrackArtsAsync());
                break;
            default: // Wallpaper
                BackgroundBlur = 0;
                BackgroundImage = Wallpaper;
                break;
        }
        UpdateSpotifyPolling();
    }

    /// <summary>Compose the covers into a tiled mosaic that fills the screen (repeating to fit).</summary>
    private async Task BuildMosaicAsync(Task<IList<string>> fetch)
    {
        BackgroundImage = Wallpaper; // show the wallpaper until the mosaic is composed
        var urls = (await fetch).ToList();
        if (urls.Count == 0)
        {
            BackgroundBlur = 0;
            BackgroundImage = Wallpaper;
            return;
        }
        var mosaic = await MosaicBuilder.BuildAsync(urls, 1920, 1080);
        // The source may have changed while building.
        if (mosaic is not null && BackgroundSource is BackgroundSource.Playlists or BackgroundSource.RecentTracks)
            BackgroundImage = mosaic;
    }

    private async Task InitSettingsAsync()
    {
        try { DarkMode = await _services.Desktop.GetDarkModeAsync(); } catch { /* keep default */ }
        _settingsLoading = false;
    }

    partial void OnDarkModeChanged(bool value)
    {
        if (_settingsLoading) return;
        _ = ApplyDarkAsync(value);
    }

    private async Task ApplyDarkAsync(bool value)
    {
        await _services.Desktop.SetDarkModeAsync(value);
        await Task.Delay(300);
        await LoadWallpaperAsync(); // light/dark wallpaper + accent follow the theme
        if (AccentSource == AccentSource.CurrentTrack)
            await ApplyMusicAccentAsync(); // re-derive the music accent for the new scheme
    }

    partial void OnAccentSourceChanged(AccentSource value)
    {
        if (_settingsLoading) return;
        _config.AccentSource = value;
        ScheduleSave();
        UpdateSpotifyPolling();
        if (value == AccentSource.Wallpaper)
            _ = LoadWallpaperAsync();   // re-derive the accent from the wallpaper
        else
            _ = ApplyMusicAccentAsync(); // from the current track's cover
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        if (_settingsLoading) return;
        _config.OverlayOpacity = value;
        ScheduleSave();
    }

    partial void OnGlassRefractionChanged(double value) => UpdateGlass();
    partial void OnGlassRefractionHeightChanged(double value) => UpdateGlass();
    partial void OnGlassDepthChanged(bool value) => UpdateGlass();
    partial void OnGlassChromaticChanged(bool value) => UpdateGlass();
    partial void OnGlassBlurChanged(double value) => UpdateGlass();
    partial void OnGlassVibrancyChanged(double value) => UpdateGlass();
    partial void OnGlassBrightnessChanged(double value) => UpdateGlass();
    partial void OnGlassContrastChanged(double value) => UpdateGlass();
    partial void OnGlassHighlightChanged(double value) => UpdateGlass();
    partial void OnGlassShadowChanged(bool value) => UpdateGlass();
    partial void OnGlassShadowRadiusChanged(double value) => UpdateGlass();

    private void UpdateGlass()
    {
        if (_settingsLoading) return;
        _config.GlassRefraction = GlassRefraction;
        _config.GlassRefractionHeight = GlassRefractionHeight;
        _config.GlassDepth = GlassDepth;
        _config.GlassChromatic = GlassChromatic;
        _config.GlassBlur = GlassBlur;
        _config.GlassVibrancy = GlassVibrancy;
        _config.GlassBrightness = GlassBrightness;
        _config.GlassContrast = GlassContrast;
        _config.GlassHighlight = GlassHighlight;
        _config.GlassShadow = GlassShadow;
        _config.GlassShadowRadius = GlassShadowRadius;
        ApplyGlassResources();
        ScheduleSave();
    }

    /// <summary>Push the glass tuning into app resources so every card bound via DynamicResource follows.</summary>
    private void ApplyGlassResources()
    {
        var app = Application.Current;
        if (app is null) return;
        app.Resources["GlassRefractionAmount"] = GlassRefraction;
        app.Resources["GlassRefractionHeight"] = GlassRefractionHeight;
        app.Resources["GlassDepthEffect"] = GlassDepth;
        app.Resources["GlassChromatic"] = GlassChromatic;
        app.Resources["GlassBlurRadius"] = GlassBlur;
        app.Resources["GlassVibrancy"] = GlassVibrancy;
        app.Resources["GlassBrightness"] = GlassBrightness;
        app.Resources["GlassContrast"] = GlassContrast;
        app.Resources["GlassHighlightOpacity"] = GlassHighlight;
        app.Resources["GlassShadowEnabled"] = GlassShadow;
        app.Resources["GlassShadowRadius"] = GlassShadowRadius;
    }

    private DispatcherTimer? _saveDebounce;

    /// <summary>Coalesce rapid setting changes (slider drags) into one disk write.</summary>
    private void ScheduleSave()
    {
        if (_saveDebounce is null)
        {
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveDebounce.Tick += (_, _) => { _saveDebounce!.Stop(); _services.Config.Save(_config); };
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public async Task LoadWallpaperAsync()
    {
        try
        {
            // Follow the system theme: light scheme -> light wallpaper, dark -> dark.
            var preferDark = await _services.Desktop.GetDarkModeAsync();
            var path = await _services.Wallpaper.GetUsableWallpaperAsync(preferDark);
            if (path == null) return;
            var bmp = await Task.Run(() => new Bitmap(path));
            Wallpaper = bmp;

            if (AccentSource == AccentSource.Wallpaper)
                await ApplyAccentAsync(path, preferDark);
        }
        catch { /* keep previous / none */ }
    }

    /// <summary>
    /// Derive a full Material You scheme from the wallpaper and push it into the
    /// app resources: accent/secondary/tertiary (graphs &amp; controls) + dynamic,
    /// theme-aware text colors that harmonize with the wallpaper.
    /// </summary>
    private static async Task ApplyAccentAsync(string path, bool dark)
    {
        var palette = await Task.Run(() => MaterialPalette.FromImage(path, dark));
        if (palette is { } p) PushAccent(p);
    }

    /// <summary>Derive the Material You accent from the current Spotify cover and push it app-wide.</summary>
    private async Task ApplyMusicAccentAsync()
    {
        var art = Spotify.Art;
        if (art is null) return;
        var dark = await _services.Desktop.GetDarkModeAsync();
        var palette = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            art.Save(ms);
            return MaterialPalette.FromBytes(ms.ToArray(), dark);
        });
        if (palette is { } p) PushAccent(p);
    }

    /// <summary>Push a Material You scheme into the app resources (accent + harmonized text colors).</summary>
    private static void PushAccent(DynamicPalette p) => Dispatcher.UIThread.Post(() =>
    {
        var app = Application.Current;
        if (app is null) return;
        app.Resources["AccentBrush"] = new SolidColorBrush(p.Accent);
        app.Resources["Accent2Brush"] = new SolidColorBrush(p.Secondary);
        app.Resources["InfoBrush"] = new SolidColorBrush(p.Tertiary);
        app.Resources["OnAccentBrush"] = new SolidColorBrush(p.OnAccent);
        // Translucent accent *colors* for glass tinting (glass tints with a Color, not a Brush).
        app.Resources["AccentColor"] = Color.FromArgb(215, p.Accent.R, p.Accent.G, p.Accent.B);
        app.Resources["Accent2Color"] = Color.FromArgb(215, p.Secondary.R, p.Secondary.G, p.Secondary.B);
        app.Resources["TextPrimary"] = new SolidColorBrush(p.TextPrimary);
        app.Resources["TextSecondary"] = new SolidColorBrush(p.TextSecondary);
        app.Resources["TextMuted"] = new SolidColorBrush(p.TextMuted);
    });

    [RelayCommand]
    private void ToggleEdit()
    {
        Dashboard.ToggleEditCommand.Execute(null);
        EditMode = Dashboard.EditMode;
    }

    [RelayCommand]
    private void ToggleFullscreen() => FullscreenToggleRequested?.Invoke();

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke();
}
