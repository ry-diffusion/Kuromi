using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Kuromi.Services.Desktop;

public class KdeBackend : LinuxBackendBase
{
    public override string Name => "KDE";

    public override Task<string?> GetWallpaperPathAsync(bool preferDark)
    {
        // Parse plasma config for the most recent Image= entry.
        try
        {
            var cfg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "plasma-org.kde.plasma.desktop-appletsrc");
            if (File.Exists(cfg))
            {
                var img = File.ReadLines(cfg)
                    .Where(l => l.StartsWith("Image=", StringComparison.Ordinal))
                    .Select(l => l["Image=".Length..])
                    .LastOrDefault(s => !string.IsNullOrWhiteSpace(s));
                if (img != null)
                {
                    var path = UriToPath(img);
                    // KDE often points at a package dir; resolve contents/images/*
                    if (path != null && Directory.Exists(path))
                    {
                        var inside = Path.Combine(path, "contents", "images");
                        if (Directory.Exists(inside))
                            path = Directory.GetFiles(inside)
                                .OrderByDescending(f => new FileInfo(f).Length)
                                .FirstOrDefault();
                    }
                    return Task.FromResult(path);
                }
            }
        }
        catch { /* ignore */ }
        return Task.FromResult<string?>(null);
    }

    public override async Task<bool> GetDarkModeAsync()
    {
        try
        {
            var kdeglobals = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "kdeglobals");
            if (File.Exists(kdeglobals))
            {
                var scheme = File.ReadLines(kdeglobals)
                    .FirstOrDefault(l => l.StartsWith("ColorScheme=", StringComparison.Ordinal));
                return scheme?.Contains("Dark", StringComparison.OrdinalIgnoreCase) ?? false;
            }
        }
        catch { /* ignore */ }
        await Task.CompletedTask;
        return false;
    }

    public override async Task SetDarkModeAsync(bool dark)
    {
        if (ShellRunner.Exists("plasma-apply-colorscheme"))
            await ShellRunner.RunAsync("plasma-apply-colorscheme",
                new[] { dark ? "BreezeDark" : "BreezeLight" });
    }

    // Audio/brightness watchers come from the base (pactl/sysfs). Plasma wallpaper
    // change watching isn't wired up yet.
    public override System.IDisposable? WatchWallpaperAndTheme(System.Action onChanged) => null;
}
