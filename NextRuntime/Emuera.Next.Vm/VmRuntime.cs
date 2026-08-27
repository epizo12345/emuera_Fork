using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Vm;

// [Emuera改修:NEXT-2A-R1 2026-08-28]
// Linked hot data is value-oriented. Legacy parser/runtime objects stay outside.
public enum FunctionKind : byte { Normal, Event, Method }
public enum VmFunctionState : int
{
    CodeNotAvailable, LinkReady, LinkedSemanticPending, UnsupportedControl,
    InvalidStructure, ExecutableReady, Ready = ExecutableReady,
}
public enum VmStopReason : byte { Halted, Returned, CodeNotAvailable, SemanticNotAvailable, UnsupportedControl, InvalidFunctionId, InvalidLocalPc, StackUnderflow, StepLimit }
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
    public static VmInstruction Call(int functionId) => new((ushort)VmOpcode.Call, aux: functionId);
    public static VmInstruction Jump(int functionId) => new((ushort)VmOpcode.Jump, aux: functionId);
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

public readonly record struct SemanticFunctionKey(string FileIdentity, int StartLine);
public readonly record struct SemanticFunctionMetadata(string EffectiveName, bool EffectiveNameKnown, FunctionKind Kind);
public sealed record FunctionDefinition(string PhysicalName, string FileIdentity, SourceSpan Span,
    SourceIndexFlags Flags = SourceIndexFlags.None, FunctionKind Kind = FunctionKind.Normal,
    string? EffectiveName = null, bool EffectiveNameKnown = true)
{ public string Name => PhysicalName; }
public readonly record struct CatalogSourceRef(int FileOrdinal, int FunctionOrdinal);

// Value record; no per-function catalog object and no per-name int[].
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct FunctionCatalogEntry
{
    public readonly int FunctionId;
    public readonly CatalogSourceRef SourceRef;
    public readonly SourceSpan Span;
    public readonly int PhysicalNameId, EffectiveNameId;
    public readonly FunctionKind Kind;
    public readonly SourceIndexFlags Flags;
    public readonly bool CodeAvailable;
    public FunctionCatalogEntry(int functionId, CatalogSourceRef sourceRef, SourceSpan span, int physicalNameId, int effectiveNameId, FunctionKind kind, SourceIndexFlags flags, bool codeAvailable = false)
        => (FunctionId, SourceRef, Span, PhysicalNameId, EffectiveNameId, Kind, Flags, CodeAvailable) = (functionId, sourceRef, span, physicalNameId, effectiveNameId, kind, flags, codeAvailable);
}
public readonly record struct NameRange(int Start, int Count);

public sealed class FunctionCatalog
{
    private readonly string[] fileTable, nameTable;
    private readonly Dictionary<string, NameRange> nameRanges;
    private readonly int[] candidateFunctionIds;
    private readonly Dictionary<SemanticFunctionKey, int> sourceLookup;
    public IReadOnlyList<FunctionCatalogEntry> Entries { get; }
    public IReadOnlyList<string> FileTable => fileTable;
    public IReadOnlyList<string> NameTable => nameTable;
    public bool IgnoreCase { get; }
    public int Count => Entries.Count;
    public int CandidateIdCount => candidateFunctionIds.Length;
    public int CompactEntrySize => Marshal.SizeOf<FunctionCatalogEntry>();
    private FunctionCatalog(FunctionCatalogEntry[] entries, string[] files, string[] names, Dictionary<string, NameRange> ranges, int[] candidates, Dictionary<SemanticFunctionKey, int> lookup, bool ignoreCase)
        => (Entries, fileTable, nameTable, nameRanges, candidateFunctionIds, sourceLookup, IgnoreCase) = (entries, files, names, ranges, candidates, lookup, ignoreCase);

