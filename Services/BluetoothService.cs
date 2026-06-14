using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kuromi.Models;

namespace Kuromi.Services;

/// <summary>Thin wrapper over <c>bluetoothctl</c> for the Bluetooth widget.</summary>
public class BluetoothService
{
    public bool Available => ShellRunner.Exists("bluetoothctl");

    /// <summary>Fires (on the UI thread) when any org.bluez object/property changes.</summary>
    public IDisposable? Watch(Action onChanged)
    {
        if (!ShellRunner.Exists("gdbus")) return null;
        return new ProcessStreamWatcher("gdbus",
            new[] { "monitor", "--system", "--dest", "org.bluez" },
            _ => Dispatcher.UIThread.Post(onChanged));
    }

    public async Task<bool> GetPoweredAsync()
    {
        var r = await ShellRunner.RunAsync("bluetoothctl", new[] { "show" });
        foreach (var line in r.StdOut.Split('\n'))
            if (line.TrimStart().StartsWith("Powered:", StringComparison.Ordinal))
                return line.Contains("yes");
        return false;
    }

    public Task SetPoweredAsync(bool on)
        => ShellRunner.RunAsync("bluetoothctl", new[] { "power", on ? "on" : "off" });

    /// <summary>Scan for the given number of seconds (blocking inside bluetoothctl).</summary>
    public Task ScanAsync(int seconds = 8)
        => ShellRunner.RunAsync("bluetoothctl", new[] { "--timeout", seconds.ToString(), "scan", "on" },
            timeoutMs: (seconds + 3) * 1000);

    public async Task<List<BluetoothDeviceInfo>> ListAsync()
    {
        var listRes = await ShellRunner.RunAsync("bluetoothctl", new[] { "devices" });
        var macs = new List<(string mac, string name)>();
        foreach (var line in listRes.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "Device AA:BB:CC:DD:EE:FF Name Here"
            var parts = line.Trim().Split(' ', 3);
            if (parts.Length >= 2 && parts[0] == "Device")
                macs.Add((parts[1], parts.Length >= 3 ? parts[2] : parts[1]));
        }

        var devices = await Task.WhenAll(macs.Select(async m =>
        {
            var info = await ShellRunner.RunAsync("bluetoothctl", new[] { "info", m.mac });
            var dev = new BluetoothDeviceInfo { Mac = m.mac, Name = m.name };
            foreach (var raw in info.StdOut.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("Name:", StringComparison.Ordinal)) dev.Name = line[5..].Trim();
                else if (line.StartsWith("Paired:", StringComparison.Ordinal)) dev.Paired = line.Contains("yes");
                else if (line.StartsWith("Connected:", StringComparison.Ordinal)) dev.Connected = line.Contains("yes");
                else if (line.StartsWith("Trusted:", StringComparison.Ordinal)) dev.Trusted = line.Contains("yes");
                else if (line.StartsWith("Icon:", StringComparison.Ordinal)) dev.Icon = line[5..].Trim();
            }
            return dev;
        }));

        return devices
            .OrderByDescending(d => d.Connected)
            .ThenByDescending(d => d.Paired)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public Task ConnectAsync(string mac)
        => ShellRunner.RunAsync("bluetoothctl", new[] { "connect", mac }, timeoutMs: 15000);

    public Task DisconnectAsync(string mac)
        => ShellRunner.RunAsync("bluetoothctl", new[] { "disconnect", mac }, timeoutMs: 10000);

    public async Task PairAndTrustAsync(string mac)
    {
        await ShellRunner.RunAsync("bluetoothctl", new[] { "pair", mac }, timeoutMs: 20000);
        await ShellRunner.RunAsync("bluetoothctl", new[] { "trust", mac });
    }

    public Task RemoveAsync(string mac)
        => ShellRunner.RunAsync("bluetoothctl", new[] { "remove", mac });
}
