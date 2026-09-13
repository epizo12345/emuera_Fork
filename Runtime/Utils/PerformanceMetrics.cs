using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
#if PERFORMANCE_METRICS
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
#endif

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
    private const int MaxRandomizeTraceEntries = 1024;
    private const int MaxClockTraceEntries = 4096;
    private static readonly DateTime BenchmarkClockEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly object Sync = new();
    private static readonly Dictionary<string, double> StartupMarks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static long processStartTimestamp = Stopwatch.GetTimestamp();
    private static string logPath;
    private static int macroActive;
    private static long? benchmarkDeterministicSeed;
    private static int benchmarkDiagnosticsEnabled;
    private static int benchmarkClockEnabled;
    private static long benchmarkClockReadCount;

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
    private static int randomizeCount;
    private static bool randomizeTraceTruncated;
    private static readonly List<RandomizeTraceEntry> randomizeTrace = new(MaxRandomizeTraceEntries);
    private static int clockReadCount;
    private static bool clockReadTraceTruncated;
    private static readonly List<BenchmarkClockTraceEntry> clockReadTrace = new(MaxClockTraceEntries);
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
    private static int kojoBattleInputCommandEntries;
    private static int kojoBattleNaviEntries;
    private static int awaitCount;
    private static long saveToTicks;
    private static long sqlSaveTicks;
    private static long serializerTicks;
    private static long characterSerializationTicks;
    private static long nonCharacterSerializationTicks;
    private static long bytesWrittenTotal;
    private static long bytesWrittenMax;
    private static long saveToMaxTicks;
    private static int saveToCount;
    private static int binarySaveCount;
    private static int textSaveCount;
    private static int saveToFailureCount;
    private static int sqlSaveCallCount;
    private static int sqlSaveActiveCount;
    private static int saveCharanumMin;
    private static int saveCharanumMax;
    private static readonly Dictionary<int, int> saveToCountsByIndex = new();
#if PERFORMANCE_METRICS
    private static long cflagReadCount;
    private static long cflagWriteCount;
    private static long cflagPlusCount;
    private static long cflagBulkWriteCount;
    private static long cflagSetAllCount;
    private static long cflagRawArrayRequestCount;
    private static readonly HashSet<int> cflagReadCharacters = [];
    private static readonly HashSet<int> cflagWrittenCharacters = [];
    private static readonly HashSet<int> cflagReadRows = [];
    private static readonly HashSet<int> cflagWrittenRows = [];
    private static CFlagPostLoadSparsityMetrics cflagPostLoadSparsity;
#endif

    internal static bool Enabled => Volatile.Read(ref logPath) != null;
    internal static bool MacroActive => Volatile.Read(ref macroActive) != 0;
    internal static bool DiagnosticsEnabled => Enabled && Volatile.Read(ref benchmarkDiagnosticsEnabled) != 0;
    internal static bool BenchmarkDeterministicClockEnabled => Enabled && Volatile.Read(ref benchmarkClockEnabled) != 0;

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
#if PERFORMANCE_METRICS
        ResetCFlagAccessMetrics();
        cflagPostLoadSparsity = null;
