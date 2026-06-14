using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Models;
using Kuromi.Services;
using Kuromi.ViewModels.Widgets;

namespace Kuromi.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly KuromiConfig _config;

    public ObservableCollection<WidgetViewModel> Widgets { get; } = new();

    /// <summary>Bubbled up from SystemControls so the window can reload the wallpaper.</summary>
    public event Action? WallpaperShouldRefresh;

    [ObservableProperty] private bool _editMode;

    public int GridColumns => _config.GridColumns;
    public int GridRows => _config.GridRows;

    public IReadOnlyList<WidgetKind> AllKinds { get; } =
        Enum.GetValues<WidgetKind>().ToArray();

    public DashboardViewModel(AppServices services, KuromiConfig config)
    {
        _services = services;
        _config = config;
        foreach (var wc in _config.Widgets)
            Widgets.Add(Build(wc));
    }

    partial void OnEditModeChanged(bool value)
    {
        foreach (var w in Widgets)
            if (w.Content is QuickActionsViewModel q)
                q.SetEditMode(value);
    }

    private WidgetViewModel Build(WidgetConfig wc)
    {
        ViewModelBase content = wc.Kind switch
        {
            WidgetKind.Clock          => new ClockViewModel(),
            WidgetKind.SystemStats    => new SystemStatsViewModel(_services.Monitor),
            WidgetKind.Reminders      => new RemindersViewModel(_services.Reminders),
            WidgetKind.ClaudeUsage    => new ClaudeUsageViewModel(_services.Claude, _services.ClaudeQuota),
            WidgetKind.AppList        => new AppListViewModel(_services.Processes),
            WidgetKind.QuickActions   => new QuickActionsViewModel(_config, Persist),
            WidgetKind.Bluetooth      => new BluetoothViewModel(_services.Bluetooth),
            WidgetKind.SystemControls => BuildControls(),
            WidgetKind.Media          => new MediaViewModel(_services.Media),
            _                         => new ClockViewModel(),
        };
        return new WidgetViewModel(wc, content);
    }

    private SystemControlsViewModel BuildControls()
    {
        var vm = new SystemControlsViewModel(_services.Desktop);
        vm.WallpaperShouldRefresh += () => WallpaperShouldRefresh?.Invoke();
        return vm;
    }

    [RelayCommand]
    private void AddWidget(WidgetKind kind)
    {
        var wc = new WidgetConfig
        {
            Kind = kind,
            Col = 0, Row = 0,
            ColSpan = 4,
            RowSpan = kind is WidgetKind.Clock ? 2 : 3,
        };
        _config.Widgets.Add(wc);
        var w = Build(wc);
        if (w.Content is QuickActionsViewModel q) q.SetEditMode(EditMode);
        Widgets.Add(w);
        Persist();
    }

    [RelayCommand]
    private void RemoveWidget(WidgetViewModel widget)
    {
        _config.Widgets.Remove(widget.Config);
        Widgets.Remove(widget);
        (widget.Content as IDisposable)?.Dispose();
        Persist();
    }

    [RelayCommand]
    private void ToggleEdit() => EditMode = !EditMode;

    public void Persist() => _services.Config.Save(_config);
}
