using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kuromi.Services;

namespace Kuromi.ViewModels.Widgets;

public partial class AppListViewModel : ViewModelBase, IDisposable
{
    private const int TopN = 30;
    private readonly ProcessService _service;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<AppItemViewModel> Apps { get; } = new();

    [ObservableProperty] private int _groupCount;
    [ObservableProperty] private bool _busy;

    public AppListViewModel(ProcessService service)
    {
        _service = service;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var groups = await _service.SampleAsync();
            GroupCount = groups.Count;
            var top = groups.Take(TopN).ToList();

            // Merge into the existing collection to keep scroll position stable.
            var byName = Apps.ToDictionary(a => a.Name);
            for (int i = 0; i < top.Count; i++)
            {
                var g = top[i];
                if (byName.TryGetValue(g.Name, out var existing))
                {
                    existing.Update(g);
                    var curIdx = Apps.IndexOf(existing);
                    if (curIdx != i) Apps.Move(curIdx, Math.Min(i, Apps.Count - 1));
                    byName.Remove(g.Name);
                }
                else
                {
                    Apps.Insert(Math.Min(i, Apps.Count), new AppItemViewModel(g));
                }
            }
            // Remove entries that dropped out of the top list.
            foreach (var gone in byName.Values)
                Apps.Remove(gone);
        }
        catch { /* ignore */ }
        finally { Busy = false; }
    }

    public void Dispose() => _timer.Stop();
}
