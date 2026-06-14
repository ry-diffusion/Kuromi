using CommunityToolkit.Mvvm.ComponentModel;
using Kuromi.Models;

namespace Kuromi.ViewModels;

/// <summary>
/// A placed dashboard tile: chrome metadata (title/glyph), cell position/span on
/// the bento grid, and the actual content view-model rendered inside it.
/// </summary>
public partial class WidgetViewModel : ObservableObject
{
    public WidgetConfig Config { get; }
    public WidgetKind Kind => Config.Kind;
    public ViewModelBase Content { get; }
    public string Title { get; }
    public string IconKind { get; }

    [ObservableProperty] private int _col;
    [ObservableProperty] private int _row;
    [ObservableProperty] private int _colSpan;
    [ObservableProperty] private int _rowSpan;

    public WidgetViewModel(WidgetConfig config, ViewModelBase content)
    {
        Config = config;
        Content = content;
        _col = config.Col;
        _row = config.Row;
        _colSpan = config.ColSpan;
        _rowSpan = config.RowSpan;
        (Title, IconKind) = Describe(config.Kind);
    }

    partial void OnColChanged(int value) => Config.Col = value;
    partial void OnRowChanged(int value) => Config.Row = value;
    partial void OnColSpanChanged(int value) => Config.ColSpan = value;
    partial void OnRowSpanChanged(int value) => Config.RowSpan = value;

    public static (string title, string iconKind) Describe(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock          => ("Relógio", "clock"),
        WidgetKind.SystemStats    => ("Sistema", "cpu"),
        WidgetKind.Reminders      => ("Lembretes", "list-checks"),
        WidgetKind.ClaudeUsage    => ("Claude", "sparkles"),
        WidgetKind.AppList        => ("Aplicativos", "layout-grid"),
        WidgetKind.QuickActions   => ("Ações rápidas", "zap"),
        WidgetKind.Bluetooth      => ("Bluetooth", "bluetooth"),
        WidgetKind.SystemControls => ("Controles", "sliders-horizontal"),
        WidgetKind.Media          => ("Mídia", "music"),
        _ => ("Widget", "layout-grid"),
    };
}