    public static FunctionCatalog FromSourceIndex(IReadOnlyList<SourceFileIndex> files, IReadOnlyDictionary<SemanticFunctionKey, SemanticFunctionMetadata>? overlay = null, bool ignoreCase = true)
    {
        var definitions = new List<FunctionDefinition>();
        foreach (var file in files) foreach (var function in file.Functions)
        {
            var key = new SemanticFunctionKey(Path.GetFullPath(file.FileIdentity), function.Span.StartLine);
            if (overlay is not null && overlay.TryGetValue(key, out var metadata))
                definitions.Add(new(function.Name, file.FileIdentity, function.Span, function.Flags, metadata.Kind, metadata.EffectiveName, metadata.EffectiveNameKnown));
            else definitions.Add(new(function.Name, file.FileIdentity, function.Span, function.Flags));
        }
        return FromDefinitions(definitions, ignoreCase);
    }
    public static FunctionCatalog FromDefinitions(IEnumerable<FunctionDefinition> definitions, bool ignoreCase = true)
    {
        var list = definitions.ToArray(); var files = new List<string>(); var fileIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var functionOrdinals = new Dictionary<int, int>(); var names = new List<string>(); var nameIds = new Dictionary<string, int>(StringComparer.Ordinal); var entries = new FunctionCatalogEntry[list.Length]; var lookup = new Dictionary<SemanticFunctionKey, int>();
        int NameId(string value) { if (!nameIds.TryGetValue(value, out var id)) { id = names.Count; names.Add(value); nameIds.Add(value, id); } return id; }
        for (var id = 0; id < list.Length; id++)
        {
            var d = list[id]; var full = Path.GetFullPath(d.FileIdentity); if (!fileIds.TryGetValue(full, out var fileOrdinal)) { fileOrdinal = files.Count; files.Add(full); fileIds.Add(full, fileOrdinal); functionOrdinals[fileOrdinal] = 0; }
            var functionOrdinal = functionOrdinals[fileOrdinal]++; var known = d.EffectiveNameKnown && !string.IsNullOrEmpty(d.EffectiveName ?? d.PhysicalName); entries[id] = new(id, new(fileOrdinal, functionOrdinal), d.Span, NameId(d.PhysicalName), known ? NameId(d.EffectiveName ?? d.PhysicalName) : -1, d.Kind, d.Flags); lookup[new(full, d.Span.StartLine)] = id;
        }
        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal; var grouped = Enumerable.Range(0, entries.Length).Where(id => entries[id].EffectiveNameId >= 0).GroupBy(id => names[entries[id].EffectiveNameId], comparer).ToArray(); var ranges = new Dictionary<string, NameRange>(comparer); var candidates = new List<int>();
        foreach (var group in grouped) { var start = candidates.Count; candidates.AddRange(group); ranges[group.Key] = new(start, group.Count()); }
        return new(entries, files.ToArray(), names.ToArray(), ranges, candidates.ToArray(), lookup, ignoreCase);
    }
    public FunctionCatalogEntry this[int id] => Entries[id];
    public string GetPhysicalName(int id) => nameTable[Entries[id].PhysicalNameId];
    public string? GetEffectiveName(int id) => Entries[id].EffectiveNameId < 0 ? null : nameTable[Entries[id].EffectiveNameId];
    public string GetFileIdentity(int id) => fileTable[Entries[id].SourceRef.FileOrdinal];
    public bool TryGetId(string fileIdentity, int startLine, out int id) => sourceLookup.TryGetValue(new(Path.GetFullPath(fileIdentity), startLine), out id);
    public IReadOnlyList<int> FindByName(string name, bool? requestedIgnoreCase = null)
    { if (requestedIgnoreCase.HasValue && requestedIgnoreCase.Value != IgnoreCase) return Array.Empty<int>(); return nameRanges.TryGetValue(name, out var range) ? new ArraySegment<int>(candidateFunctionIds, range.Start, range.Count) : Array.Empty<int>(); }
    public void MarkCodeAvailable(IEnumerable<int> ids)
    { var values = (FunctionCatalogEntry[])Entries; foreach (var id in ids) { var e = values[id]; values[id] = new(e.FunctionId, e.SourceRef, e.Span, e.PhysicalNameId, e.EffectiveNameId, e.Kind, e.Flags, true); } }
}

