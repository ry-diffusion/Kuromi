using System.Collections.Generic;

namespace Kuromi.Models;

/// <summary>The kinds of widgets the user can place on the dashboard.</summary>
public enum WidgetKind
{
    Clock,
    SystemStats,
    Reminders,
    ClaudeUsage,
    AppList,
    QuickActions,
    Bluetooth,
    SystemControls,
    Media,
}

/// <summary>A placed widget: kind + cell position/span on the bento grid.</summary>
public class WidgetConfig
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public WidgetKind Kind { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public int ColSpan { get; set; } = 3;
    public int RowSpan { get; set; } = 2;
}

/// <summary>A user-defined quick action button (runs a shell command).</summary>
public class QuickAction
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Ação";
    public string Glyph { get; set; } = "★";
    public string Command { get; set; } = "";
    public string Accent { get; set; } = "#FF7AB6";
}

/// <summary>Root persisted configuration (~/.config/kuromi/config.json).</summary>
public class KuromiConfig
{
    /// <summary>Bumped when the layout schema changes so old configs are regenerated.
    /// Defaults to 0 so a legacy file (no Version field) is detected and rebuilt.</summary>
    public int Version { get; set; }
    public const int CurrentVersion = 3;

    public int GridColumns { get; set; } = 12;
    public int GridRows { get; set; } = 8;

    /// <summary>GNOME-friendly screenshot: runs the bundled helper that captures via
    /// the XDG portal and copies the image straight to the clipboard (+ saves a copy).</summary>
    public const string ScreenshotCommand =
        "python3 \"${XDG_CONFIG_HOME:-$HOME/.config}/kuromi/screenshot-clipboard.py\"";

    public List<WidgetConfig> Widgets { get; set; } = new();
    public List<QuickAction> QuickActions { get; set; } = new();
    public bool UseDarkWallpaper { get; set; } = true;
    public double BlurRadius { get; set; } = 34;
    public double OverlayOpacity { get; set; } = 0.30;
    /// <summary>Derive accent colors from the wallpaper.</summary>
    public bool AccentFromWallpaper { get; set; } = true;

    // --- Liquid-glass tuning (live-adjustable from Ajustes; the cards bind via DynamicResource) ---
    public double GlassRefraction { get; set; } = 72;       // RefractionAmount
    public double GlassRefractionHeight { get; set; } = 32; // RefractionHeight
    public bool GlassDepth { get; set; } = true;            // DepthEffect
    public bool GlassChromatic { get; set; } = true;        // ChromaticAberration
    public double GlassBlur { get; set; } = 2;              // BlurRadius
    public double GlassVibrancy { get; set; } = 1.2;        // Vibrancy
    public double GlassBrightness { get; set; }             // Brightness (0)
    public double GlassContrast { get; set; } = 1;          // Contrast
    public double GlassHighlight { get; set; } = 0.5;       // HighlightOpacity
    public bool GlassShadow { get; set; } = true;           // ShadowEnabled
    public double GlassShadowRadius { get; set; } = 24;     // ShadowRadius
    /// <summary>One-time guard so the screenshot quick action is added only once.</summary>
    public bool ScreenshotActionSeeded { get; set; }
    /// <summary>One-time guard for shrinking the media widget to a single row.</summary>
    public bool MediaCompacted { get; set; }

    public static KuromiConfig CreateDefault()
    {
        // Clean 3-column bento on a 12x8 grid (no overlaps, fills the screen).
        return new KuromiConfig
        {
            Version = CurrentVersion,
            ScreenshotActionSeeded = true,
            MediaCompacted = true,
            Widgets =
            {
                new WidgetConfig { Kind = WidgetKind.Clock,          Col = 0, Row = 0, ColSpan = 4, RowSpan = 2 },
                new WidgetConfig { Kind = WidgetKind.Reminders,      Col = 0, Row = 2, ColSpan = 4, RowSpan = 3 },
                new WidgetConfig { Kind = WidgetKind.AppList,        Col = 0, Row = 5, ColSpan = 4, RowSpan = 3 },
                new WidgetConfig { Kind = WidgetKind.SystemStats,    Col = 4, Row = 0, ColSpan = 4, RowSpan = 3 },
                new WidgetConfig { Kind = WidgetKind.QuickActions,   Col = 4, Row = 3, ColSpan = 4, RowSpan = 2 },
                new WidgetConfig { Kind = WidgetKind.SystemControls, Col = 4, Row = 5, ColSpan = 4, RowSpan = 3 },
                new WidgetConfig { Kind = WidgetKind.ClaudeUsage,    Col = 8, Row = 0, ColSpan = 4, RowSpan = 3 },
                new WidgetConfig { Kind = WidgetKind.Media,          Col = 8, Row = 3, ColSpan = 4, RowSpan = 1 },
                new WidgetConfig { Kind = WidgetKind.Bluetooth,      Col = 8, Row = 4, ColSpan = 4, RowSpan = 4 },
            },
            QuickActions =
            {
                new QuickAction { Label = "Bloquear",      Glyph = "lock",     Command = "loginctl lock-session", Accent = "#9B8CFF" },
                new QuickAction { Label = "Configurações", Glyph = "settings", Command = "gnome-control-center", Accent = "#7AD7FF" },
                new QuickAction { Label = "Terminal",      Glyph = "terminal", Command = "kgx", Accent = "#7CFFB2" },
                new QuickAction { Label = "Suspender",     Glyph = "power",    Command = "systemctl suspend", Accent = "#FFB37A" },
                new QuickAction { Label = "Captura",       Glyph = "camera",   Command = ScreenshotCommand, Accent = "#FF8AB0" },
            },
        };
    }
}
