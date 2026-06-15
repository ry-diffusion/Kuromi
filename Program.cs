using Avalonia;
using Kuromi.Glass;
using Kuromi.Logging;
using Kuromi.Services;
using System;
using System.IO;

namespace Kuromi;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Log.Configure(
#if DEBUG
            LogLevel.Debug,
#else
            LogLevel.Info,
#endif
            Path.Combine(ConfigService.CacheDir, "kuromi.log"));

        var log = Log.For("App");
        log.Info("starting Kuromi");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            log.Info("Kuromi exited");
        }
        catch (Exception ex)
        {
            log.Error("fatal: unhandled exception, app is crashing", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .UseKuromiGlass()
            .LogToTrace();
}
