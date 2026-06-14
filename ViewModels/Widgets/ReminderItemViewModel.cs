using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Kuromi.Models;

namespace Kuromi.ViewModels.Widgets;

public partial class ReminderItemViewModel : ObservableObject
{
    public Reminder Model { get; }

    public ReminderItemViewModel(Reminder model)
    {
        Model = model;
        _done = model.Done;
    }

    [ObservableProperty] private bool _done;

    partial void OnDoneChanged(bool value) => Model.Done = value;

    public string Text => Model.Text;

    public string DueText
    {
        get
        {
            if (Model.DueAt is not { } due) return "";
            var now = DateTime.Now;
            if (due.Date == now.Date) return $"hoje {due:HH:mm}";
            if (due.Date == now.Date.AddDays(1)) return $"amanhã {due:HH:mm}";
            return due.ToString("dd/MM HH:mm");
        }
    }

    public bool HasDue => Model.DueAt != null;
    public bool Overdue => Model.DueAt is { } d && !Model.Done && d < DateTime.Now;

    /// <summary>Re-evaluate time-dependent bindings (called periodically).</summary>
    public void RefreshTimeState() => OnPropertyChanged(nameof(Overdue));
}
