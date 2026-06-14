using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Models;
using Kuromi.Services;

namespace Kuromi.ViewModels.Widgets;

public partial class RemindersViewModel : ViewModelBase, IDisposable
{
    private readonly ReminderService _service;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<ReminderItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _newText = "";
    [ObservableProperty] private int _pendingCount;

    public RemindersViewModel(ReminderService service)
    {
        _service = service;
        foreach (var r in _service.Load().OrderBy(r => r.DueAt ?? DateTime.MaxValue))
            Items.Add(new ReminderItemViewModel(r));
        Recount();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => CheckDue();
        _timer.Start();
    }

    [RelayCommand]
    private void Add(string? offsetMinutes)
    {
        var text = NewText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        DateTime? due = null;
        if (int.TryParse(offsetMinutes, out var min) && min > 0)
            due = DateTime.Now.AddMinutes(min);

        var model = new Reminder { Text = text, DueAt = due };
        Items.Insert(0, new ReminderItemViewModel(model));
        NewText = "";
        Persist();
        Recount();
    }

    [RelayCommand]
    private void Remove(ReminderItemViewModel item)
    {
        Items.Remove(item);
        Persist();
        Recount();
    }

    [RelayCommand]
    private void ClearDone()
    {
        foreach (var done in Items.Where(i => i.Done).ToList())
            Items.Remove(done);
        Persist();
        Recount();
    }

    public void ToggleDone(ReminderItemViewModel item)
    {
        item.Done = !item.Done;
        Persist();
        Recount();
    }

    private void CheckDue()
    {
        foreach (var item in Items)
        {
            var m = item.Model;
            if (m.DueAt is { } due && !m.Done && !m.Notified && due <= DateTime.Now)
            {
                m.Notified = true;
                Notify(m.Text);
            }
            item.RefreshTimeState();
        }
        Persist();
    }

    private static void Notify(string text)
    {
        if (ShellRunner.Exists("notify-send"))
            _ = ShellRunner.RunAsync("notify-send", new[] { "-a", "Kuromi", "Lembrete", text });
    }

    private void Recount() => PendingCount = Items.Count(i => !i.Done);

    private void Persist() => _service.Save(Items.Select(i => i.Model).ToList());

    public void Dispose() => _timer.Stop();
}
