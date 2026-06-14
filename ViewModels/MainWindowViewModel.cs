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

        _ = LoadWallpaperAsync();

        // Reload the wallpaper + accent when the system wallpaper/theme changes.
        _wallpaperDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _wallpaperDebounce.Tick += (_, _) => { _wallpaperDebounce.Stop(); _ = LoadWallpaperAsync(); };
        var w = _services.Desktop.WatchWallpaperAndTheme(
            () => { _wallpaperDebounce.Stop(); _wallpaperDebounce.Start(); });
        if (w != null) _watchers.Add(w);
    }

    partial void OnEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ControlsVisible));
        OnPropertyChanged(nameof(ControlsOpacity));
    }

    partial void OnShowControlsChanged(bool value)
    {
        OnPropertyChanged(nameof(ControlsVisible));
        OnPropertyChanged(nameof(ControlsOpacity));
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
