#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

// Opt-in only.  This is a streaming diagnostic boundary; it neither selects
// Next functions nor changes Legacy execution when no capture path is given.
internal sealed partial class Process
{
    private StreamWriter? r1_4iWriter;
    private StreamWriter? r1_4iDetailWriter;
    private long r1_4iSequence;
    private string r1_4iLastDetailRoot = string.Empty;
    private Dictionary<string, DifferentialStateDetail> r1_4iLastDetails = new(StringComparer.Ordinal);
    private long r1_4iDetailDeltaCount;
    private long r1_4iDetailBytes;
    private long r1_4iStructuralCheckpointCount;
    private long r1_4iStateBearingCheckpointCount;
    private long r1_4iStateHashObservationCount;
    private readonly Dictionary<CalledFunction, string> r1_4iIdentityByCall = [];
    private readonly Dictionary<CalledFunction, string> r1_4iParentByCall = [];
    private readonly Dictionary<string, int> r1_4iInvocationCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<CalledFunction, (string Stop, string RuntimeId)> r1_4iNextCompletions = [];
    private string r1_4iLastVmReturn = "<not-run>";
    private long r1_4iDispatchResultBefore;
    private Dictionary<string, DifferentialStateDetail>? r1_4iBridgeBefore;
    private readonly Dictionary<CalledFunction, R1_4IBridgePending> r1_4iPendingBridges = [];

    private sealed record R1_4IBridgePending(long ResultBefore, Dictionary<string, DifferentialStateDetail> Before,
        Dictionary<string, DifferentialStateDetail> DispatchAfter, string FallbackReason, string DispatchChangedDetails);

    private bool R1_4IDifferentialEnabled => !string.IsNullOrWhiteSpace(Program.NextRuntimeDifferentialCapturePath);

    internal void TraceR1_4IDifferentialEntry(CalledFunction called, CalledFunction? parent)
    {
        if (!R1_4IDifferentialEnabled) return;
        var parentIdentity = parent is null ? "<root>" : r1_4iIdentityByCall.GetValueOrDefault(parent, Identity(parent));
        var key = parentIdentity + "\u001f" + Identity(called);
        var ordinal = r1_4iInvocationCounts.GetValueOrDefault(key) + 1;
        r1_4iInvocationCounts[key] = ordinal;
        r1_4iIdentityByCall[called] = Identity(called) + "#" + ordinal;
        r1_4iParentByCall[called] = parentIdentity;
        WriteR1_4ICheckpoint("FunctionEntry", called, parentIdentity, ordinal, "Pending", "", "", "", "");
    }

    internal void BeginR1_4IBridgeTrace(CalledFunction? called)
    {
        if (!R1_4IDifferentialEnabled || called is null || !IsR1_4IBridgeFunction(called)) return;
        r1_4iDispatchResultBefore = vEvaluator.RESULT;
        // Title bridge localization only needs return/engine fields now that the
        // return-value defect is isolated. Keep state-delta checks in synthetic
        // regressions; avoid a full variable/character scan per title call.
        r1_4iBridgeBefore = [];
        r1_4iLastVmReturn = "<not-run>";
    }

    internal void MarkR1_4INextReturn(VmSemanticValue value)
    {
        r1_4iLastVmReturn = value.TryGetInteger(out var integer) ? $"Integer:{integer}" : value.TryGetString(out var text) ? $"String:{R1_4IClean(text)}" : "Unavailable";
    }

    internal void TraceR1_4IDifferentialDispatch(CalledFunction? called, NextRuntimeSessionResult result, string fallbackReason)
    {
        if (!R1_4IDifferentialEnabled || called is null || !ShouldCapture(called)) return;
        var runtimeId = productionLabelIds is not null && productionLabelIds.TryGetValue(called.TopLabel, out var id) ? id.Value.ToString() : "-1";
        var selected = result == NextRuntimeSessionResult.LegacyFallback ? "Legacy" : "Next";
        WriteR1_4ICheckpoint(result == NextRuntimeSessionResult.LegacyFallback ? "NextToLegacyFallback" : "LegacyToNextDispatch", called,
            ParentIdentity(called), InvocationOrdinal(called), selected, "", result.ToString(), fallbackReason, runtimeId);
        if (IsR1_4IBridgeFunction(called) && r1_4iBridgeBefore is not null)
        {
            var dispatchAfter = new Dictionary<string, DifferentialStateDetail>(StringComparer.Ordinal);
            var dispatchChanged = ChangedDetails(r1_4iBridgeBefore, dispatchAfter);
            var pending = new R1_4IBridgePending(r1_4iDispatchResultBefore, r1_4iBridgeBefore, dispatchAfter, fallbackReason, dispatchChanged);
            if (result == NextRuntimeSessionResult.LegacyFallback) r1_4iPendingBridges[called] = pending;
            else CompleteR1_4IBridge(called, result, fallbackReason, pending, "Next", r1_4iLastVmReturn);
            r1_4iBridgeBefore = null;
        }
    }

