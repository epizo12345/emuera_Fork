#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private R1_4ETraceBuffer? r1_4eTrace;
    private readonly Dictionary<CalledFunction, long> r1_4eInvocationIds = [];
    private readonly Stack<R1_4ECallTrace> r1_4ePendingCalls = [];
    private long r1_4eNextInvocationId;
    private int r1_4eBackEdges;
    private bool r1_4eEarlyFlushDone;
    private bool r1_4eFirstDispatchCaptured;
    private LoopInstructionLine? r1_4eActiveLoop;

    private sealed record R1_4ECallTrace(CalledFunction Callee, LoopInstructionLine? Loop, long? CounterBefore);

    private R1_4ETraceBuffer? R1_4ETrace =>
        Program.NextRuntimeR1_4ETraceEnabled
            ? r1_4eTrace ??= new R1_4ETraceBuffer(GetR1_4ETracePath())
            : null;

    private static string GetR1_4ETracePath()
    {
        var dataDirectory = new DirectoryInfo(Program.ExeDir);
        var root = dataDirectory.Name.Equals("Data", StringComparison.OrdinalIgnoreCase)
            ? dataDirectory.Parent?.FullName ?? dataDirectory.FullName
            : dataDirectory.FullName;
        return Path.Combine(root, "R1.4E2_SET_EQUIP_VAR_trace.tsv");
    }

    private bool IsR1_4ETarget(CalledFunction? called)
    {
        if (called is null) return false;
        if (called.FunctionName.Equals("SET_EQUIP_VAR", StringComparison.OrdinalIgnoreCase)) return true;
        return productionLabelIds is not null
            && productionLabelIds.TryGetValue(called.TopLabel, out var id)
            && id.Value == Program.NextRuntimeR1_4ETraceFunctionId;
    }

    internal void TraceR1_4ECalledFunctionCreated(CalledFunction called)
    {
        if (!IsR1_4ETarget(called) || R1_4ETrace is null) return;
        r1_4eInvocationIds.TryAdd(called, ++r1_4eNextInvocationId);
        TraceR1_4E("CalledFunctionCreated", called, "Created", "");
    }

    internal void TraceR1_4E(string eventKind, CalledFunction? called = null, string entryToken = "", string stopReason = "", string loopState = "")
    {
        if (R1_4ETrace is null) return;
        called ??= state is not null && state.functionCount != 0 ? state.CurrentCalled : null;
        if (!IsR1_4ETarget(called)) return;
        if (!r1_4eInvocationIds.TryGetValue(called, out var invocationId))
            r1_4eInvocationIds[called] = invocationId = ++r1_4eNextInvocationId;
        var line = state?.CurrentLine;
        var label = called!.TopLabel ?? called.CurrentLabel;
        var sourceLine = line?.Position?.LineNo ?? label?.Position?.LineNo ?? 0;
        var sourcePath = line?.Position?.Filename ?? label?.Position?.Filename ?? "";
        var vmPc = nextRuntimeSession is null ? -1 : nextRuntimeSession.Machine.CurrentFrame.Pc;
        R1_4ETrace.Record(
            invocationId,
            Program.NextRuntimeR1_4ETraceFunctionId,
            called.FunctionName,
            state?.functionCount ?? 0,
            sourcePath,
            sourceLine,
            vmPc,
            eventKind,
            entryToken,
            nextRuntimeSession is not null ? "YES" : "NO",
            stopReason,
            string.IsNullOrEmpty(loopState) ? $"LineCount={state?.lineCount ?? 0}" : loopState);
    }

    internal long? ReadR1_4ELoopCounter(LoopInstructionLine loop, ExpressionMediator mediator)
    {
        if (loop.LoopCounter is null) return null;
        try { return loop.LoopCounter.GetIntValue(mediator); }
        catch { return null; }
    }

    internal void TraceR1_4EForInit(LoopInstructionLine loop, ExpressionMediator mediator)
    {
        if (state.functionCount == 0 || !IsR1_4ETarget(state.CurrentCalled)) return;
        r1_4eActiveLoop = loop;
        var counter = ReadR1_4ELoopCounter(loop, mediator)?.ToString() ?? "<unavailable>";
        TraceR1_4E("LegacyForState", state.CurrentCalled, loopState:
            $"Phase=FOR;Iteration=0;CounterBeforeNext=<not-applicable>;CounterAfterNext={counter};End={loop.LoopEnd};Step={loop.LoopStep};ContinueDecision=NotApplicable;OwnerSourceIdentity={SourceIdentity(loop)};LoopVariableName={loop.LoopCounter?.Identifier.Name ?? "<unavailable>"};LoopVariableValueBefore=<not-applicable>;LoopVariableValueAfter={counter}");
    }

    internal void TraceR1_4EForNext(LoopInstructionLine loop, ExpressionMediator mediator, long? counterBefore, bool continueDecision)
    {
        if (state.functionCount == 0 || !IsR1_4ETarget(state.CurrentCalled)) return;
        r1_4eActiveLoop = loop;
        var counterAfter = ReadR1_4ELoopCounter(loop, mediator);
        var iteration = Math.Min(r1_4eBackEdges + 1, 16);
        TraceR1_4E("LegacyForState", state.CurrentCalled, loopState:
            $"Phase=NEXT;Iteration={iteration};CounterBeforeNext={counterBefore?.ToString() ?? "<unavailable>"};CounterAfterNext={counterAfter?.ToString() ?? "<unavailable>"};End={loop.LoopEnd};Step={loop.LoopStep};ContinueDecision={(continueDecision ? "Continue" : "Exit")};OwnerSourceIdentity={SourceIdentity(loop)};LoopVariableName={loop.LoopCounter?.Identifier.Name ?? "<unavailable>"};LoopVariableValueBefore={counterBefore?.ToString() ?? "<unavailable>"};LoopVariableValueAfter={counterAfter?.ToString() ?? "<unavailable>"}");
    }

    internal void TraceR1_4ECallEntered(CalledFunction caller, InstructionLine line, CalledFunction callee)
    {
        if (!IsR1_4ETarget(caller) || r1_4ePendingCalls.Count >= 16) return;
        var loop = r1_4eActiveLoop;
        var before = loop is null ? null : ReadR1_4ELoopCounter(loop, exm);
        var selected = IsR1_4ENextEligible(callee) ? "Next" : "Legacy";
        r1_4ePendingCalls.Push(new(callee, loop, before));
        TraceR1_4E("TryCCallForm", caller, loopState:
            $"ResolvedTargetName={callee.FunctionName};TargetFound=YES;CalleeEntered=YES;CalleeFunctionIdentity={callee.TopLabel?.Position?.Filename ?? ""}:{callee.TopLabel?.Position?.LineNo.ToString() ?? "0"};CalleeSelected={selected};CounterBeforeCall={before?.ToString() ?? "<unavailable>"};EndBeforeCall={loop?.LoopEnd.ToString() ?? "<unavailable>"};StepBeforeCall={loop?.LoopStep.ToString() ?? "<unavailable>"}");
    }

    internal void TraceR1_4ECallReturned(CalledFunction callee)
    {
        if (r1_4ePendingCalls.Count == 0 || !ReferenceEquals(r1_4ePendingCalls.Peek().Callee, callee)) return;
        var pending = r1_4ePendingCalls.Pop();
        if (state.functionCount == 0 || !IsR1_4ETarget(state.CurrentCalled)) return;
        var after = pending.Loop is null ? null : ReadR1_4ELoopCounter(pending.Loop, exm);
        TraceR1_4E("TryCCallFormReturn", state.CurrentCalled, loopState:
            $"ResolvedTargetName={callee.FunctionName};TargetFound=YES;CalleeEntered=YES;CalleeFunctionIdentity={callee.TopLabel?.Position?.Filename ?? ""}:{callee.TopLabel?.Position?.LineNo.ToString() ?? "0"};CounterAfterReturn={after?.ToString() ?? "<unavailable>"};EndAfterReturn={pending.Loop?.LoopEnd.ToString() ?? "<unavailable>"};StepAfterReturn={pending.Loop?.LoopStep.ToString() ?? "<unavailable>"};BeforeAfterLoopStateCaptured=YES");
    }

    internal void TraceR1_4EFirstDispatch(CalledFunction? called, bool tokenBefore, bool tokenAfter, bool eligible, NextRuntimeSessionResult result, string fallbackReason)
    {
        if (r1_4eFirstDispatchCaptured || !IsR1_4ETarget(called)) return;
        r1_4eFirstDispatchCaptured = true;
        var vmRan = result != NextRuntimeSessionResult.LegacyFallback;
        TraceR1_4E("FirstDispatchObservation", called, entryToken: $"TokenBefore={(tokenBefore ? "Pending" : "Consumed")};TokenAfter={(tokenAfter ? "Pending" : "Consumed")}", stopReason: result.ToString(), loopState:
            $"LegacySourceLine={state.CurrentLine?.Position?.LineNo ?? 0};TokenBefore={(tokenBefore ? "Pending" : "Consumed")};TokenAfter={(tokenAfter ? "Pending" : "Consumed")};Eligibility={(eligible ? "YES" : "NO")};DispatchResult={result};NextSessionCreated={(vmRan ? "YES" : "NO")};MachineRunStarted={(vmRan ? "YES" : "NO")};VmStopReason={(vmRan ? result.ToString() : "NotRun")};VmStopFunctionId=-1;VmStopPc=-1;SemanticStatus={(vmRan ? "Observed" : "NotRun")};SemanticFault=None;LastHostOperation={(vmRan ? "Observed" : "NotRun")};LastHostSymbol={(vmRan ? "Observed" : "NotRun")};LastHostResult={(vmRan ? "Observed" : "NotRun")};LegacyFallbackReason={fallbackReason};DidVmExecuteBeforeLegacyFallback={(vmRan ? "YES" : "NO")};DidVmPerformHostMutationBeforeFallback=NO");
    }

    private bool IsR1_4ENextEligible(CalledFunction callee)
    {
        return productionLabelIds is not null && productionLabelIds.TryGetValue(callee.TopLabel, out var id)
            && productionDispatchEntryReadyIds.Contains(id.Value);
    }

    private static string SourceIdentity(LoopInstructionLine loop) =>
        $"{loop.Position?.Filename ?? ""}:{loop.Position?.LineNo ?? 0}";

    internal void TraceR1_4EInstruction(InstructionLine line, CalledFunction called, string eventKind)
    {
        if (!IsR1_4ETarget(called)) return;
        TraceR1_4E(eventKind, called, loopState: $"FunctionCode={line.FunctionCode};LineCount={state.lineCount}");
    }

    internal void TraceR1_4EInstructionIfRelevant(InstructionLine line, CalledFunction called)
    {
        var eventKind = line.FunctionCode switch
        {
            FunctionCode.FOR => "FOR init",
            FunctionCode.NEXT => "FOR/NEXT update",
            FunctionCode.BREAK => "BREAK",
            FunctionCode.CONTINUE => "CONTINUE",
            FunctionCode.CALL or FunctionCode.CALLFORM or FunctionCode.TRYCALLFORM or FunctionCode.TRYCCALLFORM => "CALL",
            FunctionCode.JUMP or FunctionCode.JUMPFORM or FunctionCode.TRYJUMPFORM or FunctionCode.TRYCJUMPFORM => "JUMP",
            FunctionCode.RETURN or FunctionCode.RETURNFORM or FunctionCode.RETURNF => "RETURN",
            FunctionCode.SET => "HostWrite",
            _ => null
        };
        if (eventKind is not null) TraceR1_4EInstruction(line, called, eventKind);
    }

    internal void TraceR1_4EInstructionAfter(InstructionLine line, CalledFunction called, LogicalLine before, LogicalLine after)
    {
        if (!IsR1_4ETarget(called) || line.FunctionCode != FunctionCode.NEXT) return;
        if (before?.Position is not null && after?.Position is not null
            && before.Position.Value.Filename.Equals(after.Position.Value.Filename, StringComparison.OrdinalIgnoreCase)
            && after.Position.Value.LineNo <= before.Position.Value.LineNo)
        {
            TraceR1_4E("LoopBackEdge", called, loopState: $"FromLine={before.Position.Value.LineNo};ToLine={after.Position.Value.LineNo};LineCount={state.lineCount}");
            r1_4eBackEdges++;
            if (r1_4eBackEdges >= 16 && !r1_4eEarlyFlushDone)
            {
                r1_4eEarlyFlushDone = true;
                R1_4ETrace?.Flush("EarlyBackEdge16");
            }
        }
    }

    internal void FlushR1_4EBeforeInfiniteLoopDialog()
    {
        R1_4ETrace?.Flush("InfiniteLoopDialog");
    }

    private sealed class R1_4ETraceBuffer
    {
        private const int Capacity = 8192;
        private const int PrefixCapacity = 256;
        private readonly string[] rows = new string[Capacity];
        private readonly string[] prefixRows = new string[PrefixCapacity];
        private readonly string path;
        private int count;
        private int next;
        private long sequence;
        private int dropped;
        private int prefixCount;

        internal R1_4ETraceBuffer(string path) { this.path = path; }

        internal void Record(long invocationId, int runtimeFunctionId, string functionName, int legacyDepth, string sourcePath, int sourceLine, int vmPc, string eventKind, string entryToken, string vmSession, string stopReason, string loopState)
        {
            var fields = new[]
            {
                (++sequence).ToString(), invocationId.ToString(), runtimeFunctionId.ToString(), Clean(functionName), legacyDepth.ToString(),
                Clean(sourcePath), sourceLine.ToString(), vmPc.ToString(), eventKind, entryToken, vmSession, stopReason, Clean(loopState)
            };
            rows[next] = string.Join('\t', fields);
            if (prefixCount < PrefixCapacity) prefixRows[prefixCount++] = rows[next];
            next = (next + 1) % Capacity;
            if (count < Capacity) count++;
            else dropped++;
        }

        internal void Flush(string reason)
        {
            var output = new StringBuilder();
            output.AppendLine("TraceFormat=R1.4E2-v1");
            output.AppendLine($"TraceCapacity={Capacity}");
            output.AppendLine($"EventsDropped={dropped}");
            output.AppendLine($"FlushReason={reason}");
            output.AppendLine($"PrefixTraceCapacity={PrefixCapacity}");
            output.AppendLine("PREFIX");
            output.AppendLine("Sequence\tInvocationTraceId\tRuntimeFunctionId\tFunctionName\tLegacyCallDepth\tLegacySourcePath\tLegacySourceLine\tVmPc\tEventKind\tEntryDispatchTokenState\tVmSessionPresent\tStopReason\tRelevantLoopState");
            for (var index = 0; index < prefixCount; index++) output.AppendLine(prefixRows[index]);
            output.AppendLine("TAIL");
            output.AppendLine("Sequence\tInvocationTraceId\tRuntimeFunctionId\tFunctionName\tLegacyCallDepth\tLegacySourcePath\tLegacySourceLine\tVmPc\tEventKind\tEntryDispatchTokenState\tVmSessionPresent\tStopReason\tRelevantLoopState");
            var first = (next - count + Capacity) % Capacity;
            for (var index = 0; index < count; index++)
                output.AppendLine(rows[(first + index) % Capacity]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
        }

        private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
