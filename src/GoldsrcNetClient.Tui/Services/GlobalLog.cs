using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Tui.Services;

/// <summary>Global in-memory log sink consumed by the UI's log panel.</summary>
public static class GlobalLog
{
    private static readonly ConcurrentQueue<string> Entries = new();

    public static void Write(string entry) => Entries.Enqueue(entry);
    public static bool TryRead(out string? entry) => Entries.TryDequeue(out entry);
}

/// <summary>ILogger adapter that funnels Core diagnostics into <see cref="GlobalLog"/>.</summary>
public sealed class GlobalLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        GlobalLog.Write($"[Core/{logLevel}] {message}");
    }
}
