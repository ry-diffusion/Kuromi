namespace Kuromi;

/// <summary>
/// Code-side mirror of the color module (Styles/Palette.axaml) so view-models and
/// controls don't hard-code hex values. Keep these in sync with the XAML keys.
/// </summary>
public static class Palette
{
    // Usage / status colors (also OkBrush / WarnBrush / CritBrush in XAML).
    public const string Ok = "#2FB872";
    public const string Warn = "#E8983A";
    public const string Crit = "#E5484D";

    /// <summary>Foreground used on top of an accent fill (dark ink).</summary>
    public const string OnAccent = "#1A1622";

    /// <summary>Thresholds for usage coloring.</summary>
    public static string ForUsage(double percent) =>
        percent < 60 ? Ok : percent < 85 ? Warn : Crit;
}
