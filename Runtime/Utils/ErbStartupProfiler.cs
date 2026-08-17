#if PERFORMANCE_METRICS
using MinorShift.Emuera.Runtime.Script.Statements;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace MinorShift.Emuera.Runtime.Utils;

// Development-only ERB profiling. Timing and instruction counters are deliberately separate modes.
internal static class ErbStartupProfiler
{
    internal enum Mode { Off, Timing, Counters }

    static readonly object Sync = new();
    static ConcurrentBag<ErbStartupFileProfile> files = [];
    static ThreadLocal<ErbStartupScriptTotals> threadTotals = new(() => new ErbStartupScriptTotals(), true);
    static string outputDirectory;
    static Mode mode;
    static int scriptWallMilliseconds, labelCount, initiallyParsedLabelCount, remainingLabelCount, parallelLabelCount;
    internal static bool Enabled => Volatile.Read(ref outputDirectory) != null;
    internal static bool TimingEnabled => mode == Mode.Timing;
    internal static bool CountersEnabled => mode == Mode.Counters;

    internal static void Configure(string directory, string requestedMode)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        mode = requestedMode?.ToLowerInvariant() switch { "timing" => Mode.Timing, "counters" => Mode.Counters, _ => Mode.Off };
        if (mode == Mode.Off) return;
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        lock (Sync)
        {
            files = [];
            threadTotals.Dispose();
            threadTotals = new ThreadLocal<ErbStartupScriptTotals>(() => new ErbStartupScriptTotals(), true);
            scriptWallMilliseconds = labelCount = initiallyParsedLabelCount = remainingLabelCount = parallelLabelCount = 0;
            Volatile.Write(ref outputDirectory, directory);
        }
    }

    internal static ErbStartupFileProfile BeginFile(string filename) => Enabled ? new(filename) : null;
    internal static void CompleteFile(ErbStartupFileProfile profile)
    {
        if (profile == null) return;
        profile.ElapsedTicks = Stopwatch.GetTimestamp() - profile.StartTimestamp;
        files.Add(profile);
    }
    internal static void RecordFunction(ErbStartupFunctionProfile profile)
    {
        if (profile != null) threadTotals.Value.Add(profile);
    }
    internal static void RecordArgument(FunctionCode code, bool forced)
    {
        if (!CountersEnabled) return;
        threadTotals.Value.RecordArgument(code, forced);
    }
    internal static void SetScriptInfo(int labels, int initiallyParsed, int remaining, int parallel)
    {
        labelCount = labels; initiallyParsedLabelCount = initiallyParsed; remainingLabelCount = remaining; parallelLabelCount = parallel;
    }
    internal static void SetScriptWallMilliseconds(long milliseconds) => scriptWallMilliseconds = (int)milliseconds;

    internal static void Write()
    {
        string directory = Volatile.Read(ref outputDirectory);
        if (directory == null) return;
        ErbStartupFileProfile[] snapshot = files.ToArray();
        ErbStartupScriptTotals totals = new();
        foreach (ErbStartupScriptTotals item in threadTotals.Values) totals.Add(item);

        using (var primary = new StreamWriter(Path.Combine(directory, "primary-parse.txt"), false))
        {
            primary.WriteLine("ERB PrimaryParse counters and longest observed loadErb wall spans; parallelism, GC, and scheduling mean spans are not per-file CPU cost.");
            primary.WriteLine($"Mode\t{mode}");
            primary.WriteLine($"Files\t{snapshot.Length}");
            primary.WriteLine($"PhysicalLines\t{snapshot.Sum(file => file.PhysicalLines)}");
            primary.WriteLine($"ReadEnabledLineReturns\t{snapshot.Sum(file => file.ReadEnabledLineReturns)}");
            primary.WriteLine($"LogicalLines\t{snapshot.Sum(file => file.LogicalLines)}");
            primary.WriteLine($"LabelLines\t{snapshot.Sum(file => file.LabelLines)}");
            primary.WriteLine($"SharpLines\t{snapshot.Sum(file => file.SharpLines)}");
            primary.WriteLine($"ScriptLines\t{snapshot.Sum(file => file.ScriptLines)}");
            primary.WriteLine($"PreprocessorLines\t{snapshot.Sum(file => file.PreprocessorLines)}");
            primary.WriteLine($"RenameInputLines\t{snapshot.Sum(file => file.RenameInputLines)}");
            primary.WriteLine($"RenameCandidates\t{snapshot.Sum(file => file.RenameCandidates)}");
        }
        using (var spans = new StreamWriter(Path.Combine(directory, "longest-observed-loadErb-wall-spans.tsv"), false))
        {
            spans.WriteLine("rank\telapsedMilliseconds\tphysicalLines\tlogicalLines\treadEnabledLineReturns\tlabelLines\tsharpLines\tscriptLines\tfile");
            int rank = 0;
            foreach (ErbStartupFileProfile file in snapshot.OrderByDescending(file => file.ElapsedTicks).Take(20))
                spans.WriteLine(string.Join('\t', ++rank, Milliseconds(file.ElapsedTicks), file.PhysicalLines, file.LogicalLines, file.ReadEnabledLineReturns, file.LabelLines, file.SharpLines, file.ScriptLines, file.Filename));
        }
        using var script = new StreamWriter(Path.Combine(directory, mode == Mode.Timing ? "script-timing.txt" : "script-counters.txt"), false);
        script.WriteLine($"Mode\t{mode}");
        script.WriteLine("Timing values are aggregated elapsed spans, not CPU time; parallel functions can make totals exceed wall time.");
        script.WriteLine($"WallMilliseconds\t{scriptWallMilliseconds}");
        script.WriteLine($"Functions\t{totals.FunctionCount}");
        if (mode == Mode.Timing)
        {
            script.WriteLine($"FunctionAggregatedElapsedMilliseconds\t{Milliseconds(totals.FunctionTicks)}");
            script.WriteLine($"setArgumentAggregatedElapsedMilliseconds\t{Milliseconds(totals.SetArgumentTicks)}");
            script.WriteLine($"nestCheckAggregatedElapsedMilliseconds\t{Milliseconds(totals.NestCheckTicks)}");
            script.WriteLine($"setJumpToAggregatedElapsedMilliseconds\t{Milliseconds(totals.SetJumpToTicks)}");
            script.WriteLine($"OtherAggregatedElapsedMilliseconds\t{Milliseconds(totals.FunctionTicks - totals.SetArgumentTicks - totals.NestCheckTicks - totals.SetJumpToTicks)}");
        }
        else
        {
            script.WriteLine($"SetArgumentToCalls\t{totals.SetArgumentCalls}");
            script.WriteLine($"ForceSetArgumentCalls\t{totals.ForceSetArgumentCalls}");
            script.WriteLine("FunctionCode\tSetArgumentToCalls");
            for (int i = 0; i < totals.ArgumentCounts.Length; i++)
                if (totals.ArgumentCounts[i] != 0)
                    script.WriteLine($"{(FunctionCode)i}\t{totals.ArgumentCounts[i]}");
        }
        script.WriteLine($"Labels\t{labelCount}");
        script.WriteLine($"InitiallyParsedLabels\t{initiallyParsedLabelCount}");
        script.WriteLine($"RemainingLabels\t{remainingLabelCount}");
        script.WriteLine($"ParallelRemainingLabels\t{parallelLabelCount}");
    }

