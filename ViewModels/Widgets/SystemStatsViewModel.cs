using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kuromi.Models;
using Kuromi.Services;

namespace Kuromi.ViewModels.Widgets;

public partial class SystemStatsViewModel : ViewModelBase, IDisposable
{
    private const int HistoryLength = 60;
    private readonly SystemMonitorService _monitor;
    private readonly DispatcherTimer _timer;

    private readonly List<double> _cpuHist = new();
    private readonly List<double> _memHist = new();
    private readonly List<double> _gpuHist = new();

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memPercent;
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private bool _gpuAvailable;
    [ObservableProperty] private string _gpuName = "GPU";
    [ObservableProperty] private string _memText = "";
    [ObservableProperty] private string _swapText = "";
    [ObservableProperty] private double _swapPercent;

    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _memHistory = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _gpuHistory = Array.Empty<double>();

    public SystemStatsViewModel(SystemMonitorService monitor)
    {
        _monitor = monitor;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _timer.Tick += async (_, _) => await SampleAsync();
        _timer.Start();
        _ = SampleAsync();
    }

    private async System.Threading.Tasks.Task SampleAsync()
    {
        SystemSnapshot s;
        try { s = await _monitor.SampleAsync(); }
        catch { return; }

        CpuPercent = Math.Round(s.CpuPercent, 1);
        MemPercent = Math.Round(s.MemPercent, 1);
        SwapPercent = Math.Round(s.SwapPercent, 1);
        GpuName = string.IsNullOrEmpty(s.GpuName) ? "GPU" : s.GpuName;
        GpuAvailable = s.GpuPercent >= 0;
        GpuPercent = GpuAvailable ? Math.Round(s.GpuPercent, 1) : 0;

        MemText = $"{Human(s.MemUsedBytes)} / {Human(s.MemTotalBytes)}";
        SwapText = s.SwapTotalBytes > 0
            ? $"{Human(s.SwapUsedBytes)} / {Human(s.SwapTotalBytes)}"
            : "sem swap";

        Push(_cpuHist, CpuPercent); CpuHistory = _cpuHist.ToArray();
        Push(_memHist, MemPercent); MemHistory = _memHist.ToArray();
        if (GpuAvailable) { Push(_gpuHist, GpuPercent); GpuHistory = _gpuHist.ToArray(); }
    }

    private static void Push(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > HistoryLength) list.RemoveAt(0);
    }

    private static string Human(ulong bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double b = bytes; int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return $"{b:0.#} {u[i]}";
    }

    public void Dispose() => _timer.Stop();
}
