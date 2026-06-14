using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Models;
using Kuromi.Services.Desktop;

namespace Kuromi.ViewModels.Widgets;

public partial class SystemControlsViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopBackend _backend;
    private bool _loading = true;

    private readonly IDisposable? _audioWatch;
    private readonly IDisposable? _brightnessWatch;
    private readonly DispatcherTimer _debounce;

    /// <summary>Raised when something changes that affects the wallpaper (e.g. dark mode).</summary>
    public event Action? WallpaperShouldRefresh;

    [ObservableProperty] private bool _brightnessAvailable;
    [ObservableProperty] private double _brightness = 50;
    [ObservableProperty] private bool _volumeAvailable;
    [ObservableProperty] private double _volume = 50;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private bool _darkMode = true;
    [ObservableProperty] private string _backendName = "";

    public ObservableCollection<AudioSink> Outputs { get; } = new();
    [ObservableProperty] private AudioSink? _selectedOutput;
    [ObservableProperty] private bool _hasOutputs;

    public SystemControlsViewModel(IDesktopBackend backend)
    {
        _backend = backend;
        BackendName = backend.Name;

        // Coalesce bursts of external-change events into one re-read.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await ReadAsync(); };

        _ = ReadAsync();

        _audioWatch = _backend.WatchAudio(Bump);
        _brightnessWatch = _backend.WatchBrightness(Bump);
    }

    private void Bump() { _debounce.Stop(); _debounce.Start(); }

    private async Task ReadAsync()
    {
        _loading = true;
        try
        {
            var b = await _backend.GetBrightnessAsync();
            BrightnessAvailable = b >= 0;
            if (BrightnessAvailable) Brightness = b;

            var v = await _backend.GetVolumeAsync();
            VolumeAvailable = v >= 0;
            if (VolumeAvailable) Volume = v;

            Muted = await _backend.GetMutedAsync();
            DarkMode = await _backend.GetDarkModeAsync();

            var outputs = await _backend.GetOutputsAsync();
            var defaultName = await _backend.GetDefaultOutputAsync();

            // Rebuild the list only when the set of sinks actually changed (avoids flicker).
            if (!outputs.Select(o => o.Name).SequenceEqual(Outputs.Select(o => o.Name)))
            {
                Outputs.Clear();
                foreach (var o in outputs) Outputs.Add(o);
            }
            HasOutputs = Outputs.Count > 0;
            SelectedOutput = Outputs.FirstOrDefault(o => o.Name == defaultName) ?? Outputs.FirstOrDefault();
        }
        catch { /* ignore */ }
        finally { _loading = false; }
    }

    partial void OnSelectedOutputChanged(AudioSink? value)
    {
        if (_loading || value == null) return;
        _ = _backend.SetDefaultOutputAsync(value.Name);
    }

    partial void OnBrightnessChanged(double value)
    {
        if (_loading) return;
        _ = _backend.SetBrightnessAsync((int)Math.Round(value));
    }

    partial void OnVolumeChanged(double value)
    {
        if (_loading) return;
        _ = _backend.SetVolumeAsync((int)Math.Round(value));
    }

    partial void OnMutedChanged(bool value)
    {
        if (_loading) return;
        _ = _backend.SetMutedAsync(value);
    }

    partial void OnDarkModeChanged(bool value)
    {
        if (_loading) return;
        _ = ApplyDarkAsync(value);
    }

    private async Task ApplyDarkAsync(bool value)
    {
        await _backend.SetDarkModeAsync(value);
        await Task.Delay(300);
        WallpaperShouldRefresh?.Invoke();
    }

    [RelayCommand]
    private void ToggleMute() => Muted = !Muted;

    public void Dispose()
    {
        _debounce.Stop();
        _audioWatch?.Dispose();
        _brightnessWatch?.Dispose();
    }
}
