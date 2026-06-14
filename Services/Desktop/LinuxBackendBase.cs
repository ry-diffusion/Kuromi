using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kuromi.Models;

namespace Kuromi.Services.Desktop;

/// <summary>
/// Shared brightness/volume implementation that works the same on most modern
/// Linux desktops (brightnessctl + PipeWire/wpctl, falling back to pactl).
/// GNOME/KDE only differ on wallpaper + color scheme, handled by subclasses.
/// </summary>
public abstract class LinuxBackendBase : IDesktopBackend
{
    public abstract string Name { get; }
    public abstract Task<string?> GetWallpaperPathAsync(bool preferDark);
    public abstract Task SetDarkModeAsync(bool dark);
    public abstract Task<bool> GetDarkModeAsync();

    // ---------------- Brightness (brightnessctl) ----------------

    public async Task<int> GetBrightnessAsync()
    {
        if (!ShellRunner.Exists("brightnessctl")) return -1;
        var r = await ShellRunner.RunAsync("brightnessctl", new[] { "-m" });
        if (!r.Success) return -1;
        // device,class,current,percent,max  -> field index 3 is "53%"
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',');
            if (parts.Length >= 4 && parts[3].EndsWith('%') &&
                int.TryParse(parts[3].TrimEnd('%'), out var pct))
                return Math.Clamp(pct, 0, 100);
        }
        return -1;
    }

    public async Task SetBrightnessAsync(int percent)
    {
        if (!ShellRunner.Exists("brightnessctl")) return;
        percent = Math.Clamp(percent, 1, 100);
        await ShellRunner.RunAsync("brightnessctl", new[] { "set", $"{percent}%" });
    }

    // ---------------- Volume (wpctl / pactl) ----------------

    public async Task<int> GetVolumeAsync()
    {
        if (ShellRunner.Exists("wpctl"))
        {
            var r = await ShellRunner.RunAsync("wpctl", new[] { "get-volume", "@DEFAULT_AUDIO_SINK@" });
            // "Volume: 0.65" or "Volume: 0.65 [MUTED]"
            var tok = r.Trimmed.Replace("Volume:", "").Trim().Split(' ');
            if (tok.Length > 0 && double.TryParse(tok[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return (int)Math.Round(v * 100);
        }
        if (ShellRunner.Exists("pactl"))
        {
            var r = await ShellRunner.RunAsync("pactl", new[] { "get-sink-volume", "@DEFAULT_SINK@" });
            var idx = r.StdOut.IndexOf('%');
            if (idx > 0)
            {
                var start = idx;
                while (start > 0 && (char.IsDigit(r.StdOut[start - 1]))) start--;
                if (int.TryParse(r.StdOut[start..idx], out var pct)) return pct;
            }
        }
        return -1;
    }

    public async Task SetVolumeAsync(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (ShellRunner.Exists("wpctl"))
        {
            var frac = (percent / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            await ShellRunner.RunAsync("wpctl", new[] { "set-volume", "@DEFAULT_AUDIO_SINK@", frac });
        }
        else if (ShellRunner.Exists("pactl"))
        {
            await ShellRunner.RunAsync("pactl", new[] { "set-sink-volume", "@DEFAULT_SINK@", $"{percent}%" });
        }
    }

    public async Task<bool> GetMutedAsync()
    {
        if (ShellRunner.Exists("wpctl"))
        {
            var r = await ShellRunner.RunAsync("wpctl", new[] { "get-volume", "@DEFAULT_AUDIO_SINK@" });
            return r.StdOut.Contains("MUTED");
        }
        return false;
    }

    public async Task SetMutedAsync(bool muted)
    {
        if (ShellRunner.Exists("wpctl"))
            await ShellRunner.RunAsync("wpctl", new[] { "set-mute", "@DEFAULT_AUDIO_SINK@", muted ? "1" : "0" });
        else if (ShellRunner.Exists("pactl"))
            await ShellRunner.RunAsync("pactl", new[] { "set-sink-mute", "@DEFAULT_SINK@", muted ? "1" : "0" });
    }

    // ---------------- Audio output selection (pactl) ----------------

    public async Task<List<AudioSink>> GetOutputsAsync()
    {
        var list = new List<AudioSink>();
        if (!ShellRunner.Exists("pactl")) return list;

        var r = await ShellRunner.RunAsync("pactl", new[] { "list", "sinks" });
        string? name = null;
        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Name:", StringComparison.Ordinal))
                name = line[5..].Trim();
            else if (line.StartsWith("Description:", StringComparison.Ordinal) && name != null)
            {
                list.Add(new AudioSink { Name = name, Description = line[12..].Trim() });
                name = null;
            }
        }
        return list;
    }

    public async Task<string?> GetDefaultOutputAsync()
    {
        if (!ShellRunner.Exists("pactl")) return null;
        var r = await ShellRunner.RunAsync("pactl", new[] { "get-default-sink" });
        return string.IsNullOrWhiteSpace(r.Trimmed) ? null : r.Trimmed;
    }

    public async Task SetDefaultOutputAsync(string sinkName)
    {
        if (!ShellRunner.Exists("pactl") || string.IsNullOrEmpty(sinkName)) return;
        await ShellRunner.RunAsync("pactl", new[] { "set-default-sink", sinkName });

        // Move already-playing streams to the new sink so the switch is immediate.
        var inputs = await ShellRunner.RunAsync("pactl", new[] { "list", "short", "sink-inputs" });
        foreach (var line in inputs.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var id = line.Split('\t', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (int.TryParse(id, out _))
                await ShellRunner.RunAsync("pactl", new[] { "move-sink-input", id, sinkName });
        }
    }

    // ---------------- Live watchers ----------------

    public IDisposable? WatchAudio(Action onChanged)
    {
        if (!ShellRunner.Exists("pactl")) return null;
        return new ProcessStreamWatcher("pactl", new[] { "subscribe" }, line =>
        {
            // sink = volume/mute change; server = default-sink change.
            if (line.Contains(" on sink #") || line.Contains(" on server"))
                Dispatcher.UIThread.Post(onChanged);
        });
    }

    /// <summary>Default: watch the sysfs backlight file (subclasses may override
    /// with something more reliable, e.g. GNOME's power daemon).</summary>
    public virtual IDisposable? WatchBrightness(Action onChanged)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories("/sys/class/backlight"))
            {
                if (!File.Exists(Path.Combine(dir, "brightness"))) continue;
                var fsw = new FileSystemWatcher(dir, "brightness")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                fsw.Changed += (_, _) => Dispatcher.UIThread.Post(onChanged);
                return fsw;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    public abstract IDisposable? WatchWallpaperAndTheme(Action onChanged);

    // ---------------- helpers ----------------

    /// <summary>Convert a file:// URI (possibly quoted/percent-encoded) to a real path.</summary>
    protected static string? UriToPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim().Trim('\'', '"');
        if (raw.StartsWith("file://", StringComparison.Ordinal))
        {
            try { return new Uri(raw).LocalPath; } catch { return null; }
        }
        return File.Exists(raw) ? raw : null;
    }
}
