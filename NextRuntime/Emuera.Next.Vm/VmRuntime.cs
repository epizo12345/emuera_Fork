using System.Collections.Immutable;
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
public enum VmStopReason : byte { Halted, Returned, CodeNotAvailable, SemanticNotAvailable, UnsupportedControl, InvalidStructure, InvalidFunctionId, InvalidLocalPc, StackUnderflow, StepLimit }
public enum VmReturnKind : int { Normal, Propagate }
public enum VmStructuralKind : int { None, Sif, If, ElseIf, Else, EndIf, SelectCase, Case, CaseElse, EndSelect, Repeat, Rend, For, Next, While, Wend, Do, Loop, Break, Continue }
[Flags] public enum LoopDescriptorFlags : int { None = 0, BreakAdvancesCounter = 1 }

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
}
public enum VmOpcode : ushort { Nop, Halt, Branch, Call, Jump, Return, Structural, SemanticBarrier, UnsupportedControl }

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

// *Pc fields contain only function-local PCs. Side-table domains are explicit.
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

public sealed record SourceFunctionPrototype(SourceFunctionId SourceId, ImmutableArray<PrototypeInstruction> Instructions, ImmutableArray<string> Operands);
public sealed record RuntimeFunctionPrototype(RuntimeFunctionId RuntimeId, ImmutableArray<PrototypeInstruction> Instructions, ImmutableArray<string> Operands);
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
            result[i] = new(runtimeId, source.Instructions, source.Operands);
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
    public LinkedProgram(VmInstruction[] code, VmFunctionDescriptor[] descriptors, StructuralLinkRecord[] structuralLinks, SifLinkRecord[]? sifLinks = null, IfGroupDescriptor[]? ifGroups = null, IfClauseRecord[]? ifClauses = null, SelectGroupDescriptor[]? selectGroups = null, SelectCaseRecord[]? selectCases = null, LoopDescriptor[]? loops = null)
        => (Code, Descriptors, StructuralLinks, SifLinks, IfGroups, IfClauses, SelectGroups, SelectCases, Loops) = (code, descriptors, structuralLinks, sifLinks ?? [], ifGroups ?? [], ifClauses ?? [], selectGroups ?? [], selectCases ?? [], loops ?? []);
}
public enum StructuralClassification : byte { Valid, ValidWithStructuralWarning, InvalidStructure }
public readonly record struct StructuralDiagnostic(RuntimeFunctionId RuntimeId, int Pc, StructuralClassification Classification, string Message);
public sealed record ControlLinkResult(LinkedProgram Program, IReadOnlyList<string> Diagnostics, int Calls, int Jumps, int CallScanFailures, int ResolvedCalls, int MissingTargets, int WrongKinds, int CodeAvailableTargets, int CodeUnavailableTargets, int SemanticBarriers, int LinkReadyFunctions = 0, int SemanticPendingFunctions = 0)
{
    public IReadOnlyList<StructuralDiagnostic> StructuralDiagnostics { get; init; } = [];
}

