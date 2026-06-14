namespace Kuromi.Models;

/// <summary>A single snapshot of system resource usage. All percentages are 0-100.</summary>
public readonly record struct SystemSnapshot
{
    public double CpuPercent { get; init; }
    public double[] PerCoreCpu { get; init; }
    public double MemPercent { get; init; }
    public ulong MemUsedBytes { get; init; }
    public ulong MemTotalBytes { get; init; }
    public double SwapPercent { get; init; }
    public ulong SwapUsedBytes { get; init; }
    public ulong SwapTotalBytes { get; init; }

    /// <summary>-1 when GPU usage is not available.</summary>
    public double GpuPercent { get; init; }
    public string GpuName { get; init; }
    public double GpuMemPercent { get; init; }

    public static SystemSnapshot Empty => new()
    {
        PerCoreCpu = System.Array.Empty<double>(),
        GpuPercent = -1,
        GpuName = "",
    };
}
