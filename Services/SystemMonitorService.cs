using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kuromi.Models;

namespace Kuromi.Services;

/// <summary>
/// Reads CPU (/proc/stat), memory (/proc/meminfo) and best-effort GPU usage.
/// GPU works for NVIDIA (nvidia-smi) and AMD (sysfs gpu_busy_percent); on Intel
/// it reports the adapter name and tries intel_gpu_top, otherwise GpuPercent = -1.
/// </summary>
public class SystemMonitorService
{
    private ulong[] _prevIdle = Array.Empty<ulong>();
    private ulong[] _prevTotal = Array.Empty<ulong>();
    private string _gpuName = "";
    private string _gpuMode = ""; // "nvidia" | "amd:<path>" | "intel-fdinfo" | "none"

    // Intel fdinfo engine accounting (busy nanoseconds, summed per drm-client-id).
    // Order: render, copy, video, video-enhance.
    private readonly long[] _prevEng = new long[4];
    private long _prevEngineTick;

    public SystemMonitorService() => DetectGpu();

    public async Task<SystemSnapshot> SampleAsync()
    {
        var (cpu, cores) = ReadCpu();
        var (memPct, memUsed, memTotal, swapPct, swapUsed, swapTotal) = ReadMem();
        var (gpuPct, gpuMem) = await ReadGpuAsync();

        return new SystemSnapshot
        {
            CpuPercent = cpu,
            PerCoreCpu = cores,
            MemPercent = memPct,
            MemUsedBytes = memUsed,
            MemTotalBytes = memTotal,
            SwapPercent = swapPct,
            SwapUsedBytes = swapUsed,
            SwapTotalBytes = swapTotal,
            GpuPercent = gpuPct,
            GpuMemPercent = gpuMem,
            GpuName = _gpuName,
        };
    }

    // ---------------- CPU ----------------

    private (double total, double[] cores) ReadCpu()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/stat");
            var cpuLines = lines.Where(l => l.StartsWith("cpu", StringComparison.Ordinal)).ToArray();
            // cpuLines[0] is the aggregate "cpu", the rest are per-core "cpuN".
            if (_prevIdle.Length != cpuLines.Length)
            {
                _prevIdle = new ulong[cpuLines.Length];
                _prevTotal = new ulong[cpuLines.Length];
            }

            var usages = new double[cpuLines.Length];
            for (int i = 0; i < cpuLines.Length; i++)
            {
                var parts = cpuLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                ulong idle = 0, total = 0;
                for (int j = 1; j < parts.Length; j++)
                {
                    if (!ulong.TryParse(parts[j], out var v)) continue;
                    total += v;
                    if (j == 4 || j == 5) idle += v; // idle + iowait
                }
                var dTotal = total - _prevTotal[i];
                var dIdle = idle - _prevIdle[i];
                usages[i] = dTotal > 0 ? Math.Clamp((1.0 - (double)dIdle / dTotal) * 100.0, 0, 100) : 0;
                _prevIdle[i] = idle;
                _prevTotal[i] = total;
            }

