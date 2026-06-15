using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace Kuromi.Services;

/// <summary>
/// Spotify Web API access via SpotifyAPI-NET using the Authorization Code + PKCE flow (no client secret).
/// The Client ID is configured in Ajustes; the token is persisted to the config dir and refreshed
/// automatically. UI-agnostic: raises <see cref="StateChanged"/> (possibly off the UI thread) when the
/// auth state flips.
/// </summary>
public class SpotifyService
{
    private const int CallbackPort = 5543;
    public static string RedirectUri => $"http://127.0.0.1:{CallbackPort}/callback";

    private static string TokenPath => Path.Combine(ConfigService.ConfigDir, "spotify-token.json");

    private readonly ConfigService _config;
    private string? _clientId;
    private SpotifyClient? _client;
    private EmbedIOAuthServer? _server;

    public SpotifyService(ConfigService config) => _config = config;

    /// <summary>The authenticated client, or null when signed out.</summary>
    public SpotifyClient? Client => _client;
    public bool IsAuthenticated => _client is not null;
    public bool HasClientId => !string.IsNullOrWhiteSpace(_clientId);

    /// <summary>Raised when sign-in/out completes (may fire off the UI thread).</summary>
    public event Action? StateChanged;

    public void SetClientId(string? id) => _clientId = string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    /// <summary>Load a persisted token (if any) and bring up an authenticated client by refreshing it.</summary>
    public async Task InitAsync()
    {
        if (!HasClientId)
            return;
        var refreshToken = LoadRefreshToken();
        if (refreshToken is null)
            return;
        try
        {
            var fresh = await new OAuthClient().RequestToken(new PKCETokenRefreshRequest(_clientId!, refreshToken));
            if (string.IsNullOrEmpty(fresh.RefreshToken))
                fresh.RefreshToken = refreshToken; // Spotify may omit it on refresh
            CreateClient(fresh);
        }
        catch { /* token revoked/invalid — stay signed out */ }
    }

    /// <summary>Interactive PKCE sign-in: opens the browser and captures the loopback redirect.</summary>
    public async Task LoginAsync()
    {
        if (!HasClientId)
            throw new InvalidOperationException("Set the Spotify Client ID in Ajustes first.");

        var (verifier, challenge) = PKCEUtil.GenerateCodes();

        _server?.Dispose();
        _server = new EmbedIOAuthServer(new Uri(RedirectUri), CallbackPort);
        await _server.Start();

        var done = new TaskCompletionSource<bool>();
        _server.AuthorizationCodeReceived += async (_, response) =>
        {
            try
            {
                await _server.Stop();
                var token = await new OAuthClient().RequestToken(
                    new PKCETokenRequest(_clientId!, response.Code, _server.BaseUri, verifier));
                CreateClient(token);
                done.TrySetResult(true);
            }
            catch (Exception ex) { done.TrySetException(ex); }
        };

        var login = new LoginRequest(_server.BaseUri, _clientId!, LoginRequest.ResponseType.Code)
        {
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            Scope = new List<string>
            {
                Scopes.UserReadPrivate,
                Scopes.UserReadPlaybackState,
                Scopes.UserModifyPlaybackState,
                Scopes.UserReadCurrentlyPlaying,
                Scopes.PlaylistReadPrivate,
                Scopes.PlaylistReadCollaborative,
                Scopes.UserReadRecentlyPlayed,
                Scopes.UserLibraryRead,
                Scopes.UserLibraryModify,
            },
        };
        BrowserUtil.Open(login.ToUri());

        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
        timeout.Token.Register(() => done.TrySetCanceled());
        await done.Task;
    }

    public void Logout()
    {
        _client = null;
        try { File.Delete(TokenPath); } catch { /* ignore */ }
        StateChanged?.Invoke();
    }

