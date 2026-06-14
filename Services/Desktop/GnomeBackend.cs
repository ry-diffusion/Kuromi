using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kuromi.Services;

namespace Kuromi.Services.Desktop;

public class GnomeBackend : LinuxBackendBase
{
    public override string Name => "GNOME";

    // GNOME exposes screen brightness on the power daemon, which emits
    // PropertiesChanged — more reliable than inotify on sysfs.
    public override IDisposable? WatchBrightness(Action onChanged)
    {
        if (!ShellRunner.Exists("gdbus")) return base.WatchBrightness(onChanged);
        return new ProcessStreamWatcher("gdbus",
            new[] { "monitor", "--session", "--dest", "org.gnome.SettingsDaemon.Power" }, line =>
            {
                if (line.Contains("Brightness")) Dispatcher.UIThread.Post(onChanged);
            });
    }

    public override IDisposable? WatchWallpaperAndTheme(Action onChanged)
    {
        if (!ShellRunner.Exists("gsettings")) return null;
        void Fire(string _) => Dispatcher.UIThread.Post(onChanged);
        var bg = new ProcessStreamWatcher("gsettings",
            new[] { "monitor", "org.gnome.desktop.background", "picture-uri" }, Fire);
        var bgDark = new ProcessStreamWatcher("gsettings",
            new[] { "monitor", "org.gnome.desktop.background", "picture-uri-dark" }, Fire);
        var scheme = new ProcessStreamWatcher("gsettings",
            new[] { "monitor", "org.gnome.desktop.interface", "color-scheme" }, Fire);
        return new CompositeDisposable(bg, bgDark, scheme);
    }

    public override async Task<string?> GetWallpaperPathAsync(bool preferDark)
    {
        var key = preferDark ? "picture-uri-dark" : "picture-uri";
        var r = await ShellRunner.RunAsync("gsettings", new[] { "get", "org.gnome.desktop.background", key });
        var path = UriToPath(r.Trimmed);
        if (path != null) return path;

        // fall back to the light key
        r = await ShellRunner.RunAsync("gsettings", new[] { "get", "org.gnome.desktop.background", "picture-uri" });
        return UriToPath(r.Trimmed);
    }

    public override async Task<bool> GetDarkModeAsync()
    {
        var r = await ShellRunner.RunAsync("gsettings", new[] { "get", "org.gnome.desktop.interface", "color-scheme" });
        return r.Trimmed.Contains("dark");
    }

    public override async Task SetDarkModeAsync(bool dark)
    {
        await ShellRunner.RunAsync("gsettings", new[]
        {
            "set", "org.gnome.desktop.interface", "color-scheme",
            dark ? "prefer-dark" : "default",
        });
    }
}