    internal void TraceR1_4IDifferentialLegacyCompletion(CalledFunction called, long returnArgument)
    {
        if (!R1_4IDifferentialEnabled || !r1_4iPendingBridges.Remove(called, out var pending)) return;
        CompleteR1_4IBridge(called, NextRuntimeSessionResult.LegacyFallback, pending.FallbackReason, pending,
            "Legacy", $"Integer:{returnArgument}");
    }

    private void CompleteR1_4IBridge(CalledFunction called, NextRuntimeSessionResult result, string fallbackReason,
        R1_4IBridgePending pending, string engine, string returnValue)
    {
        var after = new Dictionary<string, DifferentialStateDetail>(StringComparer.Ordinal);
        var changed = string.Join('|', pending.DispatchAfter.Keys.Union(after.Keys, StringComparer.Ordinal).Where(key =>
            !pending.DispatchAfter.TryGetValue(key, out var before) || !after.TryGetValue(key, out var current) || before.Value != current.Value)
            .Select(key =>
            {
                var beforeFound = pending.DispatchAfter.TryGetValue(key, out var before);
                var currentFound = after.TryGetValue(key, out var current);
                var beforeValue = beforeFound ? Convert.ToBase64String(Encoding.UTF8.GetBytes(before.Value)) : "<missing>";
                var afterValue = currentFound ? Convert.ToBase64String(Encoding.UTF8.GetBytes(current.Value)) : "<missing>";
                return $"{key}:{beforeValue}->{afterValue}";
            }));
        WriteR1_4IBridgeRow(called, result, fallbackReason, pending.DispatchChangedDetails, changed, engine, pending.ResultBefore, returnValue);
    }

    private static string ChangedDetails(IReadOnlyDictionary<string, DifferentialStateDetail> before, IReadOnlyDictionary<string, DifferentialStateDetail> after) =>
        string.Join('|', before.Keys.Union(after.Keys, StringComparer.Ordinal).Where(key =>
            !before.TryGetValue(key, out var left) || !after.TryGetValue(key, out var right) || left.Value != right.Value)
            .Select(key =>
            {
                var leftFound = before.TryGetValue(key, out var left);
                var rightFound = after.TryGetValue(key, out var right);
                var leftValue = leftFound ? Convert.ToBase64String(Encoding.UTF8.GetBytes(left.Value)) : "<missing>";
                var rightValue = rightFound ? Convert.ToBase64String(Encoding.UTF8.GetBytes(right.Value)) : "<missing>";
                return $"{key}:{leftValue}->{rightValue}";
            }));

    private static bool IsR1_4IBridgeFunction(CalledFunction called) =>
        called.FunctionName.Equals("PRINT_TITLE", StringComparison.OrdinalIgnoreCase) ||
        called.FunctionName.Equals("SET_EXTRA_TITLE_VAR", StringComparison.OrdinalIgnoreCase) ||
        called.FunctionName.StartsWith("IS_TITLE_USEABLE_", StringComparison.OrdinalIgnoreCase) ||
        called.FunctionName.Equals("PRINT_EVENT_PICTURE", StringComparison.OrdinalIgnoreCase);

    internal void MarkR1_4INextCompletion(CalledFunction? called, string stop, string runtimeId)
    {
        if (R1_4IDifferentialEnabled && called is not null) r1_4iNextCompletions[called] = (stop, runtimeId);
    }

    internal void TraceR1_4IDifferentialExit(CalledFunction called)
    {
        if (!R1_4IDifferentialEnabled) return;
        var next = r1_4iNextCompletions.Remove(called, out var completion);
        WriteR1_4ICheckpoint("FunctionExit", called, ParentIdentity(called), InvocationOrdinal(called), next ? "Next" : "Legacy", next ? completion.Stop : "", "", "", next ? completion.RuntimeId : "-1");
        r1_4iIdentityByCall.Remove(called);
        r1_4iParentByCall.Remove(called);
    }

