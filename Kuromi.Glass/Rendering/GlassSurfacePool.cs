using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Kuromi.Glass.Rendering;

/// <summary>
/// A render-thread pool of intermediate <see cref="SKSurface"/>s used for the blur/grade pass. Glass
/// surfaces re-render often (any time their region recomposites), so recreating a GPU surface each draw
/// is the main avoidable cost — pooling by pixel size keeps allocation/teardown off the hot path.
/// </summary>
internal static class GlassSurfacePool
{
    private const int MaxTotal = 24;
    private const int MaxPerSize = 3;

    [ThreadStatic] private static Dictionary<long, Stack<SKSurface>>? t_pool;
    [ThreadStatic] private static int t_ctxId;
    [ThreadStatic] private static int t_count;

    public static SKSurface? Rent(SKImageInfo info, GRContext? ctx)
    {
        int ctxId = ctx is null ? 0 : RuntimeHelpers.GetHashCode(ctx);
        if (t_pool is null || ctxId != t_ctxId)
        {
            Clear();
            t_pool = new Dictionary<long, Stack<SKSurface>>();
            t_ctxId = ctxId;
        }

        long key = Key(info.Width, info.Height);
        if (t_pool.TryGetValue(key, out Stack<SKSurface>? stack) && stack.Count > 0)
        {
            SKSurface reused = stack.Pop();
            t_count--;
            reused.Canvas.Clear(SKColors.Transparent);
            return reused;
        }

        SKSurface? created = ctx is not null ? SKSurface.Create(ctx, false, info) : null;
        created ??= SKSurface.Create(info);
        return created;
    }

    public static void Return(SKSurface surface, SKImageInfo info)
    {
        if (t_pool is null || t_count >= MaxTotal)
        {
            surface.Dispose();
            return;
        }

        long key = Key(info.Width, info.Height);
        if (!t_pool.TryGetValue(key, out Stack<SKSurface>? stack))
        {
            stack = new Stack<SKSurface>();
            t_pool[key] = stack;
        }

        if (stack.Count >= MaxPerSize)
        {
            surface.Dispose();
            return;
        }

        stack.Push(surface);
        t_count++;
    }

    private static void Clear()
    {
        if (t_pool is null)
            return;
        foreach (Stack<SKSurface> stack in t_pool.Values)
            foreach (SKSurface surface in stack)
                surface.Dispose();
        t_pool.Clear();
        t_count = 0;
    }

    private static long Key(int w, int h) => ((long)w << 32) | (uint)h;
}
