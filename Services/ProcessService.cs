using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kuromi.Models;

namespace Kuromi.Services;

/// <summary>
/// Enumerates /proc, sums resident memory per executable name and resolves an
/// icon for each group. Multiple processes that share a name (e.g. all the
/// "chrome" / "firefox" helpers) are merged into one row.
/// </summary>
public class ProcessService
{
    private readonly IconResolver _icons;
    private readonly long _pageSize = Environment.SystemPageSize;

    public ProcessService(IconResolver icons) => _icons = icons;

    public async Task<List<ProcessGroup>> SampleAsync(int iconLimit = 48)
    {
        var groups = new Dictionary<string, ProcessGroup>(StringComparer.Ordinal);

        foreach (var dir in EnumeratePidDirs())
        {
            try
            {
                var name = ReadName(dir);
                if (string.IsNullOrEmpty(name)) continue;

                var rss = ReadRssBytes(dir);
                if (rss == 0) continue;

                if (!groups.TryGetValue(name, out var g))
                {
                    g = new ProcessGroup { Name = name };
                    groups[name] = g;
                }
                g.Count++;
                g.MemBytes += rss;
            }
            catch { /* process vanished mid-scan */ }
        }

        var ordered = groups.Values
            .OrderByDescending(g => g.MemBytes)
            .ToList();

        // Resolve icons for the heaviest groups in parallel.
        var top = ordered.Take(iconLimit).ToList();
        await Task.WhenAll(top.Select(async g =>
        {
            g.IconPath = await _icons.ResolveAsync(g.Name);
            g.DisplayName = _icons.ResolveName(g.Name);
        }));

        return ordered;
    }

    private static IEnumerable<string> EnumeratePidDirs()
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories("/proc"); }
        catch { yield break; }

        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (name.Length > 0 && char.IsDigit(name[0]))
                yield return d;
        }
    }

    private static string ReadName(string pidDir)
    {
        // /proc/<pid>/comm is the kernel thread/exec name (max 15 chars but stable).
        try
        {
            var comm = File.ReadAllText(Path.Combine(pidDir, "comm")).Trim();
            if (!string.IsNullOrEmpty(comm)) return comm;
        }
        catch { /* ignore */ }
        return "";
    }

    private ulong ReadRssBytes(string pidDir)
    {
        try
        {
            // /proc/<pid>/statm: size resident shared ... (in pages)
            var statm = File.ReadAllText(Path.Combine(pidDir, "statm")).Split(' ');
            if (statm.Length >= 2 && ulong.TryParse(statm[1], out var residentPages))
                return residentPages * (ulong)_pageSize;
        }
        catch { /* ignore */ }
        return 0;
    }
}
