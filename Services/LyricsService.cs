using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kuromi.Services;

/// <summary>One lyric line; <see cref="TimeMs"/> is the start time for synced lyrics (0 for plain).</summary>
public record LyricLine(int TimeMs, string Text);

public class LyricsResult
{
    public bool Synced { get; init; }
    public IReadOnlyList<LyricLine> Lines { get; init; } = Array.Empty<LyricLine>();
}

/// <summary>
/// Fetches lyrics from LRCLIB (lrclib.net) — free, no auth — preferring time-synced LRC, matching the
/// approach used by plasmoid-spotify. Tries the exact /api/get first, then falls back to /api/search.
/// </summary>
public class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex Stamp = new(@"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // LRCLIB asks clients to identify themselves.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Kuromi/1.0 (https://github.com/zesmoi/Kuromi)");
        return c;
    }

    public async Task<LyricsResult?> GetAsync(string track, string artist, string album, int durationSec)
    {
        if (string.IsNullOrWhiteSpace(track) || string.IsNullOrWhiteSpace(artist))
            return null;

        // 1) exact match (most reliable for synced lyrics)
        var exact = await TryGet(
            $"https://lrclib.net/api/get?track_name={Esc(track)}&artist_name={Esc(artist)}&album_name={Esc(album)}&duration={durationSec}");
        var fromExact = exact is null ? null : Build(exact);
        if (fromExact is not null)
            return fromExact;

        // 2) fuzzy search — take the first result that has synced lyrics, else the first plain one.
        try
        {
            var json = await Http.GetStringAsync($"https://lrclib.net/api/search?track_name={Esc(track)}&artist_name={Esc(artist)}");
            var hits = JsonSerializer.Deserialize<List<LrcDto>>(json, Json) ?? new();
            var best = hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.SyncedLyrics))
                       ?? hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.PlainLyrics));
            return best is null ? null : Build(best);
        }
        catch { return null; }
    }

    private static async Task<LrcDto?> TryGet(string url)
    {
        try
        {
            var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return null;
            return JsonSerializer.Deserialize<LrcDto>(await resp.Content.ReadAsStringAsync(), Json);
        }
        catch { return null; }
    }

    private static LyricsResult? Build(LrcDto dto)
    {
        if (dto.Instrumental)
            return new LyricsResult { Synced = false, Lines = new[] { new LyricLine(0, "♪ instrumental ♪") } };

        if (!string.IsNullOrWhiteSpace(dto.SyncedLyrics))
        {
            var lines = ParseSynced(dto.SyncedLyrics!);
            if (lines.Count > 0)
                return new LyricsResult { Synced = true, Lines = lines };
        }

        if (!string.IsNullOrWhiteSpace(dto.PlainLyrics))
        {
            var lines = dto.PlainLyrics!
                .Replace("\r\n", "\n").Split('\n')
                .Select(l => new LyricLine(0, l)).ToList();
            return new LyricsResult { Synced = false, Lines = lines };
        }

        return null;
    }

    private static List<LyricLine> ParseSynced(string lrc)
    {
        var result = new List<LyricLine>();
        foreach (var raw in lrc.Replace("\r\n", "\n").Split('\n'))
        {
            var stamps = Stamp.Matches(raw);
            if (stamps.Count == 0)
                continue;
            var text = Stamp.Replace(raw, "").Trim();
            foreach (Match m in stamps)
            {
                int min = int.Parse(m.Groups[1].Value);
                int sec = int.Parse(m.Groups[2].Value);
                int frac = 0;
                if (m.Groups[3].Success)
                {
                    var f = m.Groups[3].Value;
                    frac = f.Length == 2 ? int.Parse(f) * 10 : int.Parse(f.PadRight(3, '0')[..3]);
                }
                result.Add(new LyricLine((min * 60 + sec) * 1000 + frac, text));
            }
        }
        return result.OrderBy(l => l.TimeMs).ToList();
    }

    private static string Esc(string s) => Uri.EscapeDataString(s ?? "");

    private class LrcDto
    {
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
        [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
    }
}
