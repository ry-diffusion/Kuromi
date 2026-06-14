using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Kuromi.Services;

/// <summary>
/// Resolves an application/process name to a cached PNG icon, following the
/// freedesktop spec well enough for common apps: maps the executable to a
/// .desktop file's Icon=, looks the icon name up across icon themes and pixmaps,
/// and rasterizes SVG-only icons to PNG via ImageMagick.
/// </summary>
public class IconResolver
{
    private readonly object _gate = new();
    private Dictionary<string, string>? _execToIcon;     // exec basename -> icon name
    private Dictionary<string, string>? _execToName;     // exec basename -> pretty Name=
    private readonly ConcurrentDictionary<string, string?> _resolved = new(); // proc name -> png path

    private static readonly string[] AppDirs =
    {
        "/usr/share/applications",
        "/usr/local/share/applications",
        "/var/lib/flatpak/exports/share/applications",
    };

    private static readonly string[] IconRoots =
    {
        "/usr/share/icons",
        "/usr/local/share/icons",
        "/var/lib/flatpak/exports/share/icons",
        "/usr/share/pixmaps",
    };

    private static readonly string[] Sizes =
        { "scalable", "512x512", "256x256", "192x192", "128x128", "96x96", "64x64", "48x48", "32x32" };

    public async Task<string?> ResolveAsync(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        if (_resolved.TryGetValue(processName, out var cached)) return cached;

        EnsureDesktopMap();

        var iconName = LookupIconName(processName);
        var path = iconName != null ? await ResolveIconNameAsync(iconName) : null;
        _resolved[processName] = path;
        return path;
    }

    private string? LookupIconName(string proc)
    {
        var map = _execToIcon!;
        var key = proc.ToLowerInvariant();
        if (map.TryGetValue(key, out var icon)) return icon;

        // try a looser contains match (e.g. "chrome" vs "google-chrome")
        var hit = map.Keys.FirstOrDefault(k => k.Contains(key) || key.Contains(k));
        if (hit != null) return map[hit];

        // last resort: maybe the process name *is* an icon name
        return proc;
    }

    private void EnsureDesktopMap()
    {
        if (_execToIcon != null) return;
        lock (_gate)
        {
            if (_execToIcon != null) return;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new List<string>(AppDirs);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add(Path.Combine(home, ".local/share/applications"));
            dirs.Add(Path.Combine(home, ".local/share/flatpak/exports/share/applications"));

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in SafeEnumerate(dir, "*.desktop"))
                {
                    try
                    {
                        string? exec = null, icon = null, wmClass = null, name = null;
                        foreach (var line in File.ReadLines(file))
                        {
                            if (exec == null && line.StartsWith("Exec=", StringComparison.Ordinal))
                                exec = line["Exec=".Length..];
                            else if (icon == null && line.StartsWith("Icon=", StringComparison.Ordinal))
                                icon = line["Icon=".Length..].Trim();
                            else if (name == null && line.StartsWith("Name=", StringComparison.Ordinal))
                                name = line["Name=".Length..].Trim();
                            else if (wmClass == null && line.StartsWith("StartupWMClass=", StringComparison.Ordinal))
                                wmClass = line["StartupWMClass=".Length..].Trim();
                            if (line.StartsWith("[Desktop Action", StringComparison.Ordinal)) break;
                        }
                        if (icon == null) continue;

                        void AddKey(string key)
                        {
                            map.TryAdd(key, icon);
                            if (!string.IsNullOrEmpty(name)) names.TryAdd(key, name!);
                        }

                        if (exec != null)
                        {
                            var first = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                            if (first != null)
                            {
                                var baseName = Path.GetFileName(first.Trim('"'));
                                if (!string.IsNullOrEmpty(baseName)) AddKey(baseName);
                            }
                        }
                        if (wmClass != null) AddKey(wmClass);
                        // desktop file id (chrome.desktop -> chrome)
                        AddKey(Path.GetFileNameWithoutExtension(file));
                    }
                    catch { /* ignore one file */ }
                }
            }
            _execToName = names;
            _execToIcon = map;
        }
    }

    /// <summary>Pretty display name from the matching .desktop's Name=, or null.</summary>
    public string? ResolveName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        EnsureDesktopMap();
        var map = _execToName!;
        var key = processName.ToLowerInvariant();
        if (map.TryGetValue(key, out var name)) return name;
        var hit = map.Keys.FirstOrDefault(k => k.Contains(key) || key.Contains(k));
        return hit != null ? map[hit] : null;
    }

    private async Task<string?> ResolveIconNameAsync(string iconName)
    {
        // Absolute path straight from the .desktop file.
        if (Path.IsPathRooted(iconName) && File.Exists(iconName))
            return await NormalizeAsync(iconName, iconName);

        // Search icon themes / pixmaps for a matching file.
        string? bestSvg = null;
        foreach (var root in IconRoots)
        {
            if (!Directory.Exists(root)) continue;

            // pixmaps are flat
            if (root.EndsWith("pixmaps"))
            {
                foreach (var ext in new[] { ".png", ".svg", ".xpm" })
                {
                    var p = Path.Combine(root, iconName + ext);
                    if (File.Exists(p))
                    {
                        if (ext == ".png") return await NormalizeAsync(p, iconName);
                        bestSvg ??= p;
                    }
                }
                continue;
            }

            foreach (var theme in SafeDirs(root))
            {
                foreach (var size in Sizes)
                {
                    var apps = Path.Combine(theme, size, "apps");
                    var png = Path.Combine(apps, iconName + ".png");
                    if (File.Exists(png)) return await NormalizeAsync(png, iconName);
                    var svg = Path.Combine(apps, iconName + ".svg");
                    if (File.Exists(svg)) bestSvg ??= svg;
                    // some themes use apps/<size> ordering
                    var apps2 = Path.Combine(theme, "apps", size);
                    var png2 = Path.Combine(apps2, iconName + ".png");
                    if (File.Exists(png2)) return await NormalizeAsync(png2, iconName);
                }
            }
        }

        return bestSvg != null ? await NormalizeAsync(bestSvg, iconName) : null;
    }

    /// <summary>PNG icons are returned as-is; SVG/XPM are rasterized to a cached PNG.</summary>
    private static async Task<string?> NormalizeAsync(string path, string iconName)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".png") return path;

        if (!ShellRunner.Exists("magick")) return null;

        var safe = string.Concat(iconName.Split(Path.GetInvalidFileNameChars()));
        var outPath = Path.Combine(ConfigService.CacheDir, $"icon-{safe}.png");
        if (File.Exists(outPath)) return outPath;

        var r = await ShellRunner.RunAsync("magick",
            new[] { "-background", "none", $"{path}", "-resize", "96x96", outPath }, timeoutMs: 8000);
        return r.Success && File.Exists(outPath) ? outPath : null;
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return Enumerable.Empty<string>(); }
    }
}
