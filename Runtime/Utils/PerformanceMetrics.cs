using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    internal static bool Enabled => Volatile.Read(ref logPath) != null;
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
    public string StateSha256 { get; set; }
    public string DisplaySha256 { get; set; }
    public int DisplayLineCount { get; set; }
}
