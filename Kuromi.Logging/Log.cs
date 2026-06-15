using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Kuromi.Logging;

public enum LogLevel { Trace, Debug, Info, Warn, Error }

/// <summary>A single log record.</summary>
public readonly record struct LogEntry(DateTime Time, LogLevel Level, string Category, string Message, Exception? Error);

/// <summary>A destination for log records (console, file, …).</summary>
public interface ILogSink { void Write(in LogEntry entry); }

/// <summary>A category-scoped logger. Get one via <see cref="Log.For{T}"/>.</summary>
public interface ILog
{
    void Write(LogLevel level, string message, Exception? error = null);
    bool IsEnabled(LogLevel level);
}

/// <summary>
/// Central logging facade for the whole app. Configure once at startup with <see cref="Configure"/>, then
/// grab category loggers anywhere via <see cref="For{T}"/> / <see cref="For(string)"/>. Thread-safe; a
/// broken sink can never crash the app.
/// </summary>
public static class Log
{
    private static LogLevel _min = LogLevel.Info;
    private static readonly List<ILogSink> Sinks = new();
    private static readonly object Gate = new();

    /// <summary>Set the minimum level and (re)build the sinks. Call once at startup.</summary>
    public static void Configure(LogLevel minLevel, string? filePath = null, bool console = true)
    {
        lock (Gate)
        {
            _min = minLevel;
            foreach (var s in Sinks)
                (s as IDisposable)?.Dispose();
            Sinks.Clear();
            if (console)
                Sinks.Add(new ConsoleSink());
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                try { Sinks.Add(new FileSink(filePath!)); }
                catch { /* logging must never break startup */ }
            }
        }
        For("Log").Info($"logging started · min={minLevel}" + (filePath is null ? "" : $" · {filePath}"));
    }

    public static ILog For(string category) => new Logger(category);
    public static ILog For<T>() => new Logger(typeof(T).Name);

    internal static LogLevel MinLevel => _min;

    internal static void Emit(in LogEntry entry)
    {
        lock (Gate)
            foreach (var sink in Sinks)
            {
                try { sink.Write(entry); } catch { /* a broken sink must never crash the app */ }
            }
    }
}

internal sealed class Logger(string category) : ILog
{
    public bool IsEnabled(LogLevel level) => level >= Log.MinLevel;

    public void Write(LogLevel level, string message, Exception? error = null)
    {
        if (level < Log.MinLevel)
            return;
        Log.Emit(new LogEntry(DateTime.Now, level, category, message, error));
    }
}

public static class LogExtensions
{
    public static void Trace(this ILog log, string message) => log.Write(LogLevel.Trace, message);
    public static void Debug(this ILog log, string message) => log.Write(LogLevel.Debug, message);
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this ILog log, string message, Exception? error = null) => log.Write(LogLevel.Warn, message, error);
    public static void Error(this ILog log, string message, Exception? error = null) => log.Write(LogLevel.Error, message, error);

    /// <summary>Trace a step: logs its start and its elapsed time on dispose. Wrap a step in <c>using</c>.</summary>
    public static IDisposable Track(this ILog log, string step) => new StepScope(log, step);
}

internal sealed class StepScope : IDisposable
{
    private readonly ILog _log;
    private readonly string _step;
    private readonly long _start;

    public StepScope(ILog log, string step)
    {
        _log = log;
        _step = step;
        _start = Stopwatch.GetTimestamp();
        _log.Write(LogLevel.Debug, $"→ {step}");
    }

    public void Dispose()
    {
        double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
        _log.Write(LogLevel.Debug, $"✓ {_step} ({ms:0}ms)");
    }
}