#endif
        Volatile.Write(ref logPath, fullPath);
        lock (Sync)
        {
            StartupMarks.Clear();
            StartupMarks["ProcessStart"] = 0;
        }
    }

    internal static void Configure(string path, long? deterministicSeed, bool diagnosticsEnabled, bool deterministicClock)
    {
        if ((deterministicSeed.HasValue || diagnosticsEnabled || deterministicClock) && string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("benchmark seed/diagnostics/clock require --BenchmarkLog");
        benchmarkDeterministicSeed = deterministicSeed;
        Volatile.Write(ref benchmarkDiagnosticsEnabled, diagnosticsEnabled ? 1 : 0);
        Volatile.Write(ref benchmarkClockEnabled, deterministicClock ? 1 : 0);
        Interlocked.Exchange(ref benchmarkClockReadCount, 0);
        Configure(path);
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

#if PERFORMANCE_METRICS
    [Conditional("PERFORMANCE_METRICS")]
    internal static void WriteInstructionStorageCensus(string phase)
    {
        if (!DiagnosticsEnabled)
            return;
        WriteRecord(GlobalStatic.Process.CreateInstructionStorageCensusRecord(phase));
    }
#endif

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
        kojoBattleInputCommandEntries = 0;
        kojoBattleNaviEntries = 0;
        randomizeCount = 0;
        randomizeTraceTruncated = false;
        lock (Sync)
            randomizeTrace.Clear();
        awaitCount = 0;
        ResetSaveMetrics();
#if PERFORMANCE_METRICS
        ResetCFlagAccessMetrics();
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
        RandomizeTraceEntry[] reseedTrace;
        lock (Sync)
            reseedTrace = randomizeTrace.ToArray();
        BenchmarkClockTraceEntry[] clockTrace;
        lock (Sync)
            clockTrace = clockReadTrace.ToArray();

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
            KojoBattleInputCommandEntries = kojoBattleInputCommandEntries,
            KojoBattleNaviEntries = kojoBattleNaviEntries,
            BenchmarkDeterministicSeed = benchmarkDeterministicSeed,
            BenchmarkDiagnosticsEnabled = DiagnosticsEnabled,
            BenchmarkDeterministicClockEnabled = BenchmarkDeterministicClockEnabled,
            RandomizeCount = randomizeCount,
            RandomizeTrace = reseedTrace,
            RandomizeTraceTruncated = randomizeTraceTruncated,
            ClockReadCount = clockReadCount,
            ClockReadTrace = clockTrace,
            ClockReadTraceTruncated = clockReadTraceTruncated,
            AwaitCount = awaitCount,
            RandomTraceHash = unchecked((ulong)randomTraceHash).ToString("X16"),
            AllocatedBytes = allocatedBytesAfter - allocatedBytesBefore,
            ManagedBytesBefore = managedBytesBefore,
            ManagedBytesAfter = managedBytesAfter,
            WorkingSetBefore = workingSetBefore,
            WorkingSetAfter = workingSetAfter,
            Gen0Collections = GC.CollectionCount(0) - gen0Before,
            Gen1Collections = GC.CollectionCount(1) - gen1Before,
            Gen2Collections = GC.CollectionCount(2) - gen2Before,
            SaveToCount = saveToCount,
            Save401Count = GetSaveCount(401),
            SaveToCountsByIndex = GetSaveToCountsByIndex(),
            BinarySaveCount = binarySaveCount,
            TextSaveCount = textSaveCount,
            SaveToFailureCount = saveToFailureCount,
            SaveToTotalMilliseconds = TicksToMilliseconds(saveToTicks),
            SaveToMeanMilliseconds = saveToCount == 0 ? 0 : Math.Round(saveToTicks * 1000.0 / Stopwatch.Frequency / saveToCount, 3),
            SaveToMaxMilliseconds = TicksToMilliseconds(saveToMaxTicks),
            SqlSaveTotalMilliseconds = TicksToMilliseconds(sqlSaveTicks),
            SqlSaveCallCount = sqlSaveCallCount,
            SqlSaveActiveCount = sqlSaveActiveCount,
            SerializerTotalMilliseconds = TicksToMilliseconds(serializerTicks),
            CharacterSerializationTotalMilliseconds = TicksToMilliseconds(characterSerializationTicks),
            NonCharacterSerializationTotalMilliseconds = TicksToMilliseconds(nonCharacterSerializationTicks),
            BytesWrittenTotal = bytesWrittenTotal,
            BytesWrittenMean = saveToCount == 0 ? 0 : bytesWrittenTotal / saveToCount,
            BytesWrittenMax = bytesWrittenMax,
            SaveCharanumMin = saveToCount == 0 ? null : saveCharanumMin,
            SaveCharanumMax = saveToCount == 0 ? null : saveCharanumMax,
#if PERFORMANCE_METRICS
            CdfFlagAccess = DiagnosticsEnabled ? GetCFlagAccessMetrics() : null,
            CdfFlagPostLoad = DiagnosticsEnabled ? cflagPostLoadSparsity : null
#endif
        };
    }

