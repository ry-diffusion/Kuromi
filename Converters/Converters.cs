using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Kuromi.Converters;

/// <summary>Formats a byte count (long/ulong/double) as "1.2 GB".</summary>
public class BytesToHumanConverter : IValueConverter
{
    public static readonly BytesToHumanConverter Instance = new();
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double bytes = value switch
        {
            ulong u => u,
            long l => l,
            int i => i,
            double d => d,
            _ => 0,
        };
        int unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1) { bytes /= 1024; unit++; }
        return $"{bytes:0.#} {Units[unit]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="Kuromi.Models.WidgetKind"/> to its pt-BR title.</summary>
public class WidgetKindToTitleConverter : IValueConverter
{
    public static readonly WidgetKindToTitleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Kuromi.Models.WidgetKind k
            ? Kuromi.ViewModels.WidgetViewModel.Describe(k).title
            : value?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="Kuromi.Models.BackgroundSource"/> to its pt-BR label.</summary>
public class BackgroundSourceToLabelConverter : IValueConverter
{
    public static readonly BackgroundSourceToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Kuromi.Models.BackgroundSource.Wallpaper => "Wallpaper",
            Kuromi.Models.BackgroundSource.CurrentTrack => "Música atual",
            Kuromi.Models.BackgroundSource.Playlists => "Playlists",
            Kuromi.Models.BackgroundSource.RecentTracks => "Tocadas recentemente",
            _ => value?.ToString(),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps an <see cref="Kuromi.Models.AccentSource"/> to its pt-BR label.</summary>
public class AccentSourceToLabelConverter : IValueConverter
{
    public static readonly AccentSourceToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Kuromi.Models.AccentSource.Wallpaper => "Wallpaper",
            Kuromi.Models.AccentSource.CurrentTrack => "Música atual",
            _ => value?.ToString(),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a "#RRGGBB" string to a <see cref="Avalonia.Media.SolidColorBrush"/>.</summary>
public class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Avalonia.Media.Color.TryParse(s, out var c))
            return new Avalonia.Media.SolidColorBrush(c);
        return Avalonia.Media.Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Loads a file-system image path into a cached <see cref="Bitmap"/>.</summary>
public class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        return Cache.GetOrAdd(path, p =>
        {
            try { return System.IO.File.Exists(p) ? new Bitmap(p) : null; }
            catch { return null; }
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
