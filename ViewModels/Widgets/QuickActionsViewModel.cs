using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Models;
using Kuromi.Services;

namespace Kuromi.ViewModels.Widgets;

public partial class QuickActionItemViewModel : ObservableObject
{
    public QuickAction Model { get; }
    public QuickActionItemViewModel(QuickAction model) => Model = model;

    public string Label => Model.Label;
    public string Glyph => Model.Glyph;
    public string Accent => Model.Accent;

    [RelayCommand]
    private void Run()
    {
        if (string.IsNullOrWhiteSpace(Model.Command)) return;
        _ = ShellRunner.RunAsync("sh", new[] { "-c", Model.Command }, timeoutMs: 4000);
    }
}

public partial class QuickActionsViewModel : ViewModelBase
{
    private readonly KuromiConfig _config;
    private readonly Action _persist;

    public ObservableCollection<QuickActionItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _editMode;
    [ObservableProperty] private string _newLabel = "";
    [ObservableProperty] private string _newGlyph = "zap";
    [ObservableProperty] private string _newCommand = "";

    public QuickActionsViewModel(KuromiConfig config, Action persist)
    {
        _config = config;
        _persist = persist;
        foreach (var a in _config.QuickActions)
            Items.Add(new QuickActionItemViewModel(a));
    }

    public void SetEditMode(bool on) => EditMode = on;

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewCommand) || string.IsNullOrWhiteSpace(NewLabel)) return;
        var action = new QuickAction
        {
            Label = NewLabel.Trim(),
            Glyph = string.IsNullOrWhiteSpace(NewGlyph) ? "zap" : NewGlyph.Trim(),
            Command = NewCommand.Trim(),
            Accent = NextAccent(),
        };
        _config.QuickActions.Add(action);
        Items.Add(new QuickActionItemViewModel(action));
        NewLabel = ""; NewCommand = ""; NewGlyph = "zap";
        _persist();
    }

    [RelayCommand]
    private void Remove(QuickActionItemViewModel item)
    {
        _config.QuickActions.Remove(item.Model);
        Items.Remove(item);
        _persist();
    }

    private string NextAccent()
    {
        string[] palette = { "#FF7AB6", "#9B8CFF", "#7AD7FF", "#7CFFB2", "#FFB37A", "#FF8A8A" };
        return palette[Items.Count % palette.Length];
    }
}
