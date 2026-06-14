using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Kuromi.Models;

namespace Kuromi.Services;

/// <summary>
/// Reads the Claude Code OAuth token from ~/.claude/.credentials.json and queries
/// the Anthropic usage endpoint for the real quota windows (5h, 7d, Sonnet, Opus).
/// C# port of the user's `claude-quota` shell script.
/// </summary>
public class ClaudeQuotaService
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private static string CredentialsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    public async Task<List<ClaudeQuotaItem>> FetchAsync()
    {
        var result = new List<ClaudeQuotaItem>();
        try
        {
            var token = ReadToken();
            if (string.IsNullOrEmpty(token)) return result;

            using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            req.Headers.TryAddWithoutValidation("User-Agent", "claude-code/2.0.37");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Add(root, "five_hour", "5 horas", result);
            Add(root, "seven_day", "7 dias", result);
            Add(root, "seven_day_sonnet", "7 dias · Sonnet", result);
            Add(root, "seven_day_opus", "7 dias · Opus", result);
        }
        catch { /* offline / token expired -> empty */ }
        return result;
    }

    private static void Add(JsonElement root, string key, string label, List<ClaudeQuotaItem> into)
    {
        if (!root.TryGetProperty(key, out var e) || e.ValueKind != JsonValueKind.Object) return;
        if (!e.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return;

        DateTimeOffset? reset = null;
        if (e.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(ra.GetString(), out var parsed))
            reset = parsed;

        into.Add(new ClaudeQuotaItem
        {
            Label = label,
            Utilization = u.GetDouble(),
            ResetsAt = reset,
        });
    }

    private static string? ReadToken()
    {
        try
        {
            if (!File.Exists(CredentialsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.TryGetProperty("accessToken", out var tok))
                return tok.GetString();
        }
        catch { /* ignore */ }
        return null;
    }
}
