#if WEB_RUNTIME && ERB_EXECUTION_PROFILE
using System.Diagnostics;
using System.Text.Json;
using MinorShift.Emuera.Runtime.Script.Statements;

namespace MinorShift.Emuera.GameProc;

internal sealed class ErbExecutionProfiler
{
    readonly Dictionary<FunctionCode, InstructionTotals> instructions = [];
    readonly Dictionary<string, FunctionTotals> functions = new(StringComparer.Ordinal);
    readonly List<FunctionFrame> functionStack = [];
    readonly List<InstructionFrame> instructionStack = [];
    bool macroActive;
    bool enabled;
    bool instructionsEnabled;
    string mode = "off";
    long macroStarted;
    int scriptDepth;
    long segmentStart;
    int doScriptCalls;
    long doScriptTicks;

    internal static string RunCalibrationSelfCheck()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Burn(int milliseconds)
        {
            long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000 * milliseconds;
            while (Stopwatch.GetTimestamp() < until) { }
        }

        var results = new List<object>(3);
        foreach (string profileMode in new[] { "off", "functions", "full" })
        {
            var profiler = new ErbExecutionProfiler();
            long macroStarted = Stopwatch.GetTimestamp();
            profiler.BeginMacro([], profileMode, macroStarted);
            long wrapperStarted = Stopwatch.GetTimestamp();

            long call = profiler.StartDoScript();
            profiler.EnterScriptProc();
            profiler.BeginInstruction(FunctionCode.SET);
            Burn(1);
            profiler.EnterFunction(null);
            profiler.EnterScriptProc();
            profiler.BeginInstruction(FunctionCode.IF);
            Burn(1);
            profiler.EndInstruction();
            profiler.ExitScriptProc();
            profiler.ExitFunction();
            profiler.EndInstruction();
            profiler.ExitScriptProc();
            profiler.EndDoScript(call);

            call = profiler.StartDoScript();
            profiler.EnterScriptProc();
            profiler.EnterFunction(null);
            Burn(1);
            profiler.ExitScriptProc();
            profiler.EndDoScript(call);
            Burn(5);
            call = profiler.StartDoScript();
            profiler.EnterScriptProc();
            Burn(1);
            profiler.ExitFunction();
            profiler.ExitScriptProc();
            profiler.EndDoScript(call);

            string json = profiler.FinishMacro(3, Stopwatch.GetElapsedTime(wrapperStarted).Ticks);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement instrumented = root.GetProperty("instrumented");
            int instructionCalls = instrumented.GetProperty("instructionCalls").GetInt32();
            double functionExclusive = instrumented.GetProperty("functionExclusiveMilliseconds").GetDouble();
            double functionInclusive = instrumented.GetProperty("functionInclusiveMilliseconds").GetDouble();
            JsonElement functionRows = root.GetProperty("functionRanking");
            JsonElement instructionRows = root.GetProperty("instructionRanking");
            Require(root.GetProperty("doScript").GetProperty("calls").GetInt32() == 3, $"{profileMode}: DoScript outer timing");
            Require(root.GetProperty("runScriptWrapper").GetProperty("calls").GetInt32() == 3, $"{profileMode}: wrapper timing");
            Require(root.GetProperty("macroWallMilliseconds").GetDouble() > 0, $"{profileMode}: macro wall timing");
            if (profileMode == "off")
                Require(instructionCalls == 0 && functionRows.GetArrayLength() == 0 && functionExclusive == 0, "off: detailed counters must be zero");
            else
                Require(functionRows.GetArrayLength() > 0 && functionExclusive > 0 && functionInclusive >= functionExclusive, $"{profileMode}: nested function counters");
            if (profileMode == "functions")
                Require(instructionCalls == 0, "functions: instruction counters must be zero");
            if (profileMode == "full")
            {
                Require(instructionCalls == 2 && instrumented.GetProperty("instructionExclusiveMilliseconds").GetDouble() > 0, "full: exact nested instruction counters");
                var byOperation = instructionRows.EnumerateArray().ToDictionary(
                    row => row.GetProperty("operation").GetString()!,
                    row => row.GetProperty("calls").GetInt32(),
                    StringComparer.Ordinal);
                Require(byOperation.GetValueOrDefault("SET") == 1 && byOperation.GetValueOrDefault("IF") == 1, "full: SET/IF instruction ranking");
                JsonElement set = instructionRows.EnumerateArray().Single(row => row.GetProperty("operation").GetString() == "SET");
                Require(set.GetProperty("inclusiveMilliseconds").GetDouble() > set.GetProperty("exclusiveMilliseconds").GetDouble(), "full: SET exclusive excludes nested IF time");
            }
            if (profileMode != "off")
                Require(root.GetProperty("macroWallMilliseconds").GetDouble() - functionInclusive > 2, $"{profileMode}: suspended time must not accrue to functions");
            results.Add(new { mode = profileMode, macroWallMilliseconds = root.GetProperty("macroWallMilliseconds").GetDouble(), instructionCalls, functionExclusiveMilliseconds = functionExclusive, functionInclusiveMilliseconds = functionInclusive });
        }
        return JsonSerializer.Serialize(results);
    }

    internal void BeginMacro(IReadOnlyList<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine> activeFunctions, string profileMode, long started)
    {
        if (profileMode is not ("off" or "functions" or "full"))
            throw new ArgumentOutOfRangeException(nameof(profileMode));
        mode = profileMode;
        macroActive = true;
        enabled = mode != "off";
        instructionsEnabled = mode == "full";
        macroStarted = started == 0 ? Stopwatch.GetTimestamp() : started;
        instructions.Clear();
        functions.Clear();
        functionStack.Clear();
        instructionStack.Clear();
        scriptDepth = 0;
        segmentStart = 0;
        doScriptCalls = 0;
        doScriptTicks = 0;
        if (enabled)
            foreach (var label in activeFunctions)
                functionStack.Add(new(label.LabelName, label.Position?.Filename ?? "", label.Position?.LineNo ?? 0, seeded: true));
    }

    internal long StartDoScript()
    {
        if (!macroActive) return 0;
        doScriptCalls++;
        return Stopwatch.GetTimestamp();
    }

    internal void EndDoScript(long started)
    {
        if (started != 0)
            doScriptTicks += Stopwatch.GetTimestamp() - started;
    }

    internal void EnterScriptProc()
    {
        if (!enabled) return;
        if (scriptDepth > 0)
        {
            Pause();
            SuspendCurrentInstruction();
        }
        scriptDepth++;
        Resume();
    }

    internal void ExitScriptProc()
    {
        if (!enabled) return;
        Pause();
        scriptDepth--;
        if (scriptDepth > 0)
        {
            ResumeCurrentInstruction();
            Resume();
        }
    }

    internal void BeginInstruction(FunctionCode code)
    {
        if (!instructionsEnabled) return;
        Pause();
        instructionStack.Add(new(code, Stopwatch.GetTimestamp()));
        Resume();
    }

    internal void EndInstruction()
    {
        if (!instructionsEnabled || instructionStack.Count == 0) return;
        Pause();
        InstructionFrame frame = instructionStack[^1];
        instructionStack.RemoveAt(instructionStack.Count - 1);
        long inclusive = Stopwatch.GetTimestamp() - frame.Started;
        if (!instructions.TryGetValue(frame.Code, out InstructionTotals total))
            total = default;
        total.Count++;
        total.ExclusiveTicks += frame.ExclusiveTicks;
        total.InclusiveTicks += inclusive;
        instructions[frame.Code] = total;
        Resume();
    }

    internal void EnterFunction(MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine? label)
    {
        if (!enabled) return;
        Pause();
        string name = label?.LabelName ?? "<unknown>";
        functionStack.Add(new(name, label?.Position?.Filename ?? "", label?.Position?.LineNo ?? 0, seeded: false));
        GetFunction(name).EnteredCalls++;
        Resume();
    }

    internal void ExitFunction()
    {
        if (!enabled || functionStack.Count == 0) return;
        Pause();
        FunctionFrame frame = functionStack[^1];
        functionStack.RemoveAt(functionStack.Count - 1);
        CompleteFunction(frame, interrupted: false);
        Resume();
    }

    internal void ClearFunctions()
    {
        if (!enabled) return;
        Pause();
        while (functionStack.Count > 0)
        {
            FunctionFrame frame = functionStack[^1];
            functionStack.RemoveAt(functionStack.Count - 1);
            CompleteFunction(frame, interrupted: true);
        }
        Resume();
    }

    internal string FinishMacro(int runScriptCalls, long runScriptTimeSpanTicks)
    {
        if (!macroActive) return "{}";
        long macroFinished = Stopwatch.GetTimestamp();
        if (enabled) Pause();
        macroActive = false;
        enabled = false;
        instructionsEnabled = false;
        long macroWallTicks = macroFinished - macroStarted;
        double processDoScriptMilliseconds = Ms(doScriptTicks);
        double runScriptMilliseconds = Math.Round(TimeSpan.FromTicks(runScriptTimeSpanTicks).TotalMilliseconds, 3);
        long functionExclusiveTicks = functions.Values.Sum(x => x.ExclusiveTicks);
        long functionInclusiveTicks = functions.Values.Sum(x => x.InclusiveTicks);
        long instructionExclusiveTicks = instructions.Values.Sum(x => x.ExclusiveTicks);
        string[] openFrames = GetOpenFrameRows();
        functionExclusiveTicks += openFunctionExclusiveTicks;
        functionInclusiveTicks += openFunctionInclusiveTicks;
        var instructionRows = instructions
            .Select(pair => new
            {
                operation = pair.Key.ToString(),
                category = Category(pair.Key),
                calls = pair.Value.Count,
                exclusiveMilliseconds = Ms(pair.Value.ExclusiveTicks),
                inclusiveMilliseconds = Ms(pair.Value.InclusiveTicks),
                meanExclusiveMilliseconds = pair.Value.Count == 0 ? 0 : Ms(pair.Value.ExclusiveTicks) / pair.Value.Count,
                percentOfDoScript = doScriptTicks == 0 ? 0 : Math.Round(pair.Value.ExclusiveTicks * 100.0 / doScriptTicks, 3)
            })
            .OrderByDescending(x => x.exclusiveMilliseconds)
            .ToArray();
        var functionRows = functions
            .Select(pair => new
            {
                function = pair.Key,
                calls = pair.Value.EnteredCalls,
                completedCalls = pair.Value.CompletedCalls,
                interruptedCalls = pair.Value.InterruptedCalls,
                seededPartialFrames = pair.Value.SeededPartialFrames,
                openCalls = pair.Value.OpenCalls,
                inclusiveMilliseconds = Ms(pair.Value.InclusiveTicks + pair.Value.OpenInclusiveTicks),
                exclusiveMilliseconds = Ms(pair.Value.ExclusiveTicks + pair.Value.OpenExclusiveTicks),
                meanInclusiveMilliseconds = pair.Value.EnteredCalls == 0 ? 0 : Ms(pair.Value.InclusiveTicks + pair.Value.OpenInclusiveTicks) / pair.Value.EnteredCalls
            })
            .OrderByDescending(x => x.inclusiveMilliseconds)
            .ToArray();
        var categoryRows = instructionRows
            .GroupBy(x => x.category, StringComparer.Ordinal)
            .Select(group => new
            {
                category = group.Key,
                calls = group.Sum(x => x.calls),
                exclusiveMilliseconds = Math.Round(group.Sum(x => x.exclusiveMilliseconds), 3),
                percentOfDoScript = doScriptTicks == 0 ? 0 : Math.Round(group.Sum(x => x.exclusiveMilliseconds) * 100.0 / Ms(doScriptTicks), 3)
            })
            .OrderByDescending(x => x.exclusiveMilliseconds)
            .ToArray();
        return JsonSerializer.Serialize(new
        {
            type = "erbExecutionProfile",
            mode,
            stopwatchFrequency = Stopwatch.Frequency,
            macroWallTicks,
            macroWallMilliseconds = Ms(macroWallTicks),
            runScriptWrapper = new
            {
                calls = runScriptCalls,
                elapsedMilliseconds = runScriptMilliseconds,
                beyondProcessDoScriptMilliseconds = Math.Round(runScriptMilliseconds - processDoScriptMilliseconds, 3),
                boundary = "BrowserRuntimeSession.RunScript: Process.DoScript + console.CompleteInputGeneration"
            },
            scope = "macro-triggered Process.DoScript and ERB instruction execution; synchronous execution only",
            timing = "instruction exclusive pauses around nested #FUNCTION execution; instruction inclusive includes nested expression functions; function inclusive aggregates children; function exclusive is self execution",
            doScript = new { calls = doScriptCalls, inclusiveMilliseconds = Ms(doScriptTicks), meanMilliseconds = doScriptCalls == 0 ? 0 : Ms(doScriptTicks) / doScriptCalls },
            instrumented = new
            {
                instructionCalls = instructions.Values.Sum(x => x.Count),
                instructionExclusiveMilliseconds = Ms(instructionExclusiveTicks),
                functionExclusiveMilliseconds = Ms(functionExclusiveTicks),
                functionInclusiveMilliseconds = Ms(functionInclusiveTicks),
                unclassifiedDoScriptMilliseconds = Ms(Math.Max(0, doScriptTicks - functionExclusiveTicks)),
                functionTimingCanExceedDoScript = functionInclusiveTicks > doScriptTicks,
                openFunctionFrames = openFrames
            },
            instructionRanking = instructionRows,
            functionRanking = functionRows,
            instructionCategories = categoryRows
        });
    }

    long openFunctionExclusiveTicks;
    long openFunctionInclusiveTicks;

    string[] GetOpenFrameRows()
    {
        openFunctionExclusiveTicks = 0;
        openFunctionInclusiveTicks = 0;
        var rows = new List<string>(functionStack.Count);
        long childInclusive = 0;
        for (int i = functionStack.Count - 1; i >= 0; i--)
        {
            FunctionFrame frame = functionStack[i];
            long inclusive = frame.InclusiveTicks + childInclusive;
            openFunctionExclusiveTicks += frame.ExclusiveTicks;
            openFunctionInclusiveTicks += inclusive;
            FunctionTotals total = GetFunction(frame.Name);
            if (frame.Seeded)
                total.SeededPartialFrames++;
            else
                total.OpenCalls++;
            total.OpenExclusiveTicks += frame.ExclusiveTicks;
            total.OpenInclusiveTicks += inclusive;
            childInclusive = inclusive;
            rows.Add($"{frame.Name} ({frame.File}:{frame.Line}) {(frame.Seeded ? "partial-at-start" : "open-at-end")}");
        }
        rows.Reverse();
        return rows.ToArray();
    }

    void CompleteFunction(FunctionFrame frame, bool interrupted)
    {
        FunctionTotals total = GetFunction(frame.Name);
        if (frame.Seeded)
            total.SeededPartialFrames++;
        else if (interrupted)
            total.InterruptedCalls++;
        else
            total.CompletedCalls++;
        total.InclusiveTicks += frame.InclusiveTicks;
        total.ExclusiveTicks += frame.ExclusiveTicks;
        if (functionStack.Count > 0)
        {
            FunctionFrame parent = functionStack[^1];
            parent.InclusiveTicks += frame.InclusiveTicks;
            functionStack[^1] = parent;
        }
    }

    FunctionTotals GetFunction(string name)
    {
        if (!functions.TryGetValue(name, out FunctionTotals total))
            functions[name] = total = new();
        return total;
    }

    void Pause()
    {
        if (segmentStart == 0) return;
        long elapsed = Stopwatch.GetTimestamp() - segmentStart;
        segmentStart = 0;
        if (functionStack.Count > 0)
        {
            FunctionFrame current = functionStack[^1];
            current.InclusiveTicks += elapsed;
            current.ExclusiveTicks += elapsed;
            functionStack[^1] = current;
        }
        if (instructionStack.Count > 0)
        {
            InstructionFrame current = instructionStack[^1];
            if (!current.Suspended)
            {
                current.ExclusiveTicks += elapsed;
                instructionStack[^1] = current;
            }
        }
    }

    void Resume()
    {
        if (enabled && scriptDepth > 0 && segmentStart == 0)
            segmentStart = Stopwatch.GetTimestamp();
    }

    void SuspendCurrentInstruction()
    {
        if (instructionStack.Count == 0) return;
        InstructionFrame current = instructionStack[^1];
        current.Suspended = true;
        instructionStack[^1] = current;
    }

    void ResumeCurrentInstruction()
    {
        if (instructionStack.Count == 0) return;
        InstructionFrame current = instructionStack[^1];
        current.Suspended = false;
        instructionStack[^1] = current;
    }

    static string Category(FunctionCode code)
    {
        string name = code.ToString();
        if (name.StartsWith("PRINT", StringComparison.Ordinal)
            || name.StartsWith("DRAW", StringComparison.Ordinal)
            || name.StartsWith("HTML", StringComparison.Ordinal))
            return "display preparation";
        if (name is "IF" or "SIF" or "ELSEIF" or "ELSE" or "ENDIF" or "SELECTCASE" or "CASE" or "CASEELSE" or "ENDSELECT"
            || name.StartsWith("CALL", StringComparison.Ordinal)
            || name.StartsWith("JUMP", StringComparison.Ordinal)
            || name is "RETURN" or "RETURNF")
            return "flow control / branch";
        if (name.StartsWith("STR", StringComparison.Ordinal) || name.Contains("FIND", StringComparison.Ordinal) || name.Contains("FORM", StringComparison.Ordinal))
            return "string / format command";
        if (name is "SET" or "ADD" or "SUB" or "MULTI" or "DIV" or "MOD" or "SWAP" or "CLEARARRAY" or "COPYARRAY" or "SORTARRAY")
            return "variable / array command";
        return "other ERB command";
    }

    static double Ms(long ticks) => Math.Round(ticks * 1000.0 / Stopwatch.Frequency, 3);

    struct FunctionFrame(string name, string file, int line, bool seeded)
    {
        internal readonly string Name = name;
        internal readonly string File = file;
        internal readonly int Line = line;
        internal readonly bool Seeded = seeded;
        internal long InclusiveTicks;
        internal long ExclusiveTicks;
    }

    struct InstructionFrame(FunctionCode code, long started)
    {
        internal readonly FunctionCode Code = code;
        internal readonly long Started = started;
        internal long ExclusiveTicks;
        internal bool Suspended;
    }

    struct InstructionTotals { internal long Count; internal long ExclusiveTicks; internal long InclusiveTicks; }

    sealed class FunctionTotals
    {
        internal long EnteredCalls;
        internal long CompletedCalls;
        internal long InterruptedCalls;
        internal long SeededPartialFrames;
        internal long OpenCalls;
        internal long InclusiveTicks;
        internal long ExclusiveTicks;
        internal long OpenInclusiveTicks;
        internal long OpenExclusiveTicks;
    }
}

internal sealed partial class Process
{
    readonly ErbExecutionProfiler erbExecutionProfiler = new();

    internal static string RunErbExecutionProfilerSelfCheck() => ErbExecutionProfiler.RunCalibrationSelfCheck();
    internal void BeginErbExecutionProfile(string mode, long macroStartedTimestamp) => erbExecutionProfiler.BeginMacro(state.GetErbProfileFunctionStack(), mode, macroStartedTimestamp);
    internal string FinishErbExecutionProfile(int runScriptCalls, long runScriptTimeSpanTicks) => erbExecutionProfiler.FinishMacro(runScriptCalls, runScriptTimeSpanTicks);
}
#endif
