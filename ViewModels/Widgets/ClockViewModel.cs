using System;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kuromi.ViewModels.Widgets;

public partial class ClockViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly CultureInfo _culture = new("pt-BR");

    [ObservableProperty] private string _time = "--:--";
    [ObservableProperty] private string _seconds = "00";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _greeting = "";

    public ClockViewModel()
    {
        Tick();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        Time = now.ToString("HH:mm", _culture);
        Seconds = now.ToString("ss", _culture);
        Date = _culture.TextInfo.ToTitleCase(now.ToString("dddd, dd 'de' MMMM", _culture));
        Greeting = now.Hour switch
        {
            >= 5 and < 12 => "Bom dia",
            >= 12 and < 18 => "Boa tarde",
            _ => "Boa noite",
        };
    }

    public void Dispose() => _timer.Stop();
}