public readonly record struct FixedTargetScan(string Target, int Start, int End);
public static class FixedCallTargetScanner
{
    public static bool TryScan(string operand, out FixedTargetScan result)
    { var start = 0; while (start < operand.Length && (operand[start] == ' ' || operand[start] == '\t')) start++; var end = start; while (end < operand.Length && operand[end] is not '(' and not '[' and not ',' and not ';') end++; while (end > start && (operand[end - 1] == ' ' || operand[end - 1] == '\t')) end--; result = new(operand[start..end], start, end); return end > start; }
}
public readonly record struct CallResolution(bool FunctionResolved, bool CodeAvailable, int FunctionId, string? Reason);
public static class FixedCallResolver
{
    public static CallResolution Resolve(FunctionCatalog catalog, string target, bool ignoreCase, bool compatiCallEvent)
    {
        var candidates = catalog.FindByName(target, ignoreCase); if (candidates.Count == 0) return new(false, false, -1, "MissingTarget"); var entry = catalog[candidates[0]];
        if (entry.Kind == FunctionKind.Method) return new(false, false, -1, "WrongKindMethod"); if (entry.Kind == FunctionKind.Event && !compatiCallEvent) return new(false, false, -1, "WrongKindEvent"); return new(true, entry.CodeAvailable, entry.FunctionId, entry.CodeAvailable ? null : "CodeNotAvailable");
    }
}

public sealed record FunctionPrototype(int FunctionId, ImmutableArray<PrototypeInstruction> Instructions, ImmutableArray<string> Operands);
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
public sealed record ControlLinkResult(LinkedProgram Program, IReadOnlyList<string> Diagnostics, int Calls, int Jumps, int CallScanFailures, int ResolvedCalls, int MissingTargets, int WrongKinds, int CodeAvailableTargets, int CodeUnavailableTargets, int SemanticBarriers, int LinkReadyFunctions = 0, int SemanticPendingFunctions = 0);

