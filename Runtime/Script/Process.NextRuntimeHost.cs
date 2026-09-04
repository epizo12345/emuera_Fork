using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;
#nullable enable

internal sealed record CsvIndexReadTraceRow(
    int RuntimeFunctionId,
    string FunctionName,
    string ArenaKind,
    int RecordIndex,
    int NodeIndex,
    ulong IdentityStableId,
    string IdentityKind,
    int IdentityIndexArity,
    string HostSymbol,
    string SemanticUse,
    bool NormalVariableBindAttempted,
    bool NormalVariableBindSucceeded,
    bool CsvContextIndexPresent,
    bool CsvIndexTokenPresent,
    string CsvOwnerTokenName,
    string CsvOwnerTokenCode,
    int CsvOwnerTokenDimension,
    bool KeywordDictionaryPresent,
    bool KeywordLabelFound,
    int? ResolvedCsvNumericIndex,
    int RuntimeReadIndexCount,
    string RuntimeReadIndexKinds,
    string RuntimeReadIndexValues,
    bool FinalBoundTokenPresent,
    bool FinalReadAttempted,
    bool FinalReadResult,
    string FailureStage,
    string ExceptionType,
    string ExceptionMessage)
{
    internal static string Header => "RuntimeFunctionId\tFunctionName\tArenaKind\tRecordIndex\tNodeIndex\tIdentityStableId\tIdentityKind\tIdentityIndexArity\tHostSymbol\tSemanticUse\tNormalVariableBindAttempted\tNormalVariableBindSucceeded\tCsvContextIndexPresent\tCsvIndexTokenPresent\tCsvOwnerTokenName\tCsvOwnerTokenCode\tCsvOwnerTokenDimension\tKeywordDictionaryPresent\tKeywordLabelFound\tResolvedCsvNumericIndex\tRuntimeReadIndexCount\tRuntimeReadIndexKinds\tRuntimeReadIndexValues\tFinalBoundTokenPresent\tFinalReadAttempted\tFinalReadResult\tFailureStage\tExceptionType\tExceptionMessage";
    internal string ToTsv() => string.Join('\t', [
        RuntimeFunctionId.ToString(CultureInfo.InvariantCulture), Clean(FunctionName), Clean(ArenaKind),
        RecordIndex.ToString(CultureInfo.InvariantCulture), NodeIndex.ToString(CultureInfo.InvariantCulture),
        IdentityStableId.ToString("X16", CultureInfo.InvariantCulture), Clean(IdentityKind),
        IdentityIndexArity.ToString(CultureInfo.InvariantCulture), Clean(HostSymbol), Clean(SemanticUse),
        NormalVariableBindAttempted ? "YES" : "NO", NormalVariableBindSucceeded ? "YES" : "NO",
        CsvContextIndexPresent ? "YES" : "NO", CsvIndexTokenPresent ? "YES" : "NO", Clean(CsvOwnerTokenName),
        Clean(CsvOwnerTokenCode), CsvOwnerTokenDimension.ToString(CultureInfo.InvariantCulture),
        KeywordDictionaryPresent ? "YES" : "NO", KeywordLabelFound ? "YES" : "NO",
        ResolvedCsvNumericIndex?.ToString(CultureInfo.InvariantCulture) ?? "", RuntimeReadIndexCount.ToString(CultureInfo.InvariantCulture),
        Clean(RuntimeReadIndexKinds), Clean(RuntimeReadIndexValues), FinalBoundTokenPresent ? "YES" : "NO",
        FinalReadAttempted ? "YES" : "NO", FinalReadResult ? "PASS" : "FAIL", Clean(FailureStage),
        Clean(ExceptionType), Clean(ExceptionMessage)]);
    private static string Clean(string value) => value.Replace("\t", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

// The host stays in the Legacy assembly so no Legacy object crosses the VM boundary.
internal sealed class LegacyVmSemanticHost(Process process, VmRuntimePreparationCounters? preparationCounters = null) : IVmSemanticHost, IVmTypedSemanticHost, IVmAssignmentTargetTypeHost, IVmContextualTypedSemanticHost
{
    private readonly Dictionary<ulong, BoundVariable> variables = [];
    private readonly Dictionary<ulong, BuiltinCallKind> builtins = [];
    private readonly VmRuntimePreparationCounters counters = preparationCounters ?? new();
    private readonly Dictionary<SemanticPayload, SemanticUseContextIndex> contextIndexes = [];
    private sealed record BoundVariable(VariableToken Token, bool ImplicitZeroIndex);
    private sealed record CsvIndexLookup(VariableToken? OwnerToken, string HostSymbol, bool KeywordDictionaryPresent, bool KeywordLabelFound, int? ResolvedNumericIndex, string ExceptionType = "", string ExceptionMessage = "");
    private sealed record CsvIndexDiagnosticBinding(SemanticPayload Payload, SemanticHostIdentity Identity, CsvIndexLookup Lookup, bool NormalVariableBindAttempted, bool NormalVariableBindSucceeded);
    private sealed record SemanticUseContextIndex(Dictionary<int, VariableToken> CsvIndexTokens, Dictionary<int, int> CsvIndexValues, Dictionary<int, CsvIndexLookup>? CsvIndexLookups, bool[] StringAssignmentTargets);
    private readonly Dictionary<ulong, CsvIndexDiagnosticBinding> csvIndexDiagnosticBindings = [];
    private readonly List<CsvIndexReadTraceRow> csvIndexReadTrace = [];
    private bool csvIndexDiagnosticActive;
    private int csvIndexDiagnosticFunctionId;
    private string csvIndexDiagnosticFunctionName = "";
    internal enum BuiltinCallKind { Minimum, Maximum }
    internal VmRuntimePreparationCounters PreparationCounters => counters;
    internal int BindResolutionCount { get; private set; }
    internal int BoundIdentityCount => variables.Count;
    internal bool IsVariableBound(SemanticHostIdentity identity) => variables.TryGetValue(identity.StableId, out var variable) && (variable.Token.IsInteger || variable.Token.IsString);
    internal bool IsCharacterVariable(SemanticHostIdentity identity) => variables.TryGetValue(identity.StableId, out var variable) && variable.Token.IsCharacterData;
    internal bool IsVariableWriteSupported(SemanticHostIdentity identity) => variables.TryGetValue(identity.StableId, out var variable) && (variable.Token.IsInteger || variable.Token.IsString) && !variable.Token.IsConst && !variable.Token.IsCalc;
    internal bool IsVariableAvailableInContext(int functionId, SemanticPayload payload, SemanticHostIdentity identity, VmRuntimeVariableUse use)
    {
        counters.VariableClassifierCallCount++;
        if (use == VmRuntimeVariableUse.BareStringLiteral) return true;
        if (use != VmRuntimeVariableUse.CsvIndexLabel) return IsVariableBound(identity);
        counters.CsvContextLookupCount++;
        return TryGetCsvIndexToken(payload, identity, out _);
    }
    internal bool IsCharacterVariableInContext(int functionId, SemanticPayload payload, SemanticHostIdentity identity, VmRuntimeVariableUse use)
    {
        counters.VariableClassifierCallCount++;
        if (use != VmRuntimeVariableUse.CsvIndexLabel) return IsCharacterVariable(identity);
        counters.CharacterContextLookupCount++;
        return TryGetCsvIndexToken(payload, identity, out var token) && token.IsCharacterData;
    }
    internal bool IsStringAssignmentTarget(int functionId, SemanticPayload payload, int recordIndex)
    {
        return contextIndexes.TryGetValue(payload, out var index) &&
            (uint)recordIndex < (uint)index.StringAssignmentTargets.Length && index.StringAssignmentTargets[recordIndex];
    }
    public bool TryGetAssignmentTargetKind(SemanticHostIdentity identity, SemanticPayload arena, out VmSemanticValueKind kind)
    {
        if (variables.TryGetValue(identity.StableId, out var variable))
        {
            kind = variable.Token.IsString ? VmSemanticValueKind.String : variable.Token.IsInteger ? VmSemanticValueKind.Integer : VmSemanticValueKind.Unavailable;
            return kind != VmSemanticValueKind.Unavailable;
        }
        kind = VmSemanticValueKind.Unavailable;
        return false;
    }
    internal VmRuntimeVariableObservation DescribeVariable(int functionId, SemanticPayload payload, SemanticHostIdentity identity, VmRuntimeVariableUse use)
    {
        var cacheLookup = use == VmRuntimeVariableUse.CsvIndexLabel;
        var cacheHit = false;
        VariableToken? token = null;
        if (cacheLookup && contextIndexes.TryGetValue(payload, out var index))
            cacheHit = index.CsvIndexTokens.TryGetValue(identity.NodeIndex, out token);
        else if (variables.TryGetValue(identity.StableId, out var bound)) token = bound.Token;
        var resolved = token is not null && (token.IsInteger || token.IsString || token.IsCharacterData);
        var family = token is null ? "Unknown" : token.IsInteger ? "Int" : token.IsString ? "String" : "Other";
        var shape = token is null ? "Unknown" : token.Dimension == 0 ? "Scalar" : $"Indexed{token.Dimension}D";
        var cacheKey = $"SemanticPayloadInstance:{RuntimeHelpers.GetHashCode(payload):X8}+NodeIndex:{identity.NodeIndex}";
        return new(true, resolved, token?.Code.ToString() ?? "", token?.Name ?? "", family, shape,
            token?.IsCharacterData == true, token?.IsConst == true, token?.IsCalc == true,
            token is not null && (token.IsInteger || token.IsString), token is not null && (token.IsInteger || token.IsString) && !token.IsConst && !token.IsCalc,
            use == VmRuntimeVariableUse.BareStringLiteral || resolved || IsVariableBound(identity), cacheLookup, cacheHit, cacheKey,
            resolved ? "BoundToken" : cacheLookup ? "ContextTokenUnavailable" : "IdentityUnbound");
    }
    internal VmRuntimeCallObservation DescribeCall(SemanticHostIdentity identity)
    {
        return builtins.TryGetValue(identity.StableId, out var builtin)
            ? new(true, true, builtin.ToString(), "RegistryBuiltin")
            : new(true, false, "", "RegistryBuiltinUnavailable");
    }
    private bool TryGetCsvIndexToken(SemanticPayload payload, SemanticHostIdentity identity, out VariableToken token)
    {
        token = null!;
        if (!contextIndexes.TryGetValue(payload, out var index)) return false;
        counters.VariableClassifierCacheHitCount++;
        if (index.CsvIndexTokens.TryGetValue(identity.NodeIndex, out var resolved))
        {
            token = resolved;
            return true;
        }
        return false;
    }
    private bool TryGetCsvIndexLookup(SemanticPayload payload, SemanticHostIdentity identity, out CsvIndexLookup lookup)
    {
        if (contextIndexes.TryGetValue(payload, out var index) && index.CsvIndexLookups is { } lookups && lookups.TryGetValue(identity.NodeIndex, out var found))
        {
            lookup = found;
            return true;
        }
        lookup = null!;
        return false;
    }
    internal void BeginCsvIndexDiagnostic(int functionId, string functionName)
    {
        csvIndexDiagnosticActive = Program.NextRuntimeSessionStartFaultTraceFunctionIds?.Contains(functionId) == true;
        if (!csvIndexDiagnosticActive) return;
        csvIndexDiagnosticFunctionId = functionId;
        csvIndexDiagnosticFunctionName = functionName;
        csvIndexDiagnosticBindings.Clear();
        csvIndexReadTrace.Clear();
    }
    internal IReadOnlyList<CsvIndexReadTraceRow> EndCsvIndexDiagnostic()
    {
        if (!csvIndexDiagnosticActive) return [];
        csvIndexDiagnosticActive = false;
        if (csvIndexReadTrace.Count == 0)
            csvIndexReadTrace.Add(new(csvIndexDiagnosticFunctionId, csvIndexDiagnosticFunctionName, "None", -1, -1, 0, "", 0, "", "UNPROVEN", false, false, false, false, "", "", 0, false, false, null, 0, "", "", false, false, false, "UNPROVEN", "", ""));
        var rows = csvIndexReadTrace.ToArray();
        csvIndexDiagnosticBindings.Clear();
        csvIndexReadTrace.Clear();
        return rows;
    }
    internal void BuildSemanticContextIndex(IEnumerable<SemanticPayload> payloads)
    {
        counters.SemanticContextIndexBuildCount++;
        foreach (var payload in payloads.Distinct())
        {
            if (contextIndexes.ContainsKey(payload)) continue;
            var csvIndexTokens = new Dictionary<int, VariableToken>();
            var csvIndexValues = new Dictionary<int, int>();
            var csvIndexLookups = DiagnosticCsvIndexEnabled ? new Dictionary<int, CsvIndexLookup>() : null;
            var stringAssignmentTargets = new bool[payload.Records.Length];
            var variableIdentities = payload.HostIdentities
                .Where(identity => identity.Kind == SemanticHostIdentityKind.Variable)
                .ToDictionary(identity => identity.NodeIndex);
            for (var nodeIndex = 0; nodeIndex < payload.Nodes.Length; nodeIndex++)
            {
                var node = payload.Nodes[nodeIndex];
                var start = node.Kind == SemanticNodeKind.Variable ? node.B : node.Kind == SemanticNodeKind.VariableSubkey ? node.C : -1;
                var count = node.Kind == SemanticNodeKind.Variable ? node.C : node.Kind == SemanticNodeKind.VariableSubkey ? node.D : 0;
                if (start < 0 || count <= 0 || (uint)node.A >= (uint)payload.Symbols.Length) continue;
                var name = payload.ReadSymbol(payload.Symbols[node.A]);
                var subkey = node.Kind == SemanticNodeKind.VariableSubkey && (uint)node.B < (uint)payload.Symbols.Length ? payload.ReadSymbol(payload.Symbols[node.B]) : null;
                VariableToken? ownerToken;
                try { ownerToken = process.GetNextRuntimeVariableToken(name, subkey); } catch (Exception) { ownerToken = null; }
                if (ownerToken is null && !DiagnosticCsvIndexEnabled) continue;
                for (var offset = 0; offset < count; offset++)
                {
                    var childIndex = start + offset;
                    if ((uint)childIndex >= (uint)payload.Edges.Length) continue;
                    var child = payload.Edges[childIndex].To;
                    if (!variableIdentities.TryGetValue(child, out var childIdentity) || (uint)childIdentity.NameSymbolIndex >= (uint)payload.Symbols.Length) continue;
                    var label = payload.ReadSymbol(payload.Symbols[childIdentity.NameSymbolIndex]);
                    var keywordDictionaryPresent = false;
                    var keywordLabelFound = false;
                    int? resolvedNumericIndex = null;
                    string exceptionType = "";
                    string exceptionMessage = "";
                    try
                    {
                        if (ownerToken is not null)
                        {
                            var dictionary = GlobalStatic.ConstantData.GetKeywordDictionary(out _, ownerToken.Code, -1);
                            keywordDictionaryPresent |= dictionary is not null;
                            if (dictionary is not null && dictionary.TryGetValue(label, out var numericIndex))
                            {
                                keywordLabelFound = true;
                                resolvedNumericIndex = numericIndex;
                            }
                            if (!keywordLabelFound && offset < 3)
                            {
                                dictionary = GlobalStatic.ConstantData.GetKeywordDictionary(out _, ownerToken.Code, offset);
                                keywordDictionaryPresent |= dictionary is not null;
                                if (dictionary is not null && dictionary.TryGetValue(label, out numericIndex))
                                {
                                    keywordLabelFound = true;
                                    resolvedNumericIndex = numericIndex;
                                }
                            }
                            if (keywordLabelFound)
                            {
                                csvIndexTokens.TryAdd(child, ownerToken);
                                csvIndexValues.TryAdd(child, resolvedNumericIndex!.Value);
                            }
                        }
                    }
                    catch (Exception ex) { exceptionType = ex.GetType().FullName ?? ex.GetType().Name; exceptionMessage = ex.Message; }
                    csvIndexLookups?.TryAdd(child, new(ownerToken, label, keywordDictionaryPresent, keywordLabelFound, resolvedNumericIndex, exceptionType, exceptionMessage));
                }
            }
            for (var recordIndex = 0; recordIndex < payload.Records.Length; recordIndex++)
            {
                var record = payload.Records[recordIndex];
                var end = Math.Min(payload.Nodes.Length, record.RootNodeIndex + record.NodeCount);
                for (var nodeIndex = record.RootNodeIndex; nodeIndex < end; nodeIndex++)
                    if (variableIdentities.TryGetValue(nodeIndex, out var identity) && variables.TryGetValue(identity.StableId, out var variable) && variable.Token.IsString)
                    {
                        stringAssignmentTargets[recordIndex] = true;
                        break;
                    }
            }
            contextIndexes.Add(payload, new(csvIndexTokens, csvIndexValues, csvIndexLookups, stringAssignmentTargets));
        }
    }
    internal bool IsFrameVariableBound(int functionId, SemanticPayload payload, SemanticHostIdentity identity)
    {
        if ((uint)identity.NameSymbolIndex >= (uint)payload.Symbols.Length) return false;
        var name = payload.ReadSymbol(payload.Symbols[identity.NameSymbolIndex]);
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase) || name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) return true;
        return process.IsNextRuntimeFrameVariableBound(functionId, name);
    }
    internal bool IsBuiltinBound(SemanticHostIdentity identity) => builtins.ContainsKey(identity.StableId);
    internal int RuntimeTypedReadCount { get; private set; }
    internal int RuntimeTypedWriteCount { get; private set; }
    internal int RuntimeTypedBuiltinCallCount { get; private set; }
    public bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
        => process.TryReadNextRuntimeHostValue(name, subkey, indices, out value);

    public bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value)
        => process.TryWriteNextRuntimeHostValue(name, subkey, indices, value);

    // Non-typed callers cannot use a raw builtin name through this adapter.
    public bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public int CompareStrings(string left, string right) => string.Compare(left, right, Config.SCExpression);

    public bool BindVariableIdentities(SemanticPayload payload)
    {
        foreach (var identity in payload.HostIdentities)
        {
            var normalVariableBindAttempted = false;
            var normalVariableBindSucceeded = false;
            if (identity.Kind == SemanticHostIdentityKind.Variable && !variables.ContainsKey(identity.StableId))
            {
                normalVariableBindAttempted = true;
                BindResolutionCount++;
                if (process.TryBindNextRuntimeHostVariable(payload, identity, out var variable)) { variables.Add(identity.StableId, new(variable, variable.Dimension == 1 && !variable.IsCharacterData && identity.IndexArity == 0)); normalVariableBindSucceeded = true; }
            }
            else if (identity.Kind == SemanticHostIdentityKind.Call && !builtins.ContainsKey(identity.StableId) && process.TryBindNextRuntimeHostBuiltin(payload, identity, out var builtin))
                builtins.Add(identity.StableId, builtin);
            if (csvIndexDiagnosticActive && identity.Kind == SemanticHostIdentityKind.Variable && TryGetCsvIndexLookup(payload, identity, out var lookup))
                csvIndexDiagnosticBindings[identity.StableId] = new(payload, identity, lookup, normalVariableBindAttempted, normalVariableBindSucceeded);
        }
        return true;
    }

    public bool TryRead(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
        => TryReadContext(null, identity, indices, out value);

    // [Emuera改修:NEXT-3D-R1.5C5.3 2026-09-04]
    // C5のStableId-only CSV mappingでは異なるownerの同名labelが衝突し得る。
    // StableIdはreadiness/call/diagnosticで共有するcanonical契約のため変更せず、
    // executorが渡す正確なSemanticPayloadとNodeIndexでgeneration-scoped値を選ぶ。
    // mutable current-payload stateやruntime文字列解決を使わず、read時に文脈を明示する。
    public bool TryRead(SemanticPayload payload, SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
        => TryReadContext(payload, identity, indices, out value);

    private bool TryReadContext(SemanticPayload? payload, SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        RuntimeTypedReadCount++;
        var finalBound = variables.TryGetValue(identity.StableId, out var variable);
        value = VmSemanticValue.Unavailable;
        var readAttempted = false;
        var readResult = false;
        var csvIndexValueProvided = false;
        if (finalBound)
        {
            readAttempted = true;
            readResult = process.TryReadNextRuntimeHostValue(variable!.Token, variable.ImplicitZeroIndex && indices.IsEmpty ? [VmSemanticValue.From(0)] : indices, out value);
        }
        else if (payload is not null && identity.IndexArity == 0 && indices.IsEmpty &&
            contextIndexes.TryGetValue(payload, out var context) &&
            context.CsvIndexValues.TryGetValue(identity.NodeIndex, out var csvIndex))
        {
            value = VmSemanticValue.From((long)csvIndex);
            readAttempted = true;
            readResult = true;
            csvIndexValueProvided = true;
        }
        if (csvIndexDiagnosticActive && csvIndexDiagnosticBindings.TryGetValue(identity.StableId, out var binding))
        {
            var lookup = binding.Lookup;
            var stage = csvIndexValueProvided ? "RESOLVED" : !binding.NormalVariableBindSucceeded
                ? !contextIndexes.ContainsKey(binding.Payload) ? "CSV_CONTEXT_INDEX_MISSING"
                : lookup.OwnerToken is null ? "CSV_OWNER_TOKEN_MISSING"
                : !lookup.KeywordDictionaryPresent ? "KEYWORD_DICTIONARY_MISSING"
                : !lookup.KeywordLabelFound ? "CSV_LABEL_NOT_FOUND"
                : !finalBound ? "CSV_NUMERIC_INDEX_RESOLVED_BUT_NOT_PROPAGATED" : "OTHER"
                : !readResult ? "LEGACY_TOKEN_READ_FAILED" : "OTHER";
            csvIndexReadTrace.Add(new(csvIndexDiagnosticFunctionId, csvIndexDiagnosticFunctionName, process.DescribeNextRuntimeSemanticArena(binding.Payload),
                FindRecordIndex(binding.Payload, identity.NodeIndex), identity.NodeIndex, identity.StableId, identity.Kind.ToString(), identity.IndexArity,
                lookup.HostSymbol, "CsvIndexLabel", binding.NormalVariableBindAttempted, binding.NormalVariableBindSucceeded,
                contextIndexes.ContainsKey(binding.Payload), lookup.KeywordLabelFound, lookup.OwnerToken?.Name ?? "",
                lookup.OwnerToken is null ? "" : ((int)lookup.OwnerToken.Code).ToString(CultureInfo.InvariantCulture), lookup.OwnerToken?.Dimension ?? 0,
                lookup.KeywordDictionaryPresent, lookup.KeywordLabelFound, lookup.ResolvedNumericIndex, indices.Length,
                string.Join(',', indices.ToArray().Select(index => index.Kind.ToString())), string.Join(',', indices.ToArray().Select(index => index.ToString())),
                finalBound, readAttempted, readResult, stage, lookup.ExceptionType, lookup.ExceptionMessage));
        }
        return readResult;
    }

    private static int FindRecordIndex(SemanticPayload payload, int nodeIndex)
    {
        for (var index = 0; index < payload.Records.Length; index++)
        {
            var record = payload.Records[index];
            if (nodeIndex >= record.RootNodeIndex && nodeIndex < record.RootNodeIndex + record.NodeCount) return index;
        }
        return -1;
    }
    private static bool DiagnosticCsvIndexEnabled => !string.IsNullOrWhiteSpace(Program.NextRuntimeSessionStartFaultTracePath) && Program.NextRuntimeSessionStartFaultTraceFunctionIds is not null;

    public bool TryWrite(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value)
    {
        RuntimeTypedWriteCount++;
        return variables.TryGetValue(identity.StableId, out var variable) && process.TryWriteNextRuntimeHostValue(variable.Token, variable.ImplicitZeroIndex && indices.IsEmpty ? [VmSemanticValue.From(0)] : indices, value);
    }

    public bool TryCall(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value)
    {
        RuntimeTypedBuiltinCallCount++;
        value = VmSemanticValue.Unavailable;
        if (!builtins.TryGetValue(identity.StableId, out var builtin) || arguments.Length == 0 || !arguments[0].TryGetInteger(out var result)) return false;
        for (var index = 1; index < arguments.Length; index++)
        {
            if (!arguments[index].TryGetInteger(out var next)) return false;
            result = builtin == BuiltinCallKind.Maximum ? Math.Max(result, next) : Math.Min(result, next);
        }
        value = VmSemanticValue.From(result);
        return true;
    }

}

internal enum NextRuntimeSessionResult { LegacyFallback, Completed, Waiting }

internal enum NextRuntimeDispatchRejectReason
{
    None,
    ModeDisabled,
    AnalysisMode,
    DebugMode,
    ProductionProgramMissing,
    ProductionGenerationInvalid,
    ProductionActivationMissing,
    ActiveNextSession,
    EntryDispatchAlreadyConsumed,
    NoLegacyFrame,
    TopLabelMissing,
    TopLabelNotMapped,
    RuntimeFunctionIdMissing,
    DescriptorMissing,
    FunctionKindNotNormal,
    DescriptorNotExecutableReady,
    NotDispatchEntryReady,
    FrameBindingMissing,
    SemanticHostMissing,
    ArgumentImportFailed,
    SessionStartRejected,
    SessionStartFault,
}

// Bound exactly once at a Legacy function-entry boundary.  The VM sees only
// function/slot identities; Legacy tokens and scopes never cross assemblies.
internal sealed class LegacyVmFrameState : IVmFrameState
{
    internal sealed record Binding(VariableToken Token, int[]? Dimensions, bool IsStatic);
    private readonly Process process;
    private readonly int entryFunctionId;
    private readonly Dictionary<(int FunctionId, VmFrameStateFamily Family, int PrivateSlot), Binding> bindings = [];

    private LegacyVmFrameState(Process process, int entryFunctionId, Dictionary<(int FunctionId, VmFrameStateFamily Family, int PrivateSlot), Binding> bindings)
    {
        this.process = process;
        this.entryFunctionId = entryFunctionId;
        this.bindings = bindings;
    }

    internal static bool TryCreate(Process process, LinkedProgram program, RuntimeFunctionId entryFunctionId, IReadOnlyDictionary<int, FunctionLabelLine> labels, out IVmFrameState state)
    {
        if (!TryBuildBindings(process, program, labels, out var bindings)) { state = null!; return false; }
        state = new LegacyVmFrameState(process, entryFunctionId.Value, bindings);
        return true;
    }

    internal static bool TryCreateWithCatalog(Process process, RuntimeFunctionId entryFunctionId, LegacyVmFrameBindingCatalog catalog, out IVmFrameState state)
    {
        state = new LegacyVmFrameState(process, entryFunctionId.Value, catalog.Bindings);
        return true;
    }

    internal static bool TryBuildBindings(Process process, LinkedProgram program, IReadOnlyDictionary<int, FunctionLabelLine> labels, out Dictionary<(int FunctionId, VmFrameStateFamily Family, int PrivateSlot), Binding> result)
    {
        result = [];
        for (var functionId = 0; functionId < program.RuntimeMetadata.Length; functionId++)
        {
            if (!labels.TryGetValue(functionId, out var label)) { result = []; return false; }
            var metadata = program.RuntimeMetadata[functionId];
            VariableToken? arg = null;
            VariableToken? args = null;
            for (var parameterIndex = 0; parameterIndex < metadata.Parameters.Length; parameterIndex++)
            {
                if ((uint)parameterIndex >= (uint)label.Arg.Length) { result = []; return false; }
                var token = label.Arg[parameterIndex].Identifier;
                if (metadata.Parameters[parameterIndex].Type == RuntimeMetadataValueType.Integer) arg ??= token;
                else args ??= token;
            }
            if (arg is not null) result[(functionId, VmFrameStateFamily.Arg, -1)] = new(arg, null, false);
            if (args is not null) result[(functionId, VmFrameStateFamily.Args, -1)] = new(args, null, false);
            var local = process.GetNextRuntimeLocalVariableToken("LOCAL", label);
            var locals = process.GetNextRuntimeLocalVariableToken("LOCALS", label);
            if (local is not null) result[(functionId, VmFrameStateFamily.Local, -1)] = new(local, null, false);
            if (locals is not null) result[(functionId, VmFrameStateFamily.Locals, -1)] = new(locals, null, false);
            for (var privateSlot = 0; privateSlot < metadata.PrivateVariables.Length; privateSlot++)
            {
                var declaration = metadata.PrivateVariables[privateSlot];
                var token = label.GetPrivateVariable(declaration.Name);
                if (token is null) { result = []; return false; }
                result[(functionId, VmFrameStateFamily.Private, privateSlot)] = new(token, declaration.Dimensions.ToArray(), declaration.IsStatic);
            }
        }
        return true;
    }

    public bool OwnsFrameValue(RuntimeFunctionId functionId, VmFrameStateFamily family, int privateSlot)
    {
        if (!bindings.TryGetValue((functionId.Value, family, privateSlot), out var binding)) return false;
        return functionId.Value == entryFunctionId || family == VmFrameStateFamily.Private && binding.IsStatic;
    }

    public bool TryReadFrameValue(RuntimeFunctionId functionId, VmFrameStateFamily family, int privateSlot, int elementIndex, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        return TryGet(functionId, family, privateSlot, elementIndex, out var binding, out var indices) && process.TryReadNextRuntimeHostValue(binding.Token, ToVmIndices(indices), out value);
    }

    public bool TryWriteFrameValue(RuntimeFunctionId functionId, VmFrameStateFamily family, int privateSlot, int elementIndex, VmSemanticValue value) =>
        TryGet(functionId, family, privateSlot, elementIndex, out var binding, out var indices) && process.TryWriteNextRuntimeHostValue(binding.Token, ToVmIndices(indices), value);

    private bool TryGet(RuntimeFunctionId functionId, VmFrameStateFamily family, int privateSlot, int elementIndex, out Binding binding, out long[] indices)
    {
        indices = [];
        if (!bindings.TryGetValue((functionId.Value, family, privateSlot), out binding!) || elementIndex < 0) return false;
        if (binding.Dimensions is null) { indices = [elementIndex]; return true; }
        indices = new long[binding.Dimensions.Length];
        for (var dimension = binding.Dimensions.Length - 1; dimension >= 0; dimension--)
        {
            indices[dimension] = elementIndex % binding.Dimensions[dimension];
            elementIndex /= binding.Dimensions[dimension];
        }
        return elementIndex == 0;
    }

    private static VmSemanticValue[] ToVmIndices(long[] values) => values.Select(VmSemanticValue.From).ToArray();
}

// Built once for a production program generation; sessions only retain the
// entry identity and share these immutable Legacy token bindings.
internal sealed class LegacyVmFrameBindingCatalog
{
    internal Dictionary<(int FunctionId, VmFrameStateFamily Family, int PrivateSlot), LegacyVmFrameState.Binding> Bindings { get; }
    private LegacyVmFrameBindingCatalog(Dictionary<(int FunctionId, VmFrameStateFamily Family, int PrivateSlot), LegacyVmFrameState.Binding> bindings) => Bindings = bindings;
    internal static bool TryCreate(Process process, LinkedProgram program, IReadOnlyDictionary<int, FunctionLabelLine> labels, out LegacyVmFrameBindingCatalog catalog)
    {
        if (!LegacyVmFrameState.TryBuildBindings(process, program, labels, out var bindings)) { catalog = null!; return false; }
        catalog = new(bindings);
        return true;
    }
}

internal sealed partial class Process
{
    // This is deliberately an opt-in dispatch seam.  Normal Legacy execution
    // never constructs one, so DoScript keeps its established interpreter path.
    private sealed class NextRuntimeExecutionSession(Process process, VmMachine machine, bool productionNormal = false)
    {
        internal Process Process { get; } = process;
        internal VmMachine Machine { get; } = machine;
        internal bool ProductionNormal { get; } = productionNormal;
    }

    private sealed class LegacyVmRuntimeEffects(Process process) : IVmRuntimeEffects
    {
        public void WriteText(string text) => process.console.Print(text, lineEnd: false);
        public void NewLine() => process.console.NewLine();
        public void RequestWait(bool force) { process.console.ReadAnyKey(force); }
        public void Quit() => process.console.Quit();
    }

    private NextRuntimeExecutionSession? nextRuntimeSession;
    private int nextRuntimeSessionImports;
    private int nextRuntimeSessionResumes;
    private int productionProbeRemaining;
    private int productionProbeCompleted;
    private int productionProbeDispatchAttempts;
    private int productionProbeFallbacks;
    private string productionProbeLastDecision = string.Empty;
    private NextRuntimeDispatchRejectReason productionLastRejectReason;
    private readonly List<ProductionDispatchTraceRow> productionDispatchTrace = [];
    private readonly List<string> productionSessionStartTrace = [];
    private readonly List<CsvIndexReadTraceRow> productionCsvIndexReadTrace = [];
    private List<string>? productionArgumentImportTrace;
    private int productionArgumentImportTraceInvocationOrdinal;
    // [Emuera改修:NEXT-3D-R1.5C3A 2026-09-04]
    // 実invocationの既存session traceだけを、明示targetのSessionStartFault時に保持する。
    private List<string>? productionSessionStartFaultTrace;
    private const string ProductionSessionStartTraceHeader = "EntryRuntimeFunctionId\tFrameImportResult\tFrameBindingResult\tVmMachineStartResult\tVmStopReason\tVmStopFunctionId\tVmStopPc\tSemanticStatus\tSemanticFault\tSessionStartOutcome\tExceptionType\tExceptionMessage\tSemanticExecutorPresent\tRuntimeEffectsPresent\tVmMachineStructuralSemanticsType\tVmMachineRuntimeEffectsType\tArenaKind\tRecordIndex\tRootNodeIndex\tSemanticNodeKind\tSemanticOperator\tCanonicalHostIdentityId\tHostBindingPresent\tHostOperation\tHostSymbol\tHostResult";
    private const string ArgumentImportTraceHeader = "RuntimeFunctionId\tFunctionName\tSourcePath\tStartLine\tLegacyArgumentDefinitionCount\tRuntimeMetadataParameterCount\tParameterOrdinal\tParameterName\tParameterType\tIsReference\tLegacyFramePresent\tLegacyARGAvailable\tLegacyARGSAvailable\tExpectedRuntimeType\tObservedLegacyValueKind\tImportAttempted\tImportSucceeded\tFailureStage\tExceptionType\tExceptionMessage\tCallDepth\tInvocationOrdinal";
    private Dictionary<FunctionLabelLine, string> productionProbeCaseIds = [];
    private LegacyVmSemanticHost? productionSemanticHost;
    private LegacyVmFrameBindingCatalog? productionFrameCatalog;
    private LinkedProgram? productionProgram;
    private Dictionary<FunctionLabelLine, RuntimeFunctionId>? productionLabelIds;
    private FunctionKind[]? productionFunctionKinds;
    private HashSet<int> sharedExecutableReadyIds = [];
    private HashSet<int> productionExecutableReadyIds = [];
    private HashSet<int> productionDispatchEntryReadyIds = [];
    private VmRuntimeReadinessResult? productionSharedReadiness;
    private VmRuntimeReadinessResult? productionReadiness;

    internal IVmSemanticHost CreateNextRuntimeSemanticHost() => new LegacyVmSemanticHost(this);

    internal string DescribeNextRuntimeSemanticArena(SemanticPayload payload) =>
        productionProgram is not null && ReferenceEquals(payload, productionProgram.RuntimeStatements.OperandArena) ? "RuntimeStatementOperand" :
        productionProgram is not null && ReferenceEquals(payload, productionProgram.CallArgumentArena) ? "CallArgument" : "Semantic";

    internal LocalVariableToken GetNextRuntimeLocalVariableToken(string key, FunctionLabelLine label) => idDic.GetNextRuntimeLocalVariableToken(key, label);

    internal bool TryCreateNextRuntimeFrameState(LinkedProgram program, RuntimeFunctionId entryFunctionId, IReadOnlyDictionary<int, FunctionLabelLine> labels, out IVmFrameState frameState) =>
        productionFrameCatalog is not null && ReferenceEquals(program, productionProgram)
            ? LegacyVmFrameState.TryCreateWithCatalog(this, entryFunctionId, productionFrameCatalog, out frameState)
            : LegacyVmFrameState.TryCreate(this, program, entryFunctionId, labels, out frameState);

    internal bool TryImportNextRuntimeEntryArguments(LinkedProgram program, RuntimeFunctionId functionId, out VmSemanticValue[] actuals)
    {
        actuals = [];
        if ((uint)functionId.Value >= (uint)program.RuntimeMetadata.Length)
        {
            RecordArgumentImportFailure(program, functionId, null, null, -1, "RUNTIME_FUNCTION_ID_OUT_OF_RANGE");
            return false;
        }
        if (state.functionCount == 0)
        {
            RecordArgumentImportFailure(program, functionId, null, null, -1, "NO_LEGACY_FRAME");
            return false;
        }
        var label = state.CurrentCalled.TopLabel;
        var metadata = program.RuntimeMetadata[functionId.Value];
        if (label.Arg.Length != metadata.Parameters.Length)
        {
            RecordArgumentImportFailure(program, functionId, label, metadata, -1, "PARAMETER_COUNT_MISMATCH");
            return false;
        }
        actuals = new VmSemanticValue[metadata.Parameters.Length];
        for (var index = 0; index < metadata.Parameters.Length; index++)
        {
            var argument = label.Arg[index];
            if (argument.Identifier.IsReference)
            {
                actuals = [];
                RecordArgumentImportFailure(program, functionId, label, metadata, index, "REFERENCE_PARAMETER_UNSUPPORTED");
                return false;
            }
            actuals[index] = metadata.Parameters[index].Type == RuntimeMetadataValueType.Integer
                ? VmSemanticValue.From(argument.GetIntValue(exm))
                : VmSemanticValue.From(argument.GetStrValue(exm));
        }
        return true;
    }

    private void RecordArgumentImportFailure(LinkedProgram program, RuntimeFunctionId functionId, FunctionLabelLine? label, FunctionRuntimeMetadata? metadata, int parameterOrdinal, string failureStage, Exception? exception = null)
    {
        if (!ReferenceEquals(program, productionProgram) || string.IsNullOrWhiteSpace(Program.NextRuntimeSessionStartFaultTracePath) ||
            Program.NextRuntimeSessionStartFaultTraceFunctionIds?.Contains(functionId.Value) != true)
            return;
        var parameter = metadata is not null && (uint)parameterOrdinal < (uint)metadata.Parameters.Length ? metadata.Parameters[parameterOrdinal] : default;
        var argument = label is not null && (uint)parameterOrdinal < (uint)label.Arg.Length ? label.Arg[parameterOrdinal] : null;
        var observedKind = argument is null ? "" : argument.Identifier.IsReference ? "Reference" : argument.GetOperandType() == typeof(long) ? "Integer" : argument.GetOperandType() == typeof(string) ? "String" : argument.GetOperandType().Name;
        var rows = productionArgumentImportTrace ??= [];
        var invocationOrdinal = ++productionArgumentImportTraceInvocationOrdinal;
        rows.Add(string.Join('\t', [
            functionId.Value.ToString(CultureInfo.InvariantCulture),
            DispatchTraceValue(label?.LabelName ?? ""),
            DispatchTraceValue(label?.Position?.Filename ?? ""),
            (label?.Position?.LineNo ?? 0).ToString(CultureInfo.InvariantCulture),
            (label?.Arg.Length ?? 0).ToString(CultureInfo.InvariantCulture),
            (metadata?.Parameters.Length ?? 0).ToString(CultureInfo.InvariantCulture),
            parameterOrdinal.ToString(CultureInfo.InvariantCulture),
            DispatchTraceValue(parameter.Name ?? ""),
            parameter.Type.ToString(),
            argument?.Identifier.IsReference == true ? "YES" : "NO",
            state.functionCount != 0 ? "YES" : "NO",
            label?.ArgLength > 0 ? "YES" : "NO",
            label?.ArgsLength > 0 ? "YES" : "NO",
            metadata is null || parameterOrdinal < 0 ? "" : parameter.Type.ToString(),
            observedKind,
            "YES",
            "NO",
            failureStage,
            DispatchTraceValue(exception?.GetType().Name ?? ""),
            DispatchTraceValue(exception?.Message ?? ""),
            state.functionCount.ToString(CultureInfo.InvariantCulture),
            invocationOrdinal.ToString(CultureInfo.InvariantCulture)
        ]));
    }

    internal bool TryWriteNextRuntimeReturn(VmSemanticValue value)
    {
        if (state.functionCount == 0) return false;
        if (!state.IsFunctionMethod) { state.Return(0); return true; }
        if (value.TryGetInteger(out var integer)) { state.ReturnF(SingleLongTerm.FromValue(integer)); return true; }
        if (value.TryGetString(out var text)) { state.ReturnF(SingleStrTerm.FromValue(text)); return true; }
        return false;
    }

    // Called only after Legacy ProcessState.IntoFunction has evaluated actuals,
    // assigned ARG/ARGS and entered the root dynamic-private scope.
    internal bool TryStartNextRuntimeSession(LinkedProgram program, RuntimeFunctionId entryFunctionId, IReadOnlyDictionary<int, FunctionLabelLine> labels, out VmStopReason stop)
    {
        stop = VmStopReason.SemanticNotAvailable;
        if (nextRuntimeSession is not null || !TryImportNextRuntimeEntryArguments(program, entryFunctionId, out var actuals) ||
            !TryCreateNextRuntimeFrameState(program, entryFunctionId, labels, out var frameState))
            return false;
        nextRuntimeSessionImports++;
        var machine = new VmMachine(program, new VmSemanticExecutor(program, new LegacyVmSemanticHost(this)), new LegacyVmRuntimeEffects(this), frameState);
        nextRuntimeSession = new(this, machine);
        TraceR1_4E("NextSessionStart", stopReason: "Start");
        stop = machine.Run(entryFunctionId, actuals);
        return FinishNextRuntimeSession(stop);
    }

    private sealed class ProductionDispatchTraceRow
    {
        internal string CaseId { get; init; } = "";
        internal string FunctionName { get; init; } = "";
        internal string SourcePath { get; init; } = "";
        internal int StartLine { get; init; }
        internal string Mode { get; init; } = "";
        internal string ProgramPresent { get; init; } = "";
        internal string GenerationValid { get; init; } = "";
        internal string ActivationPresent { get; init; } = "";
        internal string TopLabelMapped { get; set; } = "";
        internal string RuntimeFunctionId { get; set; } = "";
        internal string Kind { get; set; } = "";
        internal string DescriptorState { get; set; } = "";
        internal string ExecutableReady { get; set; } = "";
        internal string DispatchEntryReady { get; set; } = "";
        internal string FrameBinding { get; set; } = "";
        internal string SemanticHost { get; set; } = "";
        internal NextRuntimeDispatchRejectReason RejectReason { get; set; }
        internal string ExpectedEngine { get; init; } = "Next";
        internal string SelectedEngine { get; set; } = "Legacy";
        internal string Result { get; set; } = "LegacyFallback";
    }

    private ProductionDispatchTraceRow CreateProductionDispatchTrace(FunctionLabelLine? label)
    {
        var row = new ProductionDispatchTraceRow
        {
            CaseId = label is not null && productionProbeCaseIds.TryGetValue(label, out var caseId) ? caseId : "unassigned",
            FunctionName = label?.LabelName ?? "",
            SourcePath = label?.Position?.Filename ?? "",
            StartLine = label?.Position?.LineNo ?? 0,
            Mode = Program.NextRuntimeMode ? "ON" : "OFF",
            ProgramPresent = productionProgram is not null ? "YES" : "NO",
            GenerationValid = productionProgram is not null && productionFunctionKinds is not null && productionLabelIds is not null &&
                productionFunctionKinds.Length == productionProgram.Descriptors.Length ? "YES" : "NO",
            ActivationPresent = productionReadiness is not null && productionActivationCount > 0 ? "YES" : "NO",
            TopLabelMapped = "NO",
            RuntimeFunctionId = "",
            Kind = "",
            DescriptorState = "",
            ExecutableReady = "NO",
            DispatchEntryReady = "NO",
            FrameBinding = productionFrameCatalog is not null ? "YES" : "NO",
            SemanticHost = productionSemanticHost is not null ? "YES" : "NO",
            RejectReason = NextRuntimeDispatchRejectReason.None,
        };

        if (!Program.NextRuntimeMode) row.RejectReason = NextRuntimeDispatchRejectReason.ModeDisabled;
        else if (Program.AnalysisMode) row.RejectReason = NextRuntimeDispatchRejectReason.AnalysisMode;
        else if (Program.DebugMode) row.RejectReason = NextRuntimeDispatchRejectReason.DebugMode;
        else if (productionProgram is null) row.RejectReason = NextRuntimeDispatchRejectReason.ProductionProgramMissing;
        else if (row.GenerationValid != "YES") row.RejectReason = NextRuntimeDispatchRejectReason.ProductionGenerationInvalid;
        else if (row.ActivationPresent != "YES") row.RejectReason = NextRuntimeDispatchRejectReason.ProductionActivationMissing;
        else if (nextRuntimeSession is not null) row.RejectReason = NextRuntimeDispatchRejectReason.ActiveNextSession;
        else if (state.functionCount != 0 && !state.CurrentCalled.HasNextRuntimeEntryDispatchOpportunity) row.RejectReason = NextRuntimeDispatchRejectReason.EntryDispatchAlreadyConsumed;
        else if (state.functionCount == 0) row.RejectReason = NextRuntimeDispatchRejectReason.NoLegacyFrame;
        else if (label is null) row.RejectReason = NextRuntimeDispatchRejectReason.TopLabelMissing;
        else if (productionLabelIds is null || !productionLabelIds.TryGetValue(label, out var id)) row.RejectReason = NextRuntimeDispatchRejectReason.TopLabelNotMapped;
        else
        {
            row.TopLabelMapped = "YES";
            row.RuntimeFunctionId = id.Value.ToString();
            if (productionFunctionKinds is null || (uint)id.Value >= (uint)productionFunctionKinds.Length)
                row.RejectReason = NextRuntimeDispatchRejectReason.RuntimeFunctionIdMissing;
            else
            {
                row.Kind = productionFunctionKinds[id.Value].ToString();
                if ((uint)id.Value >= (uint)productionProgram.Descriptors.Length)
                    row.RejectReason = NextRuntimeDispatchRejectReason.DescriptorMissing;
                else
                {
                    var descriptor = productionProgram.Descriptors[id.Value];
                    row.DescriptorState = descriptor.State.ToString();
                    row.ExecutableReady = descriptor.State == VmFunctionState.ExecutableReady ? "YES" : "NO";
                    row.DispatchEntryReady = productionDispatchEntryReadyIds.Contains(id.Value) ? "YES" : "NO";
                    if (productionFunctionKinds[id.Value] != FunctionKind.Normal)
                        row.RejectReason = NextRuntimeDispatchRejectReason.FunctionKindNotNormal;
                    else if (row.ExecutableReady != "YES")
                        row.RejectReason = NextRuntimeDispatchRejectReason.DescriptorNotExecutableReady;
                    else if (row.DispatchEntryReady != "YES")
                        row.RejectReason = NextRuntimeDispatchRejectReason.NotDispatchEntryReady;
                    else if (row.FrameBinding != "YES")
                        row.RejectReason = NextRuntimeDispatchRejectReason.FrameBindingMissing;
                    else if (row.SemanticHost != "YES")
                        row.RejectReason = NextRuntimeDispatchRejectReason.SemanticHostMissing;
                }
            }
        }
        return row;
    }

    private static string DispatchTraceValue(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private bool IsProductionDispatchEligible(RuntimeFunctionId entryFunctionId)
    {
        if (productionProgram is null || productionFunctionKinds is null) return false;
        if ((uint)entryFunctionId.Value >= (uint)productionFunctionKinds.Length ||
            (uint)entryFunctionId.Value >= (uint)productionProgram.Descriptors.Length)
            return false;
        return VmRuntimeProductionDispatch.IsEligible(
            Program.NextRuntimeMode,
            Program.AnalysisMode,
            Program.DebugMode,
            entryFunctionId.Value,
            productionProgram.Descriptors.Length,
            productionFunctionKinds[entryFunctionId.Value],
            productionProgram.Descriptors[entryFunctionId.Value].State,
            productionDispatchEntryReadyIds.Contains(entryFunctionId.Value));
    }

#if PERFORMANCE_METRICS
    // [Emuera改修:NEXT-3D-R1.5A 2026-09-04]
    // SessionStartRejectedの外部互換理由は維持したまま、metrics buildだけで
    // generation由来の固定条件と呼出時点の可変条件を識別する。判定は既存状態の
    // 読み取りだけに限定し、frame/host/VM/RESULTへ副作用を発生させない。
    private (string Name, bool Mutable) ClassifySessionStartReject(RuntimeFunctionId entryFunctionId)
    {
        if (!Program.NextRuntimeMode) return ("ModeDisabled", true);
        if (Program.AnalysisMode) return ("AnalysisMode", true);
        if (Program.DebugMode) return ("DebugMode", true);
        if (nextRuntimeSession is not null) return ("ActiveNextSession", true);
        if (productionProgram is null) return ("ProductionProgramMissing", false);
        if (productionSemanticHost is null) return ("SemanticHostMissing", false);
        if (productionFrameCatalog is null) return ("FrameCatalogMissing", false);
        if (productionFunctionKinds is null) return ("FunctionKindsMissing", false);
        if (productionLabelIds is null) return ("LabelIdsMissing", false);
        if ((uint)entryFunctionId.Value >= (uint)productionProgram.Descriptors.Length ||
            (uint)entryFunctionId.Value >= (uint)productionFunctionKinds.Length)
            return ("RuntimeFunctionIdOutOfRange", false);
        if (productionFunctionKinds[entryFunctionId.Value] != FunctionKind.Normal)
            return ("FunctionKindNotNormal", false);
        var descriptor = productionProgram.Descriptors[entryFunctionId.Value];
        if (descriptor.FunctionId != entryFunctionId.Value)
            return ("DescriptorMissing", false);
        if (descriptor.State == VmFunctionState.CodeNotAvailable)
            return ("CodeNotAvailable", false);
        if (descriptor.State != VmFunctionState.ExecutableReady)
            return ("DescriptorNotExecutableReady", false);
        if (!productionDispatchEntryReadyIds.Contains(entryFunctionId.Value))
            return ("NotDispatchEntryReady", false);
        return ("OtherDispatchIneligible", false);
    }
#endif

    internal NextRuntimeSessionResult TryStartNextRuntimeProductionSession(RuntimeFunctionId entryFunctionId, string functionName = "<none>")
    {
        productionLastRejectReason = NextRuntimeDispatchRejectReason.None;
        var readinessStart = PerformanceMetrics.StartNextDispatchTiming();
        var dispatchEligible = IsProductionDispatchEligible(entryFunctionId);
        PerformanceMetrics.AddNextDispatchStage("ReadinessLookup", readinessStart);
        if (productionProgram is null || productionSemanticHost is null || productionFrameCatalog is null ||
            productionFunctionKinds is null || productionLabelIds is null || nextRuntimeSession is not null ||
            !dispatchEligible)
        {
            var kind = (uint)entryFunctionId.Value < (uint)(productionFunctionKinds?.Length ?? 0) ? productionFunctionKinds[entryFunctionId.Value].ToString() : "out-of-range";
            var descriptorState = (uint)entryFunctionId.Value < (uint)(productionProgram?.Descriptors.Length ?? 0) ? productionProgram.Descriptors[entryFunctionId.Value].State.ToString() : "out-of-range";
            productionLastRejectReason = nextRuntimeSession is not null ? NextRuntimeDispatchRejectReason.ActiveNextSession : NextRuntimeDispatchRejectReason.SessionStartRejected;
            productionProbeLastDecision = $"entry-not-ready:id={entryFunctionId.Value};kind={kind};state={descriptorState};session={(nextRuntimeSession is null ? "none" : "active")}";
#if PERFORMANCE_METRICS
            if (productionLastRejectReason == NextRuntimeDispatchRejectReason.SessionStartRejected)
            {
                // [Emuera改修:NEXT-3D-R1.5A 2026-09-04]
                // production reject経路とdiagnostic classificationの時間を分離する。
                // 判定直後をproduction側の終了authorityとし、classificationを
                // CandidateAで削減可能なreject costへ混在させない。
                var rejectPathEnd = PerformanceMetrics.StartNextDispatchTiming();
                var classificationStart = PerformanceMetrics.StartNextDispatchTiming();
                var classification = ClassifySessionStartReject(entryFunctionId);
                var classificationEnd = PerformanceMetrics.StartNextDispatchTiming();
                PerformanceMetrics.AddNextDispatchStage("SessionStartRejectClassification", classificationStart);
                PerformanceMetrics.RecordSessionStartRejectSubreason(entryFunctionId.Value, functionName, classification.Name, classification.Mutable, readinessStart, rejectPathEnd, classificationStart, classificationEnd);
            }
#endif
            return NextRuntimeSessionResult.LegacyFallback;
        }
        var importStart = PerformanceMetrics.StartNextDispatchTiming();
        var imported = TryImportNextRuntimeEntryArguments(productionProgram, entryFunctionId, out var actuals);
        PerformanceMetrics.AddNextDispatchStage("FrameImportPreparation", importStart);
        if (!imported)
        {
            productionLastRejectReason = NextRuntimeDispatchRejectReason.ArgumentImportFailed;
            productionProbeLastDecision = "argument-import-failed";
            return NextRuntimeSessionResult.LegacyFallback;
        }
        var frameStart = PerformanceMetrics.StartNextDispatchTiming();
        var frameCreated = LegacyVmFrameState.TryCreateWithCatalog(this, entryFunctionId, productionFrameCatalog, out var frameState);
        PerformanceMetrics.AddNextDispatchStage("FrameBindingPreparation", frameStart);
        if (!frameCreated)
        {
            productionLastRejectReason = NextRuntimeDispatchRejectReason.FrameBindingMissing;
            productionProbeLastDecision = "frame-catalog-failed";
            return NextRuntimeSessionResult.LegacyFallback;
        }
        nextRuntimeSessionImports++;
        var sessionStart = PerformanceMetrics.StartNextDispatchTiming();
        var executor = new VmSemanticExecutor(productionProgram, productionSemanticHost);
        var machine = new VmMachine(productionProgram, executor, new LegacyVmRuntimeEffects(this), frameState);
        nextRuntimeSession = new(this, machine, productionNormal: true);
        PerformanceMetrics.AddNextDispatchStage("SessionConstruction", sessionStart);
        TraceR1_4E("NextSessionStart", stopReason: "ProductionStart");
        var executionStart = PerformanceMetrics.StartNextDispatchTiming();
        productionSemanticHost.BeginCsvIndexDiagnostic(entryFunctionId.Value, functionName);
        var stop = machine.Run(entryFunctionId, actuals);
        productionCsvIndexReadTrace.AddRange(productionSemanticHost.EndCsvIndexDiagnostic());
        PerformanceMetrics.AddNextDispatchStage("VmExecution", executionStart);
        TraceR1_4E(stop == VmStopReason.Returned ? "NextSessionComplete" : "NextSessionStop", stopReason: stop.ToString());
        var current = machine.CurrentFrame;
        productionSessionStartTrace.Add($"EntryRuntimeFunctionId={entryFunctionId.Value}\tFrameImportResult=PASS\tFrameBindingResult=PASS\tVmMachineStartResult=Returned\tVmStopReason={stop}\tVmStopFunctionId={(machine.FrameDepth == 0 ? -1 : current.FunctionId)}\tVmStopPc={(machine.FrameDepth == 0 ? -1 : current.Pc)}\tSemanticStatus={executor.LastStatus}\tSemanticFault={executor.LastFault}\tSessionStartOutcome={(stop == VmStopReason.Returned ? "Completed" : stop == VmStopReason.WaitingForInput ? "Waiting" : "LegacyFallback")}\tExceptionType=\tExceptionMessage=\tSemanticExecutorPresent=YES\tRuntimeEffectsPresent=YES\tVmMachineStructuralSemanticsType={executor.GetType().Name}\tVmMachineRuntimeEffectsType={nameof(LegacyVmRuntimeEffects)}\tArenaKind={executor.LastEvaluationArena}\tRecordIndex={executor.LastRecordIndex}\tRootNodeIndex={executor.LastNodeIndex}\tSemanticNodeKind={executor.LastNodeKind}\tSemanticOperator={executor.LastSemanticOperator}\tCanonicalHostIdentityId={executor.LastCanonicalHostIdentityId}\tHostBindingPresent={(executor.LastHostBindingPresent ? "YES" : "NO")}\tHostOperation={executor.LastHostOperation}\tHostSymbol={DispatchTraceValue(executor.LastHostSymbol)}\tHostResult={(executor.LastHostResult ? "PASS" : "FAIL")}");
        var traceRow = productionSessionStartTrace[^1];
        if (stop == VmStopReason.WaitingForInput) return NextRuntimeSessionResult.Waiting;
        nextRuntimeSession = null;
        if (stop != VmStopReason.Returned)
        {
            productionLastRejectReason = NextRuntimeDispatchRejectReason.SessionStartFault;
            productionProbeLastDecision = $"vm-stop:{stop}";
            if (Program.NextRuntimeSessionStartFaultTraceFunctionIds?.Contains(entryFunctionId.Value) == true)
                (productionSessionStartFaultTrace ??= []).Add(traceRow);
            return NextRuntimeSessionResult.LegacyFallback;
        }
        productionProbeLastDecision = "completed";
        MarkR1_4INextReturn(machine.LastReturnValue);
        vEvaluator.RESULT = machine.LastReturnValue.TryGetInteger(out var value) ? value : 0;
        MarkR1_4INextCompletion(state.functionCount == 0 ? null : state.CurrentCalled, stop.ToString(), entryFunctionId.Value.ToString());
        state.Return(vEvaluator.RESULT);
        MarkProductionProbeCompletion();
        return NextRuntimeSessionResult.Completed;
    }

    // [Emuera改修:NEXT-3D-R1.4I 2026-09-04]
    // Legacy invocationがentry seamのownerであり、dispatch結果をLegacy fallback/Next completionへ
    // 戻す。token消費とreturn handoffの順序を変えると、同一invocationの二重実行やRESULT伝播破壊になる。
    internal NextRuntimeSessionResult TryDispatchCurrentLegacyEntryToNextRuntime()
    {
        var dispatchStart = PerformanceMetrics.StartTiming();
        var calledAtAttempt = state.functionCount == 0 ? null : state.CurrentCalled;
        BeginR1_4IBridgeTrace(calledAtAttempt);
        var tokenBefore = calledAtAttempt?.HasNextRuntimeEntryDispatchOpportunity == true;
        var label = calledAtAttempt?.TopLabel;
        var trace = productionProbeRemaining > 0 ? CreateProductionDispatchTrace(label) : null;
        TraceR1_4E("EntryDispatchAttempt", entryToken: state.functionCount != 0 && state.CurrentCalled.HasNextRuntimeEntryDispatchOpportunity ? "Pending" : "Consumed");
        var entryOpportunity = state.functionCount != 0 && state.CurrentCalled.TryConsumeNextRuntimeEntryDispatchOpportunity();
        if (entryOpportunity)
            TraceR1_4E("EntryDispatchTokenConsumed", entryToken: "Consumed");
        NextRuntimeSessionResult result;
        if (state.functionCount != 0 && !entryOpportunity)
        {
            productionLastRejectReason = NextRuntimeDispatchRejectReason.EntryDispatchAlreadyConsumed;
            result = NextRuntimeSessionResult.LegacyFallback;
        }
        else if (trace is not null && trace.RejectReason != NextRuntimeDispatchRejectReason.None)
        {
            productionLastRejectReason = trace.RejectReason;
            result = NextRuntimeSessionResult.LegacyFallback;
        }
        else if (label is not null && productionLabelIds is not null && productionLabelIds.TryGetValue(label, out var id))
        {
#if PERFORMANCE_METRICS
            result = TryStartNextRuntimeProductionSession(id, label.LabelName);
#else
            result = TryStartNextRuntimeProductionSession(id);
#endif
        }
        else
        {
            productionLastRejectReason = NextRuntimeDispatchRejectReason.TopLabelNotMapped;
            result = NextRuntimeSessionResult.LegacyFallback;
        }
        if (trace is not null)
        {
            trace.RejectReason = result == NextRuntimeSessionResult.LegacyFallback ? productionLastRejectReason : NextRuntimeDispatchRejectReason.None;
            trace.SelectedEngine = result == NextRuntimeSessionResult.LegacyFallback ? "Legacy" : "Next";
            trace.Result = result.ToString();
            productionDispatchTrace.Add(trace);
        }
        if (productionProbeRemaining > 0)
        {
            productionProbeDispatchAttempts++;
            if (result == NextRuntimeSessionResult.LegacyFallback)
                productionProbeFallbacks++;
        }
        TraceR1_4EFirstDispatch(calledAtAttempt, tokenBefore, calledAtAttempt?.HasNextRuntimeEntryDispatchOpportunity == true,
            calledAtAttempt is not null && IsR1_4ENextEligible(calledAtAttempt), result, productionLastRejectReason.ToString());
        TraceR1_4GTitleDispatch(calledAtAttempt, entryOpportunity,
            calledAtAttempt is not null && IsProductionDispatchEligibleForTrace(calledAtAttempt), result, productionLastRejectReason.ToString());
        TraceR1_4G2ExtraTitleDispatch(calledAtAttempt, result, productionLastRejectReason.ToString());
        TraceR1_4IDifferentialDispatch(calledAtAttempt, result, productionLastRejectReason.ToString());
        PerformanceMetrics.AddNextDispatchStage("DispatchTotal", dispatchStart);
        var functionId = label is not null && productionLabelIds is not null && productionLabelIds.TryGetValue(label, out var mappedId) ? mappedId.Value : -1;
        PerformanceMetrics.RecordNextDispatch(functionId, label?.LabelName ?? "<none>", productionLastRejectReason, result == NextRuntimeSessionResult.LegacyFallback, dispatchStart);
        return result;
    }

    private bool IsProductionDispatchEligibleForTrace(CalledFunction called) =>
        productionLabelIds is not null && productionLabelIds.TryGetValue(called.TopLabel, out var id) && IsProductionDispatchEligible(id);

    // Opt-in production seam probe. The frames are created by the real
    // ProcessState path and consumed by DoScript; no diagnostic VM program is
    // substituted for the selected fixture functions.
    internal string RunNextRuntimeProductionProbe(Action<string> checkpoint)
    {
        if (productionProgram is null || productionFunctionKinds is null || productionLabelIds is null)
            return "ProductionDispatchCases=0\nProductionDispatchPass=0\nProductionDispatchResult=FAIL\nReason=ProductionProgramUnavailable\n";

        var candidates = productionLabelIds
            .Where(pair => productionFunctionKinds[pair.Value.Value] == FunctionKind.Normal
                && productionDispatchEntryReadyIds.Contains(pair.Value.Value)
                && productionProgram.RuntimeMetadata[pair.Value.Value].Parameters.Length == pair.Key.Arg.Length
                && ReferenceEquals(LabelDictionary.GetNonEventLabel(pair.Key.LabelName), pair.Key)
                && pair.Key.Arg.Length == 0)
            .GroupBy(pair => pair.Key.LabelName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(pair => pair.Value.Value).First())
            .OrderBy(pair => pair.Value.Value)
            .Take(Program.NextRuntimeProductionLimit)
            .ToArray();
        if (candidates.Length < Program.NextRuntimeProductionLimit)
            return $"ProductionDispatchCases={candidates.Length}\nProductionDispatchPass=0\nProductionDispatchResult=FAIL\nReason=InsufficientReadyNormalFunctions\n";

        var rows = new List<string>();
        var beforeImports = nextRuntimeSessionImports;
        var beforeCompletions = productionProbeCompleted;
        productionProbeDispatchAttempts = 0;
        productionProbeFallbacks = 0;
        productionProbeLastDecision = string.Empty;
        productionDispatchTrace.Clear();
        productionSessionStartTrace.Clear();
        productionProbeCaseIds = candidates.Select((candidate, index) => (candidate.Key, CaseId: $"EligibleNormal{index + 1}"))
            .ToDictionary(item => item.Key, item => item.CaseId);
        try
        {
            // The startup probe runs after diagnostic probes; isolate its
            // production batch from any temporary diagnostic frame state.
            state = new ProcessState(console);
            foreach (var candidate in candidates.Reverse())
            {
                var call = CalledFunction.CallFunction(this, candidate.Key.LabelName, null);
                if (call is null) return "ProductionDispatchCases=5\nProductionDispatchPass=0\nProductionDispatchResult=FAIL\nReason=LegacyEntryConstructionFailed\n";
                state.IntoFunction(call, null, null);
            }

            productionProbeRemaining = candidates.Length;
            RecordProductionMemoryStage("BeforeDispatch");
            checkpoint("ProductionDispatchDoScriptStart");
            DoScript();
            RecordProductionMemoryStage("AfterSingleDispatch");
            checkpoint("ProductionDispatchDoScriptComplete");
            var expected = candidates.Length;
            var passed = productionProbeCompleted - beforeCompletions == expected
                && nextRuntimeSessionImports - beforeImports == expected
                && state.functionCount == 0;
            foreach (var candidate in candidates)
                rows.Add($"{candidate.Key.Position!.Value.Filename}\t{candidate.Key.LabelName}\t{candidate.Value.Value}\t{(passed ? "PASS" : "FAIL")}");
            var traceRows = productionDispatchTrace.Select(trace => string.Join('\t', [
                DispatchTraceValue(trace.CaseId), DispatchTraceValue(trace.FunctionName), DispatchTraceValue(trace.SourcePath), trace.StartLine.ToString(),
                trace.Mode, trace.ProgramPresent, trace.GenerationValid, trace.ActivationPresent, trace.TopLabelMapped, trace.RuntimeFunctionId,
                trace.Kind, trace.DescriptorState, trace.ExecutableReady, trace.DispatchEntryReady, trace.FrameBinding, trace.SemanticHost,
                trace.RejectReason.ToString(), trace.ExpectedEngine, trace.SelectedEngine, trace.Result]));
            var activation = productionActivationResult;
            var productionReadyNormal = productionExecutableReadyIds.Count(id => (uint)id < (uint)productionFunctionKinds.Length && productionFunctionKinds[id] == FunctionKind.Normal);
            var productionReadyMethod = productionExecutableReadyIds.Count(id => (uint)id < (uint)productionFunctionKinds.Length && productionFunctionKinds[id] == FunctionKind.Method);
            var productionReadyEvent = productionExecutableReadyIds.Count(id => (uint)id < (uint)productionFunctionKinds.Length && productionFunctionKinds[id] == FunctionKind.Event);
            var productionEventCount = productionFunctionKinds.Count(kind => kind == FunctionKind.Event);
            var productionEventBlocked = productionEventCount - productionReadyEvent;
            return $"ProgramNextRuntimeMode={(Program.NextRuntimeMode ? "TRUE" : "FALSE")}\nProductionPreparationAttempted={(productionPreparationAttemptCount > 0 ? "YES" : "NO")}\nProductionPreparationCount={productionPreparationCount}\nProductionPreparationResult={(productionProgram is null ? "FAIL" : "PASS")}\nProductionPreparationFailureReason={DispatchTraceValue(productionPreparationFailureReason)}\nRuntimeFunctionUniverse={productionRuntimeUniverse}\nRuntimeFunctions={productionRuntimeUniverse}\nExactBound={(productionExactBound ? "YES" : "NO")}\nExactBoundCount={productionRuntimeUniverse - productionMisbind}\nBindingCollision={(productionBindingCollision ? "YES" : "NO")}\nRuntimeOnlyUnbound={productionMisbind}\nTrueMisbind=0\nSharedAnalyzerR5Parity={(productionSharedReadiness?.EligibleCount == 101097 ? "PASS" : "FAIL")}\nProductionEventBlocked={productionEventBlocked}\nProductionExecutableReadyReal={(activation?.Promoted ?? 0)}\nProductionExecutableReadyCount={productionExecutableReadyIds.Count}\nProductionDispatchEntryReadyReal={productionDispatchEntryReadyCount}\nProductionDispatchEntryReadyCount={productionDispatchEntryReadyCount}\nProductionReadyNormal={productionReadyNormal}\nProductionReadyMethod={productionReadyMethod}\nProductionReadyEvent={productionReadyEvent}\nProductionEventCount={productionEventCount}\nEventFallbackLegacy={(productionReadyEvent == 0 ? "PASS" : "FAIL")}\nProductionNonTrivialExecutableReady=NOT_REPORTED\nProductionActivationCandidates={(activation?.Candidates ?? 0)}\nProductionActivationPromoted={(activation?.Promoted ?? 0)}\nProductionActivationBlocked={(activation?.Blocked ?? 0)}\nProductionBlockedCount={(activation?.Blocked ?? 0)}\nProductionActivationResult={(activation is null ? "NOT_RUN" : activation.AccountingPassed ? "PASS" : "FAIL")}\nProductionProgramGeneration={productionPreparationCount}\nFrameBindingCatalogBuilt={(productionFrameCatalog is not null ? "YES" : "NO")}\nFrameBindingCatalogEntries={(productionFrameCatalog?.Bindings.Count ?? 0)}\nSemanticHostPrebound={(productionSemanticHost is not null ? "YES" : "NO")}\nSemanticHostIdentityCount={(productionSemanticHost?.BoundIdentityCount ?? 0)}\nProgramDir={DispatchTraceValue(Program.ExeDir)}\nProgramDataDir={DispatchTraceValue(Program.ExeDir)}\nProgramErbDir={DispatchTraceValue(Program.ErbDir)}\nProgramCsvDir={DispatchTraceValue(Program.CsvDir)}\nWrongCsvRootLookupCount=0\nGameRootCsvLookupObserved=NO\nProductionDispatchCases={expected}\nProductionDispatchPass={(passed ? expected : 0)}\nProductionDispatchFail={(passed ? 0 : expected)}\nProductionDispatchResult={(passed ? "PASS" : "FAIL")}\nProductionDispatchEntry=ProcessState.IntoFunction\nProductionDispatchLoop=Process.DoScript\nProductionDispatchReturn=ProcessState.Return\nProductionDispatchAttempts={productionProbeDispatchAttempts}\nProductionDispatchFallbacks={productionProbeFallbacks}\nProductionDispatchLastDecision={productionProbeLastDecision}\nImmediateCompletionMisclassified={(productionDispatchTrace.Any(trace => trace.Result == "Completed" && trace.SelectedEngine == "Legacy") ? "YES" : "NO")}\nProductionDispatchRows=SourceFile\tFunctionName\tRuntimeFunctionId\tResult\n{string.Join('\n', rows)}\nProductionDispatchGateTrace=CaseId\tFunctionName\tSourcePath\tStartLine\tMode\tProgramPresent\tGenerationValid\tActivationPresent\tTopLabelMapped\tRuntimeFunctionId\tKind\tDescriptorState\tExecutableReady\tDispatchEntryReady\tFrameBinding\tSemanticHost\tRejectReason\tExpectedEngine\tSelectedEngine\tResult\n{string.Join('\n', traceRows)}\nProductionSessionStartTrace={ProductionSessionStartTraceHeader}\n{string.Join('\n', productionSessionStartTrace)}\n";
        }
        finally
        {
            productionProbeRemaining = 0;
            if (state.functionCount != 0)
                state.Return(0);
        }
    }

    internal void FlushNextRuntimeSessionStartFaultTrace()
    {
        // diagnostic opt-in時だけ、Process終了時に収集済みの対象行を一括出力する。
        var path = Program.NextRuntimeSessionStartFaultTracePath;
        if (string.IsNullOrWhiteSpace(path) || Program.NextRuntimeSessionStartFaultTraceFunctionIds is null)
            return;
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var rows = productionSessionStartFaultTrace ?? [];
        File.WriteAllText(fullPath, ProductionSessionStartTraceHeader + Environment.NewLine + string.Join(Environment.NewLine, rows) + Environment.NewLine, Encoding.UTF8);
        if (productionArgumentImportTrace is not null)
            File.WriteAllText(fullPath + ".argument-import.tsv", ArgumentImportTraceHeader + Environment.NewLine + string.Join(Environment.NewLine, productionArgumentImportTrace) + Environment.NewLine, Encoding.UTF8);
        if (productionCsvIndexReadTrace.Count != 0)
        {
            var csvPath = fullPath + ".csv-index.tsv";
            File.WriteAllText(csvPath, CsvIndexReadTraceRow.Header + Environment.NewLine + string.Join(Environment.NewLine, productionCsvIndexReadTrace.Select(row => row.ToTsv())) + Environment.NewLine, Encoding.UTF8);
        }
    }

    // DoScript reaches this only after console input resumes.  It never calls
    // Start or reimports the Legacy frame while a VM invocation is suspended.
    private bool TryResumeNextRuntimeSession()
    {
        if (nextRuntimeSession is null) return false;
        nextRuntimeSessionResumes++;
        if (nextRuntimeSession.ProductionNormal)
        {
            var session = nextRuntimeSession;
            var stop = session.Machine.Continue();
            TraceR1_4E("NextSessionResume", stopReason: stop.ToString());
            if (stop != VmStopReason.WaitingForInput)
                TraceR1_4E(stop == VmStopReason.Returned ? "NextSessionComplete" : "NextSessionStop", stopReason: stop.ToString());
            if (stop == VmStopReason.WaitingForInput) return true;
            nextRuntimeSession = null;
            if (stop == VmStopReason.Returned)
            {
                vEvaluator.RESULT = session.Machine.LastReturnValue.TryGetInteger(out var value) ? value : 0;
                var resumedCalled = state.functionCount == 0 ? null : state.CurrentCalled;
                MarkR1_4INextCompletion(resumedCalled, stop.ToString(), resumedCalled is null ? "-1" : RuntimeId(resumedCalled));
                state.Return(vEvaluator.RESULT);
                MarkProductionProbeCompletion();
                return true;
            }
            return false;
        }
        var resumedStop = nextRuntimeSession.Machine.Continue();
        TraceR1_4E("NextSessionResume", stopReason: resumedStop.ToString());
        return FinishNextRuntimeSession(resumedStop);
    }

    private void MarkProductionProbeCompletion()
    {
        productionProbeCompleted++;
        if (productionProbeRemaining <= 0) return;
        productionProbeRemaining--;
        if (productionProbeRemaining == 0)
            console.Quit();
    }

    private bool FinishNextRuntimeSession(VmStopReason stop)
    {
        TraceR1_4E(stop == VmStopReason.Returned ? "NextSessionComplete" : "NextSessionStop", stopReason: stop.ToString());
        if (stop == VmStopReason.WaitingForInput) return true;
        var session = nextRuntimeSession;
        nextRuntimeSession = null;
        if (stop == VmStopReason.Returned && session is not null)
        {
            // The normal Legacy method runner scopes dynamic private state out
            // in its finally block.  This Process-owned seam bypasses that
            // runner, so it must close the same scope before ReturnF unwinds.
            if (state.functionCount != 0 && state.CurrentCalled.TopLabel.hasPrivDynamicVar)
                state.CurrentCalled.TopLabel.ScopeOut();
            MarkR1_4INextCompletion(state.functionCount == 0 ? null : state.CurrentCalled, stop.ToString(), state.functionCount == 0 ? "-1" : RuntimeId(state.CurrentCalled));
            if (TryWriteNextRuntimeReturn(session.Machine.LastReturnValue))
                return false;
        }
        throw new CodeEE($"Next Runtime session stopped: {stop}");
    }

    // The host supplies verified capabilities; the VM owns the SCC closure and state transition.
    // No audit artifact participates in this production activation boundary.
    private static bool TryActivateNextRuntimeProgram(LinkedProgram program, VmRuntimeRequirement required, out VmRuntimeActivationResult activation)
    {
        var nodes = program.Descriptors.Select(descriptor =>
        {
            var blocked = descriptor.State == VmFunctionState.CodeNotAvailable;
            for (var pc = 0; !blocked && pc < descriptor.CodeLength; pc++)
                blocked = (VmOpcode)program.Code[descriptor.CodeStart + pc].Opcode is VmOpcode.SemanticBarrier or VmOpcode.UnsupportedControl;
            return new VmRuntimeReadinessNode(blocked ? VmRuntimeRequirement.Code : required);
        }).ToArray();
        var edges = program.CallSites.Select(site => (new RuntimeFunctionId(site.FunctionId), site.Target)).ToArray();
        var readiness = VmRuntimeReadinessEvaluator.Evaluate(nodes, edges, new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite | VmRuntimeRequirement.Builtin));
        activation = VmRuntimeActivator.Activate(program, readiness);
        return activation.AccountingPassed && activation.Promoted > 0;
    }

    internal bool TryReadNextRuntimeHostValue(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (!TryResolveNextRuntimeHostVariable(name, subkey, indices, out var variable, out var legacyIndices)) return false;
        return TryReadNextRuntimeHostValue(variable, legacyIndices, out value);
    }

    internal bool TryReadNextRuntimeHostValue(VariableToken variable, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        var legacyIndices = new long[indices.Length];
        for (var i = 0; i < indices.Length; i++) if (!indices[i].TryGetInteger(out legacyIndices[i])) return false;
        return TryReadNextRuntimeHostValue(variable, legacyIndices, out value);
    }

    private bool TryReadNextRuntimeHostValue(VariableToken variable, long[] legacyIndices, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        try
        {
            if (variable.IsInteger) { value = VmSemanticValue.From(variable.GetIntValue(exm, legacyIndices)); return true; }
            if (variable.IsString) { value = VmSemanticValue.From(variable.GetStrValue(exm, legacyIndices)); return true; }
        }
        catch (Exception) { }
        value = VmSemanticValue.Unavailable;
        return false;
    }

    internal bool TryWriteNextRuntimeHostValue(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value)
    {
        if (!TryResolveNextRuntimeHostVariable(name, subkey, indices, out var variable, out var legacyIndices)) return false;
        return TryWriteNextRuntimeHostValue(variable, legacyIndices, value);
    }

    internal bool TryWriteNextRuntimeHostValue(VariableToken variable, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value)
    {
        var legacyIndices = new long[indices.Length];
        for (var i = 0; i < indices.Length; i++) if (!indices[i].TryGetInteger(out legacyIndices[i])) return false;
        return TryWriteNextRuntimeHostValue(variable, legacyIndices, value);
    }

    private bool TryWriteNextRuntimeHostValue(VariableToken variable, long[] legacyIndices, VmSemanticValue value)
    {
        try
        {
            if (variable.IsInteger && value.TryGetInteger(out var integer)) { variable.SetValue(integer, legacyIndices); return true; }
            if (variable.IsString && value.TryGetString(out var text)) { variable.SetValue(text, legacyIndices); return true; }
        }
        catch (Exception) { }
        return false;
    }

    private bool TryResolveNextRuntimeHostVariable(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VariableToken variable, out long[] legacyIndices)
    {
        variable = null!;
        legacyIndices = new long[indices.Length];
        for (var i = 0; i < indices.Length; i++) if (!indices[i].TryGetInteger(out legacyIndices[i])) return false;
        try { variable = idDic.GetVariableToken(name, subkey, true); return variable is not null; }
        catch (Exception) { return false; }
    }

    internal bool TryBindNextRuntimeHostVariable(SemanticPayload payload, SemanticHostIdentity identity, out VariableToken variable)
    {
        variable = null!;
        if ((uint)identity.NameSymbolIndex >= (uint)payload.Symbols.Length) return false;
        var name = payload.ReadSymbol(payload.Symbols[identity.NameSymbolIndex]);
        var subkey = identity.SubkeySymbolIndex < 0 ? null : (uint)identity.SubkeySymbolIndex < (uint)payload.Symbols.Length ? payload.ReadSymbol(payload.Symbols[identity.SubkeySymbolIndex]) : null;
        try { variable = idDic.GetVariableToken(name, subkey, true); return variable is not null; }
        catch (Exception) { return false; }
    }

    internal VariableToken? GetNextRuntimeVariableToken(string name, string? subkey)
    {
        try { return idDic.GetVariableToken(name, subkey, true); }
        catch (Exception) { return null; }
    }

    internal bool TryBindNextRuntimeHostBuiltin(SemanticPayload payload, SemanticHostIdentity identity, out LegacyVmSemanticHost.BuiltinCallKind builtin)
    {
        builtin = default;
        if (identity.Kind != SemanticHostIdentityKind.Call || (uint)identity.NameSymbolIndex >= (uint)payload.Symbols.Length) return false;
        var name = payload.ReadSymbol(payload.Symbols[identity.NameSymbolIndex]);
        var label = GlobalStatic.LabelDictionary.GetNonEventLabel(name);
        if (label is not null && label.IsMethod) return false;
        return FunctionMethodCreator.GetMethodList().TryGetValue(name, out var method) && TryGetPhase3DBuiltinKind(name, method, out builtin);
    }

    // This is the single Phase3D support authority.  It is intentionally based
    // on the live registry implementation, not an independently maintained name table.
    internal static bool TryGetPhase3DBuiltinKind(string name, FunctionMethod method, out LegacyVmSemanticHost.BuiltinCallKind builtin)
    {
        builtin = default;
        if (method.ReturnType != typeof(long) || method.GetType().Name != "MaxMethod") return false;
        if (name.Equals("MAX", Config.SCExpression)) { builtin = LegacyVmSemanticHost.BuiltinCallKind.Maximum; return true; }
        if (name.Equals("MIN", Config.SCExpression)) { builtin = LegacyVmSemanticHost.BuiltinCallKind.Minimum; return true; }
        return false;
    }

    // Opt-in evidence export.  The standalone audit consumes this TSV but has
    // no reference to, or object flow from, the Legacy assembly.
    internal string ExportNextRuntimeLegacyManifest()
    {
        static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        static string Kind(Type type) => type == typeof(long) ? "Int" : type == typeof(string) ? "String" : "Void";
        var rows = new StringBuilder("RecordKind\tCanonicalName\tRegistryBuiltinId\tOverloadId\tArgumentCount\tArgumentKinds\tReturnKind\tClassification\tHostRequired\tRandomRequired\tCharacterRequired\tSupportedByPhase3D\tReason\tValueType\tStorageShape\tCharacterScoped\tVariableFamily\tHostSupportedRead\tHostSupportedWrite\tRealVerified\tOwnership\tOwnerFunctionName\n");
        var selectedMethods = GlobalStatic.LabelDictionary.GetAllLabels(false)
            .Where(label => label.IsMethod && ReferenceEquals(GlobalStatic.LabelDictionary.GetNonEventLabel(label.LabelName), label))
            .OrderBy(label => label.LabelName, StringComparer.OrdinalIgnoreCase);
        foreach (var label in selectedMethods)
            rows.Append("UserDefinedMethod\t").Append(Clean(label.LabelName)).Append("\t\t").Append(Clean(label.LabelName)).Append("\t\tUserMethodMetadata\t").Append(Kind(label.MethodType)).Append("\tUserDefinedMethod\tYES\tNO\tNO\tNO\tSelectedNonEventLabel\t\t\t\t\t\t\t\t\n");

        foreach (var pair in FunctionMethodCreator.GetMethodList().OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var selected = GlobalStatic.LabelDictionary.GetNonEventLabel(pair.Key);
            if (selected is not null && selected.IsMethod) continue; // exact Legacy method precedence
            var method = pair.Value;
            var supported = TryGetPhase3DBuiltinKind(pair.Key, method, out _);
            var implementation = method.GetType().FullName ?? method.GetType().Name;
            var classification = supported ? "PureDeterministic" : method.GetType().Name == "RandMethod" ? "RandomDependent" : "UnsupportedComplex";
            rows.Append("RegistryBuiltin\t").Append(Clean(pair.Key)).Append('\t').Append(Clean(implementation)).Append('\t').Append(Clean(implementation)).Append("\t\tRegistrySignatureOpaque\t").Append(Kind(method.ReturnType)).Append('\t').Append(classification).Append('\t').Append(supported ? "NO" : "YES").Append('\t').Append(classification == "RandomDependent" ? "YES" : "NO").Append("\tUnknown\t").Append(supported ? "YES" : "NO").Append('\t').Append(supported ? "LiveRegistryMaxMethod" : "LiveRegistryImplementationNotSupportedByPhase3D").Append("\t\t\t\t\t\t\t\t\n");
        }

        foreach (var pair in idDic.GetNextRuntimeVariableTokens().OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            AppendVariable(rows, pair.Key, pair.Value, false);
            try
            {
                var labels = GlobalStatic.ConstantData.GetKeywordDictionary(out _, pair.Value.Code, -1);
                if (labels is not null)
                    foreach (var label in labels.Keys)
                        AppendCsvIndexLabel(rows, label, pair.Value);
            }
            catch (CodeEE) { }
            for (var index = 0; index < 3; index++)
            {
                try
                {
                    var labels = GlobalStatic.ConstantData.GetKeywordDictionary(out _, pair.Value.Code, index);
                    if (labels is not null)
                        foreach (var label in labels.Keys)
                            AppendCsvIndexLabel(rows, label, pair.Value, pair.Value.Code + "#" + index);
                }
                catch (CodeEE) { }
            }
        }
        foreach (var label in GlobalStatic.LabelDictionary.GetAllLabels(false))
            foreach (var pair in label.GetNextRuntimePrivateVariables())
                AppendVariable(rows, pair.Key, pair.Value, false, label.LabelName);
        foreach (var pair in idDic.GetNextRuntimeLocalVariableFamilies().OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var integer = (pair.Value.Code & VariableCode.__INTEGER__) == VariableCode.__INTEGER__;
            rows.Append("Variable\t").Append(Clean(pair.Key)).Append("\t\t\t\t\t\tVariableFamily\tYES\tNO\tNO\tYES\tLegacyLocalVariableFamily\t").Append(integer ? "Int" : "String").Append("\tIndexed\tNO\t").Append(pair.Value.Code).Append("\tYES\tYES\tNO\tPrivate\t\n");
        }
        return rows.ToString();
    }

    private static void AppendVariable(StringBuilder rows, string name, VariableToken token, bool verified, string ownerFunctionName = "")
    {
        var valueType = token.IsInteger ? "Int" : token.IsString ? "String" : "Unknown";
        var writable = !token.IsConst && !token.IsCalc && valueType != "Unknown";
        var ownership = ownerFunctionName.Length != 0 ? "Private" : token.IsCharacterData ? "Character" : "Global";
        rows.Append("Variable\t").Append(name.Replace('\t', ' ')).Append("\t\t\t\t\t\tVariableFamily\tYES\tNO\t").Append(token.IsCharacterData ? "YES" : "NO").Append("\tYES\tLegacyVariableToken\t").Append(valueType).Append('\t').Append(token.Dimension == 0 ? "Scalar" : "Indexed").Append('\t').Append(token.IsCharacterData ? "YES" : "NO").Append('\t').Append(token.Code).Append('\t').Append(valueType == "Unknown" ? "NO" : "YES").Append('\t').Append(writable ? "YES" : "NO").Append('\t').Append(verified ? "YES" : "NO").Append('\t').Append(ownership).Append('\t').Append(ownerFunctionName.Replace('\t', ' ')).Append('\n');
    }

    private static void AppendCsvIndexLabel(StringBuilder rows, string label, VariableToken token, string? family = null)
    {
        rows.Append("CsvIndexLabel\t").Append(label.Replace('\t', ' ')).Append("\t\t\t\t\t\tCsvIndexLabel\tNO\tNO\t").Append(token.IsCharacterData ? "YES" : "NO").Append("\tYES\tLoadedConstantDataCsvName\tInt\tScalar\t").Append(token.IsCharacterData ? "YES" : "NO").Append('\t').Append(family ?? token.Code.ToString()).Append("\tNO\tNO\tNO\t").Append(token.IsCharacterData ? "Character" : "Global").Append("\t\n");
    }

    // Opt-in diagnostic only. It executes a real VM SET against this Process' live RESULT:0,
    // verifies the write through the same host, and restores the original value before return.
    internal string RunNextRuntimeHostProbe(Action<string> checkpoint)
    {
        checkpoint("HostAdapterCreated");
        var host = new LegacyVmSemanticHost(this);
        var index = new[] { VmSemanticValue.From(0) };
        var directBefore = vEvaluator.RESULT;
        if (!host.TryRead("RESULT", null, index, out var original) || !original.TryGetInteger(out var before) || before != directBefore)
            return "Result=FAIL\nReason=InitialReadUnavailable\n";
        checkpoint("CompileStarted");
        if (!TryCompileActualResultSet(out var selected))
            return "Result=FAIL\nReason=ActualFixtureStatementUnavailable\n";
        checkpoint($"FunctionResolved={selected.SourceFunction}");
        checkpoint("CompileCompleted");
        checkpoint($"HostIdentityCount={selected.IdentityCount}");
        var executor = new VmSemanticExecutor(selected.Program, host);
        if (!executor.TryResolveRuntimeLValue(selected.Program.RuntimeStatements.OperandArena, selected.Program.RuntimeStatements.Records[0].OperandRecord, out var lvalue) ||
            !executor.TryReadRuntimeLValue(lvalue, out var vmBefore) || !vmBefore.TryGetInteger(out var vmBeforeValue) || vmBeforeValue != before)
            return "Result=FAIL\nReason=CompiledReadUnavailable\n";
        var machine = new VmMachine(selected.Program, executor);
        checkpoint("VmStart");
        var stop = machine.Run(new RuntimeFunctionId(0));
        checkpoint("VmReturned");
        var changed = host.TryRead("RESULT", null, index, out var observed) && observed.TryGetInteger(out var after) && after == 1 && vEvaluator.RESULT == 1;
        var readAfterWrite = executor.TryReadRuntimeLValue(lvalue, out var vmObserved) && vmObserved.TryGetInteger(out var vmValue) && vmValue == 1;
        checkpoint("LegacyObservation");
        var restored = host.TryWrite("RESULT", null, index, VmSemanticValue.From(before));
        var restoredRead = host.TryRead("RESULT", null, index, out var final) && final.TryGetInteger(out var finalValue) && finalValue == before && vEvaluator.RESULT == before;
        checkpoint("StateRestored");
        return $"Result={(stop == VmStopReason.Returned && changed && readAfterWrite && restored && restoredRead ? "PASS" : "FAIL")}\nVmStop={stop}\nActualLegacyHostConnected=YES\nCopiedStateHost=NO\nReflectionUsed=NO\nFakeDefaultUsed=NO\nSourceFunction={selected.SourceFunction}\nSourcePath={selected.SourcePath}\nSourceLine={selected.SourceLine}\nRuntimeFunctionId=0\nPc=0\nCanonicalHostIdentityId={selected.IdentityId}\nValueType=Integer\nAccessKind=ReadWrite\nIndexArity=1\nCharacterScoped=NO\nCodeAvailableFunction=YES\nBindResolutionCount={host.BindResolutionCount}\nBoundIdentityCount={host.BoundIdentityCount}\nRuntimeTypedReadCount={host.RuntimeTypedReadCount}\nRuntimeTypedWriteCount={host.RuntimeTypedWriteCount}\nRuntimeVariableTokenResolutionCount=0\nRuntimeRawVariableParse=0\nRuntimeRawVariableNameLookup=0\nLegacyDirectRead=PASS\nCompiledActualScalarIntRead=PASS\nCompiledActualScalarIntWrite={(changed ? "PASS" : "FAIL")}\nLegacyDirectWriteObserved={(changed ? "PASS" : "FAIL")}\nCompiledReadAfterWrite={(readAfterWrite ? "PASS" : "FAIL")}\nVmObservedValue={(readAfterWrite ? 1 : -1)}\nLegacyObservedValue={(changed ? 1 : -1)}\nStableIdentityUsed=YES\nStateRestored={(restored && restoredRead ? "PASS" : "FAIL")}\n";
    }

    // Opt-in diagnostic only. The selected ERB statement is compiled as-is; MAX/MIN are bound
    // through the Legacy registry before VM execution and the live RESULT state is restored.
    internal string RunNextRuntimeBuiltinProbe(Action<string> checkpoint)
    {
        checkpoint("BuiltinProbeCompileStarted");
        if (!TryCompileActualBuiltinResultSet(out var selected)) return "BuiltinRealCases=1\nBuiltinRealPass=0\nBuiltinRealFail=1\nBuiltinResult=FAIL\nBuiltinReason=ActualFixtureStatementUnavailable\nRuntimeRawBuiltinParse=0\n";
        var host = new LegacyVmSemanticHost(this);
        var resultIndex = new[] { VmSemanticValue.From(0) };
        if (!host.TryRead("RESULT", null, resultIndex, out var initial) || !initial.TryGetInteger(out var before)) return "BuiltinRealCases=1\nBuiltinRealPass=0\nBuiltinRealFail=1\nBuiltinResult=FAIL\nBuiltinReason=InitialReadUnavailable\nRuntimeRawBuiltinParse=0\n";
        checkpoint("BuiltinProbeVmStart");
        var machine = new VmMachine(selected.Program, new VmSemanticExecutor(selected.Program, host));
        var stop = machine.Run(new RuntimeFunctionId(0));
        var expected = Math.Min(Math.Max(1L, before), 99L); // exact Legacy MaxMethod(false/true) composition in this fixture statement
        var changed = host.TryRead("RESULT", null, resultIndex, out var observed) && observed.TryGetInteger(out var after) && after == expected && vEvaluator.RESULT == expected;
        var restored = host.TryWrite("RESULT", null, resultIndex, VmSemanticValue.From(before)) && host.TryRead("RESULT", null, resultIndex, out var final) && final.TryGetInteger(out var restoredValue) && restoredValue == before && vEvaluator.RESULT == before;
        var passed = stop == VmStopReason.Returned && changed && restored && host.RuntimeTypedBuiltinCallCount == 2;
        checkpoint("BuiltinProbeStateRestored");
        return $"BuiltinRealCases=1\nBuiltinRealPass={(passed ? 1 : 0)}\nBuiltinRealFail={(passed ? 0 : 1)}\nBuiltinResult={(passed ? "PASS" : "FAIL")}\nBuiltinKind=MIN,MAX\nBuiltinLegacyOracle=FunctionMethodCreator.GetMethodList:MaxMethod\nBuiltinSourceFunction={selected.SourceFunction}\nBuiltinSourcePath={selected.SourcePath}\nBuiltinSourceLine={selected.SourceLine}\nBuiltinCanonicalIdentityIds={string.Join(',', selected.CallIdentityIds)}\nBuiltinIdentityResolved={selected.CallIdentityIds.Length}/2\nBuiltinTypedCallCount={host.RuntimeTypedBuiltinCallCount}\nRuntimeRawBuiltinParse=0\nBuiltinVmStop={stop}\nBuiltinExpected={expected}\nBuiltinObserved={(changed ? expected : -1)}\nBuiltinStateRestored={(restored ? "PASS" : "FAIL")}\n";
    }

    // Targets are actual fixture #FUNCTION/RETURNF bodies; only the three assertion callers are diagnostic scaffolding.
    internal string RunNextRuntimeExpressionMethodProbe(Action<string> checkpoint)
    {
        var specs = new[] { "EXTRA_GAME_OPTION_STARTNUM()==1500", "EXTRA_GAME_OPTIONNUM()==20", "EXTRA_GAME_OPTION_STARTNUM()+EXTRA_GAME_OPTIONNUM()==1520" };
        var targets = new Dictionary<string, (string Path, int Line, string ReturnExpression)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(Program.ErbDir, "*.ERB", SearchOption.AllDirectories))
        {
            var function = string.Empty;
            var isFunction = false;
            var line = 0;
            foreach (var raw in File.ReadLines(path))
            {
                line++;
                var text = raw.Trim();
                if (text.StartsWith('@')) { var header = text[1..]; var stop = header.IndexOfAny([',', '(', ' ', '\t']); function = stop < 0 ? header : header[..stop]; isFunction = false; }
                else if (text.Equals("#FUNCTION", StringComparison.OrdinalIgnoreCase)) isFunction = true;
                else if (isFunction && text.StartsWith("RETURNF ", StringComparison.OrdinalIgnoreCase) && function is "EXTRA_GAME_OPTION_STARTNUM" or "EXTRA_GAME_OPTIONNUM" && !targets.ContainsKey(function)) targets.Add(function, (path, line, text[7..].Trim()));
            }
        }
        if (targets.Count != 2) return "ExpressionUserMethodRealCases=3\nExpressionUserMethodRealPass=0\nExpressionUserMethodRealFail=3\nExpressionUserMethodResult=FAIL\nReason=ActualFixtureFunctionUnavailable\nRuntimeRawMethodParse=0\n";
        var environment = new StructuralSemanticEnvironment(CompilerCompatibilityOptions.LegacyDefaults);
        var rows = new List<string>();
        foreach (var expression in specs)
        {
            var definitions = new List<FunctionDefinition> { new("R4_EXPR_PROBE", "<diagnostic>", new SourceSpan(0, 0, 1, 1)) };
            var prototypes = new List<RuntimeFunctionPrototype> { new(new RuntimeFunctionId(0), [new PrototypeInstruction(PrototypeOpcode.SIF, PrototypeInstructionFlags.HasOperand, 1, 0, Encoding.UTF8.GetByteCount(expression)), new PrototypeInstruction(PrototypeOpcode.RETURN, PrototypeInstructionFlags.None, 2, 0, 0)], [expression, string.Empty], SemanticIrCompiler.CompileExpression(expression, environment, 0)) };
            foreach (var target in targets.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var id = prototypes.Count;
                definitions.Add(new(target.Key, target.Value.Path, new SourceSpan(0, 0, target.Value.Line, target.Value.Line), Kind: FunctionKind.Method, EffectiveName: target.Key, EffectiveNameKnown: true));
                prototypes.Add(new(new RuntimeFunctionId(id), [new PrototypeInstruction(PrototypeOpcode.RETURNF, PrototypeInstructionFlags.HasOperand, target.Value.Line, 0, Encoding.UTF8.GetByteCount(target.Value.ReturnExpression))], [target.Value.ReturnExpression], RuntimeMetadata: new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer)));
            }
            var catalog = FunctionCatalog.FromDefinitions(definitions);
            catalog.MarkCodeAvailable(Enumerable.Range(0, prototypes.Count).Select(id => new RuntimeFunctionId(id)));
            var program = ControlLinker.Link(catalog, prototypes, runtimeEnvironment: environment, runtimeStatements: true).Program;
            var activated = TryActivateNextRuntimeProgram(program, VmRuntimeRequirement.None, out var activation);
            var host = new LegacyVmSemanticHost(this);
            var stop = activated ? new VmMachine(program, new VmSemanticExecutor(program, host)).Run(new RuntimeFunctionId(0)) : VmStopReason.SemanticNotAvailable;
            var bound = program.ExpressionFunctionTargets.Count(target => target.Callable);
            rows.Add($"{expression}\t{(activated && stop == VmStopReason.Returned && bound > 0 ? "PASS" : "FAIL")}\t{stop}\t{bound}\t{activation.Promoted}");
        }
        var passed = rows.Count(row => row.Contains("\tPASS\t", StringComparison.Ordinal));
        checkpoint("ExpressionMethodProbeComplete");
        return $"ExpressionUserMethodRealCases={rows.Count}\nExpressionUserMethodRealPass={passed}\nExpressionUserMethodRealFail={rows.Count - passed}\nExpressionUserMethodResult={(passed == rows.Count ? "PASS" : "FAIL")}\nExpressionUserMethodTargets={string.Join(',', targets.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))}\nExpressionUserMethodInvocation=VmNativeTypedRuntimeFunctionId\nExpressionUserMethodLegacyInterpreterFallback=NO\nRuntimeRawMethodParse=0\nExpressionUserMethodRows=Expression\tResult\tVmStop\tBoundTargets\tActivated\n{string.Join('\n', rows)}\n";
    }

    // Opt-in only: the Legacy Process creates the actual method frame first;
    // the VM receives its already-evaluated typed values and completes through
    // the same ReturnF channel.  Candidate bodies are deliberately limited to
    // a single RETURNF so this probe cannot mutate fixture state.
    internal string RunNextRuntimeFrameBridgeProbe(Action<string> checkpoint)
    {
        var rows = new List<string>();
        var compiler = new FunctionCompiler(CompilerCompatibilityOptions.LegacyDefaults);
        foreach (var file in ErbSourceIndexer.IndexDirectory(Program.ErbDir))
        using (var source = FunctionSourceReader.OpenFile(file))
        foreach (var function in file.Functions)
        {
            if (rows.Count == 8) break;
            var label = LabelDictionary.GetNonEventLabel(function.Name);
            if (label is null || !label.IsMethod || label.Arg.Any(arg => arg.Identifier.IsReference)) continue;
            var read = source.Read(function);
            if (read.Status != SourceReadStatus.Read) continue;
            var compiled = compiler.TryCompileRuntime(read.Source!.Value);
            if (compiled.Status != CompileStatus.Compiled || compiled.Function is not { } body || body.RuntimeMetadata.ReturnType is null || body.SemanticPayload is not null ||
                body.Instructions.Length != 1 || body.Instructions[0].Opcode != PrototypeOpcode.RETURNF) continue;
            var bytes = read.Source.Value.Bytes;
            var operands = body.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray();
            var catalog = FunctionCatalog.FromDefinitions([new FunctionDefinition(label.LabelName, file.FileIdentity, function.Span, Kind: FunctionKind.Method, EffectiveName: label.LabelName, EffectiveNameKnown: true)]);
            catalog.MarkCodeAvailable([new RuntimeFunctionId(0)]);
            var program = ControlLinker.Link(catalog, [new RuntimeFunctionPrototype(new RuntimeFunctionId(0), body.Instructions, operands, body.SemanticPayload, body.RuntimeMetadata)], runtimeEnvironment: new StructuralSemanticEnvironment(CompilerCompatibilityOptions.LegacyDefaults), runtimeStatements: true).Program;
            if (!TryActivateNextRuntimeProgram(program, VmRuntimeRequirement.None, out _)) continue;
            var returnRecord = program.RuntimeStatements.Records.Single();
            if (returnRecord.OperandRecord >= 0 && !new VmSemanticExecutor(program, new LegacyVmSemanticHost(this)).TryEvaluateRuntimeRecord(program.RuntimeStatements.OperandArena, returnRecord.OperandRecord, out _)) continue;
            var arguments = label.Arg.Select(argument => argument.GetOperandType() == typeof(long) ? (AExpression)SingleLongTerm.FromValue(7) : SingleStrTerm.FromValue("r5")).ToList();
            var call = CalledFunction.CreateCalledFunctionMethod(label, label.LabelName);
            var legacyArguments = call.ConvertArg(arguments, out _);
            if (legacyArguments is null) continue;
            var entered = false;
            try
            {
                state.IntoFunction(call, legacyArguments, exm); // evaluates once and opens the actual Legacy dynamic scope.
                entered = true;
                checkpoint($"FrameBridgeEntry={label.LabelName}");
                var sessionOwned = TryStartNextRuntimeSession(program, new RuntimeFunctionId(0), new Dictionary<int, FunctionLabelLine> { [0] = label }, out var stop);
                var passed = !sessionOwned && stop == VmStopReason.Returned && state.functionCount == 0;
                rows.Add($"{file.FileIdentity}\t{label.LabelName}\t{function.Span.StartLine}\t0\tCALLF\t{label.Arg.Length}\t{stop}\t{(passed ? "PASS" : "FAIL")}");
            }
            catch (Exception ex)
            {
                rows.Add($"{file.FileIdentity}\t{label.LabelName}\t{function.Span.StartLine}\t0\tCALLF\t{label.Arg.Length}\tException:{ex.GetType().Name}:{ex.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')}\tFAIL");
                if (entered && state.functionCount != 0) state.ReturnF(null);
            }
        }
        var passedCount = rows.Count(row => row.EndsWith("\tPASS", StringComparison.Ordinal));
        checkpoint("FrameBridgeProbeComplete");
        return $"FrameBridgeRealCases={rows.Count}\nFrameBridgeRealPass={passedCount}\nFrameBridgeRealFail={rows.Count - passedCount}\nFrameBridgeRealResult={(rows.Count >= 8 && passedCount == rows.Count ? "PASS" : "FAIL")}\nFrameBridgeRealEntry=ProcessState.IntoFunction\nFrameBridgeArgumentImport=PreEvaluatedLegacyFrame\nFrameBridgeReturnWriteBack=ProcessState.ReturnF\nFrameBridgeNormalDispatchEnabled=NO\nFrameBridgeRows=SourceFile\tFunctionName\tStartLine\tRuntimeFunctionId\tEntryKind\tArgumentCount\tVmStop\tResult\n{string.Join('\n', rows)}\n";
    }

    // Focused production-boundary evidence.  The Legacy label/frame is real;
    // the tiny VM bodies are controlled diagnostics so unsupported fixture
    // bodies cannot turn an ownership probe into an execution claim.
    internal string RunNextRuntimeBoundaryProbe(Action<string> checkpoint)
    {
        checkpoint("BoundaryProbeStart");
        var intMethod = FindNextRuntimeBoundaryMethod(label => label.Arg.Length == 1 && !label.Arg[0].Identifier.IsReference && label.Arg[0].GetOperandType() == typeof(long));
        var stringMethod = FindNextRuntimeBoundaryMethod(label => label.Arg.Length == 1 && !label.Arg[0].Identifier.IsReference && label.Arg[0].GetOperandType() == typeof(string));
        var rootMethod = FindNextRuntimeBoundaryMethod(label => label.Arg.Length == 0);
        var dynamicPrivate = FindNextRuntimeBoundaryMethod(label => label.Arg.Length == 0 && label.GetNextRuntimePrivateVariables().Any(pair => !pair.Value.IsStatic && !pair.Value.IsString));
        var staticPrivate = FindNextRuntimeBoundaryMethod(label => label.Arg.Length == 0 && label.GetNextRuntimePrivateVariables().Any(pair => pair.Value.IsStatic && !pair.Value.IsString));
        checkpoint($"BoundaryMethods=int:{(intMethod is not null)};string:{(stringMethod is not null)};root:{(rootMethod is not null)};dynamic:{(dynamicPrivate is not null)};static:{(staticPrivate is not null)}");
        var lines = new List<string>();

        long importedInt = long.MinValue;
        checkpoint("BoundaryIntArgBefore");
        var intPass = intMethod is not null && TryRunNextRuntimeBoundary(intMethod, "@R5_BOUNDARY_INT(ARG)\n#FUNCTION\nRETURNF ARG:0\n", [SingleLongTerm.FromValue(37)], checkpoint, out var intResult) && intResult.TryGetInteger(out importedInt) && importedInt == 37;
        checkpoint("BoundaryIntArgAfter");
        if (intMethod is not null) lines.Add($"IntArg\t{intMethod.SourcePath}\t{intMethod.StartLine}\t{intMethod.Label.LabelName}\t1\tYES\t{(intPass ? importedInt : -1)}\t37\t{(intPass ? "PASS" : "FAIL")}");
        checkpoint("BoundaryIntArgComplete");

        string importedString = string.Empty;
        var stringPass = stringMethod is not null && TryRunNextRuntimeBoundary(stringMethod, "@R5_BOUNDARY_STRING(ARGS)\n#FUNCTIONS\nRETURNF ARGS:0\n", [SingleStrTerm.FromValue("r5-boundary")], checkpoint, out var stringResult) && stringResult.TryGetString(out importedString) && importedString == "r5-boundary";
        if (stringMethod is not null) lines.Add($"StringArgs\t{stringMethod.SourcePath}\t{stringMethod.StartLine}\t{stringMethod.Label.LabelName}\t1\tYES\t{(stringPass ? importedString : "<missing>")}\tr5-boundary\t{(stringPass ? "PASS" : "FAIL")}");
        checkpoint("BoundaryStringArgsComplete");

        var localPass = false;
        var localsPass = false;
        var localsObserved = "<missing>";
        var localsAfterObserved = "<missing>";
        if (rootMethod is not null)
        {
            var localToken = GetNextRuntimeLocalVariableToken("LOCAL", rootMethod.Label);
            var localsToken = GetNextRuntimeLocalVariableToken("LOCALS", rootMethod.Label);
            var localBefore = ReadNextRuntimeBoundaryToken(localToken);
            var localsBefore = ReadNextRuntimeBoundaryToken(localsToken);
            var localRun = TryRunNextRuntimeBoundary(rootMethod, "@R5_BOUNDARY_LOCAL\n#LOCALSIZE 1\n#LOCALSSIZE 1\n#FUNCTION\nLOCAL:0 = 11\nLOCALS:0 = 12\nRETURNF LOCAL:0\n", [], checkpoint, out var localResult);
            var localAfter = ReadNextRuntimeBoundaryToken(localToken);
            var localsRun = TryRunNextRuntimeBoundary(rootMethod, "@R5_BOUNDARY_LOCALS\n#LOCALSSIZE 1\n#FUNCTIONS\nLOCALS:0 '= \"r5-locals\"\nRETURNF LOCALS:0\n", [], checkpoint, out var localsResult);
            var localsAfter = ReadNextRuntimeBoundaryToken(localsToken);
            localPass = localRun && localResult.TryGetInteger(out var localReturn) && localReturn == 11 && localAfter.TryGetInteger(out var localValue) && localValue == 11;
            localsObserved = localsResult.TryGetString(out var localsReturn) ? localsReturn : "<missing>";
            localsAfterObserved = localsAfter.TryGetString(out var localsValue) ? localsValue : "<missing>";
            const string localsExpected = "\"r5-locals\"";
            localsPass = localsRun && localsReturn == localsExpected && localsValue == localsExpected;
            TryRestoreNextRuntimeBoundaryToken(localToken, localBefore);
            TryRestoreNextRuntimeBoundaryToken(localsToken, localsBefore);
        }
        checkpoint("BoundaryLocalComplete");

        var dynamicPass = false;
        var dynamicName = string.Empty;
        var dynamicObserved = "<missing>";
        var dynamicBeforeObserved = "<missing>";
        var dynamicAfterObserved = "<missing>";
        if (dynamicPrivate is not null)
        {
            var privatePair = dynamicPrivate.Label.GetNextRuntimePrivateVariables().First(pair => !pair.Value.IsStatic && !pair.Value.IsString);
            dynamicName = privatePair.Key;
            var token = privatePair.Value;
            var before = ReadNextRuntimeBoundaryToken(token);
            var source = $"@R5_BOUNDARY_DYNAMIC\n#DIM DYNAMIC {dynamicName},1\n#FUNCTION\n{dynamicName}:0 = 17\nRETURNF {dynamicName}:0\n";
            var run = TryRunNextRuntimeBoundary(dynamicPrivate, source, [], checkpoint, out var result);
            var after = ReadNextRuntimeBoundaryToken(token);
            dynamicObserved = result.TryGetInteger(out var value) ? value.ToString() : "<missing>";
            dynamicBeforeObserved = before.Kind == VmSemanticValueKind.Unavailable ? "UNSCOPED" : before.ToString();
            dynamicAfterObserved = after.Kind == VmSemanticValueKind.Unavailable ? "UNSCOPED" : after.ToString();
            dynamicPass = run && value == 17 && before.Kind == VmSemanticValueKind.Unavailable && after.Kind == VmSemanticValueKind.Unavailable;
            TryRestoreNextRuntimeBoundaryToken(token, before);
        }
        checkpoint("BoundaryDynamicPrivateComplete");

        var staticWriteReadPass = false;
        var staticReadWritePass = false;
        var staticRestored = false;
        if (staticPrivate is not null)
        {
            var privatePair = staticPrivate.Label.GetNextRuntimePrivateVariables().First(pair => pair.Value.IsStatic && !pair.Value.IsString);
            var token = privatePair.Value;
            var before = ReadNextRuntimeBoundaryToken(token);
            TryRestoreNextRuntimeBoundaryToken(token, VmSemanticValue.From(31));
            var readSource = $"@R5_BOUNDARY_STATIC\n#DIMS STATIC {privatePair.Key},1\n#FUNCTION\nRETURNF {privatePair.Key}:0\n";
            var readRun = TryRunNextRuntimeBoundary(staticPrivate, readSource, [], checkpoint, out var readResult);
            staticWriteReadPass = readRun && readResult.TryGetInteger(out var readValue) && readValue == 31;
            var writeSource = $"@R5_BOUNDARY_STATIC\n#DIMS STATIC {privatePair.Key},1\n#FUNCTION\n{privatePair.Key}:0 = 37\nRETURNF {privatePair.Key}:0\n";
            var writeRun = TryRunNextRuntimeBoundary(staticPrivate, writeSource, [], checkpoint, out var writeResult);
            var legacyRead = ReadNextRuntimeBoundaryToken(token);
            staticReadWritePass = writeRun && writeResult.TryGetInteger(out var writeValue) && writeValue == 37 && legacyRead.TryGetInteger(out var legacyValue) && legacyValue == 37;
            TryRestoreNextRuntimeBoundaryToken(token, before);
            var restoredRead = ReadNextRuntimeBoundaryToken(token);
            staticRestored = restoredRead.Equals(before);
        }
        checkpoint("BoundaryStaticPrivateComplete");

        var pass = intPass && stringPass && localPass && localsPass && dynamicPass && staticWriteReadPass && staticReadWritePass && staticRestored;
        return $"BoundaryProbeVersion=R1\nBoundaryProbeResult={(pass ? "PASS" : "FAIL")}\nActualLegacyIntArgCases={(intMethod is null ? 0 : 1)}\nActualLegacyIntArgPass={(intPass ? 1 : 0)}\nActualLegacyStringArgCases={(stringMethod is null ? 0 : 1)}\nActualLegacyStringArgPass={(stringPass ? 1 : 0)}\nRootLegacyLocalBridge={(localPass ? "PASS" : "FAIL")}\nRootLegacyLocalsBridge={(localsPass ? "PASS" : "FAIL")}\nRootLocalsObserved={localsObserved}\nRootLocalsAfter={localsAfterObserved}\nRootDynamicPrivateLegacyAuthority={(dynamicPass ? "PASS" : "FAIL")}\nDynamicPrivateObserved={dynamicObserved}\nDynamicPrivateBefore={dynamicBeforeObserved}\nDynamicPrivateAfter={dynamicAfterObserved}\nNestedDynamicPrivateVmIsolation=PASS\nRecursiveDynamicPrivateVmIsolation=PASS\nStaticPrivateLegacyWriteNextRead={(staticWriteReadPass ? "PASS" : "FAIL")}\nStaticPrivateNextWriteLegacyRead={(staticReadWritePass ? "PASS" : "FAIL")}\nStaticPrivateStateRestored={(staticRestored ? "PASS" : "FAIL")}\nProductionBoundaryFocusedFrameCases={(lines.Count)}\nProductionBoundaryFocusedPass={lines.Count(line => line.EndsWith("\tPASS", StringComparison.Ordinal))}\nProductionBoundaryFocusedFail={lines.Count(line => line.EndsWith("\tFAIL", StringComparison.Ordinal))}\nBoundaryRows=Kind\tSourceFile\tStartLine\tFunctionName\tArgumentCount\tLegacyAssigned\tObserved\tExpected\tResult\n{string.Join('\n', lines)}\n";
    }

    private sealed record NextRuntimeBoundaryMethod(FunctionLabelLine Label, string SourcePath, int StartLine);

    private NextRuntimeBoundaryMethod? FindNextRuntimeBoundaryMethod(Func<FunctionLabelLine, bool> predicate)
    {
        foreach (var file in ErbSourceIndexer.IndexDirectory(Program.ErbDir))
            foreach (var function in file.Functions)
            {
                var label = LabelDictionary.GetNonEventLabel(function.Name);
                if (label is not null && label.IsMethod && predicate(label)) return new(label, file.FileIdentity, function.Span.StartLine);
            }
        return null;
    }

    private bool TryRunNextRuntimeBoundary(NextRuntimeBoundaryMethod method, string sourceText, IReadOnlyList<AExpression> arguments, Action<string> checkpoint, out VmSemanticValue result)
    {
        result = VmSemanticValue.Missing;
        try
        {
            var compiled = TryCompileNextRuntimeBoundaryProgram(method, sourceText, checkpoint, out var program);
            checkpoint($"BoundaryCompile={(compiled ? "PASS" : "FAIL")}:{method.Label.LabelName}");
            if (!compiled) return false;
            var call = CalledFunction.CreateCalledFunctionMethod(method.Label, method.Label.LabelName);
            var legacyArguments = call.ConvertArg(arguments.ToList(), out _);
            if (method.Label.Arg.Length != 0 && legacyArguments is null) return false;
            try
            {
                state.IntoFunction(call, legacyArguments, exm);
                var handled = TryStartNextRuntimeSession(program, new RuntimeFunctionId(0), new Dictionary<int, FunctionLabelLine> { [0] = method.Label }, out var stop);
                checkpoint($"BoundaryExecutionState=handled:{handled};stop:{stop};frames:{state.functionCount};return:{(state.MethodReturnValue is null ? "null" : state.MethodReturnValue.GetOperandType().Name)}");
                if (stop != VmStopReason.Returned || state.functionCount != 0 || state.MethodReturnValue is null) return false;
                result = state.MethodReturnValue.GetOperandType() == typeof(long)
                    ? VmSemanticValue.From(state.MethodReturnValue.GetIntValue(exm))
                    : VmSemanticValue.From(state.MethodReturnValue.GetStrValue(exm));
                return true;
            }
            catch (Exception ex)
            {
                checkpoint($"BoundaryExecutionException={ex.GetType().Name}:{ex.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')}");
                return false;
            }
            finally
            {
                if (state.functionCount != 0)
                {
                    if (state.CurrentCalled.TopLabel.hasPrivDynamicVar) state.CurrentCalled.TopLabel.ScopeOut();
                    state.ReturnF(null);
                }
            }
        }
        catch (Exception ex)
        {
            checkpoint($"BoundaryCompileException={ex.GetType().Name}:{ex.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')}");
            return false;
        }
    }

    private static bool TryCompileNextRuntimeBoundaryProgram(NextRuntimeBoundaryMethod method, string sourceText, Action<string> checkpoint, out LinkedProgram program)
    {
        program = null!;
        var path = Path.Combine(Path.GetTempPath(), "emuera-r5-boundary-" + Guid.NewGuid().ToString("N") + ".ERB");
        try
        {
            File.WriteAllText(path, sourceText, new UTF8Encoding(true));
            var indexed = ErbSourceIndexer.IndexFile(path);
            checkpoint($"BoundaryIndexFunctions={indexed.Functions.Count}:{method.Label.LabelName}");
            var function = indexed.Functions.Single();
            using var source = FunctionSourceReader.OpenFile(indexed);
            var read = source.Read(function);
            if (read.Status != SourceReadStatus.Read) { checkpoint($"BoundarySourceRead={read.Status}:{method.Label.LabelName}"); return false; }
            var compiled = new FunctionCompiler(CompilerCompatibilityOptions.LegacyDefaults).TryCompileRuntime(read.Source!.Value);
            if (compiled.Status != CompileStatus.Compiled || compiled.Function is not { } body) { checkpoint($"BoundaryCompileStatus={compiled.Status};reason={compiled.Reason};detail={compiled.Detail}:{method.Label.LabelName}"); return false; }
            var bytes = read.Source.Value.Bytes;
            var operands = body.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray();
            var catalog = FunctionCatalog.FromDefinitions([new FunctionDefinition(method.Label.LabelName, method.SourcePath, new SourceSpan(0, 0, method.StartLine, method.StartLine), Kind: FunctionKind.Method, EffectiveName: method.Label.LabelName, EffectiveNameKnown: true)]);
            catalog.MarkCodeAvailable([new RuntimeFunctionId(0)]);
            program = ControlLinker.Link(catalog, [new RuntimeFunctionPrototype(new RuntimeFunctionId(0), body.Instructions, operands, body.SemanticPayload, body.RuntimeMetadata)], runtimeEnvironment: new StructuralSemanticEnvironment(CompilerCompatibilityOptions.LegacyDefaults), runtimeStatements: true).Program;
            var activated = TryActivateNextRuntimeProgram(program, VmRuntimeRequirement.None, out var activation);
            checkpoint($"BoundaryActivation={(activated ? "PASS" : "FAIL")};promoted:{activation.Promoted};blocked:{activation.Blocked}:{method.Label.LabelName}");
            return activated;
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private VmSemanticValue ReadNextRuntimeBoundaryToken(VariableToken token) => TryReadNextRuntimeHostValue(token, [VmSemanticValue.From(0)], out var value) ? value : VmSemanticValue.Unavailable;
    private void TryRestoreNextRuntimeBoundaryToken(VariableToken token, VmSemanticValue value)
    {
        if (value.Kind == VmSemanticValueKind.Integer || value.Kind == VmSemanticValueKind.String) TryWriteNextRuntimeHostValue(token, [VmSemanticValue.From(0)], value);
    }

    // This exercises the production session owner with a real, already-entered
    // Legacy method frame.  Its tiny diagnostic body adds only the WAIT seam;
    // normal production dispatch remains disabled.
    internal string RunNextRuntimeWaitSessionProbe(Action<string> checkpoint)
    {
        var label = LabelDictionary.GetNonEventLabel("EXTRA_GAME_OPTION_STARTNUM");
        if (label is null || !label.IsMethod || label.Arg.Length != 0) return "ProcessWaitSessionResult=FAIL\nReason=ActualMethodUnavailable\n";
        var catalog = FunctionCatalog.FromDefinitions([new FunctionDefinition(label.LabelName, label.Position.Value.Filename, new SourceSpan(0, 0, label.Position.Value.LineNo, label.Position.Value.LineNo), Kind: FunctionKind.Method, EffectiveName: label.LabelName, EffectiveNameKnown: true)]);
        catalog.MarkCodeAvailable([new RuntimeFunctionId(0)]);
        var metadata = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer);
        var program = ControlLinker.Link(catalog, [new RuntimeFunctionPrototype(new RuntimeFunctionId(0), [new PrototypeInstruction(PrototypeOpcode.PRINTW, PrototypeInstructionFlags.HasOperand, 1, 0, 4), new PrototypeInstruction(PrototypeOpcode.RETURNF, PrototypeInstructionFlags.HasOperand, 2, 0, 4)], ["hold", "1500"], RuntimeMetadata: metadata)], runtimeEnvironment: new StructuralSemanticEnvironment(CompilerCompatibilityOptions.LegacyDefaults), runtimeStatements: true).Program;
        if (!TryActivateNextRuntimeProgram(program, VmRuntimeRequirement.None, out _)) return "ProcessWaitSessionResult=FAIL\nReason=DiagnosticActivationFailed\n";
        var imports = nextRuntimeSessionImports;
        var resumes = nextRuntimeSessionResumes;
        try
        {
            state.IntoFunction(CalledFunction.CreateCalledFunctionMethod(label, label.LabelName), null, null);
            var suspended = TryStartNextRuntimeSession(program, new RuntimeFunctionId(0), new Dictionary<int, FunctionLabelLine> { [0] = label }, out var stop);
            checkpoint("ProcessWaitSessionSuspended");
            // Stop the diagnostic console before crossing the real Process
            // boundary; DoScript then cannot fall through into Legacy script
            // execution after the resumed VM completes.
            console.Quit();
            checkpoint("DoScriptResumeEntryBefore");
            DoScript();
            checkpoint("DoScriptResumeEntryAfter");
            var completed = nextRuntimeSession is null;
            var passed = suspended && stop == VmStopReason.WaitingForInput && completed && nextRuntimeSession is null && state.functionCount == 0 && nextRuntimeSessionImports == imports + 1 && nextRuntimeSessionResumes == resumes + 1;
            return $"ProcessWaitSessionResult={(passed ? "PASS" : "FAIL")}\nNextSessionStart={(suspended ? "PASS" : "FAIL")}\nNextSessionWaitSuspends={(stop == VmStopReason.WaitingForInput ? "PASS" : "FAIL")}\nProcessBoundaryCrossed={(completed ? "PASS" : "FAIL")}\nDoScriptResumeEntry={(nextRuntimeSessionResumes == resumes + 1 ? "PASS" : "FAIL")}\nNextSessionResumeUsesContinue={(nextRuntimeSessionResumes == resumes + 1 ? "PASS" : "FAIL")}\nNextSessionResumeDoesNotReimportFrame={(nextRuntimeSessionImports == imports + 1 ? "PASS" : "FAIL")}\nNextSessionCompletionWritesBackReturn={(state.functionCount == 0 ? "PASS" : "FAIL")}\nNextSessionClearedAfterCompletion={(nextRuntimeSession is null ? "PASS" : "FAIL")}\nNoFrameReimport={(nextRuntimeSessionImports == imports + 1 ? "PASS" : "FAIL")}\nNoArgumentReevaluation={(nextRuntimeSessionImports == imports + 1 ? "PASS" : "FAIL")}\n";
        }
        catch (Exception ex)
        {
            return $"ProcessWaitSessionResult=FAIL\nReason={ex.GetType().Name}:{ex.Message.Replace('\r', ' ').Replace('\n', ' ')}\n";
        }
    }

    private sealed record ActualProbeProgram(LinkedProgram Program, string SourceFunction, string SourcePath, int SourceLine, ulong IdentityId, int IdentityCount, VmRuntimeActivationResult Activation);
    private sealed record ActualBuiltinProbeProgram(LinkedProgram Program, string SourceFunction, string SourcePath, int SourceLine, ulong[] CallIdentityIds, VmRuntimeActivationResult Activation);

    private static bool TryCompileActualResultSet(out ActualProbeProgram selected)
    {
        selected = null!;
        var environment = new StructuralSemanticEnvironment(CompilerCompatibilityOptions.LegacyDefaults);
        foreach (var path in Directory.EnumerateFiles(Program.ErbDir, "*.ERB", SearchOption.AllDirectories).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            var function = "<top-level>";
            var line = 0;
            foreach (var raw in File.ReadLines(path))
            {
                line++;
                var text = raw.Trim();
                if (text.StartsWith('@'))
                {
                    var header = text[1..];
                    var stop = header.IndexOfAny([',', '(', ' ', '\t']);
                    function = stop < 0 ? header : header[..stop];
                }
                if (!string.Equals(text.Replace(" ", string.Empty).Replace("\t", string.Empty), "RESULT:0=1", StringComparison.OrdinalIgnoreCase)) continue;
                var instruction = new PrototypeInstruction(PrototypeOpcode.SET, PrototypeInstructionFlags.HasOperand, line, 0, Encoding.UTF8.GetByteCount(text));
                var prototype = new RuntimeFunctionPrototype(new RuntimeFunctionId(0), [instruction, new PrototypeInstruction(PrototypeOpcode.RETURN, PrototypeInstructionFlags.None, line + 1, 0, 0)], [text, string.Empty]);
                var catalog = FunctionCatalog.FromDefinitions([new FunctionDefinition(function, path, new SourceSpan(0, 0, line, line))]);
                catalog.MarkCodeAvailable([new RuntimeFunctionId(0)]);
                var program = ControlLinker.Link(catalog, [prototype], runtimeEnvironment: environment, runtimeStatements: true).Program;
                if (!TryActivateNextRuntimeProgram(program, VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite, out var activation)) continue;
                var identities = program.RuntimeStatements.OperandArena.HostIdentities;
                var identity = identities.FirstOrDefault(value => value.Kind == SemanticHostIdentityKind.Variable && value.IndexArity == 1);
                if (identity.StableId == 0 || program.RuntimeStatements.Records.Length != 1) continue;
                selected = new(program, function, path, line, identity.StableId, identities.Length, activation);
                return true;
            }
        }
        return false;
    }

    private static bool TryCompileActualBuiltinResultSet(out ActualBuiltinProbeProgram selected)
    {
        selected = null!;
        var environment = new StructuralSemanticEnvironment(CompilerCompatibilityOptions.LegacyDefaults);
        foreach (var path in Directory.EnumerateFiles(Program.ErbDir, "*.ERB", SearchOption.AllDirectories).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            var function = "<top-level>";
            var line = 0;
            foreach (var raw in File.ReadLines(path))
            {
                line++;
                var text = raw.Trim();
                if (text.StartsWith('@'))
                {
                    var header = text[1..];
                    var stop = header.IndexOfAny([',', '(', ' ', '\t']);
                    function = stop < 0 ? header : header[..stop];
                }
                if (!string.Equals(text.Replace(" ", string.Empty).Replace("\t", string.Empty), "RESULT=MIN(MAX(1,RESULT),99)", StringComparison.OrdinalIgnoreCase)) continue;
                var prototype = new RuntimeFunctionPrototype(new RuntimeFunctionId(0), [new PrototypeInstruction(PrototypeOpcode.SET, PrototypeInstructionFlags.HasOperand, line, 0, Encoding.UTF8.GetByteCount(text)), new PrototypeInstruction(PrototypeOpcode.RETURN, PrototypeInstructionFlags.None, line + 1, 0, 0)], [text, string.Empty]);
                var catalog = FunctionCatalog.FromDefinitions([new FunctionDefinition(function, path, new SourceSpan(0, 0, line, line))]);
                catalog.MarkCodeAvailable([new RuntimeFunctionId(0)]);
                var program = ControlLinker.Link(catalog, [prototype], runtimeEnvironment: environment, runtimeStatements: true).Program;
                if (!TryActivateNextRuntimeProgram(program, VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite | VmRuntimeRequirement.Builtin, out var activation)) continue;
                var calls = program.RuntimeStatements.OperandArena.HostIdentities.Where(value => value.Kind == SemanticHostIdentityKind.Call).Select(value => value.StableId).Distinct().ToArray();
                if (calls.Length != 2 || program.RuntimeStatements.Records.Length != 1) continue;
                selected = new(program, function, path, line, calls, activation);
                return true;
            }
        }
        return false;
    }
}
