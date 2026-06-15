using System;
using System.IO;

namespace Kuromi.Logging;

internal sealed class ConsoleSink : ILogSink
{
    public void Write(in LogEntry e)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = e.Level switch
        {
            LogLevel.Trace => ConsoleColor.DarkGray,
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.Cyan,
            LogLevel.Warn => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => prev,
        };
        Console.WriteLine(Format(e));
        if (e.Error is not null)
            Console.WriteLine(e.Error);
        Console.ForegroundColor = prev;
    }

    internal static string Format(in LogEntry e) =>
        $"{e.Time:HH:mm:ss.fff} {Tag(e.Level)} {e.Category}: {e.Message}";

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "?",
    };
}

/// <summary>Appends to a fresh log file (truncated each run, so the latest session is easy to read).</summary>
internal sealed class FileSink : ILogSink, IDisposable
{
    private readonly StreamWriter _writer;

    public FileSink(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
    }

    public void Write(in LogEntry e)
    {
        _writer.WriteLine(ConsoleSink.Format(e));
        if (e.Error is not null)
            _writer.WriteLine(e.Error);
    }

    public void Dispose() => _writer.Dispose();
}
