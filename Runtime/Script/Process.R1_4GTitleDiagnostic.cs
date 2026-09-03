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
    private readonly Dictionary<CalledFunction, long> r1_4gTitleInvocations = [];
    private long r1_4gTitleSequence;
    private long r1_4gTitleNextInvocation;

    private bool R1_4GTitleTraceEnabled => Program.NextRuntimeR1_4GTitleTraceEnabled;

    private string R1_4GTitleTracePath
    {
        get
        {
            var root = new DirectoryInfo(Program.ExeDir);
            if (root.Name.Equals("Data", StringComparison.OrdinalIgnoreCase)) root = root.Parent ?? root;
            return Path.Combine(root.FullName, "R1.4G_TITLE_FLOW_trace.tsv");
        }
    }

    private bool IsR1_4GTitleFunction(CalledFunction? called) =>
        called is not null && (called.FunctionName.Equals("GETS_USEABLE_EXTRA_TITLE", StringComparison.OrdinalIgnoreCase)
            || called.FunctionName.Equals("GET_USEABLE_EXTRA_TITLE", StringComparison.OrdinalIgnoreCase));

    private long R1_4GTitleInvocation(CalledFunction called) =>
        r1_4gTitleInvocations.TryGetValue(called, out var id) ? id : r1_4gTitleInvocations[called] = ++r1_4gTitleNextInvocation;

    internal void TraceR1_4GTitleDispatch(CalledFunction? called, bool opportunity, bool eligible, object result, string reason)
    {
        if (!R1_4GTitleTraceEnabled || !IsR1_4GTitleFunction(called)) return;
        var label = called!.TopLabel;
        WriteR1_4GTitleRow("EntryDispatch", called, label?.Position?.LineNo ?? 0,
            $"Opportunity={Bool(opportunity)};Eligibility={Bool(eligible)};Result={Clean(result.ToString() ?? string.Empty)};RejectReason={Clean(reason)}");
    }

    internal void TraceR1_4GTitleSif(InstructionLine line, ProcessState currentState, long value)
    {
        var called = currentState.functionCount == 0 ? null : currentState.CurrentCalled;
        if (!R1_4GTitleTraceEnabled || !IsR1_4GTitleFunction(called)) return;
        var expression = (line.Argument as ExpressionArgument)?.Term?.ToString() ?? string.Empty;
        WriteR1_4GTitleRow("SIF", called, line.Position?.LineNo ?? 0,
            $"Expression={Clean(expression)};EvaluatedInteger={value};Branch={(value == 0 ? "SkipNext" : "FallThrough")}");
    }

    internal void TraceR1_4GTitleFindElement(string arrayName, string targetType, string targetValue, long start, long end, bool exact, long result)
    {
        var called = state.functionCount == 0 ? null : state.CurrentCalled;
        if (!R1_4GTitleTraceEnabled || !IsR1_4GTitleFunction(called)) return;
        WriteR1_4GTitleRow("FINDELEMENT", called, state.CurrentLine?.Position?.LineNo ?? 0,
            $"Array={Clean(arrayName)};TargetType={targetType};Target={Clean(targetValue)};Start={start};End={end};Exact={Bool(exact)};Return={result}");
    }

    internal void TraceR1_4GTitleReturn(InstructionLine line, SingleTerm? value, ExpressionMediator mediator)
    {
        var called = state.functionCount == 0 ? null : state.CurrentCalled;
        if (!R1_4GTitleTraceEnabled || !IsR1_4GTitleFunction(called)) return;
        var rendered = value is null ? "<default>" : value.IsInteger ? value.GetIntValue(mediator).ToString() : Clean(value.GetStrValue(mediator));
        WriteR1_4GTitleRow("RETURNF", called, line.Position?.LineNo ?? 0, $"ReturnType={(value?.IsInteger == true ? "Integer" : "String")};Return={rendered}");
    }

    internal void TraceR1_4GTitleInstruction(InstructionLine line, CalledFunction called)
    {
        if (!R1_4GTitleTraceEnabled || !IsR1_4GTitleFunction(called)) return;
        WriteR1_4GTitleRow("Instruction", called, line.Position?.LineNo ?? 0, $"FunctionCode={line.FunctionCode}");
    }

    private void WriteR1_4GTitleRow(string eventKind, CalledFunction called, int sourceLine, string details)
    {
        var path = R1_4GTitleTracePath;
        var sourcePath = called.TopLabel?.Position?.Filename ?? called.CurrentLabel?.Position?.Filename ?? string.Empty;
        var id = productionLabelIds is not null && productionLabelIds.TryGetValue(called.TopLabel, out var runtimeId) ? runtimeId.Value.ToString() : "-1";
        var selected = nextRuntimeSession is not null ? "Next" : "Legacy";
        var row = string.Join('\t', (++r1_4gTitleSequence).ToString(), eventKind, Clean(called.FunctionName), id,
            Clean(sourcePath), sourceLine.ToString(), selected, state.functionCount.ToString(), R1_4GTitleInvocation(called).ToString(), details);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllText(path, "Sequence\tEvent\tFunctionName\tRuntimeFunctionId\tSourcePath\tSourceLine\tSelectedEngine\tCallDepth\tInvocationId\tDetails\n", new UTF8Encoding(false));
            File.AppendAllText(path, row + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
    }

    private static string Bool(bool value) => value ? "YES" : "NO";
    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
