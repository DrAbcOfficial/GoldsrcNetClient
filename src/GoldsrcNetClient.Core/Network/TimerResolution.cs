using System.Runtime.InteropServices;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Raises the Windows system timer resolution to 1 ms while held — the same
/// thing the engine does. Without it <c>Task.Delay(10)</c> sleeps 15-60 ms, the
/// keepalive cadence develops silence gaps above the server's
/// <c>sv_failuretime</c> (0.05 s), the server stops draining its reliable
/// stream towards us, and the connection is dropped with
/// <c>"Reliable channel overflowed"</c>. Reference counted so nested
/// connections share one <c>timeBeginPeriod</c> call; a no-op off Windows.
/// </summary>
internal static partial class TimerResolution
{
    private static int _refs;

    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint period);

    public static void Acquire()
    {
        if (!OperatingSystem.IsWindows())
            return;
        lock (typeof(TimerResolution))
        {
            if (_refs++ == 0)
                timeBeginPeriod(1);
        }
    }

    public static void Release()
    {
        if (!OperatingSystem.IsWindows())
            return;
        lock (typeof(TimerResolution))
        {
            if (_refs > 0 && --_refs == 0)
                timeEndPeriod(1);
        }
    }
}