public static class ControlLinker
{
    private static readonly HashSet<PrototypeOpcode> Structure = [PrototypeOpcode.IF, PrototypeOpcode.SIF, PrototypeOpcode.ELSEIF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF, PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT, PrototypeOpcode.REPEAT, PrototypeOpcode.REND, PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.WHILE, PrototypeOpcode.WEND, PrototypeOpcode.DO, PrototypeOpcode.LOOP, PrototypeOpcode.BREAK, PrototypeOpcode.CONTINUE];
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Kinds = new() { [PrototypeOpcode.SIF]=VmStructuralKind.Sif,[PrototypeOpcode.IF]=VmStructuralKind.If,[PrototypeOpcode.ELSEIF]=VmStructuralKind.ElseIf,[PrototypeOpcode.ELSE]=VmStructuralKind.Else,[PrototypeOpcode.ENDIF]=VmStructuralKind.EndIf,[PrototypeOpcode.SELECTCASE]=VmStructuralKind.SelectCase,[PrototypeOpcode.CASE]=VmStructuralKind.Case,[PrototypeOpcode.CASEELSE]=VmStructuralKind.CaseElse,[PrototypeOpcode.ENDSELECT]=VmStructuralKind.EndSelect,[PrototypeOpcode.REPEAT]=VmStructuralKind.Repeat,[PrototypeOpcode.REND]=VmStructuralKind.Rend,[PrototypeOpcode.FOR]=VmStructuralKind.For,[PrototypeOpcode.NEXT]=VmStructuralKind.Next,[PrototypeOpcode.WHILE]=VmStructuralKind.While,[PrototypeOpcode.WEND]=VmStructuralKind.Wend,[PrototypeOpcode.DO]=VmStructuralKind.Do,[PrototypeOpcode.LOOP]=VmStructuralKind.Loop,[PrototypeOpcode.BREAK]=VmStructuralKind.Break,[PrototypeOpcode.CONTINUE]=VmStructuralKind.Continue };
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Openers = new() { [PrototypeOpcode.IF]=VmStructuralKind.If,[PrototypeOpcode.SELECTCASE]=VmStructuralKind.SelectCase,[PrototypeOpcode.REPEAT]=VmStructuralKind.Repeat,[PrototypeOpcode.FOR]=VmStructuralKind.For,[PrototypeOpcode.WHILE]=VmStructuralKind.While,[PrototypeOpcode.DO]=VmStructuralKind.Do };
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> Closers = new() { [PrototypeOpcode.ENDIF]=VmStructuralKind.If,[PrototypeOpcode.ENDSELECT]=VmStructuralKind.SelectCase,[PrototypeOpcode.REND]=VmStructuralKind.Repeat,[PrototypeOpcode.NEXT]=VmStructuralKind.For,[PrototypeOpcode.WEND]=VmStructuralKind.While,[PrototypeOpcode.LOOP]=VmStructuralKind.Do };
    private static readonly HashSet<VmStructuralKind> LoopKinds = [VmStructuralKind.Repeat, VmStructuralKind.For, VmStructuralKind.While, VmStructuralKind.Do];
    public static ControlLinkResult Link(FunctionCatalog catalog, IReadOnlyList<FunctionPrototype> prototypes, bool ignoreCase = true, bool compatiCallEvent = false)
    {
        var code = new List<VmInstruction>(); var descriptors = Enumerable.Range(0, catalog.Count).Select(id => new VmFunctionDescriptor(id, 0, 0, VmFunctionState.CodeNotAvailable)).ToArray(); var records = new List<StructuralLinkRecord>(); var sifs = new List<SifLinkRecord>(); var ifGroups = new List<IfGroupDescriptor>(); var ifClauses = new List<IfClauseRecord>(); var selectGroups = new List<SelectGroupDescriptor>(); var selectCases = new List<SelectCaseRecord>(); var loops = new List<LoopDescriptor>(); var diagnostics = new List<string>();
        var calls = 0; var jumps = 0; var scans = 0; var resolved = 0; var missing = 0; var wrong = 0; var yes = 0; var no = 0; var barriers = 0;
        foreach (var prototype in prototypes.OrderBy(x => x.FunctionId))
        {
            var start = code.Count; var state = VmFunctionState.LinkedSemanticPending; var local = new List<StructuralLinkRecord>();
            for (var pc = 0; pc < prototype.Instructions.Length; pc++)
            {
                var p = prototype.Instructions[pc]; var operand = pc < prototype.Operands.Length ? prototype.Operands[pc] : string.Empty;
                if (p.Opcode is PrototypeOpcode.CALL or PrototypeOpcode.JUMP)
                {
                    if (p.Opcode == PrototypeOpcode.CALL) calls++; else jumps++; if (!FixedCallTargetScanner.TryScan(operand, out var scan)) { scans++; state = VmFunctionState.UnsupportedControl; diagnostics.Add($"function={prototype.FunctionId} pc={pc} scan-fail"); code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                    var resolution = FixedCallResolver.Resolve(catalog, scan.Target, ignoreCase, compatiCallEvent); if (!resolution.FunctionResolved) { if (resolution.Reason?.StartsWith("WrongKind", StringComparison.Ordinal) == true) wrong++; else missing++; state = VmFunctionState.UnsupportedControl; code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                    resolved++; if (resolution.CodeAvailable) yes++; else no++; code.Add(Linked(p, p.Opcode == PrototypeOpcode.CALL ? VmOpcode.Call : VmOpcode.Jump, resolution.FunctionId)); continue;
                }
                if (p.Opcode == PrototypeOpcode.RETURN) { code.Add(Linked(p, VmOpcode.Return)); continue; }
                if (Structure.Contains(p.Opcode)) { if (p.Opcode == PrototypeOpcode.SIF) { if (pc + 1 >= prototype.Instructions.Length) { state = VmFunctionState.InvalidStructure; diagnostics.Add($"function={prototype.FunctionId} pc={pc} malformed=SIF-no-next"); } else sifs.Add(new(prototype.FunctionId, pc, pc + 1, pc + 2)); } local.Add(new(prototype.FunctionId, pc, -1, -1, Kinds[p.Opcode], 0)); code.Add(Linked(p, VmOpcode.Structural)); continue; }
                if (p.Opcode is PrototypeOpcode.GOTO or PrototypeOpcode.TRYJUMP or PrototypeOpcode.TRYGOTO or PrototypeOpcode.TRYGOTOFORM) { state = VmFunctionState.UnsupportedControl; code.Add(Linked(p, VmOpcode.UnsupportedControl)); continue; }
                barriers++; code.Add(Linked(p, VmOpcode.SemanticBarrier));
            }
            var linked = LinkStructure(prototype.FunctionId, prototype.Instructions, local, diagnostics); if (linked.Invalid) state = VmFunctionState.InvalidStructure; records.AddRange(linked.Records); ifGroups.AddRange(linked.IfGroups); ifClauses.AddRange(linked.IfClauses); selectGroups.AddRange(linked.SelectGroups); selectCases.AddRange(linked.SelectCases); loops.AddRange(linked.Loops); descriptors[prototype.FunctionId] = new(prototype.FunctionId, start, code.Count - start, state);
        }
        var program = new LinkedProgram(code.ToArray(), descriptors, records.ToArray(), sifs.ToArray(), ifGroups.ToArray(), ifClauses.ToArray(), selectGroups.ToArray(), selectCases.ToArray(), loops.ToArray()); return new(program, diagnostics, calls, jumps, scans, resolved, missing, wrong, yes, no, barriers, 0, prototypes.Count);
    }
    private static VmInstruction Linked(PrototypeInstruction p, VmOpcode opcode, int aux = -1) => new((ushort)opcode, (ushort)p.Flags, p.OperandOffset, p.OperandLength, aux);
    private sealed class OpenFrame
    { public VmStructuralKind Family; public int RecordIndex, HeaderPc, Depth; public List<int> Clauses = [], Cases = []; public OpenFrame(VmStructuralKind family, int recordIndex, int headerPc, int depth) => (Family, RecordIndex, HeaderPc, Depth) = (family, recordIndex, headerPc, depth); }
    private sealed record StructureResult(StructuralLinkRecord[] Records, IfGroupDescriptor[] IfGroups, IfClauseRecord[] IfClauses, SelectGroupDescriptor[] SelectGroups, SelectCaseRecord[] SelectCases, LoopDescriptor[] Loops, bool Invalid);
    private static StructureResult LinkStructure(int functionId, IReadOnlyList<PrototypeInstruction> instructions, List<StructuralLinkRecord> records, List<string> diagnostics)
    {
        var stack = new List<OpenFrame>(); var invalid = false; var depth = 0; var ifGroups = new List<IfGroupDescriptor>(); var ifClauses = new List<IfClauseRecord>(); var selectGroups = new List<SelectGroupDescriptor>(); var selectCases = new List<SelectCaseRecord>(); var loops = new List<LoopDescriptor>(); var indexByPc = records.Select((x, i) => (x.Pc, Index: i)).ToDictionary(x => x.Pc, x => x.Index);
        void Bad(int pc, string text) { invalid = true; diagnostics.Add($"function={functionId} pc={pc} malformed={text}"); }
        foreach (var pair in indexByPc.OrderBy(x => x.Key))
        {
            var pc = pair.Key; var op = instructions[pc].Opcode; var recordIndex = pair.Value; var record = records[recordIndex];
            if (Openers.TryGetValue(op, out var family)) { depth++; stack.Add(new(family, recordIndex, pc, depth)); records[recordIndex] = record with { Depth = depth }; continue; }
            if (op is PrototypeOpcode.ELSEIF or PrototypeOpcode.ELSE) { if (stack.Count == 0 || stack[^1].Family != VmStructuralKind.If) { Bad(pc, op.ToString()); continue; } var frame = stack[^1]; if (op == PrototypeOpcode.ELSE && frame.Clauses.Any(x => instructions[x].Opcode == PrototypeOpcode.ELSE)) Bad(pc, "duplicate-ELSE"); frame.Clauses.Add(pc); records[recordIndex] = record with { Depth = depth }; continue; }
            if (op is PrototypeOpcode.CASE or PrototypeOpcode.CASEELSE) { if (stack.Count == 0 || stack[^1].Family != VmStructuralKind.SelectCase) { Bad(pc, op.ToString()); continue; } var frame = stack[^1]; if (op == PrototypeOpcode.CASEELSE && frame.Cases.Any(x => instructions[x].Opcode == PrototypeOpcode.CASEELSE)) Bad(pc, "duplicate-CASEELSE"); frame.Cases.Add(pc); records[recordIndex] = record with { Depth = depth }; continue; }
            if (Closers.TryGetValue(op, out var close))
            {
                if (stack.Count == 0 || stack[^1].Family != close) { Bad(pc, $"crossed-or-missing-{op}"); continue; }
                var frame = stack[^1]; stack.RemoveAt(stack.Count - 1); depth--; records[frame.RecordIndex] = records[frame.RecordIndex] with { TargetPc = pc, AuxiliaryPc = pc + 1 }; records[recordIndex] = record with { TargetPc = frame.HeaderPc, Depth = depth };
                if (close == VmStructuralKind.If) { var clausePcs = new[] { frame.HeaderPc }.Concat(frame.Clauses).ToArray(); var group = ifGroups.Count; var first = ifClauses.Count; for (var i = 0; i < clausePcs.Length; i++) { var clausePc = clausePcs[i]; ifClauses.Add(new(functionId, clausePc, Kinds[instructions[clausePc].Opcode])); var ix = indexByPc[clausePc]; records[ix] = records[ix] with { TargetPc = i + 1 < clausePcs.Length ? clausePcs[i + 1] : pc, AuxiliaryPc = pc + 1, GroupIndex = group }; } ifGroups.Add(new(functionId, frame.HeaderPc, first, clausePcs.Length, pc + 1)); }
                else if (close == VmStructuralKind.SelectCase) { var casePcs = frame.Cases.ToArray(); var group = selectGroups.Count; var first = selectCases.Count; for (var i = 0; i < casePcs.Length; i++) { var casePc = casePcs[i]; selectCases.Add(new(functionId, casePc, Kinds[instructions[casePc].Opcode])); var ix = indexByPc[casePc]; records[ix] = records[ix] with { TargetPc = i + 1 < casePcs.Length ? casePcs[i + 1] : pc, AuxiliaryPc = pc + 1, GroupIndex = group }; } selectGroups.Add(new(functionId, frame.HeaderPc, first, casePcs.Length, pc + 1)); }
                else { var loop = new LoopDescriptor(functionId, close, frame.HeaderPc, frame.HeaderPc + 1, pc, pc + 1, close == VmStructuralKind.Do ? pc : close == VmStructuralKind.While ? frame.HeaderPc : pc, close is VmStructuralKind.Repeat or VmStructuralKind.For ? LoopDescriptorFlags.BreakAdvancesCounter : LoopDescriptorFlags.None); var loopIndex = loops.Count; loops.Add(loop); for (var i = 0; i < records.Count; i++) if (records[i].LoopIndex == frame.RecordIndex) { var target = records[i].Kind == VmStructuralKind.Break ? loop.ExitPc : close is VmStructuralKind.Repeat or VmStructuralKind.For ? loop.BodyEntryPc : loop.ContinueCheckPc; records[i] = records[i] with { TargetPc = target, AuxiliaryPc = loop.ContinueCheckPc, LoopIndex = loopIndex }; } }
            }
            if (op is PrototypeOpcode.BREAK or PrototypeOpcode.CONTINUE) { var frame = stack.LastOrDefault(x => LoopKinds.Contains(x.Family)); if (frame is null) Bad(pc, $"loop-control-outside-{op}"); else records[recordIndex] = record with { TargetPc = -1, AuxiliaryPc = frame.RecordIndex, LoopIndex = frame.RecordIndex, Depth = depth }; }
        }
        foreach (var frame in stack) Bad(frame.HeaderPc, $"missing-close-{frame.Family}"); return new(records.ToArray(), ifGroups.ToArray(), ifClauses.ToArray(), selectGroups.ToArray(), selectCases.ToArray(), loops.ToArray(), invalid);
    }
}

public sealed class VmMachine
{
    private readonly LinkedProgram program; private VmFrame[] stack = new VmFrame[16]; private int stackCount;
    public VmMachine(LinkedProgram program) => this.program = program;
    public VmStopReason Run(int entryFunctionId, int maxSteps = 100_000)
    {
        stackCount = 0; if (!TryPush(entryFunctionId, VmReturnKind.Normal, out var reason)) return reason;
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
    private static VmStopReason StateReason(VmFunctionState state) => state switch { VmFunctionState.CodeNotAvailable => VmStopReason.CodeNotAvailable, VmFunctionState.UnsupportedControl or VmFunctionState.InvalidStructure => VmStopReason.UnsupportedControl, _ => VmStopReason.SemanticNotAvailable };
}
public sealed class VmSyntheticProgramBuilder
{
    private readonly List<VmInstruction[]> functions = [];
    public int AddFunction(params VmInstruction[] instructions) { functions.Add(instructions); return functions.Count - 1; }
    public LinkedProgram Build() { var code = functions.SelectMany(x => x).ToArray(); var descriptors = new VmFunctionDescriptor[functions.Count]; var start = 0; for (var i = 0; i < functions.Count; i++) { descriptors[i] = new(i, start, functions[i].Length, VmFunctionState.ExecutableReady); start += functions[i].Length; } return new(code, descriptors, []); }
}