    private void CreateClient(PKCETokenResponse token)
    {
        SaveRefreshToken(token.RefreshToken);
        var authenticator = new PKCEAuthenticator(_clientId!, token);
        authenticator.TokenRefreshed += (_, t) => SaveRefreshToken(t.RefreshToken);
        _client = new SpotifyClient(SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator));
        StateChanged?.Invoke();
    }

    // Persist only the refresh token (enough to silently re-auth on next launch).
    private sealed class TokenStore { public string RefreshToken { get; set; } = ""; }

    private string? LoadRefreshToken()
    {
        var s = _config.LoadJson<TokenStore?>(TokenPath, () => null);
        return string.IsNullOrEmpty(s?.RefreshToken) ? null : s!.RefreshToken;
    }

    private void SaveRefreshToken(string? rt)
    {
        if (!string.IsNullOrEmpty(rt))
            _config.SaveJson(TokenPath, new TokenStore { RefreshToken = rt! });
    }

    // --- Read --------------------------------------------------------------------------------------
    public Task<CurrentlyPlayingContext?> GetPlaybackAsync() => Safe(c => c.Player.GetCurrentPlayback())!;
    public Task<QueueResponse?> GetQueueAsync() => Safe(c => c.Player.GetQueue())!;

    public async Task<IList<Device>> GetDevicesAsync()
    {
        var r = await Safe(c => c.Player.GetAvailableDevices());
        return r?.Devices ?? new List<Device>();
    }

    public async Task<IList<FullPlaylist>> GetPlaylistsAsync()
    {
        var page = await Safe(c => c.Playlists.CurrentUsers(new PlaylistCurrentUsersRequest { Limit = 50 }));
        return page?.Items ?? new List<FullPlaylist>();
    }

    /// <summary>Album-art URLs of recently played tracks (for the configurable background).</summary>
    public async Task<IList<string>> GetRecentTrackArtsAsync()
    {
        var r = await Safe(c => c.Player.GetRecentlyPlayed(new PlayerRecentlyPlayedRequest { Limit = 40 }));
        return r?.Items?
            .Select(i => i.Track?.Album?.Images?.FirstOrDefault()?.Url)
            .Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct().ToList()
            ?? new List<string>();
    }

    /// <summary>Cover-art URLs of the user's playlists (for the configurable background).</summary>
    public async Task<IList<string>> GetPlaylistArtsAsync()
    {
        var pls = await GetPlaylistsAsync();
        return pls.Select(p => p.Images?.FirstOrDefault()?.Url)
            .Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct().ToList();
    }

    // --- Control -----------------------------------------------------------------------------------
    public Task PlayPauseAsync(bool isPlaying) =>
        Run(c => isPlaying ? c.Player.PausePlayback() : c.Player.ResumePlayback());

    public Task NextAsync() => Run(c => c.Player.SkipNext());
    public Task PreviousAsync() => Run(c => c.Player.SkipPrevious());

    public Task TransferAsync(string deviceId) =>
        Run(c => c.Player.TransferPlayback(new PlayerTransferPlaybackRequest(new List<string> { deviceId }) { Play = true }));

    public Task PlayContextAsync(string contextUri) =>
        Run(c => c.Player.ResumePlayback(new PlayerResumePlaybackRequest { ContextUri = contextUri }));

    public Task SeekAsync(int positionMs) =>
        Run(c => c.Player.SeekTo(new PlayerSeekToRequest(positionMs)));

    public Task SetShuffleAsync(bool on) =>
        Run(c => c.Player.SetShuffle(new PlayerShuffleRequest(on)));

    public Task SetRepeatAsync(PlayerSetRepeatRequest.State state) =>
        Run(c => c.Player.SetRepeat(new PlayerSetRepeatRequest(state)));

    public Task SetVolumeAsync(int percent) =>
        Run(c => c.Player.SetVolume(new PlayerVolumeRequest(Math.Clamp(percent, 0, 100))));

    public Task PlayTrackAsync(string uri) =>
        Run(c => c.Player.ResumePlayback(new PlayerResumePlaybackRequest { Uris = new List<string> { uri } }));

    public Task QueueAsync(string uri) =>
        Run(c => c.Player.AddToQueue(new PlayerAddToQueueRequest(uri)));

    public Task SaveTrackAsync(string id) =>
        Run(c => c.Library.SaveItems(new LibrarySaveItemsRequest(new List<string> { $"spotify:track:{id}" })));

    public Task UnsaveTrackAsync(string id) =>
        Run(c => c.Library.RemoveItems(new LibraryRemoveItemsRequest(new List<string> { $"spotify:track:{id}" })));

    public async Task<bool> IsSavedAsync(string id)
    {
        var r = await Safe(c => c.Library.CheckItems(new LibraryCheckItemsRequest(new List<string> { $"spotify:track:{id}" })));
        return r is { Count: > 0 } && r[0];
    }

    public async Task<IList<FullTrack>> SearchTracksAsync(string query)
    {
        var r = await Safe(c => c.Search.Item(new SearchRequest(SearchRequest.Types.Track, query) { Limit = 24 }));
        return r?.Tracks?.Items ?? new List<FullTrack>();
    }

    // --- helpers -----------------------------------------------------------------------------------
    private async Task<T?> Safe<T>(Func<SpotifyClient, Task<T>> call) where T : class
    {
        if (_client is null) return null;
        try { return await call(_client); }
        catch { return null; }
    }

    private async Task Run(Func<SpotifyClient, Task> call)
    {
        if (_client is null) return;
        try { await call(_client); } catch { /* no active device, etc. */ }
    }
}