            var cores = usages.Skip(1).ToArray();
            return (usages.Length > 0 ? usages[0] : 0, cores);
        }
        catch
        {
            return (0, Array.Empty<double>());
        }
    }

    // ---------------- Memory ----------------

    private static (double memPct, ulong used, ulong total, double swapPct, ulong swapUsed, ulong swapTotal) ReadMem()
    {
        try
        {
            var dict = new Dictionary<string, ulong>();
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                var idx = line.IndexOf(':');
                if (idx < 0) continue;
                var key = line[..idx];
                var rest = line[(idx + 1)..].Trim().Split(' ');
                if (ulong.TryParse(rest[0], out var kb))
                    dict[key] = kb * 1024UL; // kB -> bytes
            }

            ulong total = dict.GetValueOrDefault("MemTotal");
            ulong avail = dict.GetValueOrDefault("MemAvailable");
            ulong used = total > avail ? total - avail : 0;
            double memPct = total > 0 ? (double)used / total * 100.0 : 0;

            ulong swapTotal = dict.GetValueOrDefault("SwapTotal");
            ulong swapFree = dict.GetValueOrDefault("SwapFree");
            ulong swapUsed = swapTotal > swapFree ? swapTotal - swapFree : 0;
            double swapPct = swapTotal > 0 ? (double)swapUsed / swapTotal * 100.0 : 0;

            return (memPct, used, total, swapPct, swapUsed, swapTotal);
        }
        catch
        {
            return (0, 0, 0, 0, 0, 0);
        }
    }

    // ---------------- GPU ----------------

    private void DetectGpu()
    {
        if (ShellRunner.Exists("nvidia-smi"))
        {
            _gpuMode = "nvidia";
        }
        else
        {
            // AMD exposes gpu_busy_percent in sysfs.
            try
            {
                foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*"))
                {
                    var busy = Path.Combine(card, "device", "gpu_busy_percent");
                    if (File.Exists(busy)) { _gpuMode = "amd:" + busy; break; }
                }
            }
            catch { /* ignore */ }
            // Intel (i915): no sysfs busy %, but DRM fdinfo exposes engine busy ns.
            if (_gpuMode == "" && HasI915Fdinfo())
                _gpuMode = "intel-fdinfo";
        }
        if (_gpuMode == "") _gpuMode = "none";

        // Name via lspci (one-shot, best effort).
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("lspci")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p != null)
            {
                var outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1500);
                var m = Regex.Match(outp, @"VGA compatible controller:\s*(.+)");
                if (m.Success)
                {
                    var name = m.Groups[1].Value.Trim();
                    name = Regex.Replace(name, @"^(Intel Corporation|Advanced Micro Devices, Inc\.|NVIDIA Corporation)\s*", "");
                    name = Regex.Replace(name, @"\s*\(rev .+?\)", "");
                    _gpuName = name.Trim();
                }
            }
        }
        catch { /* ignore */ }
        if (string.IsNullOrEmpty(_gpuName)) _gpuName = "GPU";
    }

    private static bool HasI915Fdinfo()
    {
        try
        {
            foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*"))
            {
                var driver = Path.Combine(card, "device", "driver");
                if (Directory.Exists(driver) || File.Exists(driver))
                {
                    // resolve the driver symlink name
                    var target = new FileInfo(driver).LinkTarget ?? "";
                    if (target.Contains("i915") || target.Contains("xe")) return true;
                }
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private async Task<(double pct, double mem)> ReadGpuAsync()
    {
        try
        {
            switch (_gpuMode)
            {
                case "nvidia":
                {
                    var r = await ShellRunner.RunAsync("nvidia-smi", new[]
                    {
                        "--query-gpu=utilization.gpu,memory.used,memory.total",
                        "--format=csv,noheader,nounits",
                    });
                    var parts = r.Trimmed.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length >= 3 &&
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var u) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mu) &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var mt))
                        return (u, mt > 0 ? mu / mt * 100 : -1);
                    break;
                }
                case var s when s.StartsWith("amd:"):
                {
                    var txt = await File.ReadAllTextAsync(s["amd:".Length..]);
                    if (double.TryParse(txt.Trim(), out var pct)) return (pct, -1);
                    break;
                }
                case "intel-fdinfo":
                {
                    return (ReadIntelFdinfo(), -1);
                }
            }
        }
        catch { /* ignore */ }
        return (-1, -1);
    }

    private static readonly string[] EngineKeys =
        { "drm-engine-render:", "drm-engine-copy:", "drm-engine-video:", "drm-engine-video-enhance:" };

    /// <summary>
    /// Intel/i915 GPU utilization the way Resources/nvtop do it: sum each engine's
    /// busy-nanoseconds across unique DRM clients (deduped by drm-client-id) and
    /// divide the delta by elapsed wall time. Returns the busiest engine's %.
    /// </summary>
    private double ReadIntelFdinfo()
    {
        try
        {
            // engine index -> (client-id -> cumulative ns)
            var perEngine = new Dictionary<long, long>[4];
            for (int i = 0; i < 4; i++) perEngine[i] = new Dictionary<long, long>();

            foreach (var procDir in Directory.EnumerateDirectories("/proc"))
            {
                var pidName = Path.GetFileName(procDir);
                if (pidName.Length == 0 || !char.IsDigit(pidName[0])) continue;

                var fdDir = Path.Combine(procDir, "fd");
                string[] fds;
                try { fds = Directory.GetFiles(fdDir); }
                catch { continue; }

                foreach (var fd in fds)
                {
                    try
                    {
                        var target = new FileInfo(fd).LinkTarget;
                        if (target == null || !target.Contains("/dev/dri/")) continue;

                        var fdNum = Path.GetFileName(fd);
                        var fdinfo = Path.Combine(procDir, "fdinfo", fdNum);
                        ParseFdinfo(fdinfo, perEngine);
                    }
                    catch { /* fd vanished */ }
                }
            }

            long nowTick = Environment.TickCount64;
            double deltaMs = nowTick - _prevEngineTick;
            double best = 0;

            for (int i = 0; i < 4; i++)
            {
                long total = 0;
                foreach (var v in perEngine[i].Values) total += v;
                if (_prevEngineTick > 0 && deltaMs > 0)
                {
                    double deltaNs = total - _prevEng[i];
                    double pct = deltaNs / (deltaMs * 1_000_000.0) * 100.0;
                    if (pct > best) best = pct;
                }
                _prevEng[i] = total;
            }

            _prevEngineTick = nowTick;
            return _prevEngineTick == nowTick && deltaMs <= 0 ? 0 : Math.Clamp(best, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private static void ParseFdinfo(string path, Dictionary<long, long>[] perEngine)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return; }

        bool i915 = false;
        long clientId = -1;
        var values = new long[4];
        bool[] seen = new bool[4];

        foreach (var line in lines)
        {
            if (line.StartsWith("drm-driver:", StringComparison.Ordinal))
                i915 = line.Contains("i915") || line.Contains("xe");
            else if (line.StartsWith("drm-client-id:", StringComparison.Ordinal))
                long.TryParse(Digits(line), out clientId);
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    if (!seen[i] && line.StartsWith(EngineKeys[i], StringComparison.Ordinal))
                    {
                        if (long.TryParse(Digits(line), out var ns)) { values[i] = ns; seen[i] = true; }
                        break;
                    }
                }
            }
        }

        if (!i915 || clientId < 0) return;
        for (int i = 0; i < 4; i++)
        {
            // dedup duplicate fds of the same client: keep the max cumulative value.
            if (!perEngine[i].TryGetValue(clientId, out var cur) || values[i] > cur)
                perEngine[i][clientId] = values[i];
        }
    }

    private static string Digits(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0) return "";
        var rest = line[(colon + 1)..].Trim();
        int end = 0;
        while (end < rest.Length && char.IsDigit(rest[end])) end++;
        return rest[..end];
    }
}
