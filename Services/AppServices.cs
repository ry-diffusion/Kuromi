using Kuromi.Logging;
using Kuromi.Services.Desktop;

namespace Kuromi.Services;

/// <summary>
/// Simple composition root holding the long-lived services so widgets can share
/// them (no DI container needed for a single-window kiosk).
/// </summary>
public class AppServices
{
    public ConfigService Config { get; }
    public IDesktopBackend Desktop { get; }
    public WallpaperService Wallpaper { get; }
    public SystemMonitorService Monitor { get; }
    public IconResolver Icons { get; }
    public ProcessService Processes { get; }
    public ClaudeUsageService Claude { get; }
    public ClaudeQuotaService ClaudeQuota { get; }
    public MediaService Media { get; }
    public ReminderService Reminders { get; }
    public BluetoothService Bluetooth { get; }
    public SpotifyService Spotify { get; }
    public LyricsService Lyrics { get; }

    public AppServices()
    {
        var log = Log.For<AppServices>();
        log.Info("composing services");
        Config = new ConfigService();
        Desktop = DesktopBackends.Detect();
        log.Info($"desktop backend: {Desktop.Name}");
        Wallpaper = new WallpaperService(Desktop);
        Monitor = new SystemMonitorService();
        Icons = new IconResolver();
        Processes = new ProcessService(Icons);
        Claude = new ClaudeUsageService();
        ClaudeQuota = new ClaudeQuotaService();
        Media = new MediaService();
        Reminders = new ReminderService(Config);
        Bluetooth = new BluetoothService();
        Spotify = new SpotifyService(Config);
        Lyrics = new LyricsService();
        log.Info("services ready");
    }
}
