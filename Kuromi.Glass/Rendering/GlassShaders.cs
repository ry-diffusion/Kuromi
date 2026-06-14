using System;
using System.IO;
using Avalonia.Platform;
using SkiaSharp;

namespace Kuromi.Glass.Rendering;

/// <summary>
/// Loads and caches the compiled <see cref="SKRuntimeEffect"/> instances. Compilation happens once
/// (first render) and the effects are reused for the lifetime of the process — never per frame.
/// </summary>
internal static class GlassShaders
{
    private static bool s_loaded;
    private static SKRuntimeEffect? s_lens;
    private static SKRuntimeEffect? s_highlight;

    public static SKRuntimeEffect? Lens
    {
        get { EnsureLoaded(); return s_lens; }
    }

    public static SKRuntimeEffect? Highlight
    {
        get { EnsureLoaded(); return s_highlight; }
    }

    private static void EnsureLoaded()
    {
        if (s_loaded)
            return;
        s_loaded = true;

        s_lens = Load("avares://Kuromi.Glass/Assets/Shaders/Lens.sksl");
        s_highlight = Load("avares://Kuromi.Glass/Assets/Shaders/Highlight.sksl");
    }

    private static SKRuntimeEffect? Load(string assetUri)
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri(assetUri));
            using StreamReader reader = new(stream);
            string src = reader.ReadToEnd();

            SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(src, out string? error);
            if (effect is null)
                Console.WriteLine($"[Kuromi.Glass] shader compile failed ({assetUri}): {error}");
            return effect;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Kuromi.Glass] shader load failed ({assetUri}): {ex.Message}");
            return null;
        }
    }
}
