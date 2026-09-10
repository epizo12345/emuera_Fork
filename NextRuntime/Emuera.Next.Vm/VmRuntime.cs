using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Vm;

// [Emuera改修:NEXT-2A-R3 2026-08-28]
// SourceIndexのphysical definitionとLegacy runtime semantic definitionは、
// preprocessor disabled / line-continuationにより一般に一対一ではない。
// そのため、CALL/JUMP/VM frameはruntime semantic identityを使用し、
// SourceFunctionIdをRuntimeFunctionIdとして暗黙利用しない。
// Linked hot data is value-oriented. Legacy parser/runtime objects stay outside.
public enum FunctionKind : byte { Normal, Event, Method }
public enum VmFunctionState : int
{
    CodeNotAvailable, LinkReady, LinkedSemanticPending, UnsupportedControl,
    InvalidStructure, ExecutableReady, Ready = ExecutableReady,
}
public enum VmStopReason : byte { Halted, Returned, WaitingForInput, QuitRequested, CodeNotAvailable, SemanticNotAvailable, SemanticEvaluationFault, UnsupportedControl, InvalidStructure, InvalidFunctionId, InvalidLocalPc, StackUnderflow, StepLimit, TerminalFault }
public enum VmReturnKind : int { Normal, Propagate, Expression }
public enum VmInvocationKind : byte { Call, Jump }
public enum VmDynamicCallKind : byte { CallForm, TryCallForm, TryCCallForm, CallFormMethod }
public enum VmDynamicResolutionKind : byte { Ready, Missing, Unresolved, Blocked, WrongKind }
public readonly record struct VmDynamicCallResolution(VmDynamicResolutionKind Kind, RuntimeFunctionId Target);
public interface IVmDynamicCallResolver
{
    VmDynamicCallResolution Resolve(string effectiveName, bool method);
}
public enum VmStructuralKind : int { None, Sif, If, ElseIf, Else, EndIf, SelectCase, Case, CaseElse, EndSelect, Repeat, Rend, For, Next, While, Wend, Do, Loop, Break, Continue }
[Flags] public enum LoopDescriptorFlags : int { None = 0, BreakAdvancesCounter = 1 }

