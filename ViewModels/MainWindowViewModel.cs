using System;
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
    public bool ShowSettings => SelectedTabIndex == 1;

    // --- Settings (the "Ajustes" tab) ---
    private bool _settingsLoading = true;
    [ObservableProperty] private bool _darkMode = true;
    [ObservableProperty] private bool _accentFromWallpaper = true;
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

        _accentFromWallpaper = _config.AccentFromWallpaper; // field (no persist during init)
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
        OnPropertyChanged(nameof(ShowSettings));
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
    }

    partial void OnAccentFromWallpaperChanged(bool value)
    {
        if (_settingsLoading) return;
        _config.AccentFromWallpaper = value;
        ScheduleSave();
        _ = LoadWallpaperAsync();
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

            if (_config.AccentFromWallpaper)
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
        if (palette is not { } p) return;

        Dispatcher.UIThread.Post(() =>
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
    }

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
