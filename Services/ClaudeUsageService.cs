using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kuromi.Models;

namespace Kuromi.Services;

/// <summary>
/// Runs `ccusage` (via bunx/bun x/npx) and turns its JSON into a <see cref="ClaudeUsage"/>.
/// The first call may be slow while bunx downloads the package; results are meant
/// to be polled on a relaxed interval.
/// </summary>
public class ClaudeUsageService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private (string bin, string[] prefix)? _runner;

    private (string bin, string[] prefix) ResolveRunner()
    {
        if (_runner != null) return _runner.Value;
        if (ShellRunner.Exists("bunx")) _runner = ("bunx", Array.Empty<string>());
        else if (ShellRunner.Exists("bun")) _runner = ("bun", new[] { "x" });
        else if (ShellRunner.Exists("npx")) _runner = ("npx", new[] { "-y" });
        else _runner = ("ccusage", new[] { "__direct__" });
        return _runner.Value;
    }

    public async Task<ClaudeUsage> FetchAsync()
    {
        var usage = new ClaudeUsage();
        try
        {
            var (bin, prefix) = ResolveRunner();
            bool direct = prefix.FirstOrDefault() == "__direct__";

            string[] BuildArgs(params string[] cmd) =>
                direct ? cmd : prefix.Concat(new[] { "ccusage@latest" }).Concat(cmd).ToArray();

            var blocksRes = await ShellRunner.RunAsync(direct ? "ccusage" : bin,
                BuildArgs("blocks", "--json"), timeoutMs: 60000);

            if (!blocksRes.Success && string.IsNullOrWhiteSpace(blocksRes.StdOut))
            {
                usage.Error = "ccusage indisponível";
                return usage;
            }

            ParseBlocks(blocksRes.StdOut, usage);

            var dailyRes = await ShellRunner.RunAsync(direct ? "ccusage" : bin,
                BuildArgs("daily", "--json"), timeoutMs: 60000);
            ParseDaily(dailyRes.StdOut, usage);

            usage.HasData = true;
        }
        catch (Exception ex)
        {
            usage.Error = ex.Message;
        }
        return usage;
    }

    private static void ParseBlocks(string json, ClaudeUsage usage)
    {
        var jsonStart = json.IndexOf('{');
        if (jsonStart < 0) return;
        var root = JsonSerializer.Deserialize<CcBlocksRoot>(json[jsonStart..], JsonOpts);
        if (root == null) return;

        var active = root.Blocks.FirstOrDefault(b => b.IsActive)
                     ?? root.Blocks.LastOrDefault(b => !b.IsGap);
        if (active == null) return;

        usage.ActiveCostUsd = active.CostUsd;
        usage.ActiveTokens = active.TotalTokens;
        usage.ActiveModels = active.Models;
        usage.BlockStart = active.StartTime?.ToLocalTime();
        usage.BlockEnd = active.EndTime?.ToLocalTime();
        if (active.BurnRate != null)
        {
            usage.TokensPerMinute = active.BurnRate.TokensPerMinute;
            usage.CostPerHour = active.BurnRate.CostPerHour;
        }
        if (active.Projection != null)
        {
            usage.RemainingMinutes = active.Projection.RemainingMinutes;
            usage.ProjectedCostUsd = active.Projection.TotalCost;
            usage.ProjectedTokens = active.Projection.TotalTokens;
        }
    }

    private static void ParseDaily(string json, ClaudeUsage usage)
    {
        var jsonStart = json.IndexOf('{');
        if (jsonStart < 0) return;
        var root = JsonSerializer.Deserialize<CcDailyRoot>(json[jsonStart..], JsonOpts);
        if (root == null) return;

        usage.Daily = root.Daily
            .TakeLast(14)
            .Select(d => new DailyPoint(ShortLabel(d.Label), d.TotalCost, d.TotalTokens))
            .ToList();

        if (root.Totals != null) usage.TotalCostUsd = root.Totals.TotalCost;
    }

    private static string ShortLabel(string label)
    {
        // "2026-06-13" -> "06/13"
        if (label.Length >= 10 && label[4] == '-')
            return label.Substring(5).Replace('-', '/');
        return label;
    }
}
