using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kuromi.Models;

/// <summary>Parsed, UI-friendly view of `ccusage` output.</summary>
public class ClaudeUsage
{
    public bool HasData { get; set; }
    public string? Error { get; set; }

    // Active 5h billing block.
    public double ActiveCostUsd { get; set; }
    public long ActiveTokens { get; set; }
    public int RemainingMinutes { get; set; }
    public double ProjectedCostUsd { get; set; }
    public long ProjectedTokens { get; set; }
    public double TokensPerMinute { get; set; }
    public double CostPerHour { get; set; }
    public DateTime? BlockStart { get; set; }
    public DateTime? BlockEnd { get; set; }
    public List<string> ActiveModels { get; set; } = new();

    // Daily history for charting (oldest -> newest).
    public List<DailyPoint> Daily { get; set; } = new();

    public double TotalCostUsd { get; set; }
}

public readonly record struct DailyPoint(string Label, double CostUsd, long Tokens);

/// <summary>One usage limit window from the OAuth /usage endpoint.</summary>
public class ClaudeQuotaItem
{
    public string Label { get; set; } = "";
    public double Utilization { get; set; } // 0-100
    public System.DateTimeOffset? ResetsAt { get; set; }
}

// ----- Raw DTOs matching `ccusage --json` -----

public class CcBlocksRoot
{
    [JsonPropertyName("blocks")] public List<CcBlock> Blocks { get; set; } = new();
}

public class CcBlock
{
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("isGap")] public bool IsGap { get; set; }
    [JsonPropertyName("startTime")] public DateTime? StartTime { get; set; }
    [JsonPropertyName("endTime")] public DateTime? EndTime { get; set; }
    [JsonPropertyName("costUSD")] public double CostUsd { get; set; }
    [JsonPropertyName("totalTokens")] public long TotalTokens { get; set; }
    [JsonPropertyName("models")] public List<string> Models { get; set; } = new();
    [JsonPropertyName("burnRate")] public CcBurnRate? BurnRate { get; set; }
    [JsonPropertyName("projection")] public CcProjection? Projection { get; set; }
}

public class CcBurnRate
{
    [JsonPropertyName("costPerHour")] public double CostPerHour { get; set; }
    [JsonPropertyName("tokensPerMinute")] public double TokensPerMinute { get; set; }
}

public class CcProjection
{
    [JsonPropertyName("remainingMinutes")] public int RemainingMinutes { get; set; }
    [JsonPropertyName("totalCost")] public double TotalCost { get; set; }
    [JsonPropertyName("totalTokens")] public long TotalTokens { get; set; }
}

public class CcDailyRoot
{
    [JsonPropertyName("daily")] public List<CcDaily> Daily { get; set; } = new();
    [JsonPropertyName("totals")] public CcTotals? Totals { get; set; }
}

public class CcDaily
{
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("period")] public string? Period { get; set; }
    [JsonPropertyName("totalCost")] public double TotalCost { get; set; }
    [JsonPropertyName("totalTokens")] public long TotalTokens { get; set; }

    public string Label => Date ?? Period ?? "";
}

public class CcTotals
{
    [JsonPropertyName("totalCost")] public double TotalCost { get; set; }
    [JsonPropertyName("totalTokens")] public long TotalTokens { get; set; }
}
