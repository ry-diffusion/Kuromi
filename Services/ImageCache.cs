using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Kuromi.Services;

/// <summary>Downloads remote images (album art, playlist covers) into Avalonia bitmaps, cached by URL.</summary>
public static class ImageCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, Bitmap> Cache = new();

    public static async Task<Bitmap?> GetAsync(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        lock (Cache)
            if (Cache.TryGetValue(url, out var cached))
                return cached;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            lock (Cache)
                Cache[url] = bmp;
            return bmp;
        }
        catch { return null; }
    }
}
