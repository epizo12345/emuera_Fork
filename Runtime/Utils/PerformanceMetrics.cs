using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MinorShift.Emuera.Runtime.Utils;

/// <summary>
/// [Emuera改修:MEASURE-01]
/// 起動やマクロの「どこに時間が掛かったか」を調べる開発用の計測器。
/// --BenchmarkLog 指定時のみJSON Linesへ結果を書き、通常実行ではファイルを作成しない。
/// PERFORMANCE_METRICSを付けない通常版では高頻度メソッドがコンパイル時に呼出側から消えるため、
/// プレイヤーが使うEXEへ細かなStopwatch計測の負荷を持ち込まない。
/// 参照: プロジェクト資料/06_コード案内.md
/// </summary>
internal static class PerformanceMetrics
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, double> StartupMarks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static long processStartTimestamp = Stopwatch.GetTimestamp();
    private static string logPath;
    private static int macroActive;

    private static string macroText;
    private static long macroStartTimestamp;
    private static long allocatedBytesBefore;
    private static long managedBytesBefore;
    private static long workingSetBefore;
    private static int gen0Before;
    private static int gen1Before;
    private static int gen2Before;

    private static long expansionTicks;
    private static long inputHandoffTicks;
    private static long inputLoopTicks;
    private static long erbTicks;
    private static long strFormTicks;
    private static long displayBuildTicks;
    private static long displayAddTicks;
    private static long measureTextTicks;
    private static long refreshTicks;
    private static long paintTicks;
    private static long scrollTicks;
    private static long loopEventTicks;
    private static long refreshEventTicks;
    private static long awaitTicks;
    private static long awaitRequestedMilliseconds;
    private static long randomTraceHash;
    private static int expandedInputCount;
    private static int inputDispatchCount;
    private static int erbRunCount;
    private static int refreshCount;
    private static int paintCount;
    private static int scrollUpdateCount;
    private static int uiEventPumpCount;
    private static int strFormCount;
    private static int displayBuildCount;
    private static int measureTextCount;
    private static int randomCallCount;
    private static int awaitCount;
    private static int nextDispatchAttempts;
    private static int nextDispatchFallbacks;
    private static readonly Dictionary<string, (int Count, long Ticks)> nextDispatchStages = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> nextDispatchRejections = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> nextDispatchFunctions = new(StringComparer.Ordinal);
#if PERFORMANCE_METRICS
    internal readonly record struct MeasurementToken(long Ticks, long AllocatedBytes);
    private static readonly Dictionary<string, (int Count, long Ticks, long AllocatedBytes, long MaxTicks)> nextStartupStages = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (int Count, long Ticks, long AllocatedBytes, long MaxTicks)> nextConstructionStages = new(StringComparer.Ordinal);
    private static long nextStartupEnvelopeTicks;
    private static long nextStartupEnvelopeAllocatedBytes;
    private static long nextStartupChildTicks;
    private static long nextStartupChildAllocatedBytes;
    private static int nextProgramCardinalityCount;
    private static int nextRuntimeFunctionCount;
    private static int nextDescriptorCount;
    private static int nextTotalCodeLength;
    private static int nextStructuralLinkCount;
    private static int nextLoopCount;
    private static int nextCallSiteCount;
    private static int nextExpressionTargetCount;
    private static int nextCallArgumentPayloadAdditionCount;
    private static long nextCallArgumentPrefixScanElementVisits;
    private static long nextCallArgumentPrefixScanTicks;
    private static string nextDispatchProfilePath;
    private static long nextDispatchProfileStart;
    private static long nextDispatchProfileAllocated;
    private static readonly string[] NextDispatchStageNames = ["DispatchTotal", "ReadinessLookup", "SessionStartRejectClassification", "FrameImportPreparation", "FrameBindingPreparation", "SessionConstruction", "VmExecution"];
    private static readonly long[] nextDispatchStageTicks = new long[NextDispatchStageNames.Length];
    private static readonly int[] nextDispatchStageCounts = new int[NextDispatchStageNames.Length];
    private static readonly Dictionary<(int FunctionId, MinorShift.Emuera.GameProc.NextRuntimeDispatchRejectReason Reason), (string Name, int Count, long Ticks)> nextDispatchBuckets = new();
    private static readonly Dictionary<string, (int Count, long RejectPathTicks, long ClassificationTicks, bool Mutable)> nextSessionStartRejectSubreasons = new(StringComparer.Ordinal);
    private static readonly Dictionary<(int FunctionId, string Subreason), (string Name, int Count, long RejectPathTicks)> nextSessionStartRejectFunctionBuckets = new();
    private static long entryDispatchAlreadyConsumedCount;
    private static long entryDispatchAlreadyConsumedTicks;
    private static long actionableDispatchTicks;
    private static long wholeDispatchSeamTicks;
    private static int nextReadinessRuntimeFunctionCount;
    private static long nextReadinessCodeInstructionVisits;
    private static long nextReadinessSemanticRecordVisits;
    private static long nextReadinessHostIdentityVisits;
    private static long nextReadinessHostIdentityLookupCount;
    private static long nextReadinessHostIdentityLookupHitCount;
    private static long nextReadinessHostIdentityLookupMissCount;
    private static long nextReadinessHostIdentityDirectIndexLookupCount;
    private static long nextReadinessHostIdentityLinearScanElementVisits;
    private static long nextReadinessHostIdentityPresentedElementCount;
    private static int nextReadinessMaxHostIdentitiesLength;
    private static long nextReadinessVariableLookupCount;
    private static long nextReadinessVariableLinearScanElementVisits;
    private static long nextReadinessCallLookupCount;
    private static long nextReadinessCallLinearScanElementVisits;
    private static long nextReadinessRequirementCandidateCount;
    private static long nextReadinessRequirementDedupLookupCount;
    private static long nextReadinessRequirementOutputCount;
