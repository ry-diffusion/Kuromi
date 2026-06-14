using CommunityToolkit.Mvvm.ComponentModel;
using Kuromi.Models;

namespace Kuromi.ViewModels.Widgets;

public partial class AppItemViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty] private string? _iconPath;
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private ulong _memBytes;
    [ObservableProperty] private int _count;

    public AppItemViewModel(ProcessGroup g)
    {
        Name = g.Name;
        Update(g);
    }

    public void Update(ProcessGroup g)
    {
        MemBytes = g.MemBytes;
        Count = g.Count;
        DisplayName = string.IsNullOrWhiteSpace(g.DisplayName) ? Prettify(Name) : g.DisplayName!;
        if (!string.IsNullOrEmpty(g.IconPath)) IconPath = g.IconPath;
    }

    /// <summary>Light fallback for processes with no .desktop: "gnome-shell" -> "Gnome Shell".</summary>
    private static string Prettify(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var words = raw.Replace('-', ' ').Replace('_', ' ').Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            if (words[i].Length > 0 && char.IsLower(words[i][0]))
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        return string.Join(' ', words);
    }

    public string CountText => Count > 1 ? $"×{Count}" : "";

    public bool HasIcon => !string.IsNullOrEmpty(IconPath);
    public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();

    partial void OnIconPathChanged(string? value) => OnPropertyChanged(nameof(HasIcon));
}
