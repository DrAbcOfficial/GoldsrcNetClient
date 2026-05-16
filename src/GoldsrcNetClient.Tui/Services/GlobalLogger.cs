using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Tui.Services;

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
