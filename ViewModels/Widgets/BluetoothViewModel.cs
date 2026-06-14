using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Models;
using Kuromi.Services;

namespace Kuromi.ViewModels.Widgets;

public partial class BluetoothDeviceItemViewModel : ObservableObject
{
    public BluetoothDeviceInfo Model { get; }
    public BluetoothDeviceItemViewModel(BluetoothDeviceInfo model)
    {
        Model = model;
        _connected = model.Connected;
    }

    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? Model.Mac : Model.Name;
    public string Mac => Model.Mac;
    public bool Paired => Model.Paired;
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private bool _busy;

    /// <summary>Lucide icon name chosen from the bluez Icon hint (and a name fallback).</summary>
    public string IconKind
    {
        get
        {
            var s = (Model.Icon + " " + Model.Name).ToLowerInvariant();
            if (s.Contains("headset") || s.Contains("headphone") || s.Contains("earbud") || s.Contains("airpod")) return "headphones";
            if (s.Contains("speaker") || s.Contains("audio")) return "speaker";
            if (s.Contains("mouse")) return "mouse";
            if (s.Contains("keyboard")) return "keyboard";
            if (s.Contains("gaming") || s.Contains("gamepad") || s.Contains("joystick") || s.Contains("controller")) return "gamepad-2";
            if (s.Contains("watch") || s.Contains("pebble") || s.Contains("band")) return "watch";
            if (s.Contains("phone")) return "smartphone";
            if (s.Contains("computer") || s.Contains("laptop")) return "monitor";
            return "bluetooth";
        }
    }
}

public partial class BluetoothViewModel : ViewModelBase, IDisposable
{
    private readonly BluetoothService _service;
    private readonly IDisposable? _watcher;
    private readonly Avalonia.Threading.DispatcherTimer _debounce;

    public ObservableCollection<BluetoothDeviceItemViewModel> Devices { get; } = new();

    [ObservableProperty] private bool _available;
    [ObservableProperty] private bool _powered;
    [ObservableProperty] private bool _scanning;
    [ObservableProperty] private bool _busy;

    public BluetoothViewModel(BluetoothService service)
    {
        _service = service;
        Available = _service.Available;

        _debounce = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            if (Powered) await RefreshAsync();
            else { Powered = await _service.GetPoweredAsync(); }
        };

        if (Available)
        {
            _ = InitAsync();
            // React to connect/disconnect/pair done outside the app.
            _watcher = _service.Watch(() => { _debounce.Stop(); _debounce.Start(); });
        }
    }

    private async Task InitAsync()
    {
        Powered = await _service.GetPoweredAsync();
        if (Powered) await RefreshAsync();
    }

    [RelayCommand]
    private async Task TogglePower()
    {
        await _service.SetPoweredAsync(!Powered);
        await Task.Delay(400);
        Powered = await _service.GetPoweredAsync();
        if (Powered) await RefreshAsync();
        else Devices.Clear();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Scan()
    {
        if (Scanning) return;
        Scanning = true;
        try
        {
            await _service.ScanAsync(8);
            await RefreshAsync();
        }
        finally { Scanning = false; }
    }

    private async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var list = await _service.ListAsync();
            Devices.Clear();
            foreach (var d in list)
                Devices.Add(new BluetoothDeviceItemViewModel(d));
        }
        catch { /* ignore */ }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task Toggle(BluetoothDeviceItemViewModel item)
    {
        if (item.Busy) return;
        item.Busy = true;
        try
        {
            if (item.Connected)
            {
                await _service.DisconnectAsync(item.Mac);
            }
            else
            {
                if (!item.Paired) await _service.PairAndTrustAsync(item.Mac);
                await _service.ConnectAsync(item.Mac);
            }
            await Task.Delay(600);
            await RefreshAsync();
        }
        finally { item.Busy = false; }
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
    }
}