static string Milliseconds(long ticks) => (ticks * 1000.0 / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture);
}

internal sealed class ErbStartupFileProfile
{
    internal ErbStartupFileProfile(string filename) { Filename = filename; StartTimestamp = Stopwatch.GetTimestamp(); }
    internal readonly string Filename; internal readonly long StartTimestamp; internal long ElapsedTicks;
    internal long PhysicalLines, ReadEnabledLineReturns, LogicalLines, LabelLines, SharpLines, ScriptLines, PreprocessorLines, RenameInputLines, RenameCandidates;
}
internal sealed class ErbStartupFunctionProfile { internal long TotalTicks, SetArgumentTicks, NestCheckTicks, SetJumpToTicks; }
internal sealed class ErbStartupScriptTotals
{
    internal long FunctionCount, FunctionTicks, SetArgumentTicks, NestCheckTicks, SetJumpToTicks, SetArgumentCalls, ForceSetArgumentCalls;
    internal readonly long[] ArgumentCounts = new long[Enum.GetValues<FunctionCode>().Max(code => (int)code) + 1];
    internal void RecordArgument(FunctionCode code, bool forced) { SetArgumentCalls++; if (forced) ForceSetArgumentCalls++; ArgumentCounts[(int)code]++; }
    internal void Add(ErbStartupFunctionProfile other) { FunctionCount++; FunctionTicks += other.TotalTicks; SetArgumentTicks += other.SetArgumentTicks; NestCheckTicks += other.NestCheckTicks; SetJumpToTicks += other.SetJumpToTicks; }
    internal void Add(ErbStartupScriptTotals other)
    {
        FunctionCount += other.FunctionCount; FunctionTicks += other.FunctionTicks; SetArgumentTicks += other.SetArgumentTicks; NestCheckTicks += other.NestCheckTicks; SetJumpToTicks += other.SetJumpToTicks; SetArgumentCalls += other.SetArgumentCalls; ForceSetArgumentCalls += other.ForceSetArgumentCalls;
        for (int i = 0; i < ArgumentCounts.Length; i++) ArgumentCounts[i] += other.ArgumentCounts[i];
    }
}
#endif