#endif

    internal static bool Enabled => Volatile.Read(ref logPath) != null;
    internal static bool NextDispatchProfileEnabled
    {
        get
        {
#if PERFORMANCE_METRICS
            return !string.IsNullOrWhiteSpace(nextDispatchProfilePath);
#else
            return false;
#endif
        }
    }
    internal static bool MacroActive => Volatile.Read(ref macroActive) != 0;

    internal static void MarkProcessStart()
    {
        processStartTimestamp = Stopwatch.GetTimestamp();
    }

    internal static void Configure(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        Volatile.Write(ref logPath, fullPath);
        lock (Sync)
        {
            StartupMarks.Clear();
            StartupMarks["ProcessStart"] = 0;
        }
    }

    internal static void ConfigureNextDispatchProfile(string path)
    {
#if PERFORMANCE_METRICS
        if (string.IsNullOrWhiteSpace(path)) return;
        nextDispatchProfilePath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(nextDispatchProfilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        nextDispatchProfileStart = Stopwatch.GetTimestamp();
        nextDispatchProfileAllocated = GC.GetTotalAllocatedBytes(false);
        nextStartupStages.Clear();
        nextConstructionStages.Clear();
        nextStartupEnvelopeTicks = 0;
        nextStartupEnvelopeAllocatedBytes = 0;
        nextStartupChildTicks = 0;
        nextStartupChildAllocatedBytes = 0;
        nextProgramCardinalityCount = 0;
        nextCallArgumentPayloadAdditionCount = 0;
        nextCallArgumentPrefixScanElementVisits = 0;
        nextCallArgumentPrefixScanTicks = 0;
        nextReadinessRuntimeFunctionCount = 0;
        nextReadinessCodeInstructionVisits = 0;
        nextReadinessSemanticRecordVisits = 0;
        nextReadinessHostIdentityVisits = 0;
        nextReadinessHostIdentityLookupCount = 0;
        nextReadinessHostIdentityLookupHitCount = 0;
        nextReadinessHostIdentityLookupMissCount = 0;
        nextReadinessHostIdentityDirectIndexLookupCount = 0;
        nextReadinessHostIdentityLinearScanElementVisits = 0;
        nextReadinessHostIdentityPresentedElementCount = 0;
        nextReadinessMaxHostIdentitiesLength = 0;
        nextReadinessVariableLookupCount = 0;
        nextReadinessVariableLinearScanElementVisits = 0;
        nextReadinessCallLookupCount = 0;
        nextReadinessCallLinearScanElementVisits = 0;
        nextReadinessRequirementCandidateCount = 0;
        nextReadinessRequirementDedupLookupCount = 0;
        nextReadinessRequirementOutputCount = 0;
        entryDispatchAlreadyConsumedCount = 0;
        entryDispatchAlreadyConsumedTicks = 0;
        actionableDispatchTicks = 0;
        wholeDispatchSeamTicks = 0;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteNextDispatchProfile();
#endif
    }

#if PERFORMANCE_METRICS
    private static void WriteNextDispatchProfile()
    {
        if (string.IsNullOrWhiteSpace(nextDispatchProfilePath)) return;
        var now = Stopwatch.GetTimestamp();
        var stages = new Dictionary<string, object>(StringComparer.Ordinal);
        for (var i = 0; i < NextDispatchStageNames.Length; i++)
        {
            var count = nextDispatchStageCounts[i];
            var milliseconds = TicksToMilliseconds(nextDispatchStageTicks[i]);
            stages[NextDispatchStageNames[i]] = new { Count = count, TotalMilliseconds = milliseconds, AverageMicroseconds = count == 0 ? 0 : milliseconds * 1000 / count };
        }
        var buckets = nextDispatchBuckets
            .OrderByDescending(pair => pair.Value.Ticks)
            .Select(pair => new
            {
                RuntimeFunctionId = pair.Key.FunctionId,
                FunctionName = pair.Value.Name,
                Reason = pair.Key.Reason.ToString(),
                Count = pair.Value.Count,
                TotalMilliseconds = TicksToMilliseconds(pair.Value.Ticks),
                AverageMicroseconds = pair.Value.Count == 0 ? 0 : TicksToMilliseconds(pair.Value.Ticks) * 1000 / pair.Value.Count
            }).ToArray();
        var subreasons = nextSessionStartRejectSubreasons
            .OrderByDescending(pair => pair.Value.RejectPathTicks)
            .Select(pair => new
            {
                Subreason = pair.Key,
                Scope = pair.Value.Mutable ? "MutableCallState" : "ImmutableGenerationScoped",
                Count = pair.Value.Count,
                RejectPathTotalMilliseconds = TicksToMilliseconds(pair.Value.RejectPathTicks),
                RejectPathAverageMicroseconds = pair.Value.Count == 0 ? 0 : TicksToMilliseconds(pair.Value.RejectPathTicks) * 1000 / pair.Value.Count,
                ClassificationTotalMilliseconds = TicksToMilliseconds(pair.Value.ClassificationTicks),
                ClassificationAverageMicroseconds = pair.Value.Count == 0 ? 0 : TicksToMilliseconds(pair.Value.ClassificationTicks) * 1000 / pair.Value.Count
            }).ToArray();
        var subreasonFunctions = nextSessionStartRejectFunctionBuckets
            .OrderByDescending(pair => pair.Value.RejectPathTicks)
            .Select(pair => new
            {
                RuntimeFunctionId = pair.Key.FunctionId,
                FunctionName = pair.Value.Name,
                Subreason = pair.Key.Subreason,
                Count = pair.Value.Count,
                RejectPathTotalMilliseconds = TicksToMilliseconds(pair.Value.RejectPathTicks),
                RejectPathAverageMicroseconds = pair.Value.Count == 0 ? 0 : TicksToMilliseconds(pair.Value.RejectPathTicks) * 1000 / pair.Value.Count
            }).ToArray();
        var sessionStartRejectedCount = nextDispatchRejections.TryGetValue(nameof(MinorShift.Emuera.GameProc.NextRuntimeDispatchRejectReason.SessionStartRejected), out var rejectedCount) ? rejectedCount : 0;
        var subreasonCountSum = nextSessionStartRejectSubreasons.Values.Sum(value => value.Count);
        var immutableCount = nextSessionStartRejectSubreasons.Where(pair => !pair.Value.Mutable).Sum(pair => pair.Value.Count);
        var immutableTicks = nextSessionStartRejectSubreasons.Where(pair => !pair.Value.Mutable).Sum(pair => pair.Value.RejectPathTicks);
        var mutableCount = nextSessionStartRejectSubreasons.Where(pair => pair.Value.Mutable).Sum(pair => pair.Value.Count);
        var mutableTicks = nextSessionStartRejectSubreasons.Where(pair => pair.Value.Mutable).Sum(pair => pair.Value.RejectPathTicks);
        var wallClockTicks = now - nextDispatchProfileStart;
        var consumedMilliseconds = TicksToMilliseconds(entryDispatchAlreadyConsumedTicks);
        var actionableMilliseconds = TicksToMilliseconds(actionableDispatchTicks);
        var wholeDispatchMilliseconds = TicksToMilliseconds(wholeDispatchSeamTicks);
        var report = new
        {
            Profiler = "NextRuntimePerformanceProfile",
            ThreadingAssumption = "Process dispatch is single-threaded; profiler state is updated on that execution thread.",
            TotalSessionWallClockMilliseconds = TicksToMilliseconds(wallClockTicks),
            WallClockMilliseconds = TicksToMilliseconds(wallClockTicks),
            AllocatedBytes = GC.GetTotalAllocatedBytes(false) - nextDispatchProfileAllocated,
            TotalDispatchAttempts = nextDispatchAttempts,
            Completed = nextDispatchAttempts - nextDispatchFallbacks,
            Fallbacks = nextDispatchFallbacks,
            EntryDispatchAlreadyConsumedCount = entryDispatchAlreadyConsumedCount,
            EntryDispatchAlreadyConsumedTotalTicks = entryDispatchAlreadyConsumedTicks,
            EntryDispatchAlreadyConsumedTotalMilliseconds = consumedMilliseconds,
            EntryDispatchAlreadyConsumedAverageNanoseconds = entryDispatchAlreadyConsumedCount == 0 ? 0 : entryDispatchAlreadyConsumedTicks * 1_000_000_000.0 / Stopwatch.Frequency / entryDispatchAlreadyConsumedCount,
            ActionableDispatchCount = Math.Max(0, (long)nextDispatchAttempts - entryDispatchAlreadyConsumedCount),
            ActionableDispatchTotalMilliseconds = actionableMilliseconds,
            WholeDispatchSeamTotalMilliseconds = wholeDispatchMilliseconds,
            ConsumedSeamShareOfWallClockPercent = wallClockTicks == 0 ? 0 : consumedMilliseconds * 100 / TicksToMilliseconds(wallClockTicks),
            ConsumedSeamShareOfDispatchSeamPercent = wholeDispatchMilliseconds == 0 ? 0 : consumedMilliseconds * 100 / wholeDispatchMilliseconds,
            Rejections = nextDispatchRejections,
            Stages = stages,
            StartupStages = nextStartupStages.ToDictionary(pair => pair.Key, pair => new
            {
                Count = pair.Value.Count,
                TotalMilliseconds = TicksToMilliseconds(pair.Value.Ticks),
                AverageMilliseconds = pair.Value.Count == 0 ? 0 : TicksToMilliseconds(pair.Value.Ticks) / pair.Value.Count,
                AllocatedBytes = pair.Value.AllocatedBytes
            }, StringComparer.Ordinal),
            ConstructionStages = nextConstructionStages.ToDictionary(pair => pair.Key, pair => new
            {
                Count = pair.Value.Count,
                TotalMilliseconds = TicksToMilliseconds(pair.Value.Ticks),
                AverageMilliseconds = pair.Value.Count == 0 ? 0 : TicksToMilliseconds(pair.Value.Ticks) / pair.Value.Count,
                MaxMilliseconds = TicksToMilliseconds(pair.Value.MaxTicks),
                AllocatedBytes = pair.Value.AllocatedBytes
            }, StringComparer.Ordinal),
            ReadinessRequirementStages = nextStartupStages
                .Where(pair => pair.Key.StartsWith("Readiness.Requirements.", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => new
                {
                    Count = pair.Value.Count,
                    TotalMilliseconds = TicksToMilliseconds(pair.Value.Ticks),
                    AverageMilliseconds = pair.Value.Count == 0 ? 0 : TicksToMilliseconds(pair.Value.Ticks) / pair.Value.Count,
                    AllocatedBytes = pair.Value.AllocatedBytes
                }, StringComparer.Ordinal),
            PostErbStartupEnvelopeMilliseconds = TicksToMilliseconds(nextStartupEnvelopeTicks),
            PostErbStartupEnvelopeAllocatedBytes = nextStartupEnvelopeAllocatedBytes,
            PostErbStartupOtherMilliseconds = TicksToMilliseconds(Math.Max(0, nextStartupEnvelopeTicks - nextStartupChildTicks)),
            PostErbStartupOtherAllocatedBytes = Math.Max(0, nextStartupEnvelopeAllocatedBytes - nextStartupChildAllocatedBytes),
            ProgramCardinalities = new
            {
                Count = nextProgramCardinalityCount,
                RuntimeFunctionCount = nextRuntimeFunctionCount,
                DescriptorCount = nextDescriptorCount,
                TotalCodeLength = nextTotalCodeLength,
                StructuralLinkCount = nextStructuralLinkCount,
                LoopCount = nextLoopCount,
                CallSiteCount = nextCallSiteCount,
                ExpressionTargetCount = nextExpressionTargetCount
            },
            CallArgumentLinkMetrics = new
            {
                CallArgumentPayloadAdditionCount = nextCallArgumentPayloadAdditionCount,
                AccumulatedRecordBaseLookupCount = nextCallArgumentPayloadAdditionCount,
                CallArgumentPrefixScanExecution = "REMOVED",
                CallArgumentPrefixScanElementVisits = nextCallArgumentPrefixScanElementVisits,
                CallArgumentPrefixScanTotalMilliseconds = TicksToMilliseconds(nextCallArgumentPrefixScanTicks)
            },
            ReadinessRequirementMetrics = new
            {
                RuntimeFunctionCount = nextReadinessRuntimeFunctionCount,
                CodeInstructionVisits = nextReadinessCodeInstructionVisits,
                SemanticRecordVisits = nextReadinessSemanticRecordVisits,
                HostIdentityVisits = nextReadinessHostIdentityVisits,
                RequirementCandidateCount = nextReadinessRequirementCandidateCount,
                RequirementDedupLookupCount = nextReadinessRequirementDedupLookupCount,
                RequirementOutputCount = nextReadinessRequirementOutputCount
            },
            HostIdentityLookupMetrics = new
            {
                HostIdentityLookupCount = nextReadinessHostIdentityLookupCount,
                HostIdentityLookupHitCount = nextReadinessHostIdentityLookupHitCount,
                HostIdentityLookupMissCount = nextReadinessHostIdentityLookupMissCount,
                HostIdentityDirectIndexLookupCount = nextReadinessHostIdentityDirectIndexLookupCount,
                HostIdentityLinearScanElementVisits = nextReadinessHostIdentityLinearScanElementVisits,
                AverageScanElementsPerLookup = nextReadinessHostIdentityLookupCount == 0 ? 0 : (double)nextReadinessHostIdentityLinearScanElementVisits / nextReadinessHostIdentityLookupCount,
                HostIdentityPresentedElementCount = nextReadinessHostIdentityPresentedElementCount,
                AverageHostIdentitiesPresentedPerLookup = nextReadinessHostIdentityLookupCount == 0 ? 0 : (double)nextReadinessHostIdentityPresentedElementCount / nextReadinessHostIdentityLookupCount,
                MaxHostIdentitiesLength = nextReadinessMaxHostIdentitiesLength,
                VariableLookupCount = nextReadinessVariableLookupCount,
                VariableLinearScanElementVisits = nextReadinessVariableLinearScanElementVisits,
                CallLookupCount = nextReadinessCallLookupCount,
                CallLinearScanElementVisits = nextReadinessCallLinearScanElementVisits
            },
            TopFunctionReasonBuckets = buckets,
            SessionStartRejectSubreasons = subreasons,
            SessionStartRejectSubreasonCountSum = subreasonCountSum,
            SessionStartRejectUnclassified = Math.Max(0, sessionStartRejectedCount - subreasonCountSum),
            ImmutableGenerationScopedRejectCount = immutableCount,
            ImmutableGenerationScopedRejectMilliseconds = TicksToMilliseconds(immutableTicks),
            MutableRejectCount = mutableCount,
            MutableRejectMilliseconds = TicksToMilliseconds(mutableTicks),
            TopSessionStartRejectFunctionSubreasonBuckets = subreasonFunctions
        };
        try { File.WriteAllText(nextDispatchProfilePath, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine); }
        catch { }
    }
#endif

    internal static void MarkStartup(string name)
    {
        if (!Enabled)
            return;
        lock (Sync)
            StartupMarks[name] = ElapsedMilliseconds(processStartTimestamp);
    }

    internal static void WriteStartup()
    {
        if (!Enabled)
            return;
        Dictionary<string, double> marks;
        lock (Sync)
            marks = new Dictionary<string, double>(StartupMarks, StringComparer.Ordinal);
        WriteRecord(new
        {
            type = "startup",
            utc = DateTime.UtcNow,
            processId = Environment.ProcessId,
            marksMilliseconds = marks,
            allocatedBytes = GC.GetTotalAllocatedBytes(false),
            managedBytes = GC.GetTotalMemory(false),
            workingSetBytes = Environment.WorkingSet,
            gen0Collections = GC.CollectionCount(0),
            gen1Collections = GC.CollectionCount(1),
            gen2Collections = GC.CollectionCount(2)
        });
    }

    internal static void BeginMacro(string text)
    {
        if (!Enabled || MacroActive)
            return;
        macroText = text;
        expansionTicks = 0;
        inputHandoffTicks = 0;
        inputLoopTicks = 0;
        erbTicks = 0;
        strFormTicks = 0;
        displayBuildTicks = 0;
        displayAddTicks = 0;
        measureTextTicks = 0;
        refreshTicks = 0;
        paintTicks = 0;
        scrollTicks = 0;
        loopEventTicks = 0;
        refreshEventTicks = 0;
        awaitTicks = 0;
        awaitRequestedMilliseconds = 0;
        randomTraceHash = unchecked((long)1469598103934665603UL);
        expandedInputCount = 0;
        inputDispatchCount = 0;
        erbRunCount = 0;
        refreshCount = 0;
        paintCount = 0;
        scrollUpdateCount = 0;
        uiEventPumpCount = 0;
        strFormCount = 0;
        displayBuildCount = 0;
        measureTextCount = 0;
        randomCallCount = 0;
        awaitCount = 0;
        nextDispatchAttempts = 0;
        nextDispatchFallbacks = 0;
        nextDispatchStages.Clear();
        nextDispatchRejections.Clear();
        nextDispatchFunctions.Clear();
#if PERFORMANCE_METRICS
        nextSessionStartRejectSubreasons.Clear();
        nextSessionStartRejectFunctionBuckets.Clear();
#endif
        allocatedBytesBefore = GC.GetTotalAllocatedBytes(false);
        managedBytesBefore = GC.GetTotalMemory(false);
        workingSetBefore = Environment.WorkingSet;
        gen0Before = GC.CollectionCount(0);
        gen1Before = GC.CollectionCount(1);
        gen2Before = GC.CollectionCount(2);
        macroStartTimestamp = Stopwatch.GetTimestamp();
        Volatile.Write(ref macroActive, 1);
    }

    internal static MacroResult FinishMacro(bool killed)
    {
        if (!MacroActive)
            return null;
        long endTimestamp = Stopwatch.GetTimestamp();
        long allocatedBytesAfter = GC.GetTotalAllocatedBytes(false);
        long managedBytesAfter = GC.GetTotalMemory(false);
        long workingSetAfter = Environment.WorkingSet;
        Volatile.Write(ref macroActive, 0);

        return new MacroResult
        {
            MacroText = macroText,
            Killed = killed,
            TotalMilliseconds = TicksToMilliseconds(endTimestamp - macroStartTimestamp),
            ExpansionMilliseconds = TicksToMilliseconds(expansionTicks),
            InputHandoffMilliseconds = TicksToMilliseconds(inputHandoffTicks),
            InputLoopMilliseconds = TicksToMilliseconds(inputLoopTicks),
            ErbMilliseconds = TicksToMilliseconds(erbTicks),
            StringGenerationMilliseconds = TicksToMilliseconds(strFormTicks),
            DisplayBuildMilliseconds = TicksToMilliseconds(displayBuildTicks),
            DisplayAddMilliseconds = TicksToMilliseconds(displayAddTicks),
            MeasureTextMilliseconds = TicksToMilliseconds(measureTextTicks),
            RefreshMilliseconds = TicksToMilliseconds(refreshTicks),
            PaintMilliseconds = TicksToMilliseconds(paintTicks),
            ScrollMilliseconds = TicksToMilliseconds(scrollTicks),
            UiEventMilliseconds = TicksToMilliseconds(loopEventTicks + refreshEventTicks),
            AwaitMilliseconds = TicksToMilliseconds(awaitTicks),
            AwaitRequestedMilliseconds = awaitRequestedMilliseconds,
            ExpandedInputCount = expandedInputCount,
            InputDispatchCount = inputDispatchCount,
            ErbRunCount = erbRunCount,
            RefreshCount = refreshCount,
            PaintCount = paintCount,
            ScrollUpdateCount = scrollUpdateCount,
            UiEventPumpCount = uiEventPumpCount,
            StringGenerationCount = strFormCount,
            DisplayBuildCount = displayBuildCount,
            MeasureTextCount = measureTextCount,
            RandomCallCount = randomCallCount,
            AwaitCount = awaitCount,
            RandomTraceHash = unchecked((ulong)randomTraceHash).ToString("X16"),
            AllocatedBytes = allocatedBytesAfter - allocatedBytesBefore,
            ManagedBytesBefore = managedBytesBefore,
            ManagedBytesAfter = managedBytesAfter,
            WorkingSetBefore = workingSetBefore,
            WorkingSetAfter = workingSetAfter,
            Gen0Collections = GC.CollectionCount(0) - gen0Before,
            Gen1Collections = GC.CollectionCount(1) - gen1Before,
            Gen2Collections = GC.CollectionCount(2) - gen2Before
        };
    }

    internal static void WriteMacro(MacroResult result, string stateHash, string displayHash, int displayLineCount)
    {
        if (result == null || !Enabled)
            return;
        result.StateSha256 = stateHash;
        result.DisplaySha256 = displayHash;
        result.DisplayLineCount = displayLineCount;
        lock (Sync)
        {
            result.NextRuntimeDispatchAttempts = nextDispatchAttempts;
            result.NextRuntimeDispatchFallbacks = nextDispatchFallbacks;
            result.NextRuntimeDispatchStages = nextDispatchStages.ToDictionary(pair => pair.Key, pair => new PerformanceStageResult(pair.Value.Count, TicksToMilliseconds(pair.Value.Ticks)), StringComparer.Ordinal);
            result.NextRuntimeDispatchRejections = new Dictionary<string, int>(nextDispatchRejections, StringComparer.Ordinal);
            result.NextRuntimeDispatchFunctions = new Dictionary<string, int>(nextDispatchFunctions, StringComparer.Ordinal);
        }
        WriteRecord(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long StartTiming()
    {
#if PERFORMANCE_METRICS
        return MacroActive ? Stopwatch.GetTimestamp() : 0;
#else
        return 0;
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long StartNextDispatchTiming()
    {
#if PERFORMANCE_METRICS
        return string.IsNullOrWhiteSpace(nextDispatchProfilePath) ? 0 : Stopwatch.GetTimestamp();
#else
        return 0;
#endif
    }

#if PERFORMANCE_METRICS
    internal static MeasurementToken StartNextRuntimeMeasurement() =>
        string.IsNullOrWhiteSpace(nextDispatchProfilePath)
            ? default
            : new(Stopwatch.GetTimestamp(), GC.GetTotalAllocatedBytes(false));

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextRuntimeStartupStage(string stage, MeasurementToken start, bool contributesToEnvelope = false)
    {
        if (start.Ticks == 0) return;
        var ticks = Math.Max(0, Stopwatch.GetTimestamp() - start.Ticks);
        var allocated = Math.Max(0, GC.GetTotalAllocatedBytes(false) - start.AllocatedBytes);
        nextStartupStages.TryGetValue(stage, out var value);
        nextStartupStages[stage] = (value.Count + 1, value.Ticks + ticks, value.AllocatedBytes + allocated, Math.Max(value.MaxTicks, ticks));
        if (contributesToEnvelope)
        {
            nextStartupChildTicks += ticks;
            nextStartupChildAllocatedBytes += allocated;
        }
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextRuntimeStartupEnvelope(MeasurementToken start)
    {
        if (start.Ticks == 0) return;
        nextStartupEnvelopeTicks = Math.Max(0, Stopwatch.GetTimestamp() - start.Ticks);
        nextStartupEnvelopeAllocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(false) - start.AllocatedBytes);
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextRuntimeConstructionStage(string stage, MeasurementToken start)
    {
        if (start.Ticks == 0) return;
        var ticks = Math.Max(0, Stopwatch.GetTimestamp() - start.Ticks);
        var allocated = Math.Max(0, GC.GetTotalAllocatedBytes(false) - start.AllocatedBytes);
        nextConstructionStages.TryGetValue(stage, out var value);
        nextConstructionStages[stage] = (value.Count + 1, value.Ticks + ticks, value.AllocatedBytes + allocated, Math.Max(value.MaxTicks, ticks));
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextRuntimeProgramCardinalities(int runtimeFunctionCount, int descriptorCount, int totalCodeLength, int structuralLinkCount, int loopCount, int callSiteCount, int expressionTargetCount)
    {
        if (nextProgramCardinalityCount != 0) return;
        nextProgramCardinalityCount = 1;
        nextRuntimeFunctionCount = runtimeFunctionCount;
        nextDescriptorCount = descriptorCount;
        nextTotalCodeLength = totalCodeLength;
        nextStructuralLinkCount = structuralLinkCount;
        nextLoopCount = loopCount;
        nextCallSiteCount = callSiteCount;
        nextExpressionTargetCount = expressionTargetCount;
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextRuntimeCallArgumentLinkMetrics(int additions, long visits, long ticks)
    {
        nextCallArgumentPayloadAdditionCount = additions;
        nextCallArgumentPrefixScanElementVisits = visits;
        nextCallArgumentPrefixScanTicks = ticks;
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextRuntimeReadinessCounters(int runtimeFunctionCount, long codeInstructionVisits, long semanticRecordVisits, long hostIdentityVisits, long requirementCandidateCount, long requirementDedupLookupCount, long requirementOutputCount)
    {
        nextReadinessRuntimeFunctionCount = runtimeFunctionCount;
        nextReadinessCodeInstructionVisits = codeInstructionVisits;
        nextReadinessSemanticRecordVisits = semanticRecordVisits;
        nextReadinessHostIdentityVisits = hostIdentityVisits;
        nextReadinessRequirementCandidateCount = requirementCandidateCount;
        nextReadinessRequirementDedupLookupCount = requirementDedupLookupCount;
        nextReadinessRequirementOutputCount = requirementOutputCount;
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextRuntimeHostIdentityLookupMetrics(long lookupCount, long hitCount, long missCount, long directIndexLookupCount, long scanElementVisits, long presentedElementCount, int maxIdentitiesLength, long variableLookupCount, long variableScanElementVisits, long callLookupCount, long callScanElementVisits)
    {
        nextReadinessHostIdentityLookupCount = lookupCount;
        nextReadinessHostIdentityLookupHitCount = hitCount;
        nextReadinessHostIdentityLookupMissCount = missCount;
        nextReadinessHostIdentityDirectIndexLookupCount = directIndexLookupCount;
        nextReadinessHostIdentityLinearScanElementVisits = scanElementVisits;
        nextReadinessHostIdentityPresentedElementCount = presentedElementCount;
        nextReadinessMaxHostIdentitiesLength = maxIdentitiesLength;
        nextReadinessVariableLookupCount = variableLookupCount;
        nextReadinessVariableLinearScanElementVisits = variableScanElementVisits;
        nextReadinessCallLookupCount = callLookupCount;
        nextReadinessCallLinearScanElementVisits = callScanElementVisits;
    }
#endif

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordEntryDispatchAlreadyConsumed(long start)
    {
#if PERFORMANCE_METRICS
        if (start == 0)
            return;
        entryDispatchAlreadyConsumedCount++;
        entryDispatchAlreadyConsumedTicks += Stopwatch.GetTimestamp() - start;
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordEntryDispatchSeam(long start, bool consumed)
    {
#if PERFORMANCE_METRICS
        if (start == 0)
            return;
        var elapsed = Stopwatch.GetTimestamp() - start;
        wholeDispatchSeamTicks += elapsed;
        if (!consumed)
            actionableDispatchTicks += elapsed;
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddNextDispatchStage(string stage, long start)
    {
        if (start == 0)
            return;
        var elapsed = Stopwatch.GetTimestamp() - start;
#if PERFORMANCE_METRICS
        var index = Array.IndexOf(NextDispatchStageNames, stage);
        if ((uint)index < (uint)nextDispatchStageCounts.Length)
        {
            nextDispatchStageCounts[index]++;
            nextDispatchStageTicks[index] += elapsed;
        }
#endif
        lock (Sync)
        {
            nextDispatchStages.TryGetValue(stage, out var current);
            nextDispatchStages[stage] = (current.Count + 1, current.Ticks + elapsed);
        }
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextDispatch(string function, string reason, bool fallback)
    {
        lock (Sync)
        {
            nextDispatchAttempts++;
            if (fallback)
                nextDispatchFallbacks++;
            if (!string.IsNullOrEmpty(reason))
                nextDispatchRejections[reason] = nextDispatchRejections.TryGetValue(reason, out var count) ? count + 1 : 1;
            var key = $"{function}|{reason}";
            nextDispatchFunctions[key] = nextDispatchFunctions.TryGetValue(key, out var functionCount) ? functionCount + 1 : 1;
        }
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordNextDispatch(int functionId, string functionName, MinorShift.Emuera.GameProc.NextRuntimeDispatchRejectReason reason, bool fallback, long dispatchStart)
    {
#if PERFORMANCE_METRICS
        var elapsed = dispatchStart == 0 ? 0 : Stopwatch.GetTimestamp() - dispatchStart;
        nextDispatchAttempts++;
        if (fallback) nextDispatchFallbacks++;
        if (reason != MinorShift.Emuera.GameProc.NextRuntimeDispatchRejectReason.None)
        {
            var text = reason.ToString();
            nextDispatchRejections[text] = nextDispatchRejections.TryGetValue(text, out var count) ? count + 1 : 1;
        }
        var key = (functionId, reason);
        nextDispatchBuckets.TryGetValue(key, out var bucket);
        nextDispatchBuckets[key] = (bucket.Name ?? functionName, bucket.Count + 1, bucket.Ticks + elapsed);
        nextDispatchFunctions[$"{functionName}|{reason}"] = nextDispatchFunctions.TryGetValue($"{functionName}|{reason}", out var functionCount) ? functionCount + 1 : 1;
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordSessionStartRejectSubreason(int functionId, string functionName, string subreason, bool mutable, long rejectPathStart, long rejectPathEnd, long classificationStart, long classificationEnd)
    {
#if PERFORMANCE_METRICS
        var rejectPathTicks = rejectPathStart == 0 || rejectPathEnd == 0 ? 0 : Math.Max(0, rejectPathEnd - rejectPathStart);
        var classificationTicks = classificationStart == 0 || classificationEnd == 0 ? 0 : Math.Max(0, classificationEnd - classificationStart);
        nextSessionStartRejectSubreasons.TryGetValue(subreason, out var aggregate);
        nextSessionStartRejectSubreasons[subreason] = (aggregate.Count + 1, aggregate.RejectPathTicks + rejectPathTicks, aggregate.ClassificationTicks + classificationTicks, mutable);
        var key = (functionId, subreason);
        nextSessionStartRejectFunctionBuckets.TryGetValue(key, out var bucket);
        nextSessionStartRejectFunctionBuckets[key] = (bucket.Name ?? functionName, bucket.Count + 1, bucket.RejectPathTicks + rejectPathTicks);
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddExpansion(long start) => AddTicks(ref expansionTicks, start);
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddInputHandoff(long start) => AddTicks(ref inputHandoffTicks, start);
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddInputLoop(long start) => AddTicks(ref inputLoopTicks, start);
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddErb(long start)
    {
        AddTicks(ref erbTicks, start);
        if (start != 0)
            Interlocked.Increment(ref erbRunCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddStringGeneration(long start)
    {
        AddTicks(ref strFormTicks, start);
        if (start != 0)
            Interlocked.Increment(ref strFormCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddDisplayBuild(long start)
    {
        AddTicks(ref displayBuildTicks, start);
        if (start != 0)
            Interlocked.Increment(ref displayBuildCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddDisplayAdd(long start) => AddTicks(ref displayAddTicks, start);
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddMeasureText(long start)
    {
        AddTicks(ref measureTextTicks, start);
        if (start != 0)
            Interlocked.Increment(ref measureTextCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddRefresh(long start)
    {
        AddTicks(ref refreshTicks, start);
        if (start != 0)
            Interlocked.Increment(ref refreshCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddPaint(long start)
    {
        AddTicks(ref paintTicks, start);
        if (start != 0)
            Interlocked.Increment(ref paintCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddScroll(long start)
    {
        AddTicks(ref scrollTicks, start);
        if (start != 0)
            Interlocked.Increment(ref scrollUpdateCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddLoopEvent(long start)
    {
        AddTicks(ref loopEventTicks, start);
        if (start != 0)
            Interlocked.Increment(ref uiEventPumpCount);
    }
    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddRefreshEvent(long start)
    {
        AddTicks(ref refreshEventTicks, start);
        if (start != 0)
            Interlocked.Increment(ref uiEventPumpCount);
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddAwait(int requestedMilliseconds, long start)
    {
        if (!MacroActive)
            return;
        AddTicks(ref awaitTicks, start);
        Interlocked.Add(ref awaitRequestedMilliseconds, requestedMilliseconds);
        Interlocked.Increment(ref awaitCount);
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void SetExpandedInputCount(int count) => expandedInputCount = count;
    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordInputDispatch()
    {
        if (MacroActive)
            Interlocked.Increment(ref inputDispatchCount);
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordRandom(long maximum, long value)
    {
        if (!MacroActive)
            return;
        unchecked
        {
            ulong hash = (ulong)randomTraceHash;
            hash = (hash ^ (ulong)maximum) * 1099511628211UL;
            hash = (hash ^ (ulong)value) * 1099511628211UL;
            randomTraceHash = (long)hash;
        }
        Interlocked.Increment(ref randomCallCount);
    }

    private static void AddTicks(ref long target, long start)
    {
        if (start != 0)
            Interlocked.Add(ref target, Stopwatch.GetTimestamp() - start);
    }

    private static double ElapsedMilliseconds(long start) =>
        TicksToMilliseconds(Stopwatch.GetTimestamp() - start);

    private static double TicksToMilliseconds(long ticks) =>
        Math.Round(ticks * 1000.0 / Stopwatch.Frequency, 3);

    private static void WriteRecord(object record)
    {
        string path = Volatile.Read(ref logPath);
        if (path == null)
            return;
        string json = JsonSerializer.Serialize(record, JsonOptions);
        lock (Sync)
            File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }
}

internal sealed class MacroResult
{
    public string Type { get; set; } = "macro";
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public int ProcessId { get; set; } = Environment.ProcessId;
    public string MacroText { get; set; }
    public bool Killed { get; set; }
    public double TotalMilliseconds { get; set; }
    public double ExpansionMilliseconds { get; set; }
    public double InputHandoffMilliseconds { get; set; }
    public double InputLoopMilliseconds { get; set; }
    public double ErbMilliseconds { get; set; }
    public double StringGenerationMilliseconds { get; set; }
    public double DisplayBuildMilliseconds { get; set; }
    public double DisplayAddMilliseconds { get; set; }
    public double MeasureTextMilliseconds { get; set; }
    public double RefreshMilliseconds { get; set; }
    public double PaintMilliseconds { get; set; }
    public double ScrollMilliseconds { get; set; }
    public double UiEventMilliseconds { get; set; }
    public double AwaitMilliseconds { get; set; }
    public long AwaitRequestedMilliseconds { get; set; }
    public int ExpandedInputCount { get; set; }
    public int InputDispatchCount { get; set; }
    public int ErbRunCount { get; set; }
    public int RefreshCount { get; set; }
    public int PaintCount { get; set; }
    public int ScrollUpdateCount { get; set; }
    public int UiEventPumpCount { get; set; }
    public int StringGenerationCount { get; set; }
    public int DisplayBuildCount { get; set; }
    public int MeasureTextCount { get; set; }
    public int RandomCallCount { get; set; }
    public int AwaitCount { get; set; }
    public string RandomTraceHash { get; set; }
    public long AllocatedBytes { get; set; }
    public long ManagedBytesBefore { get; set; }
    public long ManagedBytesAfter { get; set; }
    public long WorkingSetBefore { get; set; }
    public long WorkingSetAfter { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public int NextRuntimeDispatchAttempts { get; set; }
    public int NextRuntimeDispatchFallbacks { get; set; }
    public Dictionary<string, PerformanceStageResult> NextRuntimeDispatchStages { get; set; }
    public Dictionary<string, int> NextRuntimeDispatchRejections { get; set; }
    public Dictionary<string, int> NextRuntimeDispatchFunctions { get; set; }
    public string StateSha256 { get; set; }
    public string DisplaySha256 { get; set; }
    public int DisplayLineCount { get; set; }
}

internal sealed record PerformanceStageResult(int Count, double Milliseconds);