public static class ControlLinker
{
    private static readonly HashSet<PrototypeOpcode> Structure = [PrototypeOpcode.IF, PrototypeOpcode.SIF, PrototypeOpcode.ELSEIF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF, PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT, PrototypeOpcode.REPEAT, PrototypeOpcode.REND, PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.WHILE, PrototypeOpcode.WEND, PrototypeOpcode.DO, PrototypeOpcode.LOOP, PrototypeOpcode.BREAK, PrototypeOpcode.CONTINUE];
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Kinds = new() { [PrototypeOpcode.SIF]=VmStructuralKind.Sif,[PrototypeOpcode.IF]=VmStructuralKind.If,[PrototypeOpcode.ELSEIF]=VmStructuralKind.ElseIf,[PrototypeOpcode.ELSE]=VmStructuralKind.Else,[PrototypeOpcode.ENDIF]=VmStructuralKind.EndIf,[PrototypeOpcode.SELECTCASE]=VmStructuralKind.SelectCase,[PrototypeOpcode.CASE]=VmStructuralKind.Case,[PrototypeOpcode.CASEELSE]=VmStructuralKind.CaseElse,[PrototypeOpcode.ENDSELECT]=VmStructuralKind.EndSelect,[PrototypeOpcode.REPEAT]=VmStructuralKind.Repeat,[PrototypeOpcode.REND]=VmStructuralKind.Rend,[PrototypeOpcode.FOR]=VmStructuralKind.For,[PrototypeOpcode.NEXT]=VmStructuralKind.Next,[PrototypeOpcode.WHILE]=VmStructuralKind.While,[PrototypeOpcode.WEND]=VmStructuralKind.Wend,[PrototypeOpcode.DO]=VmStructuralKind.Do,[PrototypeOpcode.LOOP]=VmStructuralKind.Loop,[PrototypeOpcode.BREAK]=VmStructuralKind.Break,[PrototypeOpcode.CONTINUE]=VmStructuralKind.Continue };
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Openers = new() { [PrototypeOpcode.IF]=VmStructuralKind.If,[PrototypeOpcode.SELECTCASE]=VmStructuralKind.SelectCase,[PrototypeOpcode.REPEAT]=VmStructuralKind.Repeat,[PrototypeOpcode.FOR]=VmStructuralKind.For,[PrototypeOpcode.WHILE]=VmStructuralKind.While,[PrototypeOpcode.DO]=VmStructuralKind.Do };
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Closers = new() { [PrototypeOpcode.ENDIF]=VmStructuralKind.If,[PrototypeOpcode.ENDSELECT]=VmStructuralKind.SelectCase,[PrototypeOpcode.REND]=VmStructuralKind.Repeat,[PrototypeOpcode.NEXT]=VmStructuralKind.For,[PrototypeOpcode.WEND]=VmStructuralKind.While,[PrototypeOpcode.LOOP]=VmStructuralKind.Do };
    private static readonly HashSet<VmStructuralKind> LoopKinds = [VmStructuralKind.Repeat, VmStructuralKind.For, VmStructuralKind.While, VmStructuralKind.Do];
    // Source oracle: Runtime/Script/Statements/FunctionIdentifier.cs IsPartial and Instraction.Child.cs SIF parser.
    private static readonly HashSet<PrototypeOpcode> LegacyPartialOpcodes = [PrototypeOpcode.SIF, PrototypeOpcode.IF, PrototypeOpcode.ELSE, PrototypeOpcode.ELSEIF, PrototypeOpcode.ENDIF, PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT, PrototypeOpcode.REPEAT, PrototypeOpcode.REND, PrototypeOpcode.CONTINUE, PrototypeOpcode.BREAK, PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.WHILE, PrototypeOpcode.WEND, PrototypeOpcode.DO, PrototypeOpcode.LOOP, PrototypeOpcode.PRINTDATA, PrototypeOpcode.PRINTDATAL, PrototypeOpcode.PRINTDATAW, PrototypeOpcode.DATA, PrototypeOpcode.DATAFORM];
    public static bool IsLegacyPartialOpcode(PrototypeOpcode opcode) => LegacyPartialOpcodes.Contains(opcode);
    public static ControlLinkResult Link(FunctionCatalog catalog, IReadOnlyList<RuntimeFunctionPrototype> prototypes, bool ignoreCase = true, bool compatiCallEvent = false)
    {
        var code = new List<VmInstruction>(); var descriptors = Enumerable.Range(0, catalog.Count).Select(id => new VmFunctionDescriptor(id, 0, 0, VmFunctionState.CodeNotAvailable)).ToArray(); var records = new List<StructuralLinkRecord>(); var sifs = new List<SifLinkRecord>(); var ifGroups = new List<IfGroupDescriptor>(); var ifClauses = new List<IfClauseRecord>(); var selectGroups = new List<SelectGroupDescriptor>(); var selectCases = new List<SelectCaseRecord>(); var loops = new List<LoopDescriptor>(); var diagnostics = new List<string>(); var structuralDiagnostics = new List<StructuralDiagnostic>();
        var calls = 0; var jumps = 0; var scans = 0; var resolved = 0; var missing = 0; var wrong = 0; var yes = 0; var no = 0; var barriers = 0;
        foreach (var prototype in prototypes.OrderBy(x => x.RuntimeId.Value))
        {
            var runtimeId = prototype.RuntimeId.Value;
            var start = code.Count; var state = VmFunctionState.LinkedSemanticPending; var local = new List<StructuralLinkRecord>();
            for (var pc = 0; pc < prototype.Instructions.Length; pc++)
            {
                var p = prototype.Instructions[pc]; var operand = pc < prototype.Operands.Length ? prototype.Operands[pc] : string.Empty;
                if (p.Opcode is PrototypeOpcode.CALL or PrototypeOpcode.JUMP)
                {
                    if (p.Opcode == PrototypeOpcode.CALL) calls++; else jumps++; if (!FixedCallTargetScanner.TryScan(operand, out var scan)) { scans++; state = VmFunctionState.UnsupportedControl; diagnostics.Add($"function={runtimeId} pc={pc} scan-fail"); code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                    var resolution = FixedCallResolver.Resolve(catalog, scan.Target, ignoreCase, compatiCallEvent); if (!resolution.FunctionResolved) { if (resolution.Reason?.StartsWith("WrongKind", StringComparison.Ordinal) == true) wrong++; else missing++; state = VmFunctionState.UnsupportedControl; code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                    resolved++; if (resolution.CodeAvailable) yes++; else no++; code.Add(Linked(p, p.Opcode == PrototypeOpcode.CALL ? VmOpcode.Call : VmOpcode.Jump, resolution.RuntimeId.Value)); continue;
                }
                if (p.Opcode == PrototypeOpcode.RETURN) { code.Add(Linked(p, VmOpcode.Return)); continue; }
                if (Structure.Contains(p.Opcode)) { if (p.Opcode == PrototypeOpcode.SIF) { if (pc + 1 >= prototype.Instructions.Length) { state = VmFunctionState.InvalidStructure; diagnostics.Add($"function={runtimeId} pc={pc} malformed=SIF-no-next"); structuralDiagnostics.Add(new(new(runtimeId), pc, StructuralClassification.InvalidStructure, "SIF-no-next")); } else { if (IsLegacyPartialOpcode(prototype.Instructions[pc + 1].Opcode)) { diagnostics.Add($"function={runtimeId} pc={pc} warning=SIF-partial-next"); structuralDiagnostics.Add(new(new(runtimeId), pc, StructuralClassification.ValidWithStructuralWarning, "SIF-partial-next")); } sifs.Add(new(runtimeId, pc, pc + 1, pc + 2)); } } local.Add(new(runtimeId, pc, -1, -1, Kinds[p.Opcode], 0)); code.Add(Linked(p, VmOpcode.Structural)); continue; }
                if (p.Opcode is PrototypeOpcode.GOTO or PrototypeOpcode.TRYJUMP or PrototypeOpcode.TRYGOTO or PrototypeOpcode.TRYGOTOFORM) { state = VmFunctionState.UnsupportedControl; code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                barriers++; code.Add(Linked(p, VmOpcode.SemanticBarrier));
            }
            var linked = LinkStructure(runtimeId, prototype.Instructions, local, diagnostics, structuralDiagnostics); if (linked.Invalid) state = VmFunctionState.InvalidStructure; records.AddRange(linked.Records); ifGroups.AddRange(linked.IfGroups); ifClauses.AddRange(linked.IfClauses); selectGroups.AddRange(linked.SelectGroups); selectCases.AddRange(linked.SelectCases); loops.AddRange(linked.Loops); descriptors[runtimeId] = new(runtimeId, start, code.Count - start, state);
        }
        var program = new LinkedProgram(code.ToArray(), descriptors, records.ToArray(), sifs.ToArray(), ifGroups.ToArray(), ifClauses.ToArray(), selectGroups.ToArray(), selectCases.ToArray(), loops.ToArray()); return new ControlLinkResult(program, diagnostics, calls, jumps, scans, resolved, missing, wrong, yes, no, barriers, 0, prototypes.Count) { StructuralDiagnostics = structuralDiagnostics.ToArray() };
    }
    private static VmInstruction Linked(PrototypeInstruction p, VmOpcode opcode, int aux = -1) => new((ushort)opcode, (ushort)p.Flags, p.OperandOffset, p.OperandLength, aux);
    private sealed class OpenFrame
    { public VmStructuralKind Family; public int HeaderRecordIndex, HeaderPc, Depth; public List<int> Clauses = [], Cases = [], LoopControlRecordIndices = []; public OpenFrame(VmStructuralKind family, int headerRecordIndex, int headerPc, int depth) => (Family, HeaderRecordIndex, HeaderPc, Depth) = (family, headerRecordIndex, headerPc, depth); }
    private sealed record StructureResult(StructuralLinkRecord[] Records, IfGroupDescriptor[] IfGroups, IfClauseRecord[] IfClauses, SelectGroupDescriptor[] SelectGroups, SelectCaseRecord[] SelectCases, LoopDescriptor[] Loops, bool Invalid);
    private static StructureResult LinkStructure(int functionId, IReadOnlyList<PrototypeInstruction> instructions, List<StructuralLinkRecord> records, List<string> diagnostics, List<StructuralDiagnostic> structuralDiagnostics)
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
                if (close == VmStructuralKind.If) { var clausePcs = new[] { frame.HeaderPc }.Concat(frame.Clauses).ToArray(); var group = ifGroups.Count; var first = ifClauses.Count; for (var i = 0; i < clausePcs.Length; i++) { var clausePc = clausePcs[i]; ifClauses.Add(new(functionId, clausePc, Kinds[instructions[clausePc].Opcode])); var ix = indexByPc[clausePc]; records[ix] = records[ix] with { TargetPc = i + 1 < clausePcs.Length ? clausePcs[i + 1] : pc, AuxiliaryPc = pc + 1, GroupIndex = group }; } ifGroups.Add(new(functionId, frame.HeaderPc, first, clausePcs.Length, pc + 1)); }
                else if (close == VmStructuralKind.SelectCase) { var casePcs = frame.Cases.ToArray(); var group = selectGroups.Count; var first = selectCases.Count; for (var i = 0; i < casePcs.Length; i++) { var casePc = casePcs[i]; selectCases.Add(new(functionId, casePc, Kinds[instructions[casePc].Opcode])); var ix = indexByPc[casePc]; records[ix] = records[ix] with { TargetPc = i + 1 < casePcs.Length ? casePcs[i + 1] : pc, AuxiliaryPc = pc + 1, GroupIndex = group }; } selectGroups.Add(new(functionId, frame.HeaderPc, first, casePcs.Length, pc + 1)); }
                else { var loop = new LoopDescriptor(functionId, close, frame.HeaderPc, frame.HeaderPc + 1, pc, pc + 1, close == VmStructuralKind.Do ? pc : close == VmStructuralKind.While ? frame.HeaderPc : pc, close is VmStructuralKind.Repeat or VmStructuralKind.For ? LoopDescriptorFlags.BreakAdvancesCounter : LoopDescriptorFlags.None); var loopIndex = loops.Count; loops.Add(loop); foreach (var controlIndex in frame.LoopControlRecordIndices) { var control = records[controlIndex]; var target = control.Kind == VmStructuralKind.Break ? loop.ExitPc : close is VmStructuralKind.Repeat or VmStructuralKind.For ? loop.BodyEntryPc : loop.ContinueCheckPc; records[controlIndex] = control with { TargetPc = target, AuxiliaryPc = loop.ContinueCheckPc, LoopIndex = loopIndex }; } }
            }
            if (op is PrototypeOpcode.BREAK or PrototypeOpcode.CONTINUE) { var frame = stack.LastOrDefault(x => LoopKinds.Contains(x.Family)); if (frame is null) Bad(pc, $"loop-control-outside-{op}"); else { frame.LoopControlRecordIndices.Add(recordIndex); records[recordIndex] = record with { TargetPc = -1, AuxiliaryPc = -1, LoopIndex = -1, Depth = depth }; } }
        }
        foreach (var frame in stack) Bad(frame.HeaderPc, $"missing-close-{frame.Family}"); return new(records.ToArray(), ifGroups.ToArray(), ifClauses.ToArray(), selectGroups.ToArray(), selectCases.ToArray(), loops.ToArray(), invalid);
    }
}

