using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kuromi.Models;

namespace Kuromi.Services.Desktop;

/// <summary>
/// Abstraction over the desktop environment so the rest of the app does not care
/// whether it talks to GNOME (gsettings/gdbus) or KDE (qdbus/plasma).
/// </summary>
public interface IDesktopBackend
{
    string Name { get; }

    /// <summary>Absolute path to the current wallpaper image (any format), or null.</summary>
    Task<string?> GetWallpaperPathAsync(bool preferDark);

    /// <summary>Screen brightness 0-100, or -1 when unavailable.</summary>
    Task<int> GetBrightnessAsync();
    Task SetBrightnessAsync(int percent);

    /// <summary>Output volume 0-100, or -1 when unavailable.</summary>
    Task<int> GetVolumeAsync();
    Task SetVolumeAsync(int percent);
    Task<bool> GetMutedAsync();
    Task SetMutedAsync(bool muted);

    /// <summary>Toggle the system dark/light color scheme.</summary>
    Task SetDarkModeAsync(bool dark);
    Task<bool> GetDarkModeAsync();

    /// <summary>Available audio output devices (sinks).</summary>
    Task<List<AudioSink>> GetOutputsAsync();
    Task<string?> GetDefaultOutputAsync();
    Task SetDefaultOutputAsync(string sinkName);

    // ---- Live change notifications (event-driven). Callbacks run on the UI thread.
    // Return null when the source isn't available; dispose to stop watching.

    /// <summary>Fires when audio volume / mute / default sink changes.</summary>
    IDisposable? WatchAudio(Action onChanged);
    /// <summary>Fires when screen brightness changes externally.</summary>
    IDisposable? WatchBrightness(Action onChanged);
    /// <summary>Fires when the wallpaper or the system color scheme changes.</summary>
    IDisposable? WatchWallpaperAndTheme(Action onChanged);
}
