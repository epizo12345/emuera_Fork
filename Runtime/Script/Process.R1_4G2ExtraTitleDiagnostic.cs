#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private long r1_4g2ExtraTitleSequence;
    private bool r1_4g2ExtraTitleConstruction;
    private bool r1_4g2FirstWriteCaptured;
    private bool r1_4g2FirstWriteAfterCaptured;

    private bool R1_4G2ExtraTitleEnabled => Program.NextRuntimeR1_4G2ExtraTitleTraceEnabled;

    private string R1_4G2ExtraTitleTracePath
    {
        get
        {
            var root = new DirectoryInfo(Program.ExeDir);
            if (root.Name.Equals("Data", StringComparison.OrdinalIgnoreCase)) root = root.Parent ?? root;
            var mode = Program.NextRuntimeMode ? "next" : "legacy";
            return Path.Combine(root.FullName, $"R1.4G2_EXTRA_TITLE_{mode}.tsv");
        }
    }

    private static bool IsR1_4G2ExtraTitleFunction(CalledFunction? called) =>
        called is not null && (called.FunctionName.Equals("SET_EXTRA_TITLE_VAR", StringComparison.OrdinalIgnoreCase)
            || called.FunctionName.Equals("GETS_USEABLE_EXTRA_TITLE", StringComparison.OrdinalIgnoreCase)
            || called.FunctionName.Equals("GET_USEABLE_EXTRA_TITLE", StringComparison.OrdinalIgnoreCase));

    private static string R1_4G2Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string R1_4G2Bool(bool value) => value ? "YES" : "NO";

    private string R1_4G2Engine => nextRuntimeSession is null ? "Legacy" : "Next";

    internal void TraceR1_4G2ExtraTitleEntry(CalledFunction? caller, CalledFunction callee)
    {
        if (!R1_4G2ExtraTitleEnabled || !IsR1_4G2ExtraTitleFunction(callee)) return;
        if (callee.FunctionName.Equals("SET_EXTRA_TITLE_VAR", StringComparison.OrdinalIgnoreCase))
        {
            r1_4g2ExtraTitleConstruction = true;
            WriteR1_4G2Row("SET_EXTRA_TITLE_VAR_Entry", callee,
                $"Caller={R1_4G2Clean(caller?.FunctionName ?? "<root>")};Callee={R1_4G2Clean(callee.FunctionName)};ARGRead=NO;ARGSRead=NO");
            WriteR1_4G2Snapshot("Before_SET_EXTRA_TITLE_VAR", callee);
        }
        else
        {
            WriteR1_4G2Row("CallEntry", callee,
                $"Caller={R1_4G2Clean(caller?.FunctionName ?? "<root>")};Callee={R1_4G2Clean(callee.FunctionName)};SelectedEngine={R1_4G2Engine}");
        }
    }

    internal void TraceR1_4G2ExtraTitleExit(CalledFunction callee, SingleTerm? returnValue)
    {
        if (!R1_4G2ExtraTitleEnabled || !IsR1_4G2ExtraTitleFunction(callee)) return;
        WriteR1_4G2Row(callee.FunctionName.Equals("SET_EXTRA_TITLE_VAR", StringComparison.OrdinalIgnoreCase)
                ? "SET_EXTRA_TITLE_VAR_Exit" : "CallExit", callee,
            $"SelectedEngine={R1_4G2Engine};Return={(returnValue is null ? "<default>" : returnValue.IsInteger ? returnValue.GetIntValue(exm).ToString() : R1_4G2Clean(returnValue.GetStrValue(exm)))}");
        if (callee.FunctionName.Equals("SET_EXTRA_TITLE_VAR", StringComparison.OrdinalIgnoreCase))
        {
            WriteR1_4G2Snapshot("After_SET_EXTRA_TITLE_VAR", callee);
            WriteR1_4G2Snapshot("After_EXTRA_TITLE_Initialization", callee);
            r1_4g2ExtraTitleConstruction = false;
        }
    }

    internal void TraceR1_4G2ExtraTitleDispatch(CalledFunction? called, NextRuntimeSessionResult result, string fallbackReason)
    {
        if (!R1_4G2ExtraTitleEnabled || (!r1_4g2ExtraTitleConstruction && !IsR1_4G2ExtraTitleFunction(called))) return;
        if (called is null) return;
        var selected = result == NextRuntimeSessionResult.LegacyFallback ? "Legacy" : "Next";
        WriteR1_4G2Row("Dispatch", called,
            $"SelectedEngine={selected};EntryDispatchResult={result};VmStopReason={(selected == "Next" ? result.ToString() : "NotRun")};LegacyFallbackReason={R1_4G2Clean(fallbackReason)};DidNextExecute={R1_4G2Bool(selected == "Next")};DidNextWriteEXTRA_TITLE=NOT_OBSERVED;DidNextMutateOtherHostStateBeforeFallback=NOT_OBSERVED");
    }

    internal void TraceR1_4G2ExtraTitleWrite(InstructionLine line, VariableTerm variable, long index, string oldValue, string newValue, string operationKind)
    {
        if (!R1_4G2ExtraTitleEnabled || !variable.Identifier.Name.Equals("EXTRA_TITLE", StringComparison.OrdinalIgnoreCase)) return;
        if (!r1_4g2FirstWriteCaptured)
        {
            r1_4g2FirstWriteCaptured = true;
            WriteR1_4G2Snapshot("Before_First_EXTRA_TITLE_Write", state.CurrentCalled);
        }
        WriteR1_4G2Row("EXTRA_TITLE_Write", state.CurrentCalled,
            $"OperationKind={operationKind};ArrayIndex={index};OldStringValue={R1_4G2Clean(oldValue)};NewStringValue={R1_4G2Clean(newValue)};StartIndex={index};EndIndex={index + 1};AffectedCount=1",
            line.Position?.Filename, line.Position?.LineNo);
        if (!r1_4g2FirstWriteAfterCaptured)
        {
            r1_4g2FirstWriteAfterCaptured = true;
            WriteR1_4G2Snapshot("After_First_EXTRA_TITLE_Write", state.CurrentCalled);
        }
    }

    internal void TraceR1_4G2ExtraTitleSearch(string targetType, string target, long start, long end, bool exact, long result)
    {
        if (!R1_4G2ExtraTitleEnabled || state.functionCount == 0 ||
            !state.CurrentCalled.FunctionName.Equals("GETS_USEABLE_EXTRA_TITLE", StringComparison.OrdinalIgnoreCase)) return;
        WriteR1_4G2Row("EXTRA_TITLE_Search", state.CurrentCalled,
            $"Array=EXTRA_TITLE;TargetType={targetType};Target={R1_4G2Clean(target)};StartIndex={start};EndIndex={end};Exact={R1_4G2Bool(exact)};Return={result}");
    }

    internal void TraceR1_4G2ExtraTitleSearchStart(string targetType, string target, long start, long end, bool exact)
    {
        if (!R1_4G2ExtraTitleEnabled || state.functionCount == 0 ||
            !state.CurrentCalled.FunctionName.Equals("GETS_USEABLE_EXTRA_TITLE", StringComparison.OrdinalIgnoreCase)) return;
        WriteR1_4G2Snapshot("Before_GETS_USEABLE_EXTRA_TITLE_Search", state.CurrentCalled);
        WriteR1_4G2Row("EXTRA_TITLE_SearchStart", state.CurrentCalled,
            $"Array=EXTRA_TITLE;TargetType={targetType};Target={R1_4G2Clean(target)};StartIndex={start};EndIndex={end};Exact={R1_4G2Bool(exact)}");
    }

    internal void TraceR1_4G2ExtraTitleSearchResult(string targetType, string target, long start, long end, bool exact, long result) =>
        TraceR1_4G2ExtraTitleSearch(targetType, target, start, end, exact, result);

    private void WriteR1_4G2Snapshot(string checkpoint, CalledFunction called)
    {
        if (!R1_4G2ExtraTitleEnabled) return;
        var values = new string[100];
        try
        {
            var token = idDic.GetVariableToken("EXTRA_TITLE", null, true);
            if (token is null) return;
            for (var index = 0; index < values.Length; index++)
                values[index] = token.GetStrValue(exm, [index]) ?? string.Empty;
            var joined = string.Join("\0", values);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
            var madokaIndex = Array.FindIndex(values, value => value == "まどかマギカ");
            WriteR1_4G2Row("Snapshot", called,
                $"Checkpoint={checkpoint};NonEmptyCount0To99={values.Count(value => !string.IsNullOrEmpty(value))};Hash0To99={hash};ContainsMadokaMagica={R1_4G2Bool(madokaIndex >= 0)};MadokaMagicaIndex={madokaIndex}");
            if (madokaIndex >= 0)
            {
                var begin = Math.Max(0, madokaIndex - 2);
                var finish = Math.Min(values.Length - 1, madokaIndex + 2);
                var window = string.Join("|", Enumerable.Range(begin, finish - begin + 1).Select(index => $"{index}:{R1_4G2Clean(values[index])}"));
                WriteR1_4G2Row("SnapshotWindow", called, $"Checkpoint={checkpoint};FirstDifferingIndex=NOT_COMPARING;Window={window}");
            }
        }
        catch (Exception error)
        {
            WriteR1_4G2Row("SnapshotError", called, $"Checkpoint={checkpoint};Error={R1_4G2Clean(error.GetType().Name + ":" + error.Message)}");
        }
    }

    private void WriteR1_4G2Row(string eventKind, CalledFunction called, string details, string? sourcePathOverride = null, int? sourceLineOverride = null)
    {
        try
        {
            var label = called.TopLabel ?? called.CurrentLabel;
            var id = productionLabelIds is not null && productionLabelIds.TryGetValue(called.TopLabel, out var runtimeId)
                ? runtimeId.Value.ToString() : "-1";
            var path = sourcePathOverride ?? label?.Position?.Filename ?? state.CurrentLine?.Position?.Filename ?? string.Empty;
            var line = sourceLineOverride ?? label?.Position?.LineNo ?? state.CurrentLine?.Position?.LineNo ?? 0;
            var row = string.Join('\t', (++r1_4g2ExtraTitleSequence).ToString(), eventKind,
                R1_4G2Clean(called.FunctionName), id, R1_4G2Clean(path), line.ToString(), R1_4G2Engine,
                state.functionCount.ToString(), details);
            var file = R1_4G2ExtraTitleTracePath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (!File.Exists(file))
                File.WriteAllText(file, "Sequence\tEvent\tFunctionName\tRuntimeFunctionId\tSourcePath\tSourceLine\tSelectedEngine\tCallDepth\tDetails\n", new UTF8Encoding(false));
            File.AppendAllText(file, row + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
    }
}