#if PERFORMANCE_METRICS
    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordCFlagRead(bool isCFlag, int character, int row)
    {
#if PERFORMANCE_METRICS
        if (!DiagnosticsEnabled || !MacroActive || !isCFlag)
            return;
        Interlocked.Increment(ref cflagReadCount);
        AddCFlagAddress(cflagReadCharacters, cflagReadRows, character, row);
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordCFlagWrite(bool isCFlag, int character, int row)
    {
#if PERFORMANCE_METRICS
        if (!DiagnosticsEnabled || !MacroActive || !isCFlag)
            return;
        Interlocked.Increment(ref cflagWriteCount);
        AddCFlagAddress(cflagWrittenCharacters, cflagWrittenRows, character, row);
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordCFlagPlus(bool isCFlag, int character, int row)
    {
#if PERFORMANCE_METRICS
        if (!DiagnosticsEnabled || !MacroActive || !isCFlag)
            return;
        Interlocked.Increment(ref cflagPlusCount);
        AddCFlagAddress(cflagReadCharacters, cflagReadRows, character, row);
        AddCFlagAddress(cflagWrittenCharacters, cflagWrittenRows, character, row);
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordCFlagBulkWrite(bool isCFlag, int character, int row)
    {
#if PERFORMANCE_METRICS
        if (!DiagnosticsEnabled || !MacroActive || !isCFlag)
            return;
        Interlocked.Increment(ref cflagBulkWriteCount);
        AddCFlagAddress(cflagWrittenCharacters, cflagWrittenRows, character, row);
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordCFlagSetAll(bool isCFlag, int character, int rowCount)
    {
#if PERFORMANCE_METRICS
        if (!DiagnosticsEnabled || !MacroActive || !isCFlag)
            return;
        Interlocked.Increment(ref cflagSetAllCount);
        lock (Sync)
        {
            cflagWrittenCharacters.Add(character);
            for (int row = 0; row < rowCount; row++)
                cflagWrittenRows.Add(character * 9 + row);
        }
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordCFlagRawArrayRequest(bool isCFlag, int character)
    {
#if PERFORMANCE_METRICS
        if (!DiagnosticsEnabled || !MacroActive || !isCFlag)
            return;
        Interlocked.Increment(ref cflagRawArrayRequestCount);
        lock (Sync)
            cflagReadCharacters.Add(character);
#endif
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordCFlagPostLoadSparsity(List<CharacterData> characters, int cflagIndex)
    {
#if PERFORMANCE_METRICS
        if (!DiagnosticsEnabled)
            return;

        const int rowsPerCharacter = 9;
        const int columnsPerRow = 6000;
        long totalCells = 0;
        long nonZeroCellCount = 0;
        int charactersWithAnyNonZero = 0;
        int nullBackingCount = 0;
        int shapeMismatchCharacterCount = 0;
        long rowsWithAnyNonZero = 0;
        long[] rowCharactersWithNonZero = new long[rowsPerCharacter];
        long[] rowNonZeroCells = new long[rowsPerCharacter];
        List<long> characterNonZeroCounts = new(characters.Count);

        foreach (CharacterData character in characters)
        {
            long[,] array = character.DataIntegerArray2D[cflagIndex];
            if (array == null)
            {
                nullBackingCount++;
                totalCells += (long)rowsPerCharacter * columnsPerRow;
                characterNonZeroCounts.Add(0);
                continue;
            }

            int rowCount = array.GetLength(0);
            int columnCount = array.GetLength(1);
            totalCells += array.LongLength;
            if (rowCount != rowsPerCharacter || columnCount != columnsPerRow)
                shapeMismatchCharacterCount++;

            long characterNonZeroCount = 0;
            int scannedRows = Math.Min(rowCount, rowsPerCharacter);
            for (int row = 0; row < scannedRows; row++)
            {
                bool rowHasNonZero = false;
                for (int column = 0; column < columnCount; column++)
                {
                    if (array[row, column] == 0)
                        continue;
                    rowHasNonZero = true;
                    characterNonZeroCount++;
                    rowNonZeroCells[row]++;
                }
                if (rowHasNonZero)
                {
                    rowsWithAnyNonZero++;
                    rowCharactersWithNonZero[row]++;
                }
            }

            if (characterNonZeroCount > 0)
                charactersWithAnyNonZero++;
            characterNonZeroCounts.Add(characterNonZeroCount);
            nonZeroCellCount += characterNonZeroCount;
        }

        characterNonZeroCounts.Sort();
        int characterCount = characters.Count;
        double median = characterCount == 0
            ? 0
            : characterCount % 2 == 0
                ? (characterNonZeroCounts[characterCount / 2 - 1] + characterNonZeroCounts[characterCount / 2]) / 2.0
                : characterNonZeroCounts[characterCount / 2];
        int p90Index = characterCount == 0 ? 0 : (int)Math.Ceiling(characterCount * 0.9) - 1;
        long min = characterCount == 0 ? 0 : characterNonZeroCounts[0];
        long p90 = characterCount == 0 ? 0 : characterNonZeroCounts[p90Index];
        long max = characterCount == 0 ? 0 : characterNonZeroCounts[characterCount - 1];
        double[] rowDensityPercent = new double[rowsPerCharacter];
        long logicalCellsPerRow = (long)characterCount * columnsPerRow;
        for (int row = 0; row < rowsPerCharacter; row++)
            rowDensityPercent[row] = logicalCellsPerRow == 0 ? 0 : rowNonZeroCells[row] * 100.0 / logicalCellsPerRow;

        cflagPostLoadSparsity = new CFlagPostLoadSparsityMetrics
        {
            CharacterCount = characterCount,
            TotalCells = totalCells,
            NonZeroCellCount = nonZeroCellCount,
            DensityPercent = totalCells == 0 ? 0 : nonZeroCellCount * 100.0 / totalCells,
            CharactersWithAnyNonZero = charactersWithAnyNonZero,
            CharactersAllZero = characterCount - charactersWithAnyNonZero,
            RowsWithAnyNonZero = rowsWithAnyNonZero,
            RowsAllZero = (long)characterCount * rowsPerCharacter - rowsWithAnyNonZero,
            RowCharactersWithNonZero = rowCharactersWithNonZero,
            RowNonZeroCells = rowNonZeroCells,
            RowDensityPercent = rowDensityPercent,
            MinNonZeroCellsPerCharacter = min,
            MedianNonZeroCellsPerCharacter = median,
            P90NonZeroCellsPerCharacter = p90,
            MaxNonZeroCellsPerCharacter = max,
            NullBackingCount = nullBackingCount,
            ShapeMismatchCharacterCount = shapeMismatchCharacterCount
        };
#endif
    }

    private static void ResetCFlagAccessMetrics()
    {
#if PERFORMANCE_METRICS
        Interlocked.Exchange(ref cflagReadCount, 0);
        Interlocked.Exchange(ref cflagWriteCount, 0);
        Interlocked.Exchange(ref cflagPlusCount, 0);
        Interlocked.Exchange(ref cflagBulkWriteCount, 0);
        Interlocked.Exchange(ref cflagSetAllCount, 0);
        Interlocked.Exchange(ref cflagRawArrayRequestCount, 0);
        lock (Sync)
        {
            cflagReadCharacters.Clear();
            cflagWrittenCharacters.Clear();
            cflagReadRows.Clear();
            cflagWrittenRows.Clear();
        }
#endif
    }

    private static void AddCFlagAddress(HashSet<int> characters, HashSet<int> rows, int character, int row)
    {
#if PERFORMANCE_METRICS
        lock (Sync)
        {
            characters.Add(character);
            if (row >= 0 && row < 9)
                rows.Add(character * 9 + row);
        }
#endif
    }

    private static CFlagAccessMetrics GetCFlagAccessMetrics()
    {
#if PERFORMANCE_METRICS
        lock (Sync)
        {
            return new CFlagAccessMetrics
            {
                ReadCount = Interlocked.Read(ref cflagReadCount),
                WriteCount = Interlocked.Read(ref cflagWriteCount),
                PlusCount = Interlocked.Read(ref cflagPlusCount),
                BulkWriteCount = Interlocked.Read(ref cflagBulkWriteCount),
                SetAllCount = Interlocked.Read(ref cflagSetAllCount),
                RawArrayRequestCount = Interlocked.Read(ref cflagRawArrayRequestCount),
                DistinctCharactersRead = cflagReadCharacters.Count,
                DistinctCharactersWritten = cflagWrittenCharacters.Count,
                DistinctRowsRead = cflagReadRows.Count,
                DistinctRowsWritten = cflagWrittenRows.Count
            };
        }
    }
#endif
#endif

    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddSqlSave(long start, bool active)
    {
        if (start == 0)
            return;
        AddTicks(ref sqlSaveTicks, start);
        Interlocked.Increment(ref sqlSaveCallCount);
        if (active)
            Interlocked.Increment(ref sqlSaveActiveCount);
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddSerializer(long start) => AddTicks(ref serializerTicks, start);

    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddCharacterSerialization(long start) => AddTicks(ref characterSerializationTicks, start);

    [Conditional("PERFORMANCE_METRICS")]
    internal static void AddNonCharacterSerialization(long start) => AddTicks(ref nonCharacterSerializationTicks, start);

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordSaveTo(int saveIndex, bool binary, bool success, long start, long bytesWritten, int charanum)
    {
        if (start == 0)
            return;
        long elapsed = Stopwatch.GetTimestamp() - start;
        if (binary)
            Interlocked.Increment(ref binarySaveCount);
        else
            Interlocked.Increment(ref textSaveCount);
        if (!success)
            Interlocked.Increment(ref saveToFailureCount);
        Interlocked.Add(ref saveToTicks, elapsed);
        UpdateMaximum(ref saveToMaxTicks, elapsed);
        Interlocked.Add(ref bytesWrittenTotal, bytesWritten);
        UpdateMaximum(ref bytesWrittenMax, bytesWritten);
        UpdateMinimum(ref saveCharanumMin, charanum);
        UpdateMaximum(ref saveCharanumMax, charanum);
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordSaveToCall(int saveIndex)
    {
        if (!MacroActive)
            return;
        Interlocked.Increment(ref saveToCount);
        lock (Sync)
        {
            saveToCountsByIndex.TryGetValue(saveIndex, out int count);
            saveToCountsByIndex[saveIndex] = count + 1;
        }
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void WriteLayout(object record)
    {
        if (DiagnosticsEnabled)
            WriteRecord(record);
    }

    private static void ResetSaveMetrics()
    {
        saveToTicks = 0;
        sqlSaveTicks = 0;
        serializerTicks = 0;
        characterSerializationTicks = 0;
        nonCharacterSerializationTicks = 0;
        bytesWrittenTotal = 0;
        bytesWrittenMax = 0;
        saveToMaxTicks = 0;
        saveToCount = 0;
        binarySaveCount = 0;
        textSaveCount = 0;
        saveToFailureCount = 0;
        sqlSaveCallCount = 0;
        sqlSaveActiveCount = 0;
        saveCharanumMin = int.MaxValue;
        saveCharanumMax = 0;
        lock (Sync)
            saveToCountsByIndex.Clear();
    }

    private static int GetSaveCount(int saveIndex)
    {
        lock (Sync)
            return saveToCountsByIndex.TryGetValue(saveIndex, out int count) ? count : 0;
    }

    private static Dictionary<int, int> GetSaveToCountsByIndex()
    {
        lock (Sync)
            return new Dictionary<int, int>(saveToCountsByIndex);
    }

    private static void UpdateMinimum(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value < current)
        {
            int previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
                return;
            current = previous;
        }
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
                return;
            current = previous;
        }
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        long current = Interlocked.Read(ref target);
        while (value > current)
        {
            long previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
                return;
            current = previous;
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long StartDiagnosticTiming()
    {
#if PERFORMANCE_METRICS
        return DiagnosticsEnabled && MacroActive ? Stopwatch.GetTimestamp() : 0;
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

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordBenchmarkFunctionEntry(string functionName)
    {
        if (!DiagnosticsEnabled || !MacroActive)
            return;
        if (functionName == "KOJO_BATTLE_INPUT_COMMAND")
            Interlocked.Increment(ref kojoBattleInputCommandEntries);
        else if (functionName == "KOJO_BATTLE_NAVI")
            Interlocked.Increment(ref kojoBattleNaviEntries);
    }

    internal static long GetEffectiveRandomizeSeed(long requestedSeed)
    {
        return MacroActive && benchmarkDeterministicSeed.HasValue
            ? benchmarkDeterministicSeed.Value
            : requestedSeed;
    }

    internal static bool TryGetBenchmarkSeed(out long seed)
    {
        if (benchmarkDeterministicSeed.HasValue)
        {
            seed = benchmarkDeterministicSeed.Value;
            return true;
        }
        seed = 0;
        return false;
    }

    internal static DateTime GetBenchmarkDateTime()
    {
        if (!BenchmarkDeterministicClockEnabled)
            return DateTime.Now;
        long tick = Interlocked.Increment(ref benchmarkClockReadCount);
        return BenchmarkClockEpoch.AddTicks(tick * TimeSpan.TicksPerMillisecond);
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordClockRead(string method, long value, ScriptPosition? position)
    {
        if (!DiagnosticsEnabled)
            return;
        int count = Interlocked.Increment(ref clockReadCount);
        if (count > MaxClockTraceEntries)
        {
            clockReadTraceTruncated = true;
            return;
        }
        lock (Sync)
            clockReadTrace.Add(new BenchmarkClockTraceEntry(
                method,
                position?.Filename ?? "",
                position?.LineNo ?? 0,
                value));
    }

    [Conditional("PERFORMANCE_METRICS")]
    internal static void RecordRandomize(long requestedSeed, long effectiveSeed, ScriptPosition? position, bool applied)
    {
        if (!DiagnosticsEnabled || !MacroActive)
            return;
        int count = Interlocked.Increment(ref randomizeCount);
        if (count > MaxRandomizeTraceEntries)
        {
            randomizeTraceTruncated = true;
            return;
        }
        lock (Sync)
            randomizeTrace.Add(new RandomizeTraceEntry(
                position?.Filename ?? "",
                position?.LineNo ?? 0,
                requestedSeed,
                effectiveSeed,
                Volatile.Read(ref randomCallCount),
                applied));
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

internal sealed record RandomizeTraceEntry(string Source, int Line, long RequestedSeed, long EffectiveSeed, int RandomCallCount, bool Applied);
internal sealed record BenchmarkClockTraceEntry(string Method, string Source, int Line, long Value);

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
    public int KojoBattleInputCommandEntries { get; set; }
    public int KojoBattleNaviEntries { get; set; }
    public long? BenchmarkDeterministicSeed { get; set; }
    public bool BenchmarkDiagnosticsEnabled { get; set; }
    public bool BenchmarkDeterministicClockEnabled { get; set; }
    public int RandomizeCount { get; set; }
    public RandomizeTraceEntry[] RandomizeTrace { get; set; }
    public bool RandomizeTraceTruncated { get; set; }
    public int ClockReadCount { get; set; }
    public BenchmarkClockTraceEntry[] ClockReadTrace { get; set; }
    public bool ClockReadTraceTruncated { get; set; }
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
    public int SaveToCount { get; set; }
    public int Save401Count { get; set; }
    public Dictionary<int, int> SaveToCountsByIndex { get; set; }
    public int BinarySaveCount { get; set; }
    public int TextSaveCount { get; set; }
    public int SaveToFailureCount { get; set; }
#if PERFORMANCE_METRICS
    public CFlagAccessMetrics CdfFlagAccess { get; set; }
    public CFlagPostLoadSparsityMetrics CdfFlagPostLoad { get; set; }
#endif
    public double SaveToTotalMilliseconds { get; set; }
    public double SaveToMeanMilliseconds { get; set; }
    public double SaveToMaxMilliseconds { get; set; }
    public double SqlSaveTotalMilliseconds { get; set; }
    public int SqlSaveCallCount { get; set; }
    public int SqlSaveActiveCount { get; set; }
    public double SerializerTotalMilliseconds { get; set; }
    public double CharacterSerializationTotalMilliseconds { get; set; }
    public double NonCharacterSerializationTotalMilliseconds { get; set; }
    public long BytesWrittenTotal { get; set; }
    public long BytesWrittenMean { get; set; }
    public long BytesWrittenMax { get; set; }
    public int? SaveCharanumMin { get; set; }
    public int? SaveCharanumMax { get; set; }
}

#if PERFORMANCE_METRICS
internal sealed class CFlagAccessMetrics
{
    public long ReadCount { get; set; }
    public long WriteCount { get; set; }
    public long PlusCount { get; set; }
    public long BulkWriteCount { get; set; }
    public long SetAllCount { get; set; }
    public long RawArrayRequestCount { get; set; }
    public int DistinctCharactersRead { get; set; }
    public int DistinctCharactersWritten { get; set; }
    public int DistinctRowsRead { get; set; }
    public int DistinctRowsWritten { get; set; }
}

internal sealed class CFlagPostLoadSparsityMetrics
{
    public int CharacterCount { get; set; }
    public long TotalCells { get; set; }
    public long NonZeroCellCount { get; set; }
    public double DensityPercent { get; set; }
    public int CharactersWithAnyNonZero { get; set; }
    public int CharactersAllZero { get; set; }
    public long RowsWithAnyNonZero { get; set; }
    public long RowsAllZero { get; set; }
    public long[] RowCharactersWithNonZero { get; set; }
    public long[] RowNonZeroCells { get; set; }
    public double[] RowDensityPercent { get; set; }
    public long MinNonZeroCellsPerCharacter { get; set; }
    public double MedianNonZeroCellsPerCharacter { get; set; }
    public long P90NonZeroCellsPerCharacter { get; set; }
    public long MaxNonZeroCellsPerCharacter { get; set; }
    public int NullBackingCount { get; set; }
    public int ShapeMismatchCharacterCount { get; set; }
}
#endif
