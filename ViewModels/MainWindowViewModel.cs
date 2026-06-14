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
                await ApplyAccentAsync(path);
        }
        catch { /* keep previous / none */ }
    }

    private static async Task ApplyAccentAsync(string path)
    {
        var accent = await WallpaperService.GetAccentAsync(path);
        if (accent is not { } a) return;

        var c1 = Color.FromRgb(a.accent.R, a.accent.G, a.accent.B);
        var c2 = Color.FromRgb(a.accent2.R, a.accent2.G, a.accent2.B);

        Dispatcher.UIThread.Post(() =>
        {
            var app = Application.Current;
            if (app is null) return;
            app.Resources["AccentBrush"] = new SolidColorBrush(c1);
            app.Resources["Accent2Brush"] = new SolidColorBrush(c2);
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