// [Emuera改修:NEXT-3D-R1.4B 2026-09-04]
// LegacyのCalledFunction invocationごとに1個だけ生成し、entry seamで一度だけconsumeする。
// VMはtokenを再生成せず、WAIT resumeでも同じinvocationを再dispatchしない。ここを緩めると
// Next Completed後にLegacy bodyが重複実行され、returnや状態遷移の順序が壊れる。
public sealed class VmRuntimeEntryDispatchToken
{
    private bool pending = true;
    public bool Pending => pending;
    public bool TryConsume()
    {
        if (!pending) return false;
        pending = false;
        return true;
    }
    public VmRuntimeEntryDispatchToken Clone() => new() { pending = pending };
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VmInstruction
{
    public readonly ushort Opcode;
    public readonly ushort Flags;
    public readonly int OperandOffset;
    public readonly int OperandLength;
    public readonly int Aux;
    public VmInstruction(ushort opcode, ushort flags = 0, int operandOffset = 0, int operandLength = 0, int aux = 0)
        => (Opcode, Flags, OperandOffset, OperandLength, Aux) = (opcode, flags, operandOffset, operandLength, aux);
    public static VmInstruction Nop => new((ushort)VmOpcode.Nop);
    public static VmInstruction Halt => new((ushort)VmOpcode.Halt);
    public static VmInstruction Branch(int target) => new((ushort)VmOpcode.Branch, aux: target);
    public static VmInstruction Call(RuntimeFunctionId functionId) => new((ushort)VmOpcode.Call, aux: functionId.Value);
    public static VmInstruction Jump(RuntimeFunctionId functionId) => new((ushort)VmOpcode.Jump, aux: functionId.Value);
    public static VmInstruction Return => new((ushort)VmOpcode.Return);
    public static VmInstruction Statement(int statementIndex) => new((ushort)VmOpcode.Statement, aux: statementIndex);
    public static VmInstruction DynamicCall(int siteIndex) => new((ushort)VmOpcode.DynamicCall, aux: siteIndex);
}
public enum VmOpcode : ushort { Nop, Halt, Branch, Call, Jump, Return, Structural, Statement, DynamicCall, SemanticBarrier, UnsupportedControl }

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VmFunctionDescriptor
{
    public readonly int FunctionId, CodeStart, CodeLength;
    public readonly VmFunctionState State;
    public VmFunctionDescriptor(int functionId, int codeStart, int codeLength, VmFunctionState state) => (FunctionId, CodeStart, CodeLength, State) = (functionId, codeStart, codeLength, state);
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VmFrame
{
    public readonly int FunctionId, Pc;
    public readonly VmReturnKind ReturnKind;
    public VmFrame(int functionId, int pc, VmReturnKind returnKind) => (FunctionId, Pc, ReturnKind) = (functionId, pc, returnKind);
}

// *Pc fields contain only function-local PCs. GroupIndex is program-global within the IF/SELECT table selected by Kind; LoopIndex is program-global in Loops.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct StructuralLinkRecord
{
    public int FunctionId, Pc, TargetPc, AuxiliaryPc, Depth, GroupIndex, LoopIndex;
    public VmStructuralKind Kind;
    public StructuralLinkRecord(int functionId, int pc, int targetPc, int auxiliaryPc, VmStructuralKind kind, int depth, int groupIndex = -1, int loopIndex = -1)
        => (FunctionId, Pc, TargetPc, AuxiliaryPc, Depth, GroupIndex, LoopIndex, Kind) = (functionId, pc, targetPc, auxiliaryPc, depth, groupIndex, loopIndex, kind);
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct SifLinkRecord
{
    public readonly int FunctionId, Pc, FallthroughPc, FalsePc;
    public SifLinkRecord(int functionId, int pc, int fallthroughPc, int falsePc) => (FunctionId, Pc, FallthroughPc, FalsePc) = (functionId, pc, fallthroughPc, falsePc);
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct IfGroupDescriptor
{
    public readonly int FunctionId, OpenerPc, FirstClauseIndex, ClauseCount, ExitPc;
    public IfGroupDescriptor(int functionId, int openerPc, int firstClauseIndex, int clauseCount, int exitPc) => (FunctionId, OpenerPc, FirstClauseIndex, ClauseCount, ExitPc) = (functionId, openerPc, firstClauseIndex, clauseCount, exitPc);
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct IfClauseRecord
{
    public readonly int FunctionId, Pc;
    public readonly VmStructuralKind Kind;
    public IfClauseRecord(int functionId, int pc, VmStructuralKind kind) => (FunctionId, Pc, Kind) = (functionId, pc, kind);
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct SelectGroupDescriptor
{
    public readonly int FunctionId, OpenerPc, FirstCaseIndex, CaseCount, ExitPc;
    public SelectGroupDescriptor(int functionId, int openerPc, int firstCaseIndex, int caseCount, int exitPc) => (FunctionId, OpenerPc, FirstCaseIndex, CaseCount, ExitPc) = (functionId, openerPc, firstCaseIndex, caseCount, exitPc);
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct SelectCaseRecord
{
    public readonly int FunctionId, Pc;
    public readonly VmStructuralKind Kind;
    public SelectCaseRecord(int functionId, int pc, VmStructuralKind kind) => (FunctionId, Pc, Kind) = (functionId, pc, kind);
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct LoopDescriptor
{
    public readonly int FunctionId, Kind, HeaderPc, BodyEntryPc, EndPc, ExitPc, ContinueCheckPc;
    public readonly LoopDescriptorFlags Flags;
    public LoopDescriptor(int functionId, VmStructuralKind kind, int headerPc, int bodyEntryPc, int endPc, int exitPc, int continueCheckPc, LoopDescriptorFlags flags)
        => (FunctionId, Kind, HeaderPc, BodyEntryPc, EndPc, ExitPc, ContinueCheckPc, Flags) = (functionId, (int)kind, headerPc, bodyEntryPc, endPc, exitPc, continueCheckPc, flags);
    public VmStructuralKind LoopKind => (VmStructuralKind)Kind;
}

public readonly record struct SourceFunctionId(int Value);
public readonly record struct RuntimeFunctionId(int Value);
public sealed record FunctionDefinition(string PhysicalName, string FileIdentity, SourceSpan Span,
    SourceIndexFlags Flags = SourceIndexFlags.None, FunctionKind Kind = FunctionKind.Normal,
    string? EffectiveName = null, bool EffectiveNameKnown = false)
{ public string Name => PhysicalName; }
public readonly record struct CatalogSourceRef(int FileOrdinal, int FunctionOrdinal)
{
    public static CatalogSourceRef None => new(-1, -1);
    public bool IsValid => FileOrdinal >= 0 && FunctionOrdinal >= 0;
}

public readonly record struct RuntimeFunctionBinding(
    RuntimeFunctionId RuntimeId,
    string EffectiveName,
    FunctionKind Kind,
    SourceIndexFlags Flags,
    bool EffectiveNameKnown,
    CatalogSourceRef SourceRef);

// 16-byte value entry: source identity + effective-name table id + packed metadata.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct FunctionCatalogEntry
{
    public readonly CatalogSourceRef SourceRef;
    public readonly int EffectiveNameId;
    public readonly int PackedMetadata;
    public bool CodeAvailable => (PackedMetadata & 1) != 0;
    public bool EffectiveNameKnown => (PackedMetadata & 2) != 0;
    public FunctionKind Kind => (FunctionKind)((PackedMetadata >> 2) & 3);
    public SourceIndexFlags Flags => (SourceIndexFlags)((uint)PackedMetadata >> 4);
    public FunctionCatalogEntry(CatalogSourceRef sourceRef, int effectiveNameId, FunctionKind kind, SourceIndexFlags flags, bool effectiveNameKnown, bool codeAvailable = false)
        => (SourceRef, EffectiveNameId, PackedMetadata) = (sourceRef, effectiveNameId, (codeAvailable ? 1 : 0) | (effectiveNameKnown ? 2 : 0) | ((int)kind << 2) | ((int)flags << 4));
}
public readonly record struct NameRange(int Start, int Count);

public sealed class FunctionCatalog
{
    private readonly IReadOnlyList<SourceFileIndex>? sourceFiles;
    private readonly string[] syntheticFiles, syntheticPhysicalNames;
    private readonly SourceSpan[] syntheticSpans;
    private readonly string[] nameTable;
    private readonly Dictionary<string, NameRange> nameRanges;
    private readonly int[] candidateFunctionIds;
    private readonly FunctionCatalogEntry[] entries;
    public IReadOnlyList<FunctionCatalogEntry> Entries { get; }
    public IReadOnlyList<string> NameTable => nameTable;
    public bool IgnoreCase { get; }
    public int Count => Entries.Count;
    public int CandidateIdCount => candidateFunctionIds.Length;
    public int NameRangeCount => nameRanges.Count;
    public int CompactEntrySize => Marshal.SizeOf<FunctionCatalogEntry>();
    public int FileTableCount => sourceFiles?.Count ?? syntheticFiles.Length;
    public bool HasPermanentSourceLookup => false;
    private FunctionCatalog(FunctionCatalogEntry[] entries, IReadOnlyList<SourceFileIndex>? sourceFiles, string[] syntheticFiles, string[] physicalNames, SourceSpan[] spans, string[] names, Dictionary<string, NameRange> ranges, int[] candidates, bool ignoreCase)
        => (this.entries, Entries, this.sourceFiles, this.syntheticFiles, syntheticPhysicalNames, syntheticSpans, nameTable, nameRanges, candidateFunctionIds, IgnoreCase) = (entries, entries, sourceFiles, syntheticFiles, physicalNames, spans, names, ranges, candidates, ignoreCase);

    public static FunctionCatalog FromRuntimeBindings(IReadOnlyList<SourceFileIndex> files, IEnumerable<RuntimeFunctionBinding> bindings, bool ignoreCase = true)
    {
        var list = bindings.ToArray();
        var entries = new FunctionCatalogEntry[list.Length];
        var names = new List<string>();
        var nameIds = new Dictionary<string, int>(StringComparer.Ordinal);
        int NameId(string value)
        {
            if (!nameIds.TryGetValue(value, out var id)) { id = names.Count; names.Add(value); nameIds.Add(value, id); }
            return id;
        }
        for (var id = 0; id < list.Length; id++)
        {
            if (list[id].RuntimeId.Value != id) throw new ArgumentException("RuntimeFunctionId must be contiguous catalog order", nameof(bindings));
            var b = list[id];
            var known = b.EffectiveNameKnown && !string.IsNullOrEmpty(b.EffectiveName);
            entries[id] = new(b.SourceRef, known ? NameId(b.EffectiveName) : -1, known ? b.Kind : FunctionKind.Normal, b.Flags, known);
        }
        return Build(entries, files, [], [], [], names, ignoreCase);
    }
    public static FunctionCatalog FromDefinitions(IEnumerable<FunctionDefinition> definitions, bool ignoreCase = true)
    {
        var list = definitions.ToArray(); var files = new List<string>(); var physicalNames = new string[list.Length]; var spans = new SourceSpan[list.Length]; var fileIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var functionOrdinals = new Dictionary<int, int>(); var names = new List<string>(); var nameIds = new Dictionary<string, int>(StringComparer.Ordinal); var entries = new FunctionCatalogEntry[list.Length];
        int NameId(string value) { if (!nameIds.TryGetValue(value, out var id)) { id = names.Count; names.Add(value); nameIds.Add(value, id); } return id; }
        for (var id = 0; id < list.Length; id++)
        {
            var d = list[id]; var full = Path.GetFullPath(d.FileIdentity); if (!fileIds.TryGetValue(full, out var fileOrdinal)) { fileOrdinal = files.Count; files.Add(full); fileIds.Add(full, fileOrdinal); functionOrdinals[fileOrdinal] = 0; }
            var functionOrdinal = functionOrdinals[fileOrdinal]++; var known = d.EffectiveNameKnown && !string.IsNullOrEmpty(d.EffectiveName); physicalNames[id] = d.PhysicalName; spans[id] = d.Span; entries[id] = new(new(fileOrdinal, functionOrdinal), known ? NameId(d.EffectiveName!) : -1, known ? d.Kind : FunctionKind.Normal, d.Flags, known);
        }
        return Build(entries, null, files.ToArray(), physicalNames, spans, names, ignoreCase);
    }
    private static FunctionCatalog Build(FunctionCatalogEntry[] entries, IReadOnlyList<SourceFileIndex>? sourceFiles, string[] syntheticFiles, string[] physicalNames, SourceSpan[] spans, List<string> names, bool ignoreCase)
    {
        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal; var grouped = Enumerable.Range(0, entries.Length).Where(id => entries[id].EffectiveNameId >= 0).GroupBy(id => names[entries[id].EffectiveNameId], comparer).ToArray(); var ranges = new Dictionary<string, NameRange>(comparer); var candidates = new List<int>();
        foreach (var group in grouped) { var start = candidates.Count; candidates.AddRange(group); ranges[group.Key] = new(start, group.Count()); }
        return new(entries, sourceFiles, syntheticFiles, physicalNames, spans, names.ToArray(), ranges, candidates.ToArray(), ignoreCase);
    }
    private static string NormalizeRelative(string path) => path.Replace('\\', '/');
    public FunctionCatalogEntry this[int id] => entries[id];
    public bool HasSourceDefinition(int id) => entries[id].SourceRef.IsValid;
    public bool TryGetSourceDefinition(int id, out FunctionIndex definition)
    {
        var entry = entries[id];
        if (sourceFiles is not null && entry.SourceRef.IsValid)
        {
            definition = sourceFiles[entry.SourceRef.FileOrdinal].Functions[entry.SourceRef.FunctionOrdinal];
            return true;
        }
        if (sourceFiles is null && entry.SourceRef.IsValid)
        {
            definition = new(syntheticPhysicalNames[id], syntheticSpans[id], SourceIndexFlags.None);
            return true;
        }
        definition = default;
        return false;
    }
    public string? GetPhysicalName(int id) => TryGetSourceDefinition(id, out var definition) ? definition.Name : null;
    public string? GetEffectiveName(int id) => Entries[id].EffectiveNameId < 0 ? null : nameTable[Entries[id].EffectiveNameId];
    public SourceSpan? GetSpan(int id) => TryGetSourceDefinition(id, out var definition) ? definition.Span : null;
    public string? GetFileIdentity(int id) => sourceFiles is null && entries[id].SourceRef.IsValid ? syntheticFiles[entries[id].SourceRef.FileOrdinal] : sourceFiles is not null && entries[id].SourceRef.IsValid ? sourceFiles[entries[id].SourceRef.FileOrdinal].FileIdentity : null;
    public IReadOnlyList<int> FindByName(string name, bool? requestedIgnoreCase = null)
    { if (requestedIgnoreCase.HasValue && requestedIgnoreCase.Value != IgnoreCase) return Array.Empty<int>(); return nameRanges.TryGetValue(name, out var range) ? new ArraySegment<int>(candidateFunctionIds, range.Start, range.Count) : Array.Empty<int>(); }
    public void MarkCodeAvailable(IEnumerable<RuntimeFunctionId> ids)
    { foreach (var runtimeId in ids) { var id = runtimeId.Value; var e = entries[id]; entries[id] = new(e.SourceRef, e.EffectiveNameId, e.Kind, e.Flags, e.EffectiveNameKnown, true); } }
}

public readonly record struct FixedTargetScan(string Target, int Start, int End);
// Linked once from the prototype operand. The VM never reparses the CALL/JUMP text.
public readonly record struct VmCallSiteRecord(int FunctionId, int Pc, RuntimeFunctionId Target, VmInvocationKind Kind, int ArgumentOffset, int ArgumentLength, int ArgumentRecordStart, int ArgumentCount);
public readonly record struct VmDynamicCallSiteRecord(int FunctionId, int Pc, VmDynamicCallKind Kind, int TargetRecord,
    int ArgumentRecordStart, int ArgumentCount, int MissingTargetPc);
// StableId is bound by ControlLinker; VM execution never reparses the call name.
public readonly record struct VmExpressionFunctionTarget(ulong StableId, RuntimeFunctionId Target, RuntimeMetadataValueType? ReturnType, bool Callable);
public static class FixedCallTargetScanner
{
    public static bool TryScan(string operand, out FixedTargetScan result)
    { var start = 0; while (start < operand.Length && (operand[start] == ' ' || operand[start] == '\t')) start++; var end = start; while (end < operand.Length && operand[end] is not '(' and not '[' and not ',' and not ';') end++; while (end > start && (operand[end - 1] == ' ' || operand[end - 1] == '\t')) end--; result = new(operand[start..end], start, end); return end > start; }
}
public readonly record struct CallResolution(bool FunctionResolved, bool CodeAvailable, RuntimeFunctionId RuntimeId, string? Reason);
public static class FixedCallResolver
{
    public static CallResolution Resolve(FunctionCatalog catalog, string target, bool ignoreCase, bool compatiCallEvent)
    {
        var candidates = catalog.FindByName(target, ignoreCase); if (candidates.Count == 0) return new(false, false, new(-1), "MissingTarget"); var entry = catalog[candidates[0]];
        if (entry.Kind == FunctionKind.Method) return new(false, false, new(-1), "WrongKindMethod"); if (entry.Kind == FunctionKind.Event && !compatiCallEvent) return new(false, false, new(-1), "WrongKindEvent"); return new(true, entry.CodeAvailable, new(candidates[0]), entry.CodeAvailable ? null : "CodeNotAvailable");
    }
}

public sealed record SourceFunctionPrototype(SourceFunctionId SourceId, ImmutableArray<PrototypeInstruction> Instructions, ImmutableArray<string> Operands, SemanticPayload? SemanticPayload = null, FunctionRuntimeMetadata? RuntimeMetadata = null);
public sealed record RuntimeFunctionPrototype(RuntimeFunctionId RuntimeId, ImmutableArray<PrototypeInstruction> Instructions, ImmutableArray<string> Operands, SemanticPayload? SemanticPayload = null, FunctionRuntimeMetadata? RuntimeMetadata = null);
public static class RuntimeFunctionBinder
{
    public static IReadOnlyList<RuntimeFunctionPrototype> Remap(
        IReadOnlyList<SourceFunctionPrototype> sourcePrototypes,
        IReadOnlyDictionary<SourceFunctionId, RuntimeFunctionId> sourceToRuntime)
    {
        var seen = new HashSet<RuntimeFunctionId>();
        var result = new RuntimeFunctionPrototype[sourcePrototypes.Count];
        for (var i = 0; i < sourcePrototypes.Count; i++)
        {
            var source = sourcePrototypes[i];
            if (!sourceToRuntime.TryGetValue(source.SourceId, out var runtimeId))
                throw new InvalidOperationException($"No RuntimeFunctionId for SourceFunctionId {source.SourceId.Value}");
            if (!seen.Add(runtimeId))
                throw new InvalidOperationException($"Duplicate RuntimeFunctionId {runtimeId.Value}");
            result[i] = new(runtimeId, source.Instructions, source.Operands, source.SemanticPayload, source.RuntimeMetadata);
        }
        return result;
    }
}
public sealed class LinkedProgram
{
    public VmInstruction[] Code { get; }
    public VmFunctionDescriptor[] Descriptors { get; }
    public StructuralLinkRecord[] StructuralLinks { get; }
    public SifLinkRecord[] SifLinks { get; }
    public IfGroupDescriptor[] IfGroups { get; }
    public IfClauseRecord[] IfClauses { get; }
    public SelectGroupDescriptor[] SelectGroups { get; }
    public SelectCaseRecord[] SelectCases { get; }
    public LoopDescriptor[] Loops { get; }
    public SemanticPayload SemanticArena { get; }
    public VmRuntimeStatementArena RuntimeStatements { get; }
    public FunctionRuntimeMetadata[] RuntimeMetadata { get; }
    public VmCallSiteRecord[] CallSites { get; }
    public VmExpressionFunctionTarget[] ExpressionFunctionTargets { get; }
    public SemanticPayload CallArgumentArena { get; }
    public int[] CallArgumentRecords { get; }
    public VmDynamicCallSiteRecord[] DynamicCallSites { get; }
    public SemanticPayload DynamicCallArena { get; }
    public int[] DynamicCallArgumentRecords { get; }
    public int[] StructuralSemanticRecordIndices { get; }
    public LinkedProgram(VmInstruction[] code, VmFunctionDescriptor[] descriptors, StructuralLinkRecord[] structuralLinks, SifLinkRecord[]? sifLinks = null, IfGroupDescriptor[]? ifGroups = null, IfClauseRecord[]? ifClauses = null, SelectGroupDescriptor[]? selectGroups = null, SelectCaseRecord[]? selectCases = null, LoopDescriptor[]? loops = null, SemanticPayload? semanticArena = null, int[]? structuralSemanticRecordIndices = null, VmRuntimeStatementArena? runtimeStatements = null, FunctionRuntimeMetadata[]? runtimeMetadata = null, VmCallSiteRecord[]? callSites = null, SemanticPayload? callArgumentArena = null, int[]? callArgumentRecords = null, VmExpressionFunctionTarget[]? expressionFunctionTargets = null, VmDynamicCallSiteRecord[]? dynamicCallSites = null, SemanticPayload? dynamicCallArena = null, int[]? dynamicCallArgumentRecords = null, bool sparseRuntimeIds = false)
    {
        var arena = semanticArena ?? SemanticPayload.Empty;
        var mapping = structuralSemanticRecordIndices ?? Enumerable.Repeat(-1, structuralLinks.Length).ToArray();
        if (mapping.Length != structuralLinks.Length) throw new ArgumentException("semantic mapping length must equal structural link length");
        if (mapping.Any(index => index < -1 || index >= arena.Records.Length)) throw new ArgumentOutOfRangeException(nameof(structuralSemanticRecordIndices));
        if (mapping.Where(index => index >= 0).GroupBy(index => index).Any(group => group.Count() > 1)) throw new ArgumentException("duplicate semantic record mapping");
        var metadata = runtimeMetadata ?? Enumerable.Repeat(FunctionRuntimeMetadata.Empty, descriptors.Length).ToArray();
        if (metadata.Length != descriptors.Length) throw new ArgumentException("runtime metadata length must equal descriptor count", nameof(runtimeMetadata));
        var sites = callSites ?? [];
        if (!sparseRuntimeIds && sites.Any(site => (uint)site.FunctionId >= (uint)descriptors.Length || (uint)site.Target.Value >= (uint)descriptors.Length)) throw new ArgumentOutOfRangeException(nameof(callSites));
        var callArena = callArgumentArena ?? SemanticPayload.Empty;
        var callRecords = callArgumentRecords ?? [];
        if (sites.Any(site => site.ArgumentRecordStart < 0 || site.ArgumentCount < 0 || site.ArgumentRecordStart > callRecords.Length - site.ArgumentCount) || callRecords.Any(record => record < -1 || record >= callArena.Records.Length)) throw new ArgumentOutOfRangeException(nameof(callArgumentRecords));
        var expressionTargets = expressionFunctionTargets ?? [];
        if (expressionTargets.GroupBy(target => target.StableId).Any(group => group.Count() > 1)) throw new ArgumentException("duplicate expression function identity", nameof(expressionFunctionTargets));
        if (expressionTargets.Any(target => target.Target.Value < -1 || !sparseRuntimeIds && target.Target.Value >= descriptors.Length)) throw new ArgumentOutOfRangeException(nameof(expressionFunctionTargets));
        var dynamicSites = dynamicCallSites ?? [];
        var dynamicArena = dynamicCallArena ?? SemanticPayload.Empty;
        var dynamicRecords = dynamicCallArgumentRecords ?? [];
        if (dynamicSites.Any(site => (!sparseRuntimeIds && (uint)site.FunctionId >= (uint)descriptors.Length) || site.TargetRecord < 0 || site.TargetRecord >= dynamicArena.Records.Length || site.ArgumentRecordStart < 0 || site.ArgumentCount < 0 || site.ArgumentRecordStart > dynamicRecords.Length - site.ArgumentCount) || dynamicRecords.Any(record => record < -1 || record >= dynamicArena.Records.Length)) throw new ArgumentOutOfRangeException(nameof(dynamicCallSites));
        (Code, Descriptors, StructuralLinks, SifLinks, IfGroups, IfClauses, SelectGroups, SelectCases, Loops, SemanticArena, StructuralSemanticRecordIndices, RuntimeStatements, RuntimeMetadata, CallSites, CallArgumentArena, CallArgumentRecords, ExpressionFunctionTargets, DynamicCallSites, DynamicCallArena, DynamicCallArgumentRecords) = (code, descriptors, structuralLinks, sifLinks ?? [], ifGroups ?? [], ifClauses ?? [], selectGroups ?? [], selectCases ?? [], loops ?? [], arena, mapping, runtimeStatements ?? VmRuntimeStatementArena.Empty, metadata, sites, callArena, callRecords, expressionTargets, dynamicSites, dynamicArena, dynamicRecords);
    }
}
public enum StructuralClassification : byte { Valid, ValidWithStructuralWarning, InvalidStructure }
public readonly record struct StructuralDiagnostic(RuntimeFunctionId RuntimeId, int Pc, StructuralClassification Classification, string Message);
public sealed record ControlLinkResult(LinkedProgram Program, IReadOnlyList<string> Diagnostics, int Calls, int Jumps, int CallScanFailures, int ResolvedCalls, int MissingTargets, int WrongKinds, int CodeAvailableTargets, int CodeUnavailableTargets, int SemanticBarriers, int LinkReadyFunctions = 0, int SemanticPendingFunctions = 0)
{
    public IReadOnlyList<StructuralDiagnostic> StructuralDiagnostics { get; init; } = [];
    public int CallArgumentPayloadAdditionCount { get; init; }
    public long CallArgumentPrefixScanElementVisits { get; init; }
    public long CallArgumentPrefixScanTicks { get; init; }
}
public readonly record struct VmFunctionSignature(FunctionRuntimeMetadata Metadata, bool NoYield);
public sealed record VmFunctionLinkResult(VmFunctionExecutionContext Context, IReadOnlyList<string> Diagnostics, int DescriptorArrayLength, int MetadataArrayLength);

public static class ControlLinker
{
    private static readonly HashSet<PrototypeOpcode> Structure = [PrototypeOpcode.IF, PrototypeOpcode.SIF, PrototypeOpcode.ELSEIF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF, PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT, PrototypeOpcode.REPEAT, PrototypeOpcode.REND, PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.WHILE, PrototypeOpcode.WEND, PrototypeOpcode.DO, PrototypeOpcode.LOOP, PrototypeOpcode.BREAK, PrototypeOpcode.CONTINUE];
    private static readonly HashSet<PrototypeOpcode> HostStatements = [PrototypeOpcode.RESETCOLOR, PrototypeOpcode.CUSTOMDRAWLINE, PrototypeOpcode.SETCOLOR, PrototypeOpcode.SETFONT, PrototypeOpcode.DRAWLINE, PrototypeOpcode.RESET_STAIN, PrototypeOpcode.VARSET, PrototypeOpcode.ALIGNMENT, PrototypeOpcode.ARRAYSHIFT, PrototypeOpcode.CALLF, PrototypeOpcode.PRINTS, PrototypeOpcode.PRINTSW, PrototypeOpcode.SETBIT, PrototypeOpcode.CLEARBIT, PrototypeOpcode.INVERTBIT];
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Kinds = new() { [PrototypeOpcode.SIF]=VmStructuralKind.Sif,[PrototypeOpcode.IF]=VmStructuralKind.If,[PrototypeOpcode.ELSEIF]=VmStructuralKind.ElseIf,[PrototypeOpcode.ELSE]=VmStructuralKind.Else,[PrototypeOpcode.ENDIF]=VmStructuralKind.EndIf,[PrototypeOpcode.SELECTCASE]=VmStructuralKind.SelectCase,[PrototypeOpcode.CASE]=VmStructuralKind.Case,[PrototypeOpcode.CASEELSE]=VmStructuralKind.CaseElse,[PrototypeOpcode.ENDSELECT]=VmStructuralKind.EndSelect,[PrototypeOpcode.REPEAT]=VmStructuralKind.Repeat,[PrototypeOpcode.REND]=VmStructuralKind.Rend,[PrototypeOpcode.FOR]=VmStructuralKind.For,[PrototypeOpcode.NEXT]=VmStructuralKind.Next,[PrototypeOpcode.WHILE]=VmStructuralKind.While,[PrototypeOpcode.WEND]=VmStructuralKind.Wend,[PrototypeOpcode.DO]=VmStructuralKind.Do,[PrototypeOpcode.LOOP]=VmStructuralKind.Loop,[PrototypeOpcode.BREAK]=VmStructuralKind.Break,[PrototypeOpcode.CONTINUE]=VmStructuralKind.Continue };
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Openers = new() { [PrototypeOpcode.IF]=VmStructuralKind.If,[PrototypeOpcode.SELECTCASE]=VmStructuralKind.SelectCase,[PrototypeOpcode.REPEAT]=VmStructuralKind.Repeat,[PrototypeOpcode.FOR]=VmStructuralKind.For,[PrototypeOpcode.WHILE]=VmStructuralKind.While,[PrototypeOpcode.DO]=VmStructuralKind.Do };
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Closers = new() { [PrototypeOpcode.ENDIF]=VmStructuralKind.If,[PrototypeOpcode.ENDSELECT]=VmStructuralKind.SelectCase,[PrototypeOpcode.REND]=VmStructuralKind.Repeat,[PrototypeOpcode.NEXT]=VmStructuralKind.For,[PrototypeOpcode.WEND]=VmStructuralKind.While,[PrototypeOpcode.LOOP]=VmStructuralKind.Do };
    private static readonly HashSet<VmStructuralKind> LoopKinds = [VmStructuralKind.Repeat, VmStructuralKind.For, VmStructuralKind.While, VmStructuralKind.Do];
    // Source oracle: Runtime/Script/Statements/FunctionIdentifier.cs IsPartial and Instraction.Child.cs SIF parser.
    private static readonly HashSet<PrototypeOpcode> LegacyPartialOpcodes = [PrototypeOpcode.SIF, PrototypeOpcode.IF, PrototypeOpcode.ELSE, PrototypeOpcode.ELSEIF, PrototypeOpcode.ENDIF, PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT, PrototypeOpcode.REPEAT, PrototypeOpcode.REND, PrototypeOpcode.CONTINUE, PrototypeOpcode.BREAK, PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.WHILE, PrototypeOpcode.WEND, PrototypeOpcode.DO, PrototypeOpcode.LOOP, PrototypeOpcode.PRINTDATA, PrototypeOpcode.PRINTDATAL, PrototypeOpcode.PRINTDATAW, PrototypeOpcode.DATA, PrototypeOpcode.DATAFORM];
    public static bool IsLegacyPartialOpcode(PrototypeOpcode opcode) => LegacyPartialOpcodes.Contains(opcode);
    public static ControlLinkResult Link(FunctionCatalog catalog, IReadOnlyList<RuntimeFunctionPrototype> prototypes, bool ignoreCase = true, bool compatiCallEvent = false, StructuralSemanticEnvironment? runtimeEnvironment = null, bool runtimeStatements = false, Action<string>? phaseBoundary = null, bool measureCallArgumentPrefixScan = false)
        => LinkCore(catalog, prototypes, ignoreCase, compatiCallEvent, runtimeEnvironment, runtimeStatements, phaseBoundary, measureCallArgumentPrefixScan, false, null);

    public static VmFunctionLinkResult LinkFunction(FunctionCatalog catalog, RuntimeFunctionPrototype prototype, int codeSlot, long generation,
        Func<RuntimeFunctionId, VmFunctionSignature?> signatureReader, bool ignoreCase = true, bool compatiCallEvent = false,
        StructuralSemanticEnvironment? runtimeEnvironment = null, bool runtimeStatements = false)
    {
        ArgumentNullException.ThrowIfNull(signatureReader);
        var linked = LinkCore(catalog, [prototype], ignoreCase, compatiCallEvent, runtimeEnvironment, runtimeStatements, null, false, true, signatureReader);
        var metadata = prototype.RuntimeMetadata ?? signatureReader(prototype.RuntimeId)?.Metadata ?? FunctionRuntimeMetadata.Empty;
        var physicalLoops = linked.Program.Loops.Select((_, ordinal) => checked((prototype.RuntimeId.Value * 397) ^ ordinal)).ToImmutableArray();
        var context = new VmFunctionExecutionContext(prototype.RuntimeId, codeSlot, generation, linked.Program, metadata, physicalLoops);
        return new(context, linked.Diagnostics, linked.Program.Descriptors.Length, linked.Program.RuntimeMetadata.Length);
    }

    private static ControlLinkResult LinkCore(FunctionCatalog catalog, IReadOnlyList<RuntimeFunctionPrototype> prototypes, bool ignoreCase, bool compatiCallEvent,
        StructuralSemanticEnvironment? runtimeEnvironment, bool runtimeStatements, Action<string>? phaseBoundary, bool measureCallArgumentPrefixScan,
        bool sparseRuntimeIds, Func<RuntimeFunctionId, VmFunctionSignature?>? signatureReader)
    {
        phaseBoundary?.Invoke("ControlLinker.Start");
        var orderedPrototypes = prototypes.OrderBy(x => x.RuntimeId.Value).ToArray();
        var descriptorSlots = sparseRuntimeIds ? orderedPrototypes.Select((prototype, slot) => (prototype.RuntimeId.Value, slot)).ToDictionary(x => x.Value, x => x.slot) : null;
        var code = new List<VmInstruction>(); var descriptors = sparseRuntimeIds ? orderedPrototypes.Select(p => new VmFunctionDescriptor(p.RuntimeId.Value, 0, 0, VmFunctionState.CodeNotAvailable)).ToArray() : Enumerable.Range(0, catalog.Count).Select(id => new VmFunctionDescriptor(id, 0, 0, VmFunctionState.CodeNotAvailable)).ToArray(); var metadata = Enumerable.Repeat(FunctionRuntimeMetadata.Empty, descriptors.Length).ToArray(); var records = new List<StructuralLinkRecord>(); var callSites = new List<VmCallSiteRecord>(); var callArgumentParts = new List<SemanticPayload>(); var callArgumentRecords = new List<int>(); var dynamicCallSites = new List<VmDynamicCallSiteRecord>(); var dynamicCallParts = new List<SemanticPayload>(); var dynamicCallArgumentRecords = new List<int>(); var dynamicCallRecordCount = 0; var structuralSemanticIndices = new List<int>(); var semanticParts = new List<SemanticPayload>(); var statements = new VmRuntimeStatementArenaBuilder(); var sifs = new List<SifLinkRecord>(); var ifGroups = new List<IfGroupDescriptor>(); var ifClauses = new List<IfClauseRecord>(); var selectGroups = new List<SelectGroupDescriptor>(); var selectCases = new List<SelectCaseRecord>(); var loops = new List<LoopDescriptor>(); var diagnostics = new List<string>(); var structuralDiagnostics = new List<StructuralDiagnostic>();
        var calls = 0; var jumps = 0; var scans = 0; var resolved = 0; var missing = 0; var wrong = 0; var yes = 0; var no = 0; var barriers = 0;
        var semanticRecordCount = 0;
        var callArgumentPayloadAdditionCount = 0;
        var callArgumentRecordCount = 0;
        foreach (var prototype in orderedPrototypes)
        {
            var runtimeId = prototype.RuntimeId.Value;
            var descriptorSlot = sparseRuntimeIds ? descriptorSlots![runtimeId] : runtimeId;
            var catchTargets = LinkCatchTargets(prototype.Instructions, out var catchValid);
            var labels = new Dictionary<string, int>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var labelsValid = true;
            for (var labelPc = 0; labelPc < prototype.Instructions.Length; labelPc++)
            {
                if (prototype.Instructions[labelPc].Opcode != PrototypeOpcode.LABEL) continue;
                var label = labelPc < prototype.Operands.Length ? prototype.Operands[labelPc].Trim() : string.Empty;
                if (label.Length == 0 || !labels.TryAdd(label, labelPc))
                {
                    diagnostics.Add($"function={runtimeId} pc={labelPc} invalid-or-duplicate-label={label}");
                    labelsValid = false;
                    labels.Clear();
                    break;
                }
            }
            metadata[descriptorSlot] = prototype.RuntimeMetadata ?? signatureReader?.Invoke(prototype.RuntimeId)?.Metadata ?? FunctionRuntimeMetadata.Empty;
            var start = code.Count; var state = labelsValid && catchValid ? VmFunctionState.LinkedSemanticPending : VmFunctionState.InvalidStructure; var local = new List<StructuralLinkRecord>();
            var semanticRecordBase = semanticRecordCount;
            if (prototype.SemanticPayload is not null) { semanticParts.Add(prototype.SemanticPayload); semanticRecordCount += prototype.SemanticPayload.Records.Length; }
            var semanticByPc = prototype.SemanticPayload?.Records.Select((record, index) => (record.InstructionIndex, Index: semanticRecordBase + index)).ToDictionary(static x => x.InstructionIndex, static x => x.Index) ?? [];
            for (var pc = 0; pc < prototype.Instructions.Length; pc++)
            {
                var p = prototype.Instructions[pc]; var operand = pc < prototype.Operands.Length ? prototype.Operands[pc] : string.Empty;
                if (p.Opcode == PrototypeOpcode.LABEL) { code.Add(Linked(p, VmOpcode.Nop)); continue; }
                if (p.Opcode is PrototypeOpcode.GOTO or PrototypeOpcode.TRYGOTO)
                {
                    var label = operand.Trim();
                    if (labels.TryGetValue(label, out var labelTarget)) { code.Add(Linked(p, VmOpcode.Branch, labelTarget)); continue; }
                    if (p.Opcode == PrototypeOpcode.TRYGOTO) { code.Add(Linked(p, VmOpcode.Nop)); continue; }
                    state = VmFunctionState.UnsupportedControl;
                    diagnostics.Add($"function={runtimeId} pc={pc} missing-label={label}");
                    code.Add(Linked(p, VmOpcode.UnsupportedControl));
                    continue;
                }
                if (p.Opcode is PrototypeOpcode.CATCH or PrototypeOpcode.ENDCATCH)
                {
                    if (!catchTargets.TryGetValue(pc, out var target)) { state = VmFunctionState.InvalidStructure; code.Add(Linked(p, VmOpcode.UnsupportedControl)); }
                    else code.Add(p.Opcode == PrototypeOpcode.CATCH ? Linked(p, VmOpcode.Branch, target) : Linked(p, VmOpcode.Nop));
                    continue;
                }
                if (p.Opcode is PrototypeOpcode.CALLFORM or PrototypeOpcode.TRYCALLFORM or PrototypeOpcode.TRYCCALLFORM or PrototypeOpcode.CALLFORMF)
                {
                    if (runtimeEnvironment is null || !TryLinkDynamicCall(runtimeId, pc, p.Opcode, operand, catchTargets, runtimeEnvironment,
                            dynamicCallParts, dynamicCallArgumentRecords, ref dynamicCallRecordCount, out var dynamicSite))
                    {
                        state = VmFunctionState.UnsupportedControl; diagnostics.Add($"function={runtimeId} pc={pc} unsupported-dynamic-call"); code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue;
                    }
                    dynamicCallSites.Add(dynamicSite); code.Add(Linked(p, VmOpcode.DynamicCall, dynamicCallSites.Count - 1)); continue;
                }
                if (p.Opcode is PrototypeOpcode.CALL or PrototypeOpcode.JUMP)
                {
                    if (p.Opcode == PrototypeOpcode.CALL) calls++; else jumps++; if (!FixedCallTargetScanner.TryScan(operand, out var scan)) { scans++; state = VmFunctionState.UnsupportedControl; diagnostics.Add($"function={runtimeId} pc={pc} scan-fail"); code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                    var resolution = FixedCallResolver.Resolve(catalog, scan.Target, ignoreCase, compatiCallEvent); if (!resolution.FunctionResolved) { if (resolution.Reason?.StartsWith("WrongKind", StringComparison.Ordinal) == true) wrong++; else missing++; state = VmFunctionState.UnsupportedControl; code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                    resolved++; if (resolution.CodeAvailable) yes++; else no++; var argumentStart = scan.End; var argumentLength = Math.Max(0, operand.Length - argumentStart); var argumentRecordStart = callArgumentRecords.Count;
                    if (!TryLinkCallArguments(operand[argumentStart..], runtimeEnvironment, callArgumentParts, callArgumentRecords, measureCallArgumentPrefixScan, ref callArgumentPayloadAdditionCount, ref callArgumentRecordCount)) { state = VmFunctionState.UnsupportedControl; diagnostics.Add($"function={runtimeId} pc={pc} unsupported-call-arguments"); code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                    callSites.Add(new(runtimeId, pc, resolution.RuntimeId, p.Opcode == PrototypeOpcode.CALL ? VmInvocationKind.Call : VmInvocationKind.Jump, argumentStart, argumentLength, argumentRecordStart, callArgumentRecords.Count - argumentRecordStart)); code.Add(Linked(p, p.Opcode == PrototypeOpcode.CALL ? VmOpcode.Call : VmOpcode.Jump, resolution.RuntimeId.Value)); continue;
                }
                if (p.Opcode == PrototypeOpcode.RETURN && operand.Length != 0)
                {
                    if (runtimeStatements && runtimeEnvironment is not null && TryLinkRuntimeStatement(p.Opcode, operand, p.SourceLine, statements, runtimeEnvironment, out var returnIndex)) { code.Add(Linked(p, VmOpcode.Statement, returnIndex)); continue; }
                    state = VmFunctionState.UnsupportedControl;
                    diagnostics.Add($"function={runtimeId} pc={pc} unsupported-return-expression");
                    code.Add(Linked(p, VmOpcode.UnsupportedControl));
                    continue;
                }
                if (p.Opcode == PrototypeOpcode.RETURN) { code.Add(Linked(p, VmOpcode.Return)); continue; }
                if (Structure.Contains(p.Opcode))
                {
                    var structuralIndex = checked(records.Count + local.Count);
                    var targetPc = -1;
                    var auxiliaryPc = -1;
                    if (p.Opcode == PrototypeOpcode.SIF)
                    {
                        if (pc + 1 >= prototype.Instructions.Length)
                        {
                            state = VmFunctionState.InvalidStructure;
                            diagnostics.Add($"function={runtimeId} pc={pc} malformed=SIF-no-next");
                            structuralDiagnostics.Add(new(new(runtimeId), pc, StructuralClassification.InvalidStructure, "SIF-no-next"));
                        }
                        else
                        {
                            if (IsLegacyPartialOpcode(prototype.Instructions[pc + 1].Opcode))
                            {
                                diagnostics.Add($"function={runtimeId} pc={pc} warning=SIF-partial-next");
                                structuralDiagnostics.Add(new(new(runtimeId), pc, StructuralClassification.ValidWithStructuralWarning, "SIF-partial-next"));
                            }
                            targetPc = pc + 1;
                            auxiliaryPc = pc + 2;
                            sifs.Add(new(runtimeId, pc, targetPc, auxiliaryPc));
                        }
                    }
                    local.Add(new(runtimeId, pc, targetPc, auxiliaryPc, Kinds[p.Opcode], 0));
                    structuralSemanticIndices.Add(semanticByPc.TryGetValue(pc, out var semanticIndex) ? semanticIndex : -1);
                    // Aux is the program-global StructuralLinks identity. Structural execution must not
                    // scan by (FunctionId,Pc) in the hot loop.
                    code.Add(Linked(p, VmOpcode.Structural, structuralIndex));
                    continue;
                }
                if (p.Opcode is PrototypeOpcode.TRYJUMP or PrototypeOpcode.TRYGOTOFORM) { state = VmFunctionState.UnsupportedControl; code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                if (runtimeStatements && TryLinkRuntimeStatement(p.Opcode, operand, p.SourceLine, statements, runtimeEnvironment, out var statementIndex)) code.Add(Linked(p, VmOpcode.Statement, statementIndex));
                else { barriers++; code.Add(Linked(p, VmOpcode.SemanticBarrier)); }
            }
            var linked = LinkStructure(runtimeId, prototype.Instructions, local, diagnostics, structuralDiagnostics, ifGroups.Count, ifClauses.Count, selectGroups.Count, selectCases.Count, loops.Count); if (linked.Invalid) state = VmFunctionState.InvalidStructure; records.AddRange(linked.Records); ifGroups.AddRange(linked.IfGroups); ifClauses.AddRange(linked.IfClauses); selectGroups.AddRange(linked.SelectGroups); selectCases.AddRange(linked.SelectCases); loops.AddRange(linked.Loops); descriptors[descriptorSlot] = new(runtimeId, start, code.Count - start, state);
        }
        phaseBoundary?.Invoke("ControlLinker.AfterFunctionLink");
        var semanticArena = semanticParts.Count == 0 ? SemanticPayload.Empty : SemanticPayload.Merge(semanticParts);
        var runtimeArena = statements.Build();
        var callArena = callArgumentParts.Count == 0 ? SemanticPayload.Empty : SemanticPayload.Merge(callArgumentParts);
        var dynamicArena = dynamicCallParts.Count == 0 ? SemanticPayload.Empty : SemanticPayload.Merge(dynamicCallParts);
        phaseBoundary?.Invoke("ControlLinker.AfterArenaBuild");
        var skeleton = new LinkedProgram(code.ToArray(), descriptors, records.ToArray(), sifs.ToArray(), ifGroups.ToArray(), ifClauses.ToArray(), selectGroups.ToArray(), selectCases.ToArray(), loops.ToArray(), semanticArena, structuralSemanticIndices.ToArray(), runtimeArena, metadata, callSites.ToArray(), callArena, callArgumentRecords.ToArray(), dynamicCallSites: dynamicCallSites.ToArray(), dynamicCallArena: dynamicArena, dynamicCallArgumentRecords: dynamicCallArgumentRecords.ToArray(), sparseRuntimeIds: sparseRuntimeIds);
        phaseBoundary?.Invoke("ControlLinker.AfterSkeletonBuild");
        var program = new LinkedProgram(skeleton.Code, skeleton.Descriptors, skeleton.StructuralLinks, skeleton.SifLinks, skeleton.IfGroups, skeleton.IfClauses, skeleton.SelectGroups, skeleton.SelectCases, skeleton.Loops, skeleton.SemanticArena, skeleton.StructuralSemanticRecordIndices, skeleton.RuntimeStatements, skeleton.RuntimeMetadata, skeleton.CallSites, skeleton.CallArgumentArena, skeleton.CallArgumentRecords, BindExpressionFunctionTargets(skeleton, catalog, ignoreCase, sparseRuntimeIds, signatureReader), skeleton.DynamicCallSites, skeleton.DynamicCallArena, skeleton.DynamicCallArgumentRecords, sparseRuntimeIds);
        phaseBoundary?.Invoke("ControlLinker.AfterExpressionTargetBinding");
        return new ControlLinkResult(program, diagnostics, calls, jumps, scans, resolved, missing, wrong, yes, no, barriers, 0, prototypes.Count)
        {
            StructuralDiagnostics = structuralDiagnostics.ToArray(),
            CallArgumentPayloadAdditionCount = callArgumentPayloadAdditionCount,
            CallArgumentPrefixScanElementVisits = 0,
            CallArgumentPrefixScanTicks = 0
        };
    }
    private static Dictionary<int, int> LinkCatchTargets(IReadOnlyList<PrototypeInstruction> instructions, out bool valid)
    {
        var result = new Dictionary<int, int>();
        var stack = new Stack<(int TryPc, int CatchPc)>();
        valid = true;
        for (var pc = 0; pc < instructions.Count; pc++)
        {
            switch (instructions[pc].Opcode)
            {
                case PrototypeOpcode.TRYCCALLFORM:
                    stack.Push((pc, -1));
                    break;
                case PrototypeOpcode.CATCH:
                    if (stack.Count == 0 || stack.Peek().CatchPc >= 0) { valid = false; break; }
                    var open = stack.Pop();
                    stack.Push((open.TryPc, pc));
                    break;
                case PrototypeOpcode.ENDCATCH:
                    if (stack.Count == 0 || stack.Peek().CatchPc < 0) { valid = false; break; }
                    var closed = stack.Pop();
                    result[closed.TryPc] = closed.CatchPc + 1;
                    result[closed.CatchPc] = pc + 1;
                    result[pc] = pc + 1;
                    break;
            }
        }
        if (stack.Count != 0) valid = false;
        return result;
    }

    private static bool TryLinkDynamicCall(int functionId, int pc, PrototypeOpcode opcode, string operand,
        IReadOnlyDictionary<int, int> catchTargets, StructuralSemanticEnvironment environment,
        List<SemanticPayload> payloads, List<int> argumentRecords, ref int recordCount,
        out VmDynamicCallSiteRecord site)
    {
        site = default;
        try
        {
            var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
            if (parts.Count == 0 || string.IsNullOrWhiteSpace(parts[0])) return false;
            var target = SemanticIrCompiler.CompileFormat(parts[0].Trim(), environment);
            if (target.Records.Length != 1) return false;
            var targetRecord = recordCount;
            payloads.Add(target);
            recordCount += target.Records.Length;
            var argumentStart = argumentRecords.Count;
            for (var index = 1; index < parts.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(parts[index])) { argumentRecords.Add(-1); continue; }
                var argument = SemanticIrCompiler.CompileExpression(parts[index].Trim(), environment);
                if (argument.Records.Length != 1) return false;
                argumentRecords.Add(recordCount);
                payloads.Add(argument);
                recordCount += argument.Records.Length;
            }
            var kind = opcode switch
            {
                PrototypeOpcode.TRYCALLFORM => VmDynamicCallKind.TryCallForm,
                PrototypeOpcode.TRYCCALLFORM => VmDynamicCallKind.TryCCallForm,
                PrototypeOpcode.CALLFORMF => VmDynamicCallKind.CallFormMethod,
                _ => VmDynamicCallKind.CallForm,
            };
            var missingPc = kind == VmDynamicCallKind.TryCCallForm
                ? catchTargets.TryGetValue(pc, out var targetPc) ? targetPc : -1
                : kind == VmDynamicCallKind.TryCallForm ? pc + 1 : -1;
            if (kind == VmDynamicCallKind.TryCCallForm && missingPc < 0) return false;
            site = new(functionId, pc, kind, targetRecord, argumentStart, argumentRecords.Count - argumentStart, missingPc);
            return true;
        }
        catch (SemanticParseException) { return false; }
    }

    private static bool TryLinkRuntimeStatement(PrototypeOpcode opcode, string operand, int sourceLine, VmRuntimeStatementArenaBuilder statements, StructuralSemanticEnvironment? environment, out int index)
    {
        if (opcode is PrototypeOpcode.DEBUGPRINT or PrototypeOpcode.DEBUGPRINTL or PrototypeOpcode.DEBUGPRINTFORM or PrototypeOpcode.DEBUGPRINTFORML)
        {
            index = statements.Add(VmRuntimeStatementKind.NoOp);
            return true;
        }
        if (environment is not null && opcode == PrototypeOpcode.CALLF)
        {
            try
            {
                var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
                var expression = parts.Count > 1 ? $"{parts[0].Trim()}({string.Join(',', parts.Skip(1))})" : operand.Contains('(') ? operand : operand.Trim() + "()";
                index = statements.AddOperand(VmRuntimeStatementKind.Expression, SemanticIrCompiler.CompileExpression(expression, environment));
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.FINDELEMENT)
        {
            try
            {
                index = statements.AddAssignment(SemanticIrCompiler.CompileExpression("RESULT", environment),
                    SemanticIrCompiler.CompileExpression($"FINDELEMENT({operand})", environment), VmAssignmentOperator.Assign);
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.TIMES && TrySplitTimes(operand, environment.Compatibility, out var destination, out var multiplier))
        {
            try { index = statements.AddTimes(SemanticIrCompiler.CompileExpression(destination, environment), multiplier); return true; }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.SET && TrySplitAssignment(operand, out var assignmentDestination, out var assignment, out var source))
        {
            try
            {
                SemanticPayload? detectedFormat = null;
                try
                {
                    var candidate = SemanticIrCompiler.CompileFormat(source, environment);
                    if (candidate.Nodes.Any(node => node.Kind is SemanticNodeKind.Format or SemanticNodeKind.ConditionalFormat or SemanticNodeKind.FormattedSequence))
                        detectedFormat = candidate;
                }
                catch (SemanticParseException) { }
                var multiple = SemanticLexicalTokenStream.SplitTopLevel(source, ',', environment.Compatibility);
                if (detectedFormat is null && multiple.Count > 1 && (assignment == VmAssignmentOperator.AssignString || assignment == VmAssignmentOperator.Assign && !IsKnownStringAssignmentTarget(assignmentDestination)))
                {
                    index = statements.AddMultiple(VmRuntimeStatementKind.MultiSet,
                        SemanticIrCompiler.CompileExpression(assignmentDestination, environment),
                        multiple.Select(part => SemanticIrCompiler.CompileExpression(part.Trim(), environment)).ToArray());
                    return true;
                }
                SemanticPayload sourcePayload;
                SemanticPayload? formatPayload = null;
                if (assignment is VmAssignmentOperator.AssignString) sourcePayload = SemanticIrCompiler.CompileExpression(source, environment);
                else if (assignment is not VmAssignmentOperator.Assign && IsKnownStringAssignmentTarget(assignmentDestination))
                {
                    try { sourcePayload = SemanticIrCompiler.CompileExpression(source, environment); }
                    catch (SemanticParseException) { sourcePayload = detectedFormat ?? SemanticIrCompiler.CompileFormat(source, environment); }
                }
                else if (assignment is not VmAssignmentOperator.Assign) sourcePayload = SemanticIrCompiler.CompileExpression(source, environment);
                else
                {
                    SemanticPayload? expressionPayload = null;
                    try { expressionPayload = SemanticIrCompiler.CompileExpression(source, environment); } catch (SemanticParseException) { }
                    formatPayload = detectedFormat;
                    if (formatPayload is null) try { formatPayload = SemanticIrCompiler.CompileFormat(source, environment); } catch (SemanticParseException) { }
                    if (expressionPayload is null && formatPayload is null) { index = -1; return false; }
                    sourcePayload = expressionPayload ?? formatPayload!;
                    if (expressionPayload is null) formatPayload = null;
                }
                index = statements.AddAssignment(SemanticIrCompiler.CompileExpression(assignmentDestination, environment), sourcePayload, assignment, formatPayload);
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.SET && TrySplitIncrement(operand, out var incrementDestination, out var incrementOperator))
        {
            try
            {
                index = statements.AddAssignment(SemanticIrCompiler.CompileExpression(incrementDestination, environment),
                    SemanticIrCompiler.CompileExpression("1", environment), incrementOperator);
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.SPLIT)
        {
            try
            {
                var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
                if (parts.Count is < 3 or > 4) { index = -1; return false; }
                index = statements.AddSplit(SemanticIrCompiler.CompileExpression(parts[0].Trim(), environment),
                    SemanticIrCompiler.CompileExpression(parts[1].Trim(), environment),
                    SemanticIrCompiler.CompileExpression(parts[2].Trim(), environment),
                    SemanticIrCompiler.CompileExpression(parts.Count == 4 ? parts[3].Trim() : "RESULT", environment));
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode is (PrototypeOpcode.VARI or PrototypeOpcode.VARS) && TrySplitBodyLocal(operand, out var localName, out var localInitializer, out var isArray))
        {
            if (isArray) { index = statements.Add(VmRuntimeStatementKind.NoOp); return true; }
            try
            {
                var initialValue = opcode == PrototypeOpcode.VARI
                    ? SemanticIrCompiler.CompileExpression(localInitializer.Length == 0 ? "0" : localInitializer, environment)
                    : SemanticIrCompiler.CompileExpression(localInitializer.Length == 0 ? "\"\"" : localInitializer, environment);
                index = statements.AddDeclaration(localName, initialValue);
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.PRINTBUTTON)
        {
            try
            {
                var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
                if (parts.Count != 2) { index = -1; return false; }
                index = statements.AddPair(VmRuntimeStatementKind.PrintButton,
                    SemanticIrCompiler.CompileExpression(parts[0].Trim(), environment),
                    SemanticIrCompiler.CompileExpression(parts[1].Trim(), environment), sourceLine);
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.THROW)
        {
            try { index = statements.AddOperand(VmRuntimeStatementKind.Throw, SemanticIrCompiler.CompileFormat(operand, environment), sourceLine); return true; }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.RETURN)
        {
            try
            {
                var values = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
                if (values.Count > 1)
                    index = statements.AddMultiple(VmRuntimeStatementKind.LegacyReturnValues, null,
                        values.Select(part => SemanticIrCompiler.CompileExpression(part.Trim(), environment)).ToArray());
                else index = statements.AddLegacyReturn(SemanticIrCompiler.CompileExpression(operand, environment));
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.RETURNF)
        {
            try { index = statements.AddReturn(operand.Length == 0 ? null : SemanticIrCompiler.CompileExpression(operand, environment)); return true; }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.VARSET)
        {
            try
            {
                var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
                if (parts.Count == 1) index = statements.AddOperand(VmRuntimeStatementKind.ClearFrame, SemanticIrCompiler.CompileExpression(parts[0].Trim(), environment), sourceLine);
                else if (parts.Count == 4) index = statements.AddVarSet(
                    SemanticIrCompiler.CompileExpression(parts[0].Trim(), environment), SemanticIrCompiler.CompileExpression(parts[1].Trim(), environment),
                    SemanticIrCompiler.CompileExpression(parts[2].Trim(), environment), SemanticIrCompiler.CompileExpression(parts[3].Trim(), environment));
                else { index = -1; return false; }
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && opcode == PrototypeOpcode.SETBIT)
        {
            try
            {
                var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
                if (parts.Count < 2) { index = -1; return false; }
                index = statements.AddMultiple(VmRuntimeStatementKind.SetBit,
                    SemanticIrCompiler.CompileExpression(parts[0].Trim(), environment),
                    parts.Skip(1).Select(part => SemanticIrCompiler.CompileExpression(part.Trim(), environment)).ToArray());
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        if (environment is not null && TryLinkTypedHost(opcode, operand, environment, statements, out index))
            return true;
        // A format is linked into its own semantic arena, never retained as a raw operand string.
        if (environment is not null && opcode is PrototypeOpcode.PRINTFORM or PrototypeOpcode.PRINTFORML or PrototypeOpcode.PRINTFORMW)
        {
            try
            {
                var formatKind = opcode == PrototypeOpcode.PRINTFORM ? VmRuntimeStatementKind.Print : opcode == PrototypeOpcode.PRINTFORML ? VmRuntimeStatementKind.PrintLine : VmRuntimeStatementKind.PrintWait;
                index = statements.AddOperand(formatKind, SemanticIrCompiler.CompileFormat(operand, environment));
                return true;
            }
            catch (SemanticParseException) { index = -1; return false; }
        }
        var kind = opcode switch
        {
            PrototypeOpcode.PRINT => VmRuntimeStatementKind.Print,
            PrototypeOpcode.PRINTL => VmRuntimeStatementKind.PrintLine,
            PrototypeOpcode.PRINTW => VmRuntimeStatementKind.PrintWait,
            PrototypeOpcode.WAIT => VmRuntimeStatementKind.Wait,
            PrototypeOpcode.FORCEWAIT => VmRuntimeStatementKind.ForceWait,
            PrototypeOpcode.QUIT => VmRuntimeStatementKind.Quit,
            _ => (VmRuntimeStatementKind?)null,
        };
        if (kind is null)
        {
            if (HostStatements.Contains(opcode)) { index = statements.AddHost(opcode, operand); return true; }
            index = -1;
            return false;
        }
        index = statements.Add(kind.Value, operand);
        return true;
    }

    private static bool TryLinkTypedHost(PrototypeOpcode opcode, string operand, StructuralSemanticEnvironment environment,
        VmRuntimeStatementArenaBuilder statements, out int index)
    {
        index = -1;
        var typed = opcode is PrototypeOpcode.PRINTS or PrototypeOpcode.PRINTSL or PrototypeOpcode.HTML_PRINT or
            PrototypeOpcode.SETCOLOR or PrototypeOpcode.RESETCOLOR or PrototypeOpcode.ALIGNMENT or PrototypeOpcode.REDRAW or
            PrototypeOpcode.CLEARLINE or PrototypeOpcode.ONEINPUTS or PrototypeOpcode.SETANIMETIMER or
            PrototypeOpcode.GDISPOSE or PrototypeOpcode.GCREATEFROMFILE or PrototypeOpcode.GCREATE or
            PrototypeOpcode.GDRAWSPRITE or PrototypeOpcode.SPRITECREATE or PrototypeOpcode.SPRITECREATED or
            PrototypeOpcode.SPRITEDISPOSE;
        if (!typed) return false;
        try
        {
            var payloads = new List<SemanticPayload>();
            if (opcode == PrototypeOpcode.ALIGNMENT)
                payloads.Add(SemanticIrCompiler.CompileExpression($"\"{operand.Trim().Replace("\"", "\\\"", StringComparison.Ordinal)}\"", environment));
            else if (opcode == PrototypeOpcode.HTML_PRINT)
            {
                var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility);
                if (parts.Count is < 1 or > 2) return false;
                payloads.Add(SemanticIrCompiler.CompileExpression(parts[0].Trim(), environment));
                payloads.Add(SemanticIrCompiler.CompileExpression(parts.Count == 2 ? parts[1].Trim() : "0", environment));
            }
            else if (opcode is not (PrototypeOpcode.RESETCOLOR or PrototypeOpcode.ONEINPUTS))
            {
                foreach (var part in SemanticLexicalTokenStream.SplitTopLevel(operand, ',', environment.Compatibility))
                {
                    if (string.IsNullOrWhiteSpace(part)) return false;
                    payloads.Add(SemanticIrCompiler.CompileExpression(part.Trim(), environment));
                }
            }
            index = statements.AddTypedHost(opcode, payloads);
            return true;
        }
        catch (SemanticParseException) { return false; }
    }
    private static bool TrySplitIncrement(string operand, out string destination, out VmAssignmentOperator assignment)
    {
        var text = operand.Trim();
        assignment = VmAssignmentOperator.Add;
        if (text.StartsWith("++", StringComparison.Ordinal) || text.StartsWith("--", StringComparison.Ordinal))
        {
            assignment = text[0] == '+' ? VmAssignmentOperator.Add : VmAssignmentOperator.Subtract;
            destination = text[2..].Trim();
            return destination.Length != 0;
        }
        if (text.EndsWith("++", StringComparison.Ordinal) || text.EndsWith("--", StringComparison.Ordinal))
        {
            assignment = text[^1] == '+' ? VmAssignmentOperator.Add : VmAssignmentOperator.Subtract;
            destination = text[..^2].Trim();
            return destination.Length != 0;
        }
        destination = string.Empty;
        return false;
    }
    private static bool TrySplitBodyLocal(string operand, out string name, out string initializer, out bool isArray)
    {
        var equals = operand.IndexOf('=');
        var declaration = (equals < 0 ? operand : operand[..equals]).Trim();
        initializer = equals < 0 ? string.Empty : operand[(equals + 1)..].Trim();
        var comma = declaration.IndexOf(',');
        name = (comma < 0 ? declaration : declaration[..comma]).Trim();
        isArray = comma >= 0;
        return name.Length != 0;
    }
    private static VmExpressionFunctionTarget[] BindExpressionFunctionTargets(LinkedProgram program, FunctionCatalog catalog, bool ignoreCase,
        bool sparseRuntimeIds, Func<RuntimeFunctionId, VmFunctionSignature?>? signatureReader)
    {
        var result = new Dictionary<ulong, VmExpressionFunctionTarget>();
        foreach (var arena in new[] { program.SemanticArena, program.RuntimeStatements.OperandArena, program.CallArgumentArena, program.DynamicCallArena })
            foreach (var identity in arena.HostIdentities.Where(identity => identity.Kind == SemanticHostIdentityKind.Call))
            {
                var name = arena.ReadSymbol(arena.Symbols[identity.NameSymbolIndex]);
                var candidates = catalog.FindByName(name, ignoreCase).ToArray();
                if (candidates.Length == 0 || candidates.Any(candidate => catalog[candidate].Kind != FunctionKind.Method)) continue;
                var target = candidates.Length == 1 ? candidates[0] : -1;
                var localDescriptor = target < 0 ? -1 : Array.FindIndex(program.Descriptors, descriptor => descriptor.FunctionId == target);
                var signature = target < 0 ? null : signatureReader?.Invoke(new(target));
                var metadata = target < 0 ? FunctionRuntimeMetadata.Empty : localDescriptor >= 0 ? program.RuntimeMetadata[localDescriptor] : signature?.Metadata ?? FunctionRuntimeMetadata.Empty;
                var noYield = target >= 0 && (localDescriptor >= 0 ? !ContainsWait(program, localDescriptor) : signature?.NoYield == true);
                var binding = new VmExpressionFunctionTarget(identity.StableId, new(target), metadata.ReturnType, target >= 0 && metadata.ReturnType is not null && noYield);
                if (!result.TryAdd(identity.StableId, binding) && result[identity.StableId] != binding) throw new InvalidOperationException("ambiguous expression function identity");
            }
        return result.Values.ToArray();
    }
    private static bool ContainsWait(LinkedProgram program, int functionId)
    {
        var descriptor = program.Descriptors[functionId];
        for (var pc = 0; pc < descriptor.CodeLength; pc++)
        {
            var instruction = program.Code[descriptor.CodeStart + pc];
            if ((VmOpcode)instruction.Opcode != VmOpcode.Statement || (uint)instruction.Aux >= (uint)program.RuntimeStatements.Records.Length) continue;
            if (program.RuntimeStatements.Records[instruction.Aux].Kind is VmRuntimeStatementKind.PrintWait or VmRuntimeStatementKind.Wait or VmRuntimeStatementKind.ForceWait) return true;
        }
        return false;
    }
    private static bool TryLinkCallArguments(string suffix, StructuralSemanticEnvironment? environment, List<SemanticPayload> parts, List<int> records, bool measurePrefixScan, ref int additionCount, ref int accumulatedRecordCount)
    {
        var text = StripCallComment(suffix).Trim();
        if (text.Length == 0 || text[0] == ';') return true;
        if (environment is null) return false;
        string arguments;
        if (text[0] == '(')
        {
            if (text.Length < 2 || text[^1] != ')') return false;
            arguments = text[1..^1];
        }
        else if (text[0] == ',') arguments = text[1..];
        else return false; // target subnames stay explicit unsupported until their typed IR exists.
        foreach (var raw in SemanticLexicalTokenStream.SplitTopLevel(arguments, ',', environment.Compatibility))
        {
            var value = raw.Trim();
            if (value.Length == 0) { records.Add(-1); continue; }
            try
            {
                var payload = SemanticIrCompiler.CompileExpression(value, environment);
                // [Emuera改修:NEXT-3D-R1.5P3A 2026-09-04]
                // P2実測ではCALL引数prefix scanがFunctionLink約23.9秒中約23.1秒を占めた。
                // payload順序・record baseは変えず、反復prefix sumを単調累積値へ置換する。
                // 失敗途中の既存side effect orderingも維持し、旧Sumは診断を含めて再実行しない。
                var prefixRecords = accumulatedRecordCount;
                var nextRecordCount = checked(accumulatedRecordCount + payload.Records.Length);
                records.Add(prefixRecords);
                parts.Add(payload);
                accumulatedRecordCount = nextRecordCount;
                if (measurePrefixScan) additionCount++;
            }
            catch (SemanticParseException) { return false; }
        }
        return true;
    }
    private static string StripCallComment(string value)
    {
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            if (quote != '\0') { if (value[i] == '\\') i++; else if (value[i] == quote) quote = '\0'; }
            else if (value[i] is '\'' or '"') quote = value[i];
            else if (value[i] == ';') return value[..i];
        }
        return value;
    }
    private static bool TrySplitAssignment(string operand, out string destination, out VmAssignmentOperator assignment, out string source)
    {
        destination = source = string.Empty; assignment = VmAssignmentOperator.Assign;
        var quote = '\0'; var depth = 0;
        for (var i = 0; i < operand.Length; i++)
        {
            var c = operand[i];
            if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
            if (depth == 0 && c == '\'' && i + 1 < operand.Length && operand[i + 1] == '=')
            {
                destination = operand[..i].Trim(); source = operand[(i + 2)..].Trim(); assignment = VmAssignmentOperator.AssignString;
                return destination.Length != 0 && source.Length != 0;
            }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c is '(' or '[' or '{') { depth++; continue; }
            if (c is ')' or ']' or '}') { depth--; continue; }
            if (depth != 0) continue;
            var pair = i + 1 < operand.Length ? operand.Substring(i, 2) : string.Empty;
            assignment = pair switch
            {
                "+=" => VmAssignmentOperator.Add, "-=" => VmAssignmentOperator.Subtract,
                "*=" => VmAssignmentOperator.Multiply, "/=" => VmAssignmentOperator.Divide,
                "%=" => VmAssignmentOperator.Modulo, "|=" => VmAssignmentOperator.BitOr,
                "&=" => VmAssignmentOperator.BitAnd, "^=" => VmAssignmentOperator.BitXor,
                _ => VmAssignmentOperator.Assign,
            };
            var length = pair is "+=" or "-=" or "*=" or "/=" or "%=" or "|=" or "&=" or "^=" ? 2 : c == '=' && (i == 0 || operand[i - 1] is not ('=' or '!' or '<' or '>')) ? 1 : 0;
            if (length == 0) continue;
            destination = operand[..i].Trim(); source = operand[(i + length)..].Trim();
            return destination.Length != 0;
        }
        return false;
    }
    private static bool IsKnownStringAssignmentTarget(string destination)
    {
        var separator = destination.IndexOfAny([':', '@']);
        var name = (separator < 0 ? destination : destination[..separator]).Trim();
        return name.Equals("STR", StringComparison.OrdinalIgnoreCase) || name.Equals("TSTR", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SAVESTR", StringComparison.OrdinalIgnoreCase) || name.Equals("GLOBALS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CSTR", StringComparison.OrdinalIgnoreCase) || name.Equals("RESULTS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase);
    }
    private static bool TrySplitTimes(string operand, CompilerCompatibilityOptions options, out string destination, out double multiplier)
    {
        destination = string.Empty; multiplier = 0;
        var parts = SemanticLexicalTokenStream.SplitTopLevel(operand, ',', options);
        return parts.Count == 2 && (destination = parts[0].Trim()).Length != 0 && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier);
    }
    private static VmInstruction Linked(PrototypeInstruction p, VmOpcode opcode, int aux = -1) => new((ushort)opcode, (ushort)p.Flags, p.OperandOffset, p.OperandLength, aux);
    private sealed class OpenFrame
    { public VmStructuralKind Family; public int HeaderRecordIndex, HeaderPc, Depth; public List<int> Clauses = [], Cases = [], LoopControlRecordIndices = []; public OpenFrame(VmStructuralKind family, int headerRecordIndex, int headerPc, int depth) => (Family, HeaderRecordIndex, HeaderPc, Depth) = (family, headerRecordIndex, headerPc, depth); }
    private sealed record StructureResult(StructuralLinkRecord[] Records, IfGroupDescriptor[] IfGroups, IfClauseRecord[] IfClauses, SelectGroupDescriptor[] SelectGroups, SelectCaseRecord[] SelectCases, LoopDescriptor[] Loops, bool Invalid);
    private static StructureResult LinkStructure(int functionId, IReadOnlyList<PrototypeInstruction> instructions, List<StructuralLinkRecord> records, List<string> diagnostics, List<StructuralDiagnostic> structuralDiagnostics, int ifGroupBase, int ifClauseBase, int selectGroupBase, int selectCaseBase, int loopBase)
    {
        var stack = new List<OpenFrame>(); var invalid = false; var depth = 0; var ifGroups = new List<IfGroupDescriptor>(); var ifClauses = new List<IfClauseRecord>(); var selectGroups = new List<SelectGroupDescriptor>(); var selectCases = new List<SelectCaseRecord>(); var loops = new List<LoopDescriptor>(); var indexByPc = records.Select((x, i) => (x.Pc, Index: i)).ToDictionary(x => x.Pc, x => x.Index);
        void Bad(int pc, string text) { invalid = true; diagnostics.Add($"function={functionId} pc={pc} malformed={text}"); structuralDiagnostics.Add(new(new(functionId), pc, StructuralClassification.InvalidStructure, text)); }
        void Warn(int pc, string text) { diagnostics.Add($"function={functionId} pc={pc} warning={text}"); structuralDiagnostics.Add(new(new(functionId), pc, StructuralClassification.ValidWithStructuralWarning, text)); }
        foreach (var pair in indexByPc.OrderBy(x => x.Key))
        {
            var pc = pair.Key; var op = instructions[pc].Opcode; var recordIndex = pair.Value; var record = records[recordIndex];
            if (Openers.TryGetValue(op, out var family)) { depth++; stack.Add(new(family, recordIndex, pc, depth)); records[recordIndex] = record with { Depth = depth }; continue; }
            if (op is PrototypeOpcode.ELSEIF or PrototypeOpcode.ELSE) { if (stack.Count == 0 || stack[^1].Family != VmStructuralKind.If) { Bad(pc, op.ToString()); continue; } var frame = stack[^1]; if (frame.Clauses.Any(x => instructions[x].Opcode == PrototypeOpcode.ELSE)) Warn(pc, op == PrototypeOpcode.ELSE ? "duplicate-ELSE" : "ELSEIF-after-ELSE"); frame.Clauses.Add(pc); records[recordIndex] = record with { Depth = depth }; continue; }
            if (op is PrototypeOpcode.CASE or PrototypeOpcode.CASEELSE) { if (stack.Count == 0 || stack[^1].Family != VmStructuralKind.SelectCase) { Bad(pc, op.ToString()); continue; } var frame = stack[^1]; if (frame.Cases.Any(x => instructions[x].Opcode == PrototypeOpcode.CASEELSE)) Warn(pc, op == PrototypeOpcode.CASE ? "CASE-after-CASEELSE" : "duplicate-CASEELSE"); frame.Cases.Add(pc); records[recordIndex] = record with { Depth = depth }; continue; }
            if (Closers.TryGetValue(op, out var close))
            {
                if (stack.Count == 0 || stack[^1].Family != close) { Bad(pc, $"crossed-or-missing-{op}"); continue; }
                var frame = stack[^1]; stack.RemoveAt(stack.Count - 1); depth--; records[frame.HeaderRecordIndex] = records[frame.HeaderRecordIndex] with { TargetPc = pc, AuxiliaryPc = pc + 1 }; records[recordIndex] = record with { TargetPc = frame.HeaderPc, Depth = depth };
                if (close == VmStructuralKind.If) { var clausePcs = new[] { frame.HeaderPc }.Concat(frame.Clauses).ToArray(); var group = checked(ifGroupBase + ifGroups.Count); var first = checked(ifClauseBase + ifClauses.Count); for (var i = 0; i < clausePcs.Length; i++) { var clausePc = clausePcs[i]; ifClauses.Add(new(functionId, clausePc, Kinds[instructions[clausePc].Opcode])); var ix = indexByPc[clausePc]; records[ix] = records[ix] with { TargetPc = i + 1 < clausePcs.Length ? clausePcs[i + 1] : pc, AuxiliaryPc = pc + 1, GroupIndex = group }; } records[recordIndex] = records[recordIndex] with { GroupIndex = group }; ifGroups.Add(new(functionId, frame.HeaderPc, first, clausePcs.Length, pc + 1)); }
                else if (close == VmStructuralKind.SelectCase) { var casePcs = frame.Cases.ToArray(); var group = checked(selectGroupBase + selectGroups.Count); var first = checked(selectCaseBase + selectCases.Count); records[frame.HeaderRecordIndex] = records[frame.HeaderRecordIndex] with { GroupIndex = group }; for (var i = 0; i < casePcs.Length; i++) { var casePc = casePcs[i]; selectCases.Add(new(functionId, casePc, Kinds[instructions[casePc].Opcode])); var ix = indexByPc[casePc]; records[ix] = records[ix] with { TargetPc = i + 1 < casePcs.Length ? casePcs[i + 1] : pc, AuxiliaryPc = pc + 1, GroupIndex = group }; } records[recordIndex] = records[recordIndex] with { GroupIndex = group }; selectGroups.Add(new(functionId, frame.HeaderPc, first, casePcs.Length, pc + 1)); }
                else { var loop = new LoopDescriptor(functionId, close, frame.HeaderPc, frame.HeaderPc + 1, pc, pc + 1, close == VmStructuralKind.Do ? pc : close == VmStructuralKind.While ? frame.HeaderPc : pc, close is VmStructuralKind.Repeat or VmStructuralKind.For ? LoopDescriptorFlags.BreakAdvancesCounter : LoopDescriptorFlags.None); var loopIndex = checked(loopBase + loops.Count); loops.Add(loop); records[frame.HeaderRecordIndex] = records[frame.HeaderRecordIndex] with { LoopIndex = loopIndex }; records[recordIndex] = records[recordIndex] with { LoopIndex = loopIndex }; foreach (var controlIndex in frame.LoopControlRecordIndices) { var control = records[controlIndex]; var target = control.Kind == VmStructuralKind.Break ? loop.ExitPc : close is VmStructuralKind.Repeat or VmStructuralKind.For ? loop.BodyEntryPc : loop.ContinueCheckPc; records[controlIndex] = control with { TargetPc = target, AuxiliaryPc = loop.ContinueCheckPc, LoopIndex = loopIndex }; } }
            }
            if (op is PrototypeOpcode.BREAK or PrototypeOpcode.CONTINUE) { var frame = stack.LastOrDefault(x => LoopKinds.Contains(x.Family)); if (frame is null) Bad(pc, $"loop-control-outside-{op}"); else { frame.LoopControlRecordIndices.Add(recordIndex); records[recordIndex] = record with { TargetPc = -1, AuxiliaryPc = -1, LoopIndex = -1, Depth = depth }; } }
        }
        foreach (var frame in stack) Bad(frame.HeaderPc, $"missing-close-{frame.Family}"); return new(records.ToArray(), ifGroups.ToArray(), ifClauses.ToArray(), selectGroups.ToArray(), selectCases.ToArray(), loops.ToArray(), invalid);
    }
}

// Phase 2 owns structural control and stable identities; Phase 3 owns expression/format IR.
// Keep semantic values behind this interface so LinkedProgram does not retain raw operand strings
// or Legacy parser objects just to execute control flow.
public readonly record struct VmCountedLoopEntry(long Counter, long End, long Step);
public enum VmSemanticStatus : byte { Success, Unavailable, Fault }
public enum VmSemanticFault : byte { None, DivideByZero, ModuloByZero, StringMultiplierOutOfRange }
public readonly record struct VmSemanticIntResult(VmSemanticStatus Status, long Value, VmSemanticFault Fault)
{
    public bool Available => Status == VmSemanticStatus.Success;
    public static VmSemanticIntResult NotAvailable => new(VmSemanticStatus.Unavailable, 0, VmSemanticFault.None);
    public static VmSemanticIntResult From(long value) => new(VmSemanticStatus.Success, value, VmSemanticFault.None);
    public static VmSemanticIntResult Faulted(VmSemanticFault fault) => new(VmSemanticStatus.Fault, 0, fault);
}
public readonly record struct VmSemanticCaseResult(VmSemanticStatus Status, int Ordinal, VmSemanticFault Fault)
{
    public bool Available => Status == VmSemanticStatus.Success;
    public static VmSemanticCaseResult NotAvailable => new(VmSemanticStatus.Unavailable, -1, VmSemanticFault.None);
    public static VmSemanticCaseResult From(int ordinal) => new(VmSemanticStatus.Success, ordinal, VmSemanticFault.None);
    public static VmSemanticCaseResult Faulted(VmSemanticFault fault) => new(VmSemanticStatus.Fault, -1, fault);
}
public readonly record struct VmCountedLoopResult(VmSemanticStatus Status, VmCountedLoopEntry Entry, VmSemanticFault Fault)
{
    public bool Available => Status == VmSemanticStatus.Success;
    public static VmCountedLoopResult NotAvailable => new(VmSemanticStatus.Unavailable, default, VmSemanticFault.None);
    public static VmCountedLoopResult From(VmCountedLoopEntry entry) => new(VmSemanticStatus.Success, entry, VmSemanticFault.None);
    public static VmCountedLoopResult Faulted(VmSemanticFault fault) => new(VmSemanticStatus.Fault, default, fault);
}

public interface IVmStructuralSemantics
{
    VmSemanticIntResult EvaluateInt(RuntimeFunctionId functionId, int pc, VmStructuralKind kind);
    // Return a relative ordinal in the CASE-only prefix, or -1 when no CASE matches.
    // VmMachine truncates at the first CASEELSE so Legacy ordering is preserved.
    VmSemanticCaseResult SelectCase(RuntimeFunctionId functionId, int groupIndex, ReadOnlySpan<SelectCaseRecord> cases);
    // BeginCounted performs Legacy's counter = Start and returns the resulting counter plus captured End/Step.
    VmCountedLoopResult BeginCounted(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop);
    // AdvanceCounted dynamically re-resolves the counter lvalue in the current scope and applies captured Step.
    VmSemanticIntResult AdvanceCounted(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop, long step);
}

// Counted FOR/REPEAT state belongs to the source loop identity, not to an invocation frame.
// Legacy LoopInstructionLine is shared by recursive re-entry, so the same global LoopIndex must
// overwrite the previously captured End/Step. Counter lvalue identity is intentionally not stored
// here: its dynamic indices must be re-evaluated in the current scope when semantic execution lands.
public sealed class LoopRuntimeState
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct LoopRuntimeCell
    {
        public long CapturedEnd;
        public long CapturedStep;
        public int Initialized;
    }

    private readonly LoopDescriptor[] loops;
    private readonly LoopRuntimeCell[] cells;

    public LoopRuntimeState(LinkedProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        loops = program.Loops;
        cells = new LoopRuntimeCell[loops.Length];
    }

    public int Count => cells.Length;

    public void CaptureCounted(int loopIndex, long end, long step)
    {
        ValidateCounted(loopIndex);
        ref var cell = ref cells[loopIndex];
        cell.CapturedEnd = end;
        cell.CapturedStep = step;
        cell.Initialized = 1;
    }

    public bool TryGetCounted(int loopIndex, out long end, out long step)
    {
        ValidateCounted(loopIndex);
        ref var cell = ref cells[loopIndex];
        if (cell.Initialized == 0)
        {
            end = 0;
            step = 0;
            return false;
        }
        end = cell.CapturedEnd;
        step = cell.CapturedStep;
        return true;
    }

    private void ValidateCounted(int loopIndex)
    {
        if ((uint)loopIndex >= (uint)loops.Length)
            throw new ArgumentOutOfRangeException(nameof(loopIndex));
        if (loops[loopIndex].LoopKind is not (VmStructuralKind.Repeat or VmStructuralKind.For))
            throw new InvalidOperationException($"LoopIndex {loopIndex} is not a counted FOR/REPEAT loop");
    }
}

public sealed class VmMachine : IVmFrameVariables, IVmFrameVariableTypes, IVmExpressionFunctionInvoker
{
    private readonly LinkedProgram? program;
    private readonly IVmContextOwner? contextOwner;
    private readonly IVmStructuralSemantics? structuralSemantics;
    private readonly IVmRuntimeEffects? runtimeEffects;
    private readonly IVmFrameState? frameState;
    private readonly IVmDynamicCallResolver? dynamicCallResolver;
    private readonly VmSemanticExecutor? runtimeSemantics;
    private readonly Dictionary<(int FunctionId, int Pc), VmCallSiteRecord> callSites;
    private readonly Dictionary<ulong, VmExpressionFunctionTarget> expressionFunctionTargets;
    // Do not clear this in Run/TryPush/Return. Legacy source LoopInstructionLine state survives
    // calls and recursive re-entry, and a later traversal of the same source loop overwrites it.
    private readonly LoopRuntimeState? loopRuntime;
    private readonly Dictionary<(int FunctionId, string Name), VmSemanticValue[]> staticPrivate = [];
    private VmFrame[] stack = new VmFrame[16];
    private VmInvocationFrame?[] invocations = new VmInvocationFrame?[16];
    private int stackCount;
    private long nextFrameId;
    private VmSemanticValue expressionReturnValue = VmSemanticValue.Unavailable;
    private VmInputContinuation? pendingInput;
    public VmSemanticValue LastReturnValue { get; private set; } = VmSemanticValue.Unavailable;
    public string LastTerminalFaultMessage { get; private set; } = string.Empty;
    public int LastTerminalFaultFunctionId { get; private set; } = -1;
    public int LastTerminalFaultPc { get; private set; } = -1;
    public int LastTerminalFaultSourceLine { get; private set; } = -1;
    public int FrameDepth => contextOwner?.FrameDepth ?? stackCount;
    public VmFrame CurrentFrame => contextOwner?.CurrentFrame ?? (stackCount == 0 ? default : stack[stackCount - 1]);
    public int MaxFrameDepth { get; private set; }
    public long StaticCallsExecuted { get; private set; }
    public long DynamicCallsExecuted { get; private set; }
    public long InputResumeAttempts { get; private set; }
    public long InputResumeCompleted { get; private set; }
    public VmFrame[] ActiveFrames => contextOwner?.ActiveFrames ?? stack[..stackCount].ToArray();
    public int LastExecutedFunctionId { get; private set; } = -1;
    public int LastExecutedPc { get; private set; } = -1;
    public VmOpcode LastExecutedOpcode { get; private set; }
    public object? SemanticDiagnostic => runtimeSemantics is null ? null : new
    {
        runtimeSemantics.LastStatus,
        runtimeSemantics.LastFault,
        runtimeSemantics.LastEvaluationArena,
        runtimeSemantics.LastRecordIndex,
        runtimeSemantics.LastNodeIndex,
        runtimeSemantics.LastNodeKind,
        runtimeSemantics.LastSemanticOperator,
        runtimeSemantics.LastHostOperation,
        runtimeSemantics.LastHostSymbol,
        runtimeSemantics.LastHostBindingPresent,
        runtimeSemantics.LastHostResult,
    };

    public VmMachine(LinkedProgram program, IVmStructuralSemantics? structuralSemantics = null, IVmRuntimeEffects? runtimeEffects = null, IVmFrameState? frameState = null, IVmDynamicCallResolver? dynamicCallResolver = null)
        : this(program, structuralSemantics, runtimeEffects, frameState, VmMachineProgramLookup.Build(program), dynamicCallResolver)
    {
    }

    public VmMachine(LinkedProgram program, IVmStructuralSemantics? structuralSemantics, IVmRuntimeEffects? runtimeEffects, IVmFrameState? frameState, VmMachineProgramLookup programLookup, IVmDynamicCallResolver? dynamicCallResolver = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(programLookup);
        programLookup.EnsureFor(program);
        this.program = program;
        this.structuralSemantics = structuralSemantics;
        this.runtimeEffects = runtimeEffects;
        this.frameState = frameState;
        this.dynamicCallResolver = dynamicCallResolver;
        runtimeSemantics = structuralSemantics as VmSemanticExecutor;
        if (runtimeSemantics is not null) runtimeSemantics.FrameVariables = this;
        if (runtimeSemantics is not null) runtimeSemantics.ExpressionFunctionInvoker = this;
        loopRuntime = new(program);
        callSites = programLookup.CallSiteMap;
        expressionFunctionTargets = programLookup.ExpressionFunctionTargetMap;
    }

    public VmMachine(IVmContextOwner contextOwner, IVmRuntimeEffects? runtimeEffects = null, IVmDynamicCallResolver? dynamicCallResolver = null)
    {
        ArgumentNullException.ThrowIfNull(contextOwner);
        this.contextOwner = contextOwner;
        this.runtimeEffects = runtimeEffects;
        this.dynamicCallResolver = dynamicCallResolver;
        callSites = [];
        expressionFunctionTargets = [];
    }

    public VmMachine(IVmContextOwner contextOwner, IVmSemanticHost semanticHost,
        IVmRuntimeEffects? runtimeEffects = null, IVmDynamicCallResolver? dynamicCallResolver = null)
        : this(contextOwner, runtimeEffects, dynamicCallResolver)
    {
        runtimeSemantics = new VmSemanticExecutor(semanticHost);
        structuralSemantics = runtimeSemantics;
        runtimeSemantics.FrameVariables = this;
        runtimeSemantics.ExpressionFunctionInvoker = this;
    }

    private LinkedProgram ActiveProgram
    {
        get
        {
            if (contextOwner is null) return program!;
            if (!contextOwner.TryGetCurrentContext(out var context, out _)) throw new InvalidOperationException("owner context unavailable");
            return context.Program;
        }
    }

    public VmStopReason Run(RuntimeFunctionId entryFunctionId, int maxSteps = 100_000)
    {
        var start = Start(entryFunctionId);
        return start == VmStopReason.Returned ? Continue(maxSteps) : start;
    }

    // The embedding runtime may have already evaluated Legacy CALL/JUMP
    // actuals before it reaches the VM dispatch boundary.
    public VmStopReason Run(RuntimeFunctionId entryFunctionId, ReadOnlySpan<VmSemanticValue> actuals, int maxSteps = 100_000)
    {
        var start = Start(entryFunctionId, actuals);
        return start == VmStopReason.Returned ? Continue(maxSteps) : start;
    }

    // Start resets invocation state exactly once. Continue deliberately preserves frames, PCs and loop cells.
    public VmStopReason Start(RuntimeFunctionId entryFunctionId)
        => Start(entryFunctionId, [], FunctionKind.Normal);

    public VmStopReason Start(RuntimeFunctionId entryFunctionId, ReadOnlySpan<VmSemanticValue> actuals)
        => Start(entryFunctionId, actuals, FunctionKind.Normal);

    public VmStopReason Start(RuntimeFunctionId entryFunctionId, FunctionKind entryKind)
        => Start(entryFunctionId, [], entryKind);

    public VmStopReason Start(RuntimeFunctionId entryFunctionId, ReadOnlySpan<VmSemanticValue> actuals, FunctionKind entryKind)
    {
        if (contextOwner is not null)
        {
            if (contextOwner.IsTerminal || contextOwner.FrameDepth != 0) return VmStopReason.TerminalFault;
            var lease = contextOwner.PrepareInvocation(entryFunctionId, VmReturnKind.Normal, entryKind);
            if (!lease.Ready) return AdmissionReason(lease.Status);
            if (!contextOwner.CommitFrame(ref lease, actuals, out _))
                return OwnerFault(VmStopReason.SemanticNotAvailable, "entry actual binding failed");
            MaxFrameDepth = Math.Max(MaxFrameDepth, contextOwner.MaxFrameDepth);
            return VmStopReason.Returned;
        }
        stackCount = 0;
        LastReturnValue = VmSemanticValue.Unavailable;
        LastTerminalFaultMessage = string.Empty;
        LastTerminalFaultFunctionId = LastTerminalFaultPc = LastTerminalFaultSourceLine = -1;
        if (!TryPush(entryFunctionId.Value, VmReturnKind.Normal, actuals.ToArray(), out var reason))
            return reason;
        return VmStopReason.Returned;
    }

    public VmStopReason Continue(int maxSteps = 100_000)
    {
        var reason = ContinueToDepth(0, maxSteps);
        if (reason != VmStopReason.WaitingForInput || contextOwner is null || pendingInput is not { } input ||
            runtimeEffects is not IVmOwnedInputRuntimeEffects inputHost)
            return reason;
        if (!contextOwner.CommitInput(input))
            return OwnerFault(VmStopReason.TerminalFault, "input suspension commit failed");
        contextOwner.PublishInput(input, respond => inputHost.PublishInput(input, respond),
            value => TryResumeInput(input, value, out _));
        return VmStopReason.WaitingForInput;
    }

    private VmStopReason ContinueToDepth(int returnDepth, int maxSteps)
    {
        VmStopReason reason;
        for (var steps = 0; steps < maxSteps; steps++)
        {
            if (contextOwner?.IsTerminal == true)
                return VmStopReason.TerminalFault;
            if (contextOwner is not null && !contextOwner.TryConsumeFuel())
                return OwnerFault(VmStopReason.StepLimit, "shared owner fuel exhausted");
            var depth = FrameDepth;
            if (depth <= returnDepth)
                return VmStopReason.Returned;
            var frame = CurrentFrame;
            var descriptor = GetDescriptor(frame.FunctionId);
            if (descriptor.State != VmFunctionState.ExecutableReady)
                return OwnerFault(StateReason(descriptor.State), "function is not executable");
            if (frame.Pc == descriptor.CodeLength)
            {
                if (!Return())
                    return OwnerFault(VmStopReason.StackUnderflow, "natural return underflow");
                continue;
            }
            if (frame.Pc < 0 || frame.Pc > descriptor.CodeLength)
                return OwnerFault(VmStopReason.InvalidLocalPc, "invalid local PC");

            var executingFunctionId = frame.FunctionId;
            var executingPc = frame.Pc;
            var instruction = ActiveProgram.Code[descriptor.CodeStart + executingPc];
            LastExecutedFunctionId = executingFunctionId;
            LastExecutedPc = executingPc;
            LastExecutedOpcode = (VmOpcode)instruction.Opcode;
            if (!SetPc(executingPc + 1)) return OwnerFault(VmStopReason.InvalidLocalPc, "next PC rejected");
            try
            {
            switch ((VmOpcode)instruction.Opcode)
            {
                case VmOpcode.Nop:
                    break;
                case VmOpcode.Halt:
                    return VmStopReason.Halted;
                case VmOpcode.Branch:
                    if (!SetPc(instruction.Aux)) return OwnerFault(VmStopReason.InvalidLocalPc, "branch target rejected");
                    break;
                case VmOpcode.Call:
                    StaticCallsExecuted++;
                    if (contextOwner is not null)
                    {
                        var lease = contextOwner.PrepareInvocation(new(instruction.Aux), VmReturnKind.Normal, FunctionKind.Normal);
                        if (!lease.Ready) return OwnerFault(AdmissionReason(lease.Status), "static call admission rejected");
                        if (!TryEvaluateCallArguments(executingFunctionId, executingPc, out var callActuals, out reason)) { contextOwner.Cancel(ref lease); return OwnerFault(reason, "static actual evaluation failed"); }
                        if (!contextOwner.CommitFrame(ref lease, callActuals, out _)) return OwnerFault(VmStopReason.SemanticNotAvailable, "static call commit failed");
                        MaxFrameDepth = Math.Max(MaxFrameDepth, contextOwner.MaxFrameDepth);
                    }
                    else
                    {
                        if (!TryEvaluateCallArguments(executingFunctionId, executingPc, out var callActuals, out reason)) return reason;
                        if (!TryPush(instruction.Aux, VmReturnKind.Normal, callActuals, out reason)) return reason;
                    }
                    break;
                case VmOpcode.Jump:
                    StaticCallsExecuted++;
                    if (contextOwner is not null)
                    {
                        var lease = contextOwner.PrepareInvocation(new(instruction.Aux), VmReturnKind.Propagate, FunctionKind.Normal);
                        if (!lease.Ready) return OwnerFault(AdmissionReason(lease.Status), "jump admission rejected");
                        if (!TryEvaluateCallArguments(executingFunctionId, executingPc, out var jumpActuals, out reason)) { contextOwner.Cancel(ref lease); return OwnerFault(reason, "jump actual evaluation failed"); }
                        if (!contextOwner.CommitFrame(ref lease, jumpActuals, out _)) return OwnerFault(VmStopReason.SemanticNotAvailable, "jump commit failed");
                        MaxFrameDepth = Math.Max(MaxFrameDepth, contextOwner.MaxFrameDepth);
                    }
                    else
                    {
                        if (!TryEvaluateCallArguments(executingFunctionId, executingPc, out var jumpActuals, out reason)) return reason;
                        if (!TryPush(instruction.Aux, VmReturnKind.Propagate, jumpActuals, out reason)) return reason;
                    }
                    break;
                case VmOpcode.Return:
                    if (!Return()) return OwnerFault(VmStopReason.StackUnderflow, "return underflow");
                    break;
                case VmOpcode.Structural:
                {
                    var structuralReason = ExecuteStructural(instruction.Aux, executingFunctionId, executingPc);
                    if (structuralReason.HasValue) return OwnerFault(structuralReason.Value, "structural execution failed");
                    break;
                }
                case VmOpcode.Statement:
                {
                    var statementReason = ExecuteRuntimeStatement(instruction.Aux, executingFunctionId, executingPc);
                    if (statementReason.HasValue) return OwnerFault(statementReason.Value, "runtime statement stopped");
                    break;
                }
                case VmOpcode.DynamicCall:
                {
                    DynamicCallsExecuted++;
                    var dynamicReason = ExecuteDynamicCall(instruction.Aux, executingFunctionId, executingPc);
                    if (dynamicReason.HasValue) return OwnerFault(dynamicReason.Value, "dynamic call failed");
                    break;
                }
                case VmOpcode.SemanticBarrier:
                    return OwnerFault(VmStopReason.SemanticNotAvailable, "semantic barrier");
                default:
                    return OwnerFault(VmStopReason.UnsupportedControl, "unsupported opcode");
            }
            }
            catch (Exception ex) when (contextOwner is not null)
            {
                return OwnerFault(VmStopReason.TerminalFault, "host/runtime exception: " + ex.GetType().Name);
            }
        }
        return OwnerFault(VmStopReason.StepLimit, "step limit");
    }

    public bool HasExpressionFunction(SemanticHostIdentity identity) => contextOwner is null
        ? expressionFunctionTargets.ContainsKey(identity.StableId)
        : TryGetExpressionTarget(identity, out _);

    public bool UsesPreActualAdmission => contextOwner is not null;

    public VmExpressionPreparation PrepareExpressionFunction(SemanticHostIdentity identity)
    {
        if (contextOwner is null || !contextOwner.TryGetCurrentContext(out var caller, out var callerFrame) ||
            !TryGetExpressionTarget(identity, out var target))
            return new(VmExpressionPreparationStatus.NotUserMethod);
        if (target.Target.Value < 0)
            return new(VmExpressionPreparationStatus.NotUserMethod);
        if (!target.Callable)
            return new(VmExpressionPreparationStatus.Rejected);
        var lease = contextOwner.PrepareInvocation(target.Target, VmReturnKind.Expression, FunctionKind.Method);
        return lease.Ready
            ? new(VmExpressionPreparationStatus.Ready, lease, target.ReturnType, contextOwner.FrameDepth, callerFrame)
            : new(VmExpressionPreparationStatus.Rejected);
    }

    public void CancelExpressionFunction(ref VmExpressionPreparation preparation)
    {
        if (contextOwner is not null && preparation.Status == VmExpressionPreparationStatus.Ready)
        {
            var lease = preparation.Lease;
            contextOwner.Cancel(ref lease);
            preparation = preparation with { Lease = lease };
        }
        preparation = preparation with { Status = VmExpressionPreparationStatus.Rejected };
    }

    public bool TryInvokePreparedExpressionFunction(ref VmExpressionPreparation preparation,
        ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (contextOwner is null || preparation.Status != VmExpressionPreparationStatus.Ready ||
            !contextOwner.TryGetCurrentContext(out _, out var callerFrame) || callerFrame != preparation.CallerFrame)
        {
            CancelExpressionFunction(ref preparation);
            return false;
        }
        expressionReturnValue = VmSemanticValue.Unavailable;
        var lease = preparation.Lease;
        if (!contextOwner.CommitFrame(ref lease, arguments, out _))
        {
            preparation = preparation with { Lease = lease };
            CancelExpressionFunction(ref preparation);
            return false;
        }
        preparation = preparation with { Status = VmExpressionPreparationStatus.Rejected, Lease = lease };
        MaxFrameDepth = Math.Max(MaxFrameDepth, contextOwner.MaxFrameDepth);
        if (ContinueToDepth(preparation.CallerDepth, int.MaxValue) != VmStopReason.Returned ||
            contextOwner.IsTerminal ||
            !contextOwner.TryGetCurrentContext(out _, out var restored) || restored != preparation.CallerFrame)
            return false;
        if (preparation.ReturnType == RuntimeMetadataValueType.Integer && !expressionReturnValue.TryGetInteger(out _) ||
            preparation.ReturnType == RuntimeMetadataValueType.String && !expressionReturnValue.TryGetString(out _))
            return false;
        value = expressionReturnValue;
        return true;
    }

    public bool TryInvokeExpressionFunction(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (!expressionFunctionTargets.TryGetValue(identity.StableId, out var target) || !target.Callable || target.Target.Value < 0 || stackCount == 0)
            return false;
        expressionReturnValue = VmSemanticValue.Unavailable;
        var callerDepth = stackCount;
        if (!TryPush(target.Target.Value, VmReturnKind.Expression, arguments.ToArray(), out _)) return false;
        if (ContinueToDepth(callerDepth, 100_000) != VmStopReason.Returned) return false;
        if (target.ReturnType == RuntimeMetadataValueType.Integer && !expressionReturnValue.TryGetInteger(out _)) return false;
        if (target.ReturnType == RuntimeMetadataValueType.String && !expressionReturnValue.TryGetString(out _)) return false;
        value = expressionReturnValue;
        return true;
    }

    private bool TryGetExpressionTarget(SemanticHostIdentity identity, out VmExpressionFunctionTarget target)
    {
        var values = ActiveProgram.ExpressionFunctionTargets;
        for (var i = 0; i < values.Length; i++)
            if (values[i].StableId == identity.StableId) { target = values[i]; return true; }
        target = default;
        return false;
    }

    private bool TryEvaluationContext(out VmEvaluationContext context)
    {
        context = default;
        return contextOwner is not null && contextOwner.TryGetCurrentContext(out var function, out var frame) &&
            Return(new(function, frame, contextOwner.Stamp), out context);
    }

    private bool EvaluateRecord(SemanticPayload arena, int record, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (runtimeSemantics is null) return false;
        if (contextOwner is null) return runtimeSemantics.TryEvaluateRuntimeRecord(arena, record, out value);
        if (!TryEvaluationContext(out var context) || !contextOwner.TryBeginEvaluation(context)) return false;
        try { return runtimeSemantics.TryEvaluateRuntimeRecord(context, arena, record, out value); }
        finally { contextOwner.EndEvaluation(context); }
    }

    private bool ResolveLValue(SemanticPayload arena, int record, out VmResolvedLValue value)
    {
        value = default;
        if (runtimeSemantics is null) return false;
        if (contextOwner is null) return runtimeSemantics.TryResolveRuntimeLValue(arena, record, out value);
        if (!TryEvaluationContext(out var context) || !contextOwner.TryBeginEvaluation(context)) return false;
        try { return runtimeSemantics.TryResolveRuntimeLValue(context, arena, record, out value); }
        finally { contextOwner.EndEvaluation(context); }
    }

    private bool GetAssignmentTargetKind(SemanticPayload arena, int record, out VmSemanticValueKind kind)
    {
        kind = VmSemanticValueKind.Unavailable;
        if (runtimeSemantics is null) return false;
        if (contextOwner is null) return runtimeSemantics.TryGetRuntimeAssignmentTargetKind(arena, record, out kind);
        if (!TryEvaluationContext(out var context) || !contextOwner.TryBeginEvaluation(context)) return false;
        try { return runtimeSemantics.TryGetRuntimeAssignmentTargetKind(context, arena, record, out kind); }
        finally { contextOwner.EndEvaluation(context); }
    }

    private VmSemanticIntResult EvaluateStructuralInt(RuntimeFunctionId functionId, int pc, VmStructuralKind kind)
    {
        if (contextOwner is null || runtimeSemantics is null) return structuralSemantics!.EvaluateInt(functionId, pc, kind);
        if (!TryEvaluationContext(out var context) || !contextOwner.TryBeginEvaluation(context)) return VmSemanticIntResult.NotAvailable;
        try { return runtimeSemantics.EvaluateInt(context, functionId, pc, kind); }
        finally { contextOwner.EndEvaluation(context); }
    }

    private VmSemanticCaseResult EvaluateStructuralCase(RuntimeFunctionId functionId, int group, ReadOnlySpan<SelectCaseRecord> cases)
    {
        if (contextOwner is null || runtimeSemantics is null) return structuralSemantics!.SelectCase(functionId, group, cases);
        if (!TryEvaluationContext(out var context) || !contextOwner.TryBeginEvaluation(context)) return VmSemanticCaseResult.NotAvailable;
        try { return runtimeSemantics.SelectCase(context, functionId, group, cases); }
        finally { contextOwner.EndEvaluation(context); }
    }

    private VmCountedLoopResult EvaluateCountedBegin(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop)
    {
        if (contextOwner is null || runtimeSemantics is null) return structuralSemantics!.BeginCounted(functionId, loopIndex, loop);
        if (!TryEvaluationContext(out var context) || !contextOwner.TryBeginEvaluation(context)) return VmCountedLoopResult.NotAvailable;
        try { return runtimeSemantics.BeginCounted(context, functionId, loopIndex, loop); }
        finally { contextOwner.EndEvaluation(context); }
    }

    private VmSemanticIntResult EvaluateCountedAdvance(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop, long step)
    {
        if (contextOwner is null || runtimeSemantics is null) return structuralSemantics!.AdvanceCounted(functionId, loopIndex, loop, step);
        if (!TryEvaluationContext(out var context) || !contextOwner.TryBeginEvaluation(context)) return VmSemanticIntResult.NotAvailable;
        try { return runtimeSemantics.AdvanceCounted(context, functionId, loopIndex, loop, step); }
        finally { contextOwner.EndEvaluation(context); }
    }

    private static bool Return<T>(T source, out T value) { value = source; return true; }

    private bool TryEvaluateCallArguments(int functionId, int pc, out VmSemanticValue[] actuals, out VmStopReason reason)
    {
        actuals = [];
        reason = VmStopReason.SemanticNotAvailable;
        var callerProgram = ActiveProgram;
        VmCallSiteRecord site;
        if (contextOwner is null)
        {
            if (!callSites.TryGetValue((functionId, pc), out site)) return true;
        }
        else
        {
            var found = false;
            site = default;
            foreach (var candidate in callerProgram.CallSites)
                if (candidate.FunctionId == functionId && candidate.Pc == pc) { site = candidate; found = true; break; }
            if (!found) return true; // synthetic direct VmInstruction calls have no source CallSite.
        }
        if (runtimeSemantics is null && site.ArgumentCount != 0) return false;
        actuals = Enumerable.Repeat(VmSemanticValue.Missing, site.ArgumentCount).ToArray();
        for (var i = 0; i < site.ArgumentCount; i++)
        {
            var record = callerProgram.CallArgumentRecords[site.ArgumentRecordStart + i];
            if (record < 0) continue; // omitted argument; binding consumes the sentinel later.
            if (!EvaluateRecord(callerProgram.CallArgumentArena, record, out actuals[i]))
            {
                reason = runtimeSemantics!.LastStatus == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
                return false;
            }
        }
        reason = VmStopReason.Returned;
        return true;
    }

    private VmStopReason? ExecuteDynamicCall(int siteIndex, int functionId, int pc)
    {
        if ((uint)siteIndex >= (uint)ActiveProgram.DynamicCallSites.Length || dynamicCallResolver is null || runtimeSemantics is null)
            return VmStopReason.SemanticNotAvailable;
        var site = ActiveProgram.DynamicCallSites[siteIndex];
        if (site.FunctionId != functionId || site.Pc != pc ||
            !EvaluateRecord(ActiveProgram.DynamicCallArena, site.TargetRecord, out var targetValue) ||
            !targetValue.TryGetString(out var targetName))
            return SemanticResultReason(runtimeSemantics.LastStatus);
        var method = site.Kind == VmDynamicCallKind.CallFormMethod;
        var resolution = dynamicCallResolver.Resolve(targetName, method);
        if (resolution.Kind == VmDynamicResolutionKind.Missing)
        {
            if (site.Kind is VmDynamicCallKind.TryCallForm or VmDynamicCallKind.TryCCallForm)
                return SetPc(site.MissingTargetPc) ? null : VmStopReason.InvalidLocalPc;
            return DynamicCallFault(functionId, pc, $"dynamic target missing: {targetName}");
        }
        if (resolution.Kind != VmDynamicResolutionKind.Ready)
            return DynamicCallFault(functionId, pc, $"dynamic target {resolution.Kind}: {targetName}");
        var returnKind = method ? VmReturnKind.Expression : VmReturnKind.Normal;
        VmAdmissionLease admission = default;
        if (contextOwner is not null)
        {
            admission = contextOwner.PrepareInvocation(resolution.Target, returnKind, method ? FunctionKind.Method : FunctionKind.Normal);
            if (!admission.Ready) return AdmissionReason(admission.Status);
        }
        var callerProgram = ActiveProgram;
        var actuals = Enumerable.Repeat(VmSemanticValue.Missing, site.ArgumentCount).ToArray();
        for (var index = 0; index < actuals.Length; index++)
        {
            var record = callerProgram.DynamicCallArgumentRecords[site.ArgumentRecordStart + index];
            if (record < 0) continue;
            if (!EvaluateRecord(callerProgram.DynamicCallArena, record, out actuals[index]))
            {
                if (contextOwner is not null) contextOwner.Cancel(ref admission);
                return SemanticResultReason(runtimeSemantics.LastStatus);
            }
        }
        if (contextOwner is null)
            return TryPush(resolution.Target.Value, returnKind, actuals, out var reason) ? null : reason;
        if (!contextOwner.CommitFrame(ref admission, actuals, out _))
        {
            contextOwner.Cancel(ref admission);
            return VmStopReason.SemanticNotAvailable;
        }
        MaxFrameDepth = Math.Max(MaxFrameDepth, contextOwner.MaxFrameDepth);
        return null;
    }

    private VmStopReason DynamicCallFault(int functionId, int pc, string message)
    {
        LastTerminalFaultMessage = message;
        LastTerminalFaultFunctionId = functionId;
        LastTerminalFaultPc = pc;
        return VmStopReason.TerminalFault;
    }

    private VmStopReason? ExecuteRuntimeStatement(int index, int functionId, int pc)
    {
        var activeProgram = ActiveProgram;
        if ((uint)index >= (uint)activeProgram.RuntimeStatements.Records.Length)
            return VmStopReason.SemanticNotAvailable;
        var record = activeProgram.RuntimeStatements.Records[index];
        if (record.Kind == VmRuntimeStatementKind.Set)
            return ExecuteAssignment(record);
        if (record.Kind == VmRuntimeStatementKind.MultiSet)
            return ExecuteMultiSet(record);
        if (record.Kind == VmRuntimeStatementKind.Split)
            return ExecuteSplit(record);
        if (record.Kind == VmRuntimeStatementKind.Times)
            return ExecuteTimes(record);
        if (record.Kind == VmRuntimeStatementKind.SetBit)
            return ExecuteSetBit(record);
        if (record.Kind == VmRuntimeStatementKind.ClearFrame)
        {
            if (runtimeSemantics is null || !ResolveLValue(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var target) || !TryClearFrameVariable(target.Name))
                return VmStopReason.SemanticNotAvailable;
            return null;
        }
        if (record.Kind == VmRuntimeStatementKind.VarSet)
        {
            if (runtimeSemantics is null || !ResolveLValue(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var target) ||
                !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.SecondaryOperandRecord, out var value) ||
                !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.TertiaryOperandRecord, out var startValue) || !startValue.TryGetInteger(out var start) ||
                !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.QuaternaryOperandRecord, out var endValue) || !endValue.TryGetInteger(out var end) || start < 0 || end < start)
                return VmStopReason.SemanticNotAvailable;
            for (var i = start; i < end; i++)
                if (!runtimeSemantics.TryWriteRuntimeLValue(target with { Indices = [VmSemanticValue.From(i)] }, value)) return VmStopReason.SemanticNotAvailable;
            return null;
        }
        if (record.Kind == VmRuntimeStatementKind.DeclarePrivate)
        {
            if (runtimeSemantics is null ||
                !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var initial) ||
                !TryWriteFrame(activeProgram.RuntimeStatements.ReadText(record), null, [], initial))
                return VmStopReason.SemanticNotAvailable;
            return null;
        }
        if (record.Kind == VmRuntimeStatementKind.NoOp)
            return null;
        if (record.Kind == VmRuntimeStatementKind.Expression)
        {
            if (runtimeSemantics is null || !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out _))
                return runtimeSemantics?.LastStatus == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
            return null;
        }
        if (record.Kind == VmRuntimeStatementKind.ReturnValue)
        {
            var metadata = CurrentMetadata();
            if (metadata?.ReturnType is null)
                return VmStopReason.SemanticNotAvailable;
            var value = DefaultExpressionReturn(metadata);
            if (record.OperandRecord >= 0)
            {
                if (runtimeSemantics is null || !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out value))
                    return runtimeSemantics?.LastStatus == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
                if (metadata.ReturnType == RuntimeMetadataValueType.Integer && !value.TryGetInteger(out _) || metadata.ReturnType == RuntimeMetadataValueType.String && !value.TryGetString(out _))
                    return VmStopReason.SemanticNotAvailable;
            }
            SetCurrentReturnValue(value);
            return Return() ? null : VmStopReason.StackUnderflow;
        }
        if (record.Kind == VmRuntimeStatementKind.LegacyReturnInteger)
        {
            if (CurrentMetadata() is null || runtimeSemantics is null ||
                !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var value) ||
                !value.TryGetInteger(out _))
                return runtimeSemantics?.LastStatus == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
            SetCurrentReturnValue(value);
            return Return() ? null : VmStopReason.StackUnderflow;
        }
        if (record.Kind == VmRuntimeStatementKind.LegacyReturnValues)
        {
            if (!TryEvaluateValues(activeProgram, record, out var values)) return SemanticResultReason(runtimeSemantics?.LastStatus ?? VmSemanticStatus.Unavailable);
            if (runtimeEffects is null || !runtimeEffects.SetLegacyReturnValues(values)) return VmStopReason.SemanticNotAvailable;
            if (CurrentMetadata() is null) return VmStopReason.StackUnderflow;
            SetCurrentReturnValue(values.Length == 0 ? VmSemanticValue.From(0L) : values[0]);
            return Return() ? null : VmStopReason.StackUnderflow;
        }
        if (record.Kind == VmRuntimeStatementKind.Throw)
        {
            if (runtimeSemantics is null || !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var faultValue) || !faultValue.TryGetString(out var message))
                return runtimeSemantics?.LastStatus == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
            LastTerminalFaultMessage = message;
            LastTerminalFaultFunctionId = functionId;
            LastTerminalFaultPc = pc;
            LastTerminalFaultSourceLine = record.SourceLine;
            return VmStopReason.TerminalFault;
        }
        if (record.Kind == VmRuntimeStatementKind.PrintButton)
        {
            if (runtimeSemantics is null || runtimeEffects is null ||
                !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var labelValue) || !labelValue.TryGetString(out var label) ||
                !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.SecondaryOperandRecord, out var buttonValue))
                return runtimeSemantics?.LastStatus == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
            return runtimeEffects.PrintButton(label.Replace("\n", string.Empty, StringComparison.Ordinal), buttonValue) ? null : VmStopReason.SemanticNotAvailable;
        }
        if (record.Kind == VmRuntimeStatementKind.TypedHost)
        {
            if (runtimeEffects is null || !TryEvaluateValues(activeProgram, record, out var arguments))
                return SemanticResultReason(runtimeSemantics?.LastStatus ?? VmSemanticStatus.Unavailable);
            if (contextOwner is not null && runtimeEffects is IVmOwnedInputRuntimeEffects inputHost &&
                inputHost.TryPrepareInput((PrototypeOpcode)record.HostOpcode, arguments, out var expectedKind))
            {
                pendingInput = contextOwner.ReserveInput(expectedKind);
                return VmStopReason.WaitingForInput;
            }
            return runtimeEffects.ExecuteTypedHostStatement((PrototypeOpcode)record.HostOpcode, arguments) switch
            {
                VmHostEffectResult.Applied => null,
                VmHostEffectResult.WaitingForInput => VmStopReason.WaitingForInput,
                VmHostEffectResult.Fault => VmStopReason.TerminalFault,
                _ => VmStopReason.SemanticNotAvailable,
            };
        }
        if (runtimeEffects is null)
            return VmStopReason.SemanticNotAvailable;
        var text = activeProgram.RuntimeStatements.ReadText(record);
        if (record.OperandRecord >= 0)
        {
            if (runtimeSemantics is null || !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var value))
                return runtimeSemantics?.LastStatus == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
            text = value.ToString();
        }
        switch (record.Kind)
        {
            case VmRuntimeStatementKind.Print: runtimeEffects.WriteText(text); return null;
            case VmRuntimeStatementKind.PrintLine: runtimeEffects.WriteText(text); runtimeEffects.NewLine(); return null;
            case VmRuntimeStatementKind.PrintWait: runtimeEffects.WriteText(text); runtimeEffects.RequestWait(false); return VmStopReason.WaitingForInput;
            case VmRuntimeStatementKind.Wait: runtimeEffects.RequestWait(false); return VmStopReason.WaitingForInput;
            case VmRuntimeStatementKind.ForceWait: runtimeEffects.RequestWait(true); return VmStopReason.WaitingForInput;
            case VmRuntimeStatementKind.Quit: runtimeEffects.Quit(); return VmStopReason.QuitRequested;
            case VmRuntimeStatementKind.Host: return runtimeEffects.ExecuteHostStatement((PrototypeOpcode)record.HostOpcode, text) ? null : VmStopReason.SemanticNotAvailable;
            default: return VmStopReason.UnsupportedControl;
        }
    }

    private VmStopReason? ExecuteAssignment(in VmRuntimeStatementRecord record)
    {
        if (runtimeSemantics is null || record.OperandRecord < 0 || record.SecondaryOperandRecord < 0)
            return VmStopReason.SemanticNotAvailable;
        var arena = ActiveProgram.RuntimeStatements.OperandArena;
        if (record.Assignment is VmAssignmentOperator.Assign or VmAssignmentOperator.AssignString)
        {
            // Legacy SET evaluates the source before resolving its destination.
            var sourceRecord = record.SecondaryOperandRecord;
            if (record.Assignment == VmAssignmentOperator.Assign && record.FormatOperandRecord >= 0 &&
                GetAssignmentTargetKind(arena, record.OperandRecord, out var targetKind) &&
                targetKind == VmSemanticValueKind.String)
                sourceRecord = record.FormatOperandRecord;
            if (!EvaluateRecord(arena, sourceRecord, out var source))
                return SemanticResultReason(runtimeSemantics.LastStatus);
            if (!ResolveLValue(arena, record.OperandRecord, out var target) || !runtimeSemantics.TryWriteRuntimeLValue(target, source))
                return SemanticResultReason(runtimeSemantics.LastStatus);
            return null;
        }
        if (!ResolveLValue(arena, record.OperandRecord, out var lvalue) || !runtimeSemantics.TryReadRuntimeLValue(lvalue, out var current))
            return SemanticResultReason(runtimeSemantics.LastStatus);
        if (record.Assignment == VmAssignmentOperator.Add && current.TryGetString(out var leftText))
        {
            if (!EvaluateRecord(arena, record.SecondaryOperandRecord, out var rightTextValue) || !rightTextValue.TryGetString(out var rightText))
                return SemanticResultReason(runtimeSemantics.LastStatus);
            return runtimeSemantics.TryWriteRuntimeLValue(lvalue, VmSemanticValue.From(leftText + rightText)) ? null : SemanticResultReason(runtimeSemantics.LastStatus);
        }
        if (!current.TryGetInteger(out var left))
            return SemanticResultReason(runtimeSemantics.LastStatus);
        if (!EvaluateRecord(arena, record.SecondaryOperandRecord, out var rightValue) || !rightValue.TryGetInteger(out var right))
            return SemanticResultReason(runtimeSemantics.LastStatus);
        VmSemanticValue result;
        switch (record.Assignment)
        {
            case VmAssignmentOperator.Add: result = VmSemanticValue.From(unchecked(left + right)); break;
            case VmAssignmentOperator.Subtract: result = VmSemanticValue.From(unchecked(left - right)); break;
            case VmAssignmentOperator.Multiply: result = VmSemanticValue.From(unchecked(left * right)); break;
            case VmAssignmentOperator.Divide: if (right == 0) return VmStopReason.SemanticEvaluationFault; result = VmSemanticValue.From(left / right); break;
            case VmAssignmentOperator.Modulo: if (right == 0) return VmStopReason.SemanticEvaluationFault; result = VmSemanticValue.From(left % right); break;
            case VmAssignmentOperator.BitOr: result = VmSemanticValue.From(left | right); break;
            case VmAssignmentOperator.BitAnd: result = VmSemanticValue.From(left & right); break;
            case VmAssignmentOperator.BitXor: result = VmSemanticValue.From(left ^ right); break;
            default: return VmStopReason.UnsupportedControl;
        }
        return runtimeSemantics.TryWriteRuntimeLValue(lvalue, result) ? null : SemanticResultReason(runtimeSemantics.LastStatus);
    }

    private VmStopReason? ExecuteMultiSet(in VmRuntimeStatementRecord record)
    {
        var activeProgram = ActiveProgram;
        if (runtimeSemantics is null || !TryEvaluateValues(activeProgram, record, out var values) ||
            !ResolveLValue(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var target))
            return SemanticResultReason(runtimeSemantics?.LastStatus ?? VmSemanticStatus.Unavailable);
        for (var offset = 0; offset < values.Length; offset++)
        {
            var indices = (VmSemanticValue[])target.Indices.Clone();
            if (indices.Length == 0) indices = [VmSemanticValue.From((long)offset)];
            else
            {
                if (!indices[^1].TryGetInteger(out var last)) return VmStopReason.SemanticNotAvailable;
                indices[^1] = VmSemanticValue.From(unchecked(last + offset));
            }
            if (!runtimeSemantics.TryWriteRuntimeLValue(target with { Indices = indices }, values[offset]))
                return SemanticResultReason(runtimeSemantics.LastStatus);
        }
        return null;
    }

    private VmStopReason? ExecuteSplit(in VmRuntimeStatementRecord record)
    {
        var activeProgram = ActiveProgram;
        if (runtimeSemantics is null ||
            !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var source) || !source.TryGetString(out var sourceText) ||
            !EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.SecondaryOperandRecord, out var delimiter) || !delimiter.TryGetString(out var delimiterText) ||
            !ResolveLValue(activeProgram.RuntimeStatements.OperandArena, record.TertiaryOperandRecord, out var destination) ||
            !ResolveLValue(activeProgram.RuntimeStatements.OperandArena, record.QuaternaryOperandRecord, out var countTarget))
            return SemanticResultReason(runtimeSemantics?.LastStatus ?? VmSemanticStatus.Unavailable);
        var values = sourceText.Split([delimiterText], StringSplitOptions.None);
        if (!runtimeSemantics.TryWriteRuntimeLValue(countTarget, VmSemanticValue.From((long)values.Length)))
            return SemanticResultReason(runtimeSemantics.LastStatus);
        var capacity = FrameCapacity(destination);
        for (var offset = 0; offset < Math.Min(values.Length, capacity); offset++)
        {
            var indices = (VmSemanticValue[])destination.Indices.Clone();
            if (indices.Length == 0) indices = [VmSemanticValue.From((long)offset)];
            else if (indices[^1].TryGetInteger(out var last)) indices[^1] = VmSemanticValue.From(last + offset);
            else return VmStopReason.SemanticNotAvailable;
            if (!runtimeSemantics.TryWriteRuntimeLValue(destination with { Indices = indices }, VmSemanticValue.From(values[offset])))
                return SemanticResultReason(runtimeSemantics.LastStatus);
        }
        return null;
    }

    private int FrameCapacity(in VmResolvedLValue destination)
    {
        if (contextOwner is not null && contextOwner.TryGetCurrentContext(out _, out var address))
            return contextOwner.FrameVariableCapacity(address, destination.Name, destination.Indices);
        if (stackCount == 0 || invocations[stackCount - 1] is not { } frame ||
            !TryFrameValues(frame, destination.Name, destination.Indices, out var values, out var index)) return int.MaxValue;
        return Math.Max(0, values.Length - index);
    }

    private bool TryEvaluateValues(LinkedProgram activeProgram, in VmRuntimeStatementRecord record, out VmSemanticValue[] values)
    {
        values = new VmSemanticValue[record.ValueCount];
        if (values.Length == 0) return true;
        if (runtimeSemantics is null || record.SecondaryOperandRecord < 0) return false;
        for (var index = 0; index < values.Length; index++)
            if (!EvaluateRecord(activeProgram.RuntimeStatements.OperandArena, record.SecondaryOperandRecord + index, out values[index])) return false;
        return true;
    }

    private VmStopReason? ExecuteTimes(in VmRuntimeStatementRecord record)
    {
        var activeProgram = ActiveProgram;
        if (runtimeSemantics is null || record.OperandRecord < 0 || !ResolveLValue(activeProgram.RuntimeStatements.OperandArena, record.OperandRecord, out var lvalue) || !runtimeSemantics.TryReadRuntimeLValue(lvalue, out var current) || !current.TryGetInteger(out var value))
            return runtimeSemantics is null ? VmStopReason.SemanticNotAvailable : SemanticResultReason(runtimeSemantics.LastStatus);
        long result;
        try
        {
            if (runtimeSemantics.TimesNotRigorousCalculation)
                result = unchecked((long)((double)value * record.NumericValue));
            else
            {
                var multiplied = value * (decimal)record.NumericValue;
                result = multiplied <= long.MaxValue && multiplied >= long.MinValue ? (long)multiplied : unchecked((long)(double)multiplied);
            }
        }
        catch (OverflowException) { return VmStopReason.SemanticEvaluationFault; }
        return runtimeSemantics.TryWriteRuntimeLValue(lvalue, VmSemanticValue.From(result)) ? null : SemanticResultReason(runtimeSemantics.LastStatus);
    }

    private VmStopReason? ExecuteSetBit(in VmRuntimeStatementRecord record)
    {
        var arena = ActiveProgram.RuntimeStatements.OperandArena;
        if (runtimeSemantics is null || record.OperandRecord < 0 || record.SecondaryOperandRecord < 0)
            return VmStopReason.SemanticNotAvailable;
        for (var offset = 0; offset < record.ValueCount; offset++)
        {
            if (!EvaluateRecord(arena, record.SecondaryOperandRecord + offset, out var bitValue) ||
                !bitValue.TryGetInteger(out var bit) || bit is < 0 or > 63)
                return VmStopReason.SemanticEvaluationFault;
            // Legacy reevaluates and writes the destination after each bit expression.
            if (!ResolveLValue(arena, record.OperandRecord, out var target) ||
                !runtimeSemantics.TryReadRuntimeLValue(target, out var currentValue) || !currentValue.TryGetInteger(out var current) ||
                !runtimeSemantics.TryWriteRuntimeLValue(target, VmSemanticValue.From(current | 1L << (int)bit)))
                return SemanticResultReason(runtimeSemantics.LastStatus);
        }
        return null;
    }

    private VmStopReason? ExecuteStructural(int structuralIndex, int functionId, int pc)
    {
        if (structuralSemantics is null)
            return VmStopReason.SemanticNotAvailable;
        if ((uint)structuralIndex >= (uint)ActiveProgram.StructuralLinks.Length)
            return VmStopReason.UnsupportedControl;
        var record = ActiveProgram.StructuralLinks[structuralIndex];
        if (record.FunctionId != functionId || record.Pc != pc)
            return VmStopReason.UnsupportedControl;
        var runtimeId = new RuntimeFunctionId(functionId);

        switch (record.Kind)
        {
            case VmStructuralKind.Sif:
                if (record.AuxiliaryPc < 0) return VmStopReason.InvalidStructure;
                var sif = EvaluateStructuralInt(runtimeId, pc, record.Kind);
                if (!sif.Available) return SemanticResultReason(sif.Status);
                if (sif.Value == 0 && !SetPc(record.AuxiliaryPc))
                    return VmStopReason.InvalidLocalPc;
                return null;

            case VmStructuralKind.If:
                return ExecuteIf(runtimeId, record);
            case VmStructuralKind.ElseIf:
            case VmStructuralKind.Else:
                return SetGroupExit(ActiveProgram.IfGroups, record.GroupIndex, functionId);
            case VmStructuralKind.EndIf:
                return null;

            case VmStructuralKind.SelectCase:
                return ExecuteSelect(runtimeId, record);
            case VmStructuralKind.Case:
            case VmStructuralKind.CaseElse:
                return SetGroupExit(ActiveProgram.SelectGroups, record.GroupIndex, functionId);
            case VmStructuralKind.EndSelect:
                return null;

            case VmStructuralKind.For:
            case VmStructuralKind.Repeat:
                return BeginCounted(runtimeId, record);
            case VmStructuralKind.Next:
            case VmStructuralKind.Rend:
                return AdvanceCounted(runtimeId, record, breakAfterAdvance: false);
            case VmStructuralKind.While:
                return CheckConditionalLoop(runtimeId, record, pc, VmStructuralKind.While);
            case VmStructuralKind.Wend:
            {
                if (!TryGetLoop(record, out var loop)) return VmStopReason.InvalidStructure;
                return CheckConditionalLoop(runtimeId, record, loop.HeaderPc, VmStructuralKind.While);
            }
            case VmStructuralKind.Do:
                return null;
            case VmStructuralKind.Loop:
                return CheckConditionalLoop(runtimeId, record, pc, VmStructuralKind.Loop);
            case VmStructuralKind.Break:
                return BreakLoop(runtimeId, record);
            case VmStructuralKind.Continue:
                return ContinueLoop(runtimeId, record);
            default:
                return VmStopReason.UnsupportedControl;
        }
    }

    private VmStopReason? ExecuteIf(RuntimeFunctionId runtimeId, StructuralLinkRecord record)
    {
        if (!TryGetIfGroup(record.GroupIndex, runtimeId.Value, out var group))
            return VmStopReason.InvalidStructure;
        var end = checked(group.FirstClauseIndex + group.ClauseCount);
        for (var index = group.FirstClauseIndex; index < end; index++)
        {
            if ((uint)index >= (uint)ActiveProgram.IfClauses.Length)
                return VmStopReason.InvalidStructure;
            var clause = ActiveProgram.IfClauses[index];
            if (clause.FunctionId != runtimeId.Value)
                return VmStopReason.InvalidStructure;
            var condition = clause.Kind == VmStructuralKind.Else ? VmSemanticIntResult.From(1) : EvaluateStructuralInt(runtimeId, clause.Pc, clause.Kind);
            if (!condition.Available) return SemanticResultReason(condition.Status);
            if (condition.Value != 0)
                return SetPc(clause.Pc + 1) ? null : VmStopReason.InvalidLocalPc;
        }
        return SetPc(group.ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private VmStopReason? ExecuteSelect(RuntimeFunctionId runtimeId, StructuralLinkRecord record)
    {
        if (!TryGetSelectGroup(record.GroupIndex, runtimeId.Value, out var group))
            return VmStopReason.InvalidStructure;
        var end = checked(group.FirstCaseIndex + group.CaseCount);
        var matchCount = 0;
        var fallbackPc = -1;
        for (var index = group.FirstCaseIndex; index < end; index++)
        {
            if ((uint)index >= (uint)ActiveProgram.SelectCases.Length)
                return VmStopReason.InvalidStructure;
            var candidate = ActiveProgram.SelectCases[index];
            if (candidate.FunctionId != runtimeId.Value)
                return VmStopReason.InvalidStructure;
            if (candidate.Kind == VmStructuralKind.CaseElse)
            {
                fallbackPc = candidate.Pc;
                break;
            }
            if (candidate.Kind != VmStructuralKind.Case)
                return VmStopReason.InvalidStructure;
            matchCount++;
        }

        var selection = EvaluateStructuralCase(runtimeId, record.GroupIndex, ActiveProgram.SelectCases.AsSpan(group.FirstCaseIndex, matchCount));
        if (!selection.Available) return SemanticResultReason(selection.Status);
        var selected = selection.Ordinal;
        if (selected < -1 || selected >= matchCount)
            return VmStopReason.UnsupportedControl;
        if (selected >= 0)
        {
            var selectedIndex = checked(group.FirstCaseIndex + selected);
            var selectedCase = ActiveProgram.SelectCases[selectedIndex];
            return SetPc(selectedCase.Pc + 1) ? null : VmStopReason.InvalidLocalPc;
        }
        if (fallbackPc >= 0)
            return SetPc(fallbackPc + 1) ? null : VmStopReason.InvalidLocalPc;
        return SetPc(group.ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private VmStopReason? BeginCounted(RuntimeFunctionId runtimeId, StructuralLinkRecord record)
    {
        if (!TryGetLoop(record, out var loop))
            return VmStopReason.InvalidStructure;
        var begin = EvaluateCountedBegin(runtimeId, record.LoopIndex, loop);
        if (!begin.Available) return SemanticResultReason(begin.Status);
        var entry = begin.Entry;
        if (contextOwner is null) loopRuntime!.CaptureCounted(record.LoopIndex, entry.End, entry.Step);
        else contextOwner.CaptureLoop(runtimeId, PhysicalLoopOrdinal(record.LoopIndex), entry.End, entry.Step, entry.Counter);
        if (CountedContinues(entry.Counter, entry.End, entry.Step))
            return null;
        return SetPc(loop.ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private VmStopReason? AdvanceCounted(RuntimeFunctionId runtimeId, StructuralLinkRecord record, bool breakAfterAdvance)
    {
        if (!TryGetLoop(record, out var loop))
            return VmStopReason.InvalidStructure;
        long end;
        long step;
        long repeat;
        var initialized = contextOwner is null
            ? loopRuntime!.TryGetCounted(record.LoopIndex, out end, out step)
            : contextOwner.TryReadLoop(runtimeId, PhysicalLoopOrdinal(record.LoopIndex), out end, out step, out repeat);
        if (!initialized)
        {
            if (breakAfterAdvance)
                return VmStopReason.SemanticNotAvailable;
            return SetPc(loop.ExitPc) ? null : VmStopReason.InvalidLocalPc;
        }
        var advance = contextOwner is not null && loop.LoopKind == VmStructuralKind.Repeat
            ? contextOwner.TryAdvanceRepeat(runtimeId, PhysicalLoopOrdinal(record.LoopIndex), out var repeated)
                ? VmSemanticIntResult.From(repeated) : VmSemanticIntResult.NotAvailable
            : EvaluateCountedAdvance(runtimeId, record.LoopIndex, loop, step);
        if (!advance.Available) return SemanticResultReason(advance.Status);
        var counter = advance.Value;
        if (breakAfterAdvance)
            return SetPc(loop.ExitPc) ? null : VmStopReason.InvalidLocalPc;
        if (CountedContinues(counter, end, step))
            return SetPc(loop.BodyEntryPc) ? null : VmStopReason.InvalidLocalPc;
        return SetPc(loop.ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private VmStopReason? CheckConditionalLoop(RuntimeFunctionId runtimeId, StructuralLinkRecord record, int evaluatePc, VmStructuralKind evaluateKind)
    {
        if (!TryGetLoop(record, out var loop))
            return VmStopReason.InvalidStructure;
        var condition = EvaluateStructuralInt(runtimeId, evaluatePc, evaluateKind);
        if (!condition.Available) return SemanticResultReason(condition.Status);
        if (condition.Value != 0)
            return SetPc(loop.BodyEntryPc) ? null : VmStopReason.InvalidLocalPc;
        return SetPc(loop.ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private VmStopReason? BreakLoop(RuntimeFunctionId runtimeId, StructuralLinkRecord record)
    {
        if (!TryGetLoop(record, out var loop))
            return VmStopReason.InvalidStructure;
        if ((loop.Flags & LoopDescriptorFlags.BreakAdvancesCounter) != 0)
            return AdvanceCounted(runtimeId, record, breakAfterAdvance: true);
        return SetPc(loop.ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private VmStopReason? ContinueLoop(RuntimeFunctionId runtimeId, StructuralLinkRecord record)
    {
        if (!TryGetLoop(record, out var loop))
            return VmStopReason.InvalidStructure;
        return loop.LoopKind switch
        {
            VmStructuralKind.For or VmStructuralKind.Repeat => AdvanceCounted(runtimeId, record, breakAfterAdvance: false),
            VmStructuralKind.While => CheckConditionalLoop(runtimeId, record, loop.HeaderPc, VmStructuralKind.While),
            VmStructuralKind.Do => CheckConditionalLoop(runtimeId, record, loop.EndPc, VmStructuralKind.Loop),
            _ => VmStopReason.InvalidStructure,
        };
    }

    private bool TryGetLoop(StructuralLinkRecord record, out LoopDescriptor loop)
    {
        if ((uint)record.LoopIndex >= (uint)ActiveProgram.Loops.Length)
        {
            loop = default;
            return false;
        }
        loop = ActiveProgram.Loops[record.LoopIndex];
        return loop.FunctionId == record.FunctionId;
    }

    private int PhysicalLoopOrdinal(int localLoopIndex)
    {
        if (contextOwner is null || !contextOwner.TryGetCurrentContext(out var context, out _))
            return localLoopIndex;
        return (uint)localLoopIndex < (uint)context.PhysicalLoopIds.Length
            ? context.PhysicalLoopIds[localLoopIndex] : localLoopIndex;
    }

    private bool TryGetIfGroup(int groupIndex, int functionId, out IfGroupDescriptor group)
    {
        if ((uint)groupIndex >= (uint)ActiveProgram.IfGroups.Length)
        {
            group = default;
            return false;
        }
        group = ActiveProgram.IfGroups[groupIndex];
        return group.FunctionId == functionId;
    }

    private bool TryGetSelectGroup(int groupIndex, int functionId, out SelectGroupDescriptor group)
    {
        if ((uint)groupIndex >= (uint)ActiveProgram.SelectGroups.Length)
        {
            group = default;
            return false;
        }
        group = ActiveProgram.SelectGroups[groupIndex];
        return group.FunctionId == functionId;
    }

    private VmStopReason? SetGroupExit(IfGroupDescriptor[] groups, int groupIndex, int functionId)
    {
        if ((uint)groupIndex >= (uint)groups.Length || groups[groupIndex].FunctionId != functionId)
            return VmStopReason.InvalidStructure;
        return SetPc(groups[groupIndex].ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private VmStopReason? SetGroupExit(SelectGroupDescriptor[] groups, int groupIndex, int functionId)
    {
        if ((uint)groupIndex >= (uint)groups.Length || groups[groupIndex].FunctionId != functionId)
            return VmStopReason.InvalidStructure;
        return SetPc(groups[groupIndex].ExitPc) ? null : VmStopReason.InvalidLocalPc;
    }

    private static bool CountedContinues(long counter, long end, long step) =>
        step > 0 ? end > counter : step < 0 && end < counter;

    private bool TryPush(int id, VmReturnKind kind, VmSemanticValue[] actuals, out VmStopReason reason)
    {
        if (contextOwner is not null)
        {
            var expectedKind = kind == VmReturnKind.Expression ? FunctionKind.Method : FunctionKind.Normal;
            var lease = contextOwner.PrepareInvocation(new(id), kind, expectedKind);
            if (!lease.Ready) { reason = AdmissionReason(lease.Status); return false; }
            if (!contextOwner.CommitFrame(ref lease, actuals, out _)) { reason = VmStopReason.SemanticNotAvailable; return false; }
            MaxFrameDepth = Math.Max(MaxFrameDepth, contextOwner.MaxFrameDepth);
            reason = VmStopReason.Returned;
            return true;
        }
        if ((uint)id >= (uint)ActiveProgram.Descriptors.Length) { reason = VmStopReason.InvalidFunctionId; return false; }
        var d = ActiveProgram.Descriptors[id];
        if (d.State != VmFunctionState.ExecutableReady) { reason = StateReason(d.State); return false; }
        if (stackCount == stack.Length) { Array.Resize(ref stack, checked(stack.Length * 2)); Array.Resize(ref invocations, stack.Length); }
        var metadata = ActiveProgram.RuntimeMetadata[id];
        var state = new VmInvocationFrame(++nextFrameId, id, metadata);
        for (var i = 0; i < metadata.Parameters.Length; i++)
        {
            var parameter = metadata.Parameters[i];
            var value = i < actuals.Length ? actuals[i] : VmSemanticValue.Missing;
            if (value.Kind == VmSemanticValueKind.Missing)
                value = parameter.Type == RuntimeMetadataValueType.Integer
                    ? VmSemanticValue.From(parameter.HasDefault ? parameter.DefaultInteger : 0L)
                    : VmSemanticValue.From(parameter.HasDefault ? parameter.DefaultString ?? string.Empty : string.Empty);
            if (value.Kind == VmSemanticValueKind.Missing || value.Kind != (parameter.Type == RuntimeMetadataValueType.Integer ? VmSemanticValueKind.Integer : VmSemanticValueKind.String))
            {
                LastTerminalFaultMessage = $"argument-bind id={id} ordinal={i} name={parameter.Name} actual={value.Kind} expected={parameter.Type} default={parameter.HasDefault}";
                LastTerminalFaultFunctionId = id;
                reason = VmStopReason.SemanticNotAvailable;
                return false;
            }
            if (parameter.IsPrivate)
            {
                var declaration = metadata.PrivateVariables.First(variable => variable.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase));
                var values = declaration.IsStatic
                    ? GetOrCreateStaticPrivate(id, declaration)
                    : state.Private[parameter.Name];
                values[parameter.Slot] = value;
            }
            else if (parameter.Type == RuntimeMetadataValueType.Integer) state.Arg[parameter.Slot] = value;
            else state.Args[parameter.Slot] = value;
        }
        stack[stackCount++] = new(id, 0, kind);
        invocations[stackCount - 1] = state;
        MaxFrameDepth = Math.Max(MaxFrameDepth, stackCount);
        reason = VmStopReason.Returned;
        return true;
    }

    private bool Return()
    {
        if (contextOwner is not null)
        {
            if (!contextOwner.TryReturn(out var ownerReturn)) return false;
            if (contextOwner.FrameDepth == 0 || ownerReturn.ReturnKind == VmReturnKind.Propagate)
                LastReturnValue = ownerReturn.Value;
            if (ownerReturn.ReturnKind == VmReturnKind.Expression)
                expressionReturnValue = ownerReturn.Value;
            while (ownerReturn.ReturnKind == VmReturnKind.Propagate && contextOwner.FrameDepth > 0)
                if (!contextOwner.TryReturn(out ownerReturn)) return false;
            return true;
        }
        if (stackCount == 0) return false;
        var frame = stack[stackCount - 1];
        var invocation = invocations[stackCount - 1];
        var returned = invocation is null || invocation.ReturnValue.Kind == VmSemanticValueKind.Missing
            ? DefaultExpressionReturn(invocation?.Metadata ?? FunctionRuntimeMetadata.Empty)
            : invocation.ReturnValue;
        // Legacy JUMP unwinds the jumper as soon as its target returns.  Keep
        // the target result for the one outer Legacy write-back, just as a
        // direct root return does.
        if (stackCount == 1 || frame.ReturnKind == VmReturnKind.Propagate)
            LastReturnValue = returned;
        var propagate = frame.ReturnKind == VmReturnKind.Propagate;
        if (frame.ReturnKind == VmReturnKind.Expression)
            expressionReturnValue = returned;
        invocations[--stackCount] = null;
        while (propagate && stackCount > 0)
        {
            propagate = stack[stackCount - 1].ReturnKind == VmReturnKind.Propagate;
            invocations[--stackCount] = null;
        }
        return true;
    }
    // Legacy UserDefinedMethodTerm.GetValue converts an omitted RETURNF to the declared 0/empty return.
    private static VmSemanticValue DefaultExpressionReturn(FunctionRuntimeMetadata metadata) => metadata.ReturnType == RuntimeMetadataValueType.Integer ? VmSemanticValue.From(0L) : metadata.ReturnType == RuntimeMetadataValueType.String ? VmSemanticValue.From(string.Empty) : VmSemanticValue.Unavailable;

    public bool OwnsFrameVariable(string name)
    {
        if (contextOwner is not null)
            return contextOwner.TryGetCurrentContext(out _, out var address) && contextOwner.OwnsFrameVariable(address, name);
        return name.Equals("ARG", StringComparison.OrdinalIgnoreCase) || name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase) ||
            stackCount != 0 && invocations[stackCount - 1]?.Metadata.PrivateVariables.Any(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;
    }

    public bool TryGetFrameVariableKind(string name, out VmSemanticValueKind kind)
    {
        if (contextOwner is not null)
        {
            if (contextOwner.TryGetCurrentContext(out _, out var address))
                return contextOwner.TryGetFrameVariableKind(address, name, out kind);
            kind = VmSemanticValueKind.Unavailable;
            return false;
        }
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)) { kind = VmSemanticValueKind.Integer; return true; }
        if (name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) { kind = VmSemanticValueKind.String; return true; }
        var declaration = stackCount != 0 ? invocations[stackCount - 1]?.Metadata.PrivateVariables.FirstOrDefault(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) : null;
        if (declaration is { Name: not null }) { kind = declaration.Value.Type == RuntimeMetadataValueType.String ? VmSemanticValueKind.String : VmSemanticValueKind.Integer; return true; }
        kind = VmSemanticValueKind.Unavailable;
        return false;
    }

    public bool TryGetFrameVariableLength(string name, int dimension, out long length)
    {
        length = 0;
        if (contextOwner is not null)
            return contextOwner.TryGetCurrentContext(out _, out var address) &&
                contextOwner.TryGetFrameVariableLength(address, name, dimension, out length);
        if (stackCount == 0 || invocations[stackCount - 1] is not { } frame || dimension < 0) return false;
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = frame.Arg.Length; return true; }
        if (name.Equals("ARGS", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = frame.Args.Length; return true; }
        if (name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = frame.Local.Length; return true; }
        if (name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = frame.Locals.Length; return true; }
        var declaration = frame.Metadata.PrivateVariables.FirstOrDefault(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (declaration.Name is null || (uint)dimension >= (uint)declaration.Dimensions.Length) return false;
        length = declaration.Dimensions[dimension];
        return true;
    }

    public bool TryReadFrame(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (contextOwner is not null)
        {
            if (subkey is not null && indices.Length == 0 &&
                long.TryParse(subkey, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                return contextOwner.TryGetCurrentContext(out _, out var parsedAddress) &&
                    contextOwner.TryReadFrameValue(parsedAddress, name, new[] { VmSemanticValue.From(parsed) }, out value);
            return contextOwner.TryGetCurrentContext(out _, out var address) &&
                contextOwner.TryReadFrameValue(address, name, indices, out value);
        }
        if (stackCount == 0 || invocations[stackCount - 1] is not { } frame) return false;
        if (frameState is not null && TryFrameStateSlot(frame.Metadata, name, out var family, out var privateSlot) && frameState.OwnsFrameValue(new(frame.RuntimeFunctionId), family, privateSlot) && TryFrameStateIndex(frame.Metadata, family, privateSlot, subkey, indices, out var externalIndex))
            return frameState.TryReadFrameValue(new(frame.RuntimeFunctionId), family, privateSlot, externalIndex, out value);
        if (!TryFrameIndex(subkey, indices, out _)) return false;
        if (!TryFrameValues(frame, name, indices, out var values, out var index)) return false;
        if ((uint)index >= (uint)values.Length || values[index].Kind == VmSemanticValueKind.Missing) return false;
        value = values[index]; return true;
    }
    public bool TryWriteFrame(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value)
    {
        if (contextOwner is not null)
        {
            if (subkey is not null && indices.Length == 0 &&
                long.TryParse(subkey, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                return contextOwner.TryGetCurrentContext(out _, out var parsedAddress) &&
                    contextOwner.TryWriteFrameValue(parsedAddress, name, new[] { VmSemanticValue.From(parsed) }, value);
            return contextOwner.TryGetCurrentContext(out _, out var address) &&
                contextOwner.TryWriteFrameValue(address, name, indices, value);
        }
        if (stackCount == 0 || invocations[stackCount - 1] is not { } frame) return false;
        var declaration = frame.Metadata.PrivateVariables.FirstOrDefault(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (declaration.Name is not null && declaration.IsConst) return false;
        if (frameState is not null && TryFrameStateSlot(frame.Metadata, name, out var family, out var privateSlot) && frameState.OwnsFrameValue(new(frame.RuntimeFunctionId), family, privateSlot) && TryFrameStateIndex(frame.Metadata, family, privateSlot, subkey, indices, out var externalIndex))
            return frameState.TryWriteFrameValue(new(frame.RuntimeFunctionId), family, privateSlot, externalIndex, value);
        if (!TryFrameIndex(subkey, indices, out _)) return false;
        if (!TryFrameValues(frame, name, indices, out var values, out var index) || (uint)index >= (uint)values.Length) return false;
        values[index] = value; return true;
    }
    private bool TryFrameValues(VmInvocationFrame frame, string name, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue[] values, out int index)
    {
        index = -1;
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase)) { values = frame.Arg; return TryFrameIndex(null, indices, out index); }
        if (name.Equals("ARGS", StringComparison.OrdinalIgnoreCase)) { values = frame.Args; return TryFrameIndex(null, indices, out index); }
        if (name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)) { values = frame.Local; return TryFrameIndex(null, indices, out index); }
        if (name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) { values = frame.Locals; return TryFrameIndex(null, indices, out index); }
        var declaration = frame.Metadata.PrivateVariables.FirstOrDefault(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (declaration.Name is null || !TryPrivateIndex(declaration.Dimensions, indices, out index)) { values = []; return false; }
        values = declaration.IsStatic
            ? staticPrivate.GetValueOrDefault((frame.RuntimeFunctionId, declaration.Name)) ?? (staticPrivate[(frame.RuntimeFunctionId, declaration.Name)] = CreatePrivateValues(declaration))
            : frame.Private[declaration.Name];
        return true;
    }
    private bool TryClearFrameVariable(string name)
    {
        if (contextOwner is not null)
            return contextOwner.TryGetCurrentContext(out _, out var address) &&
                contextOwner.TryClearFrameVariable(address, name);
        if (stackCount == 0 || invocations[stackCount - 1] is not { } frame) return false;
        if (!TryFrameValues(frame, name, [], out var values, out _)) return false;
        var text = name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase) ||
            frame.Metadata.PrivateVariables.Any(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && variable.Type == RuntimeMetadataValueType.String);
        Array.Fill(values, text ? VmSemanticValue.From(string.Empty) : VmSemanticValue.From(0L));
        return true;
    }
    private static int PrivateSlot(FunctionRuntimeMetadata metadata, string name)
    {
        for (var index = 0; index < metadata.PrivateVariables.Length; index++)
            if (metadata.PrivateVariables[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
    private static bool TryFrameStateSlot(FunctionRuntimeMetadata metadata, string name, out VmFrameStateFamily family, out int privateSlot)
    {
        privateSlot = -1;
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase)) { family = VmFrameStateFamily.Arg; return true; }
        if (name.Equals("ARGS", StringComparison.OrdinalIgnoreCase)) { family = VmFrameStateFamily.Args; return true; }
        if (name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)) { family = VmFrameStateFamily.Local; return true; }
        if (name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) { family = VmFrameStateFamily.Locals; return true; }
        privateSlot = PrivateSlot(metadata, name);
        family = VmFrameStateFamily.Private;
        return privateSlot >= 0;
    }
    private static bool TryFrameStateIndex(FunctionRuntimeMetadata metadata, VmFrameStateFamily family, int privateSlot, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out int index)
    {
        index = -1;
        if (family == VmFrameStateFamily.Private)
            return privateSlot >= 0 && TryPrivateIndex(metadata.PrivateVariables[privateSlot].Dimensions, indices, out index);
        return TryFrameIndex(subkey, indices, out index);
    }
    private static bool TryPrivateIndex(ImmutableArray<int> dimensions, ReadOnlySpan<VmSemanticValue> indices, out int index)
    {
        index = 0;
        if (indices.Length == 0 && dimensions.Length == 1) return true;
        if (indices.Length != dimensions.Length) return false;
        for (var i = 0; i < dimensions.Length; i++)
        {
            if (!indices[i].TryGetInteger(out var value) || value < 0 || value >= dimensions[i]) return false;
            index = checked(index * dimensions[i] + (int)value);
        }
        return true;
    }
    private static VmSemanticValue[] CreatePrivateValues(FunctionRuntimePrivate declaration)
    {
        var length = declaration.Dimensions.Aggregate(1, static (count, dimension) => checked(count * dimension));
        var values = Enumerable.Repeat(declaration.Type == RuntimeMetadataValueType.Integer ? VmSemanticValue.From(0L) : VmSemanticValue.From(string.Empty), length).ToArray();
        if (declaration.Type == RuntimeMetadataValueType.Integer && !declaration.InitialIntegers.IsDefaultOrEmpty)
            for (var index = 0; index < declaration.InitialIntegers.Length; index++) values[index] = VmSemanticValue.From(declaration.InitialIntegers[index]);
        else if (declaration.Type == RuntimeMetadataValueType.String && !declaration.InitialStrings.IsDefaultOrEmpty)
            for (var index = 0; index < declaration.InitialStrings.Length; index++) values[index] = VmSemanticValue.From(declaration.InitialStrings[index]);
        else if (declaration.HasInitializer)
            values[0] = declaration.Type == RuntimeMetadataValueType.Integer ? VmSemanticValue.From(declaration.InitialInteger) : VmSemanticValue.From(declaration.InitialString ?? string.Empty);
        return values;
    }
    private VmSemanticValue[] GetOrCreateStaticPrivate(int functionId, FunctionRuntimePrivate declaration)
    {
        var key = (functionId, declaration.Name);
        if (!staticPrivate.TryGetValue(key, out var values)) staticPrivate.Add(key, values = CreatePrivateValues(declaration));
        return values;
    }
    private static bool TryFrameIndex(string? subkey, ReadOnlySpan<VmSemanticValue> indices, out int index)
    {
        index = -1;
        if (indices.Length == 0 && subkey is null) { index = 0; return true; }
        if (indices.Length != 0 && indices[0].TryGetInteger(out var value) && value is >= 0 and <= int.MaxValue) { index = (int)value; return true; }
        return subkey is not null && int.TryParse(subkey, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
    }
    private sealed class VmInvocationFrame
    {
        public long FrameId { get; }
        public int RuntimeFunctionId { get; }
        public FunctionRuntimeMetadata Metadata { get; }
        public VmSemanticValue[] Arg { get; }
        public VmSemanticValue[] Args { get; }
        public VmSemanticValue[] Local { get; }
        public VmSemanticValue[] Locals { get; }
        public Dictionary<string, VmSemanticValue[]> Private { get; }
        public VmSemanticValue ReturnValue { get; set; } = VmSemanticValue.Missing;
        public VmInvocationFrame(long frameId, int runtimeFunctionId, FunctionRuntimeMetadata metadata)
        {
            FrameId = frameId; RuntimeFunctionId = runtimeFunctionId; Metadata = metadata;
            Arg = Enumerable.Repeat(VmSemanticValue.Missing, Math.Max(1, metadata.Parameters.Where(p => !p.IsPrivate && p.Type == RuntimeMetadataValueType.Integer).Select(p => p.Slot + 1).DefaultIfEmpty(0).Max())).ToArray();
            Args = Enumerable.Repeat(VmSemanticValue.Missing, Math.Max(1, metadata.Parameters.Where(p => !p.IsPrivate && p.Type == RuntimeMetadataValueType.String).Select(p => p.Slot + 1).DefaultIfEmpty(0).Max())).ToArray();
            Local = Enumerable.Repeat(VmSemanticValue.From(0L), metadata.LocalSize).ToArray();
            Locals = Enumerable.Repeat(VmSemanticValue.From(string.Empty), metadata.LocalsSize).ToArray();
            Private = metadata.PrivateVariables.Where(variable => !variable.IsStatic).ToDictionary(variable => variable.Name, CreatePrivateValues, StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool SetPc(int pc)
    {
        if (contextOwner is not null) return contextOwner.TrySetCurrentPc(pc);
        var d = GetDescriptor(stack[stackCount - 1].FunctionId);
        if (pc < 0 || pc > d.CodeLength) return false;
        var f = stack[stackCount - 1];
        stack[stackCount - 1] = new(f.FunctionId, pc, f.ReturnKind);
        return true;
    }

    private FunctionRuntimeMetadata? CurrentMetadata()
    {
        if (contextOwner is not null)
            return contextOwner.TryGetCurrentContext(out var context, out _) ? context.Metadata : null;
        return stackCount == 0 ? null : invocations[stackCount - 1]?.Metadata;
    }

    private void SetCurrentReturnValue(VmSemanticValue value)
    {
        if (contextOwner is not null) contextOwner.SetCurrentReturnValue(value);
        else invocations[stackCount - 1]!.ReturnValue = value;
    }

    public bool TryResumeInput(VmInputContinuation continuation, VmSemanticValue value, out VmStopReason reason)
    {
        reason = VmStopReason.TerminalFault;
        InputResumeAttempts++;
        var applied = false;
        if (contextOwner is null || runtimeEffects is not IVmOwnedInputRuntimeEffects inputHost ||
            !contextOwner.TryResume(continuation, value, result => applied = inputHost.TryWriteInputResult(result)))
            return false;
        if (!applied)
        {
            contextOwner.TerminalFault(VmStopReason.TerminalFault, CurrentFrame.FunctionId, CurrentFrame.Pc, "input result write failed");
            return false;
        }
        pendingInput = null;
        InputResumeCompleted++;
        reason = ContinueToDepth(0, int.MaxValue);
        return true;
    }

    private VmFunctionDescriptor GetDescriptor(int id)
    {
        if (contextOwner is not null)
        {
            if (!contextOwner.TryGetCurrentContext(out var context, out _) || context.FunctionId.Value != id)
                return new(-1, 0, 0, VmFunctionState.CodeNotAvailable);
            return context.Program.Descriptors[0];
        }
        return (uint)id < (uint)program!.Descriptors.Length ? program.Descriptors[id] : new(-1, 0, 0, VmFunctionState.CodeNotAvailable);
    }

    private VmStopReason OwnerFault(VmStopReason reason, string message)
    {
        if (contextOwner is null || reason is VmStopReason.Returned or VmStopReason.WaitingForInput or VmStopReason.Halted or VmStopReason.QuitRequested)
            return reason;
        contextOwner.TerminalFault(reason, LastExecutedFunctionId, LastExecutedPc, message);
        return reason;
    }

    private static VmStopReason AdmissionReason(VmAdmissionStatus status) => status switch
    {
        VmAdmissionStatus.Missing => VmStopReason.CodeNotAvailable,
        VmAdmissionStatus.WrongKind => VmStopReason.SemanticNotAvailable,
        VmAdmissionStatus.Terminal => VmStopReason.TerminalFault,
        _ => VmStopReason.SemanticNotAvailable,
    };

    private static VmStopReason StateReason(VmFunctionState state) => state switch
    {
        VmFunctionState.CodeNotAvailable => VmStopReason.CodeNotAvailable,
        VmFunctionState.InvalidStructure => VmStopReason.InvalidStructure,
        VmFunctionState.UnsupportedControl => VmStopReason.UnsupportedControl,
        _ => VmStopReason.SemanticNotAvailable,
    };
    private static VmStopReason SemanticResultReason(VmSemanticStatus status) => status == VmSemanticStatus.Fault ? VmStopReason.SemanticEvaluationFault : VmStopReason.SemanticNotAvailable;
}
public sealed class VmSyntheticProgramBuilder
{
    private readonly List<VmInstruction[]> functions = [];
    public int AddFunction(params VmInstruction[] instructions) { functions.Add(instructions); return functions.Count - 1; }
    public LinkedProgram Build() { var code = functions.SelectMany(x => x).ToArray(); var descriptors = new VmFunctionDescriptor[functions.Count]; var start = 0; for (var i = 0; i < functions.Count; i++) { descriptors[i] = new(i, start, functions[i].Length, VmFunctionState.ExecutableReady); start += functions[i].Length; } return new(code, descriptors, []); }
}
