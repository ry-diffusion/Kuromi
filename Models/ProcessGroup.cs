namespace Kuromi.Models;

/// <summary>
/// Aggregated information for all processes that share the same executable name.
/// </summary>
public class ProcessGroup
{
    public string Name { get; set; } = "";
    /// <summary>Pretty name from the matching .desktop (Name=), when available.</summary>
    public string? DisplayName { get; set; }
    public int Count { get; set; }
    public ulong MemBytes { get; set; }
    public double CpuPercent { get; set; }
    /// <summary>Absolute path to a cached PNG icon, or null when none was resolved.</summary>
    public string? IconPath { get; set; }
}
