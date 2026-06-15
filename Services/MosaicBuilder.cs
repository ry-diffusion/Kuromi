using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Kuromi.Logging;
using SkiaSharp;

namespace Kuromi.Services;

/// <summary>
/// Composes a set of cover-art URLs into a single tiled mosaic bitmap that fills a target size: it picks a
/// grid based on a target tile size, then draws each cell as a cover-cropped art, repeating the covers as
/// needed to fill the whole canvas. Used for the Playlists / Recently-played backgrounds.
/// </summary>
public static class MosaicBuilder
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, SKBitmap> Cache = new();
    private static readonly ILog Logger = Log.For("MosaicBuilder");

    public static async Task<Bitmap?> BuildAsync(IList<string> urls, int width, int height, double tileTarget = 240)
    {
        if (urls is null || urls.Count == 0)
            return null;

        var sw = Stopwatch.StartNew();
        var tiles = new List<SKBitmap>();
        foreach (var u in urls)
        {
            var b = await GetAsync(u);
            if (b is not null)
                tiles.Add(b);
        }
        if (tiles.Count == 0)
        {
            Logger.Warn($"mosaic: no covers could be downloaded ({urls.Count} urls)");
            return null;
        }

        long downloadMs = sw.ElapsedMilliseconds;
        var result = await Task.Run(() => Compose(tiles, width, height, tileTarget));
        Logger.Info($"mosaic generated {width}x{height} from {tiles.Count} covers in {sw.ElapsedMilliseconds}ms (download {downloadMs}ms)");
        return result;
    }

    private static async Task<SKBitmap?> GetAsync(string url)
    {
        lock (Cache)
            if (Cache.TryGetValue(url, out var cached))
                return cached;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            var b = SKBitmap.Decode(bytes);
            if (b is not null)
                lock (Cache) Cache[url] = b;
            return b;
        }
        catch { return null; }
    }

    private static Bitmap Compose(List<SKBitmap> tiles, int width, int height, double tileTarget)
    {
        // Average tile size based on the canvas, then divide so the grid fills it exactly.
        int cols = Math.Max(1, (int)Math.Round(width / tileTarget));
        int rows = Math.Max(1, (int)Math.Round(height / tileTarget));
        int tileW = (int)Math.Ceiling((double)width / cols);
        int tileH = (int)Math.Ceiling((double)height / rows);

        using var bmp = new SKBitmap(cols * tileW, rows * tileH);
        using (var canvas = new SKCanvas(bmp))
        {
            int i = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var src = tiles[i % tiles.Count];
                    i++;
                    var dest = new SKRect(c * tileW, r * tileH, c * tileW + tileW, r * tileH + tileH);
                    DrawCover(canvas, src, dest);
                }
            }
        }

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    /// <summary>Draw <paramref name="src"/> into <paramref name="dest"/>, cover-cropped (UniformToFill).</summary>
    private static void DrawCover(SKCanvas canvas, SKBitmap src, SKRect dest)
    {
        canvas.Save();
        canvas.ClipRect(dest);
        float scale = Math.Max(dest.Width / src.Width, dest.Height / src.Height);
        float w = src.Width * scale, h = src.Height * scale;
        float dx = dest.MidX - w / 2, dy = dest.MidY - h / 2;
        canvas.DrawBitmap(src, new SKRect(dx, dy, dx + w, dy + h));
        canvas.Restore();
    }
}
