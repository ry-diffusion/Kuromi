using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kuromi.Logging;
using Kuromi.Services.Desktop;

namespace Kuromi.Services;

/// <summary>
/// Resolves the system wallpaper (via the desktop backend / DBus settings) into
/// an image file that Avalonia's Skia loader can actually decode. Formats such as
/// JPEG-XL (.jxl), HEIC and AVIF are transcoded to PNG with djxl / ImageMagick and
/// cached under the XDG cache dir.
/// </summary>
public class WallpaperService
{
    private static readonly string[] SkiaReadable =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico" };

    private readonly IDesktopBackend _backend;
    private static readonly ILog Logger = Log.For<WallpaperService>();

    public WallpaperService(IDesktopBackend backend) => _backend = backend;

    public async Task<string?> GetUsableWallpaperAsync(bool preferDark)
    {
        var src = await _backend.GetWallpaperPathAsync(preferDark);
        if (src == null || !File.Exists(src))
        {
            Logger.Warn($"wallpaper source not found ({src ?? "null"})");
            return null;
        }

        var ext = Path.GetExtension(src).ToLowerInvariant();
        if (Array.IndexOf(SkiaReadable, ext) >= 0)
        {
            Logger.Debug($"wallpaper ready: {src}");
            return src;
        }

        Logger.Info($"wallpaper {ext} needs transcoding: {src}");
        return await TranscodeAsync(src);
    }

    private static async Task<string?> TranscodeAsync(string src)
    {
        try
        {
            var info = new FileInfo(src);
            var key = Hash($"{src}:{info.LastWriteTimeUtc.Ticks}:{info.Length}");
            var outPath = Path.Combine(ConfigService.CacheDir, $"wallpaper-{key}.png");
            if (File.Exists(outPath))
            {
                Logger.Debug("wallpaper transcode: cache hit");
                return outPath;
            }

            var ext = Path.GetExtension(src).ToLowerInvariant();
            var sw = Stopwatch.StartNew();

            // ImageMagick reads jxl/heic/avif/webp and lets us cap the size so the
            // blur pass stays cheap (the '>' only shrinks images larger than the box).
            if (ShellRunner.Exists("magick"))
            {
                var r = await ShellRunner.RunAsync("magick",
                    new[] { src, "-resize", "2560x2560>", outPath }, timeoutMs: 45000);
                if (r.Success && File.Exists(outPath))
                {
                    Logger.Info($"wallpaper transcoded {ext}→png via magick in {sw.ElapsedMilliseconds}ms");
                    return outPath;
                }
            }

            if (ext == ".jxl" && ShellRunner.Exists("djxl"))
            {
                var r = await ShellRunner.RunAsync("djxl", new[] { src, outPath }, timeoutMs: 30000);
                if (r.Success && File.Exists(outPath))
                {
                    Logger.Info($"wallpaper transcoded jxl→png via djxl in {sw.ElapsedMilliseconds}ms");
                    return outPath;
                }
            }

            if ((ext == ".heic" || ext == ".heif") && ShellRunner.Exists("heif-convert"))
            {
                var r = await ShellRunner.RunAsync("heif-convert", new[] { src, outPath }, timeoutMs: 30000);
                if (r.Success && File.Exists(outPath))
                {
                    Logger.Info($"wallpaper transcoded {ext}→png via heif-convert in {sw.ElapsedMilliseconds}ms");
                    return outPath;
                }
            }

            Logger.Warn($"wallpaper transcode failed for {ext} (no working converter?)");
        }
        catch (Exception ex) { Logger.Warn("wallpaper transcode error", ex); }
        return null;
    }

    /// <summary>
    /// Extract a vibrant accent (and a complementary second color) from an image,
    /// the way a "material you"-style theme would. Returns null for grayscale art.
    /// </summary>
    public static async Task<(Rgb accent, Rgb accent2)?> GetAccentAsync(string pngPath)
    {
        if (!ShellRunner.Exists("magick") || !File.Exists(pngPath)) return null;
        try
        {
            var r = await ShellRunner.RunAsync("magick",
                new[] { pngPath, "-resize", "200x200", "-alpha", "off", "-colors", "24",
                        "-format", "%c", "histogram:info:-" }, timeoutMs: 15000);

            // Parse "<count>: (r,g,b) #RRGGBB ..." — read the hex (robust to magick
            // printing float RGB components like "11.95,13.1,...").
            var swatches = new List<(Rgb c, long count, double sat, double val, double hue)>();
            foreach (Match m in Regex.Matches(r.StdOut, @"(\d+):.*?#([0-9A-Fa-f]{6})"))
            {
                long count = long.Parse(m.Groups[1].Value);
                var hex = m.Groups[2].Value;
                var c = new Rgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
                var (h, s, v) = ToHsv(c);
                swatches.Add((c, count, s, v, h));
            }
            if (swatches.Count == 0) return null;

            // Keep colorful, mid-bright swatches; score by saturation × popularity.
            var vivid = swatches
                .Where(x => x.sat > 0.30 && x.val is > 0.28 and < 0.97)
                .OrderByDescending(x => x.sat * (0.5 + System.Math.Log10(x.count + 10)))
                .ToList();
            if (vivid.Count == 0) return null;

            var accent = vivid[0];
            // second color: most vivid one whose hue differs enough; else rotate.
            var second = vivid.Skip(1).FirstOrDefault(x => HueDiff(x.hue, accent.hue) > 40);
            var accent2 = second.c.Equals(default(Rgb))
                ? FromHsv((accent.hue + 45) % 360, accent.sat, accent.val)
                : second.c;

            return (accent.c, accent2);
        }
        catch { return null; }
    }

    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    }

    private static double HueDiff(double a, double b)
    {
        var d = System.Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static (double h, double s, double v) ToHsv(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = System.Math.Max(r, System.Math.Max(g, b));
        double min = System.Math.Min(r, System.Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static Rgb FromHsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - System.Math.Abs((h / 60 % 2) - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        switch ((int)(h / 60) % 6)
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            default: r = c; b = x; break;
        }
        return new Rgb((byte)System.Math.Round((r + m) * 255),
                       (byte)System.Math.Round((g + m) * 255),
                       (byte)System.Math.Round((b + m) * 255));
    }

    private static string Hash(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString()[..16];
    }
}
