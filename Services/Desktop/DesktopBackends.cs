using System;

namespace Kuromi.Services.Desktop;

public static class DesktopBackends
{
    /// <summary>Pick a backend based on XDG_CURRENT_DESKTOP / DESKTOP_SESSION.</summary>
    public static IDesktopBackend Detect()
    {
        var desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")
                       ?? Environment.GetEnvironmentVariable("DESKTOP_SESSION")
                       ?? "").ToLowerInvariant();

        if (desktop.Contains("kde") || desktop.Contains("plasma"))
            return new KdeBackend();

        // GNOME, and a sensible default for GNOME-derived shells (Cinnamon, Unity, ...)
        return new GnomeBackend();
    }
}
