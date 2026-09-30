using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using MinorShift.Emuera.Runtime.Script.Loader;

namespace MinorShift.Emuera.GameProc;

public sealed record ProcessInitializePhase(string Name, long ElapsedMilliseconds, long AllocatedBytes);

public sealed record ProcessInitializeProfile(
    long TotalElapsedMilliseconds,
    long TotalAllocatedBytes,
    int ProcessorCount,
    int ThreadPoolMaxWorkerThreads,
    int ThreadPoolAvailableWorkerThreads,
    int ErbPrimaryParseThreadCount,
    int ErbScriptParseThreadCount,
    long ErbEnumerationMilliseconds,
    long ErbPrimaryParseMilliseconds,
    long ErbLabelSetupMilliseconds,
    long ErbScriptParseMilliseconds,
    IReadOnlyList<ProcessInitializePhase> Phases);

internal sealed class ProcessInitializeProfileCollector
{
    readonly Stopwatch stopwatch = Stopwatch.StartNew();
    readonly long allocationStart = GC.GetTotalAllocatedBytes(false);
    readonly List<ProcessInitializePhase> phases = [];
    long lastElapsed;
    long lastAllocated;

    internal void CompletePhase(string name)
    {
        long elapsed = stopwatch.ElapsedMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(false) - allocationStart;
        phases.Add(new(name, elapsed - lastElapsed, Math.Max(0, allocated - lastAllocated)));
        lastElapsed = elapsed;
        lastAllocated = allocated;
    }

    internal ProcessInitializeProfile Complete(ErbLoader? loader)
    {
        CompletePhase("title-state");
        ThreadPool.GetMaxThreads(out int maxWorkers, out _);
        ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
        return new(
            stopwatch.ElapsedMilliseconds,
            Math.Max(0, GC.GetTotalAllocatedBytes(false) - allocationStart),
            Environment.ProcessorCount,
            maxWorkers,
            availableWorkers,
            loader?.PrimaryParseThreadCount ?? 0,
            loader?.ScriptParseThreadCount ?? 0,
            loader?.EnumerationMilliseconds ?? 0,
            loader?.PrimaryParseMilliseconds ?? 0,
            loader?.LabelSetupMilliseconds ?? 0,
            loader?.ScriptParseMilliseconds ?? 0,
            phases.ToArray());
    }
}
