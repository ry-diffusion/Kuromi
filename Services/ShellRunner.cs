using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kuromi.Services;

/// <summary>
/// Small helper around <see cref="Process"/> for running command line tools
/// (gsettings, pactl, bluetoothctl, bunx, ...) and capturing their output.
/// Everything is defensive: a missing binary or a non-zero exit never throws,
/// it just yields an empty/failed <see cref="ShellResult"/>.
/// </summary>
public static class ShellRunner
{
    public readonly record struct ShellResult(int ExitCode, string StdOut, string StdErr)
    {
        public bool Success => ExitCode == 0;
        public string Trimmed => StdOut.Trim();
    }

    public static async Task<ShellResult> RunAsync(
        string fileName,
        string[] args,
        int timeoutMs = 8000,
        CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            if (!proc.Start())
                return new ShellResult(-1, "", "failed to start");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { /* ignore */ }
                return new ShellResult(-2, stdout.ToString(), "timeout");
            }

            return new ShellResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return new ShellResult(-3, "", ex.Message);
        }
    }

    /// <summary>Run a command and return only its trimmed stdout (or empty on failure).</summary>
    public static async Task<string> OutAsync(string fileName, params string[] args)
    {
        var r = await RunAsync(fileName, args).ConfigureAwait(false);
        return r.Trimmed;
    }

    /// <summary>Returns true if <paramref name="binary"/> exists in PATH.</summary>
    public static bool Exists(string binary)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(':') ?? Array.Empty<string>();
        foreach (var p in paths)
        {
            try
            {
                var full = System.IO.Path.Combine(p, binary);
                if (System.IO.File.Exists(full)) return true;
            }
            catch { /* ignore */ }
        }
        return false;
    }
}
