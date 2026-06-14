using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Kuromi.Services;

/// <summary>Current media (title/artist/art/status) from the active MPRIS player via playerctl.</summary>
public readonly record struct MediaInfo(
    bool HasPlayer, string Title, string Artist, bool Playing, string? ArtPath);

public class MediaService
{
    private const string Sep = "@@K@@"; // delimiter unlikely to appear in metadata
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // The player we last showed/controlled (chosen by "is playing"), so the transport
    // buttons act on the same one even when several players are registered.
    private string? _activePlayer;

    public bool Available => ShellRunner.Exists("playerctl");

    public async Task<MediaInfo> GetAsync()
    {
        if (!Available) return Empty();

        var player = await PickActivePlayerAsync();
        if (player == null) return Empty();

        var fmt = $"{{{{status}}}}{Sep}{{{{xesam:title}}}}{Sep}{{{{xesam:artist}}}}{Sep}{{{{mpris:artUrl}}}}";
        var r = await ShellRunner.RunAsync("playerctl", new[] { "-p", player, "metadata", "--format", fmt });

        var parts = r.Trimmed.Split(Sep, StringSplitOptions.None);
        var status = parts.Length > 0 ? parts[0] : "";
        var title = parts.Length > 1 ? parts[1] : "";
        var artist = parts.Length > 2 ? parts[2] : "";
        var artUrl = parts.Length > 3 ? parts[3] : "";

        if (string.IsNullOrWhiteSpace(title)) return Empty();

        var artPath = await ResolveArtAsync(artUrl);
        return new MediaInfo(true, title, artist, status == "Playing", artPath);
    }

    /// <summary>Pick the player that is Playing; else one that is Paused; else the first.</summary>
    private async Task<string?> PickActivePlayerAsync()
    {
        var list = await ShellRunner.RunAsync("playerctl", new[] { "-l" });
        var players = list.StdOut.Split('\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (players.Length == 0) { _activePlayer = null; return null; }

        string? playing = null, paused = null;
        foreach (var p in players)
        {
            var status = (await ShellRunner.RunAsync("playerctl", new[] { "-p", p, "status" })).Trimmed;
            if (status == "Playing") { playing = p; break; }
            if (status == "Paused") paused ??= p;
        }

        _activePlayer = playing ?? paused ?? players[0];
        return _activePlayer;
    }

    /// <summary>Fires (UI thread) whenever any player's status/metadata changes.</summary>
    public IDisposable? Watch(Action onChanged)
    {
        if (!Available) return null;
        return new ProcessStreamWatcher("playerctl",
            new[] { "--follow", "--all-players", "metadata", "--format", "{{status}} {{xesam:title}}" },
            _ => Dispatcher.UIThread.Post(onChanged));
    }

    public Task PlayPauseAsync() => ShellRunner.RunAsync("playerctl", WithPlayer("play-pause"));
    public Task NextAsync() => ShellRunner.RunAsync("playerctl", WithPlayer("next"));
    public Task PreviousAsync() => ShellRunner.RunAsync("playerctl", WithPlayer("previous"));

    private string[] WithPlayer(string verb) =>
        _activePlayer != null ? new[] { "-p", _activePlayer, verb } : new[] { verb };

    private static MediaInfo Empty() => new(false, "", "", false, null);

    private static async Task<string?> ResolveArtAsync(string artUrl)
    {
        if (string.IsNullOrWhiteSpace(artUrl)) return null;
        try
        {
            if (artUrl.StartsWith("file://", StringComparison.Ordinal))
                return new Uri(artUrl).LocalPath;

            if (artUrl.StartsWith("http", StringComparison.Ordinal))
            {
                var cache = Path.Combine(ConfigService.CacheDir, $"art-{Hash(artUrl)}.img");
                if (File.Exists(cache)) return cache;
                var bytes = await Http.GetByteArrayAsync(artUrl);
                await File.WriteAllBytesAsync(cache, bytes);
                return cache;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static string Hash(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString()[..16];
    }
}
