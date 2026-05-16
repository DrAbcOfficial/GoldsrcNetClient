using System.Collections.Concurrent;

namespace GoldsrcNetClient.Tui.Services;

public static class GlobalLog
{
    private static readonly ConcurrentQueue<string> Entries = new();

    public static void Write(string entry) => Entries.Enqueue(entry);
    public static bool TryRead(out string? entry) => Entries.TryDequeue(out entry);
}
