#if R0_E2
#nullable enable
using System;
using System.Diagnostics;
using System.Threading;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal readonly record struct R0E2MemorySnapshot(long CumulativeAllocatedBytes,
    long ManagedLiveBeforeForcedGc, long ManagedLiveAfterForcedGc, long WorkingSetBytes,
    long PrivateBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);

internal static class R0E2Measurement
{
    private static long processStart;
    private static long b0;

    internal static void MarkProcessStart() => processStart = Stopwatch.GetTimestamp();
    internal static void MarkB0() => Interlocked.CompareExchange(ref b0, Stopwatch.GetTimestamp(), 0);
    internal static double ProcessToB0Milliseconds => Milliseconds(b0 - processStart);
    internal static double SinceB0Milliseconds => Milliseconds(Stopwatch.GetTimestamp() - b0);
    internal static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    internal static R0E2MemorySnapshot CaptureFinal()
    {
        var before = GC.GetTotalMemory(false);
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        var after = GC.GetTotalMemory(false);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        process.Refresh();
        return new(GC.GetTotalAllocatedBytes(true), before, after, process.WorkingSet64,
            process.PrivateMemorySize64, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }
}
#endif