public sealed class VmMachine
{
    private readonly LinkedProgram program; private VmFrame[] stack = new VmFrame[16]; private int stackCount;
    public VmMachine(LinkedProgram program) => this.program = program;
    public VmStopReason Run(RuntimeFunctionId entryFunctionId, int maxSteps = 100_000)
    {
        stackCount = 0; if (!TryPush(entryFunctionId.Value, VmReturnKind.Normal, out var reason)) return reason;
        for (var steps = 0; steps < maxSteps; steps++)
        {
            if (stackCount == 0) return VmStopReason.Returned; ref var frame = ref stack[stackCount - 1]; var descriptor = GetDescriptor(frame.FunctionId); if (descriptor.State != VmFunctionState.ExecutableReady) return StateReason(descriptor.State);
            if (frame.Pc == descriptor.CodeLength) { if (!Return()) return VmStopReason.StackUnderflow; continue; }
            if (frame.Pc < 0 || frame.Pc > descriptor.CodeLength) return VmStopReason.InvalidLocalPc; var instruction = program.Code[descriptor.CodeStart + frame.Pc]; frame = new(frame.FunctionId, frame.Pc + 1, frame.ReturnKind);
            switch ((VmOpcode)instruction.Opcode) { case VmOpcode.Nop: break; case VmOpcode.Halt: return VmStopReason.Halted; case VmOpcode.Branch: if (!SetPc(instruction.Aux)) return VmStopReason.InvalidLocalPc; break; case VmOpcode.Call: if (!TryPush(instruction.Aux, VmReturnKind.Normal, out reason)) return reason; break; case VmOpcode.Jump: if (!TryPush(instruction.Aux, VmReturnKind.Propagate, out reason)) return reason; break; case VmOpcode.Return: if (!Return()) return VmStopReason.StackUnderflow; break; case VmOpcode.SemanticBarrier: return VmStopReason.SemanticNotAvailable; default: return VmStopReason.UnsupportedControl; }
        }
        return VmStopReason.StepLimit;
    }
    private bool TryPush(int id, VmReturnKind kind, out VmStopReason reason) { if ((uint)id >= (uint)program.Descriptors.Length) { reason = VmStopReason.InvalidFunctionId; return false; } var d = program.Descriptors[id]; if (d.State != VmFunctionState.ExecutableReady) { reason = StateReason(d.State); return false; } if (stackCount == stack.Length) Array.Resize(ref stack, checked(stack.Length * 2)); stack[stackCount++] = new(id, 0, kind); reason = VmStopReason.Returned; return true; }
    private bool Return() { if (stackCount == 0) return false; var propagate = stack[stackCount - 1].ReturnKind == VmReturnKind.Propagate; stackCount--; while (propagate && stackCount > 0) { propagate = stack[stackCount - 1].ReturnKind == VmReturnKind.Propagate; stackCount--; } return true; }
    private bool SetPc(int pc) { var d = GetDescriptor(stack[stackCount - 1].FunctionId); if (pc < 0 || pc > d.CodeLength) return false; var f = stack[stackCount - 1]; stack[stackCount - 1] = new(f.FunctionId, pc, f.ReturnKind); return true; }
    private VmFunctionDescriptor GetDescriptor(int id) => (uint)id < (uint)program.Descriptors.Length ? program.Descriptors[id] : new(-1, 0, 0, VmFunctionState.CodeNotAvailable);
    private static VmStopReason StateReason(VmFunctionState state) => state switch { VmFunctionState.CodeNotAvailable => VmStopReason.CodeNotAvailable, VmFunctionState.InvalidStructure => VmStopReason.InvalidStructure, VmFunctionState.UnsupportedControl => VmStopReason.UnsupportedControl, _ => VmStopReason.SemanticNotAvailable };
}
public sealed class VmSyntheticProgramBuilder
{
    private readonly List<VmInstruction[]> functions = [];
    public int AddFunction(params VmInstruction[] instructions) { functions.Add(instructions); return functions.Count - 1; }
    public LinkedProgram Build() { var code = functions.SelectMany(x => x).ToArray(); var descriptors = new VmFunctionDescriptor[functions.Count]; var start = 0; for (var i = 0; i < functions.Count; i++) { descriptors[i] = new(i, start, functions[i].Length, VmFunctionState.ExecutableReady); start += functions[i].Length; } return new(code, descriptors, []); }
}
