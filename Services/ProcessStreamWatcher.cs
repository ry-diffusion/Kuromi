using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Kuromi.Services;

/// <summary>
/// Launches a long-running process (e.g. `pactl subscribe`, `gsettings monitor`,
/// `gdbus monitor`) and invokes <paramref name="onLine"/> for each stdout line.
/// Lines arrive on a background thread; callers marshal to the UI thread.
/// </summary>
public sealed class ProcessStreamWatcher : IDisposable
{
    private readonly Process? _proc;

    public ProcessStreamWatcher(string file, string[] args, Action<string> onLine)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
            if (_proc.Start())
                _proc.BeginOutputReadLine();
            else
                _proc = null;
        }
        catch
        {
            _proc = null;
        }
    }

    public bool Started => _proc != null;

    public void Dispose()
    {
        try
        {
            if (_proc is { HasExited: false }) _proc.Kill(true);
            _proc?.Dispose();
        }
        catch { /* ignore */ }
    }
}

/// <summary>Disposes a set of children together.</summary>
public sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _items = new();

    public CompositeDisposable(params IDisposable?[] items)
    {
        foreach (var i in items) if (i != null) _items.Add(i);
    }

    public void Dispose()
    {
        foreach (var i in _items)
        {
            try { i.Dispose(); } catch { /* ignore */ }
        }
        _items.Clear();
    }
}