    internal void TraceR1_4IDifferentialInputBoundary()
    {
        if (!R1_4IDifferentialEnabled || state.functionCount == 0) return;
        var called = state.CurrentCalled;
        WriteR1_4ICheckpoint("InputBoundary", called, ParentIdentity(called), InvocationOrdinal(called), nextRuntimeSession is null ? "Legacy" : "Next", "WaitingForInput", "", "", RuntimeId(called));
    }

    private bool ShouldCapture(CalledFunction called) => string.IsNullOrWhiteSpace(Program.NextRuntimeDifferentialCaptureFunction)
        || called.FunctionName.Equals(Program.NextRuntimeDifferentialCaptureFunction, StringComparison.OrdinalIgnoreCase);

    private string ParentIdentity(CalledFunction called) => r1_4iParentByCall.GetValueOrDefault(called, "<root>");
    private int InvocationOrdinal(CalledFunction called) => r1_4iIdentityByCall.TryGetValue(called, out var identity) && int.TryParse(identity[(identity.LastIndexOf('#') + 1)..], out var ordinal) ? ordinal : 0;
    private string RuntimeId(CalledFunction called) => productionLabelIds is not null && productionLabelIds.TryGetValue(called.TopLabel, out var id) ? id.Value.ToString() : "-1";
    private static string Identity(CalledFunction called)
    {
        var label = called.TopLabel ?? called.CurrentLabel;
        var path = label?.Position?.Filename ?? string.Empty;
        if (Path.IsPathRooted(path)) path = Path.GetRelativePath(Program.ExeDir, path);
        return path.Replace('\\', '/') + ":" + (label?.Position?.LineNo ?? 0) + ":" + called.FunctionName;
    }

    private void WriteR1_4ICheckpoint(string kind, CalledFunction called, string parentIdentity, int ordinal, string selectedEngine, string vmStopReason, string dispatchResult, string fallbackReason, string runtimeId)
    {
        if (!ShouldCapture(called)) return;
        try
        {
            var label = called.TopLabel ?? called.CurrentLabel;
            var path = label?.Position?.Filename ?? string.Empty;
            if (Path.IsPathRooted(path)) path = Path.GetRelativePath(Program.ExeDir, path);
            path = path.Replace('\\', '/');
            var sequence = ++r1_4iSequence;
            var stateBearing = IsR1_4IStateBearingCheckpoint(kind);
            if (stateBearing)
            {
                r1_4iStateBearingCheckpointCount++;
                r1_4iStateHashObservationCount++;
            }
            else r1_4iStructuralCheckpointCount++;
            var domains = stateBearing ? vEvaluator.GetDifferentialStateHashes() : new DifferentialStateHashes("UNAVAILABLE", "UNAVAILABLE", "UNAVAILABLE");
            var root = stateBearing ? GetBenchmarkStateHash() : "UNAVAILABLE";
            var detailReference = stateBearing ? WriteR1_4IDetailDelta(sequence, root) : string.Empty;
            var writer = r1_4iWriter ??= CreateR1_4IWriter();
            writer.WriteLine(string.Join('\t', [
                sequence.ToString(), R1_4IClean(kind), R1_4IClean(called.FunctionName), R1_4IClean(runtimeId), R1_4IClean(path), (label?.Position?.LineNo ?? 0).ToString(), state.functionCount.ToString(), ordinal.ToString(), R1_4IClean(parentIdentity), R1_4IClean(selectedEngine), R1_4IClean(vmStopReason), R1_4IClean(dispatchResult), R1_4IClean(fallbackReason), root, domains.VariablesHash, domains.CharacterHash, domains.RngHash, detailReference
            ]));
            writer.Flush();
            if (stateBearing) WriteR1_4IMetrics();
        }
        catch { }
    }

    private void WriteR1_4IMetrics()
    {
        var path = Path.GetFullPath(Program.NextRuntimeDifferentialCapturePath!) + ".metrics.txt";
        File.WriteAllText(path, $"CheckpointCount={r1_4iSequence}\nStructuralCheckpointCount={r1_4iStructuralCheckpointCount}\nStateBearingCheckpointCount={r1_4iStateBearingCheckpointCount}\nStateHashObservationCount={r1_4iStateHashObservationCount}\nDetailSnapshotCount={r1_4iDetailDeltaCount}\nDetailSerializedBytes={r1_4iDetailBytes}\nStateObservationMode=InputBoundary\nCaptureDefault=OFF\nRng=UNAVAILABLE\n");
    }

    private static bool IsR1_4IStateBearingCheckpoint(string kind) => string.Equals(kind, "InputBoundary", StringComparison.Ordinal);

    private static StreamWriter CreateR1_4IWriter()
    {
        var path = Path.GetFullPath(Program.NextRuntimeDifferentialCapturePath!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var created = new StreamWriter(path, append: false, new UTF8Encoding(false));
        created.WriteLine("Sequence\tCheckpointKind\tFunctionName\tRuntimeFunctionId\tNormalizedSourcePath\tStartLine\tCallDepth\tInvocationOrdinal\tParentIdentity\tSelectedEngine\tVmStopReason\tEntryDispatchResult\tFallbackReason\tStateRootHash\tVariablesHash\tCharacterHash\tRngHash\tValueDetails");
        created.Flush();
        return created;
    }

    private void WriteR1_4IBridgeRow(CalledFunction called, NextRuntimeSessionResult result, string fallbackReason, string dispatchChangedDetails, string completionChangedDetails, string engine, long resultBefore, string returnValue)
    {
        try
        {
            var path = Path.GetFullPath(Program.NextRuntimeDifferentialCapturePath!) + ".bridge.tsv";
            if (!File.Exists(path)) File.WriteAllText(path, "FunctionName\tResultBeforeDispatch\tActualReturn\tResultAtCompletion\tSelectedEngine\tFallbackReason\tDispatchStateChanged\tDispatchChangedDetails\tCompletionStateChanged\tCompletionChangedDetails\n", new UTF8Encoding(false));
            File.AppendAllText(path, string.Join('\t', [called.FunctionName, resultBefore.ToString(), R1_4IClean(returnValue), vEvaluator.RESULT.ToString(), engine, R1_4IClean(fallbackReason), dispatchChangedDetails.Length == 0 ? "NO" : "YES", R1_4IClean(dispatchChangedDetails), completionChangedDetails.Length == 0 ? "NO" : "YES", R1_4IClean(completionChangedDetails)]) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
    }

    private string WriteR1_4IDetailDelta(long sequence, string root)
    {
        if (string.Equals(root, r1_4iLastDetailRoot, StringComparison.Ordinal)) return "@" + sequence;
        var current = vEvaluator.GetDifferentialStateDetails().ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var writer = r1_4iDetailWriter ??= CreateR1_4IDetailWriter();
        foreach (var pair in current.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (r1_4iLastDetails.TryGetValue(pair.Key, out var previous) && previous.Value == pair.Value.Value) continue;
            WriteR1_4IDetailRow(writer, sequence, "Set", pair.Value);
        }
        foreach (var pair in r1_4iLastDetails.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (current.ContainsKey(pair.Key)) continue;
            WriteR1_4IDetailRow(writer, sequence, "Remove", pair.Value);
        }
        r1_4iLastDetails = current;
        r1_4iLastDetailRoot = root;
        writer.Flush();
        File.WriteAllText(Path.GetFullPath(Program.NextRuntimeDifferentialCapturePath!) + ".details.metrics.txt", $"DetailSnapshotCount={++r1_4iDetailDeltaCount}\nDetailSerializedBytes={r1_4iDetailBytes}\nDetailCaptureMode=RootChangeDelta\n");
        return "@" + sequence;
    }

    private static StreamWriter CreateR1_4IDetailWriter()
    {
        var path = Path.GetFullPath(Program.NextRuntimeDifferentialCapturePath!) + ".details.tsv";
        var created = new StreamWriter(path, append: false, new UTF8Encoding(false));
        created.WriteLine("Sequence\tOperation\tDomain\tName\tType\tIndices\tValueBase64");
        created.Flush();
        return created;
    }

    private void WriteR1_4IDetailRow(StreamWriter writer, long sequence, string operation, DifferentialStateDetail detail)
    {
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(detail.Value));
        var line = string.Join('\t', [sequence.ToString(), operation, R1_4IClean(detail.Domain), R1_4IClean(detail.Name), R1_4IClean(detail.Type), R1_4IClean(detail.Indices), value]);
        writer.WriteLine(line);
        r1_4iDetailBytes += Encoding.UTF8.GetByteCount(line) + 1;
    }

    private static string R1_4IClean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
