using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Vm;

// [Emuera改修:NEXT-2A 2026-08-27]
// Linked hot data is value-only so function code can be relocated/evicted without
// retaining Legacy LogicalLine, parser objects, or one heap object per instruction.
public enum FunctionKind : byte { Normal, Event, Method }
public enum VmFunctionState : int { Ready, SemanticNotAvailable, CodeNotAvailable, UnsupportedControl, InvalidStructure }
public enum VmStopReason : byte { Halted, Returned, CodeNotAvailable, SemanticNotAvailable, UnsupportedControl, InvalidFunctionId, InvalidLocalPc, StackUnderflow, StepLimit }
public enum VmReturnKind : int { Normal, Propagate }
public enum VmStructuralKind : int { None, If, ElseIf, Else, EndIf, SelectCase, Case, CaseElse, EndSelect, Repeat, Rend, For, Next, While, Wend, Do, Loop, Break, Continue }

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
    public readonly int FunctionId;
    public readonly int CodeStart;
    public readonly int CodeLength;
    public readonly VmFunctionState State;
    public VmFunctionDescriptor(int functionId, int codeStart, int codeLength, VmFunctionState state)
        => (FunctionId, CodeStart, CodeLength, State) = (functionId, codeStart, codeLength, state);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VmFrame
{
    public readonly int FunctionId;
    public readonly int Pc;
    public readonly VmReturnKind ReturnKind;
    public VmFrame(int functionId, int pc, VmReturnKind returnKind) => (FunctionId, Pc, ReturnKind) = (functionId, pc, returnKind);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct StructuralLinkRecord
{
    public readonly int FunctionId;
    public readonly int Pc;
    public readonly int TargetPc;
    public readonly int AuxiliaryPc;
    public readonly VmStructuralKind Kind;
    public readonly int Depth;
    public StructuralLinkRecord(int functionId, int pc, int targetPc, int auxiliaryPc, VmStructuralKind kind, int depth)
        => (FunctionId, Pc, TargetPc, AuxiliaryPc, Kind, Depth) = (functionId, pc, targetPc, auxiliaryPc, kind, depth);
}

public sealed record FunctionDefinition(string Name, string FileIdentity, SourceSpan Span,
    SourceIndexFlags Flags = SourceIndexFlags.None, FunctionKind Kind = FunctionKind.Normal);

public sealed class FunctionCatalogEntry
{
    public int FunctionId { get; }
    public string Name { get; }
    public string FileIdentity { get; }
    public SourceSpan Span { get; }
    public SourceIndexFlags Flags { get; }
    public FunctionKind Kind { get; }
    public bool CodeAvailable { get; internal set; }
    public int LinkedDescriptorIndex { get; internal set; } = -1;
    internal FunctionCatalogEntry(int id, FunctionDefinition definition) => (FunctionId, Name, FileIdentity, Span, Flags, Kind) =
        (id, definition.Name, definition.FileIdentity, definition.Span, definition.Flags, definition.Kind);
}

public sealed class FunctionCatalog
{
    private readonly Dictionary<string, int[]> nameLookup;
    private readonly Dictionary<string, int> positionLookup;
    public IReadOnlyList<FunctionCatalogEntry> Entries { get; }
    public int Count => Entries.Count;
    private FunctionCatalog(IReadOnlyList<FunctionCatalogEntry> entries)
    {
        Entries = entries;
        positionLookup = entries.Select((x, i) => (x, i)).ToDictionary(x => Key(x.x.FileIdentity, x.x.Span.StartLine), x => x.i, StringComparer.OrdinalIgnoreCase);
        nameLookup = entries.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Select(e => e.FunctionId).ToArray(), StringComparer.OrdinalIgnoreCase);
    }
    // [Emuera改修:NEXT-2A 2026-08-27]
    // SourceIndex order is the identity contract; sorting names would merge physical duplicates.
    public static FunctionCatalog FromSourceIndex(IReadOnlyList<SourceFileIndex> files)
        => new(files.SelectMany(file => file.Functions.Select(function => new FunctionDefinition(function.Name, file.FileIdentity, function.Span, function.Flags))).Select((x, i) => new FunctionCatalogEntry(i, x)).ToArray());
    public static FunctionCatalog FromDefinitions(IEnumerable<FunctionDefinition> definitions)
        => new(definitions.Select((x, i) => new FunctionCatalogEntry(i, x)).ToArray());
    public bool TryGetId(string fileIdentity, int startLine, out int id) => positionLookup.TryGetValue(Key(fileIdentity, startLine), out id);
    public FunctionCatalogEntry this[int id] => Entries[id];
    public IReadOnlyList<int> FindByName(string name, bool ignoreCase = true)
        => (ignoreCase ? nameLookup : Entries.GroupBy(x => x.Name, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Select(e => e.FunctionId).ToArray(), StringComparer.Ordinal)).GetValueOrDefault(name, []);
    public void MarkCodeAvailable(IEnumerable<int> ids) { foreach (var id in ids) Entries[id].CodeAvailable = true; }
    private static string Key(string file, int line) => $"{Path.GetFullPath(file).ToUpperInvariant()}:{line}";
}

public readonly record struct FixedTargetScan(string Target, int Start, int End);
public static class FixedCallTargetScanner
{
    // [Emuera改修:NEXT-2A 2026-08-27]
    // Match Legacy SP_CALL's non-form delimiter rule once at link time; runtime never reparses text.
    public static bool TryScan(string operand, out FixedTargetScan result)
    {
        var start = 0;
        while (start < operand.Length && (operand[start] == ' ' || operand[start] == '\t')) start++;
        var end = start;
        while (end < operand.Length && operand[end] is not '(' and not '[' and not ',' and not ';') end++;
        while (end > start && (operand[end - 1] == ' ' || operand[end - 1] == '\t')) end--;
        result = new(operand[start..end], start, end);
        return end > start;
    }
}

public readonly record struct CallResolution(bool FunctionResolved, bool CodeAvailable, int FunctionId, string? Reason);
public static class FixedCallResolver
{
    public static CallResolution Resolve(FunctionCatalog catalog, string target, bool ignoreCase, bool compatiCallEvent)
    {
        var candidates = catalog.FindByName(target, ignoreCase);
        foreach (var id in candidates)
        {
            var entry = catalog[id];
            if (entry.Kind == FunctionKind.Method) continue;
            if (entry.Kind == FunctionKind.Event && !compatiCallEvent) continue;
            return new(true, entry.CodeAvailable, id, entry.CodeAvailable ? null : "CodeNotAvailable");
        }
        return new(false, false, -1, candidates.Count > 0 ? "WrongKind" : "MissingTarget");
    }
}

public sealed record FunctionPrototype(int FunctionId, ImmutableArray<PrototypeInstruction> Instructions, ImmutableArray<string> Operands);
public sealed class LinkedProgram
{
    public VmInstruction[] Code { get; }
    public VmFunctionDescriptor[] Descriptors { get; }
    public StructuralLinkRecord[] StructuralLinks { get; }
    public LinkedProgram(VmInstruction[] code, VmFunctionDescriptor[] descriptors, StructuralLinkRecord[] structuralLinks)
        => (Code, Descriptors, StructuralLinks) = (code, descriptors, structuralLinks);
}
public sealed record ControlLinkResult(LinkedProgram Program, IReadOnlyList<string> Diagnostics, int Calls, int Jumps, int CallScanFailures, int ResolvedCalls, int MissingTargets, int WrongKinds, int CodeAvailableTargets, int CodeUnavailableTargets, int SemanticBarriers);

public static class ControlLinker
{
    // [Emuera改修:NEXT-2A 2026-08-27]
    // All branch/loop targets are local PCs in immutable side records; no target-1 runtime correction.
    private static readonly HashSet<PrototypeOpcode> Structure = [PrototypeOpcode.IF, PrototypeOpcode.ELSEIF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF, PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT, PrototypeOpcode.REPEAT, PrototypeOpcode.REND, PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.WHILE, PrototypeOpcode.WEND, PrototypeOpcode.DO, PrototypeOpcode.LOOP, PrototypeOpcode.BREAK, PrototypeOpcode.CONTINUE];
    private static readonly Dictionary<PrototypeOpcode, VmStructuralKind> StructureKinds = new()
    {
        [PrototypeOpcode.IF]=VmStructuralKind.If,[PrototypeOpcode.ELSEIF]=VmStructuralKind.ElseIf,[PrototypeOpcode.ELSE]=VmStructuralKind.Else,[PrototypeOpcode.ENDIF]=VmStructuralKind.EndIf,
        [PrototypeOpcode.SELECTCASE]=VmStructuralKind.SelectCase,[PrototypeOpcode.CASE]=VmStructuralKind.Case,[PrototypeOpcode.CASEELSE]=VmStructuralKind.CaseElse,[PrototypeOpcode.ENDSELECT]=VmStructuralKind.EndSelect,
        [PrototypeOpcode.REPEAT]=VmStructuralKind.Repeat,[PrototypeOpcode.REND]=VmStructuralKind.Rend,[PrototypeOpcode.FOR]=VmStructuralKind.For,[PrototypeOpcode.NEXT]=VmStructuralKind.Next,
        [PrototypeOpcode.WHILE]=VmStructuralKind.While,[PrototypeOpcode.WEND]=VmStructuralKind.Wend,[PrototypeOpcode.DO]=VmStructuralKind.Do,[PrototypeOpcode.LOOP]=VmStructuralKind.Loop,
        [PrototypeOpcode.BREAK]=VmStructuralKind.Break,[PrototypeOpcode.CONTINUE]=VmStructuralKind.Continue
    };
    private static readonly Dictionary<PrototypeOpcode, string> Closers = new() { [PrototypeOpcode.ENDIF]="IF", [PrototypeOpcode.ENDSELECT]="SELECT", [PrototypeOpcode.REND]="REPEAT", [PrototypeOpcode.NEXT]="FOR", [PrototypeOpcode.WEND]="WHILE", [PrototypeOpcode.LOOP]="DO" };
    private static readonly Dictionary<PrototypeOpcode, string> Openers = new() { [PrototypeOpcode.IF]="IF", [PrototypeOpcode.SELECTCASE]="SELECT", [PrototypeOpcode.REPEAT]="REPEAT", [PrototypeOpcode.FOR]="FOR", [PrototypeOpcode.WHILE]="WHILE", [PrototypeOpcode.DO]="DO" };
    public static ControlLinkResult Link(FunctionCatalog catalog, IReadOnlyList<FunctionPrototype> prototypes, bool ignoreCase = true, bool compatiCallEvent = false)
    {
        var code = new List<VmInstruction>(); var descriptors = Enumerable.Range(0, catalog.Count).Select(id => new VmFunctionDescriptor(id, 0, 0, VmFunctionState.CodeNotAvailable)).ToArray(); var records = new List<StructuralLinkRecord>(); var diagnostics = new List<string>();
        var calls = 0; var jumps = 0; var scanFailures = 0; var resolved = 0; var missing = 0; var wrong = 0; var codeYes = 0; var codeNo = 0; var barriers = 0;
        foreach (var prototype in prototypes.OrderBy(x => x.FunctionId))
        {
            var start = code.Count; var state = VmFunctionState.Ready; var localRecords = new List<StructuralLinkRecord>();
            for (var pc = 0; pc < prototype.Instructions.Length; pc++)
            {
                var p = prototype.Instructions[pc]; var operand = pc < prototype.Operands.Length ? prototype.Operands[pc] : string.Empty;
                if (p.Opcode is PrototypeOpcode.CALL or PrototypeOpcode.JUMP)
                {
                    if (p.Opcode == PrototypeOpcode.CALL) calls++; else jumps++;
                    if (!FixedCallTargetScanner.TryScan(operand, out var scan)) { scanFailures++; state = VmFunctionState.UnsupportedControl; diagnostics.Add($"function={prototype.FunctionId} pc={pc} static-target-scan=FAIL"); code.Add(new((ushort)VmOpcode.UnsupportedControl)); continue; }
                    var resolution = FixedCallResolver.Resolve(catalog, scan.Target, ignoreCase, compatiCallEvent);
                    if (!resolution.FunctionResolved) { if (resolution.Reason == "WrongKind") wrong++; else missing++; state = VmFunctionState.UnsupportedControl; code.Add(new((ushort)VmOpcode.UnsupportedControl)); continue; }
                    resolved++; if (resolution.CodeAvailable) codeYes++; else codeNo++;
                    code.Add(p.Opcode == PrototypeOpcode.CALL ? VmInstruction.Call(resolution.FunctionId) : VmInstruction.Jump(resolution.FunctionId)); continue;
                }
                if (p.Opcode == PrototypeOpcode.RETURN) { code.Add(new((ushort)VmOpcode.Return)); continue; }
                if (Structure.Contains(p.Opcode)) { localRecords.Add(new(prototype.FunctionId, pc, -1, -1, StructureKinds[p.Opcode], 0)); code.Add(new((ushort)VmOpcode.Structural)); continue; }
                if (p.Opcode is PrototypeOpcode.GOTO or PrototypeOpcode.TRYJUMP or PrototypeOpcode.TRYGOTO or PrototypeOpcode.TRYGOTOFORM) { state = VmFunctionState.UnsupportedControl; code.Add(new((ushort)VmOpcode.UnsupportedControl)); continue; }
                barriers++; state = VmFunctionState.SemanticNotAvailable; code.Add(new((ushort)VmOpcode.SemanticBarrier, operandOffset: p.OperandOffset, operandLength: p.OperandLength));
            }
            LinkStructure(prototype.FunctionId, prototype.Instructions, localRecords, diagnostics, ref state);
            records.AddRange(localRecords); descriptors[prototype.FunctionId] = new(prototype.FunctionId, start, code.Count - start, state); catalog[prototype.FunctionId].LinkedDescriptorIndex = prototype.FunctionId;
        }
        return new(new(code.ToArray(), descriptors, records.ToArray()), diagnostics, calls, jumps, scanFailures, resolved, missing, wrong, codeYes, codeNo, barriers);
    }

    private static void LinkStructure(int functionId, IReadOnlyList<PrototypeInstruction> instructions, List<StructuralLinkRecord> records, List<string> diagnostics, ref VmFunctionState state)
    {
        var stack = new List<(string Family, int Record)>(); var recordByPc = records.Select((x, i) => (x.Pc, Index: i)).ToDictionary(x => x.Pc, x => x.Index); var depth = 0;
        for (var pc = 0; pc < instructions.Count; pc++)
        {
            var op = instructions[pc].Opcode;
            if (Openers.TryGetValue(op, out var family)) { stack.Add((family, recordByPc[pc])); depth++; Update(records, recordByPc[pc], new(functionId, pc, -1, -1, records[recordByPc[pc]].Kind, depth)); continue; }
            if (op is PrototypeOpcode.ELSEIF or PrototypeOpcode.ELSE or PrototypeOpcode.CASE or PrototypeOpcode.CASEELSE)
            {
                var valid = stack.Count > 0 && (op is PrototypeOpcode.ELSEIF or PrototypeOpcode.ELSE ? stack[^1].Family == "IF" : stack[^1].Family == "SELECT");
                if (!valid) { state = VmFunctionState.InvalidStructure; diagnostics.Add($"function={functionId} pc={pc} malformed={op}"); }
                else Update(records, recordByPc[pc], new(functionId, pc, -1, -1, records[recordByPc[pc]].Kind, depth));
            }
            if (Closers.TryGetValue(op, out var close))
            {
                if (stack.Count == 0 || stack[^1].Family != close) { state = VmFunctionState.InvalidStructure; diagnostics.Add($"function={functionId} pc={pc} crossed-or-missing={op}"); continue; }
                var opening = stack[^1].Record; stack.RemoveAt(stack.Count - 1); depth--; Update(records, opening, new(functionId, records[opening].Pc, pc, records[opening].Pc + 1, records[opening].Kind, records[opening].Depth));
                Update(records, recordByPc[pc], new(functionId, pc, opening, -1, records[recordByPc[pc]].Kind, depth));
                if (close is "REPEAT" or "FOR" or "WHILE" or "DO")
                    for (var index = 0; index < records.Count; index++)
                        if (records[index].FunctionId == functionId && records[index].AuxiliaryPc == opening && records[index].Kind is VmStructuralKind.Break or VmStructuralKind.Continue)
                            Update(records, index, new(functionId, records[index].Pc, records[index].Kind == VmStructuralKind.Break ? pc + 1 : opening, opening, records[index].Kind, records[index].Depth));
            }
            if (op is PrototypeOpcode.BREAK or PrototypeOpcode.CONTINUE)
            {
                var loop = stack.LastOrDefault(x => x.Family is "REPEAT" or "FOR" or "WHILE" or "DO");
                if (loop == default) { state = VmFunctionState.InvalidStructure; diagnostics.Add($"function={functionId} pc={pc} loop-control-outside-loop={op}"); }
                else Update(records, recordByPc[pc], new(functionId, pc, loop.Record, loop.Record, records[recordByPc[pc]].Kind, depth));
            }
        }
        if (stack.Count > 0) { state = VmFunctionState.InvalidStructure; diagnostics.Add($"function={functionId} missing-close={stack[^1].Family}"); }
    }
    private static void Update(List<StructuralLinkRecord> records, int index, StructuralLinkRecord value) => records[index] = value;
}

public sealed class VmMachine
{
    // [Emuera改修:NEXT-2A 2026-08-27]
    // PC is the next instruction, while Propagate frames preserve Legacy JUMP return propagation.
    private readonly LinkedProgram program; private VmFrame[] stack = new VmFrame[16]; private int stackCount;
    public VmMachine(LinkedProgram program) => this.program = program;
    public VmStopReason Run(int entryFunctionId, int maxSteps = 100_000)
    {
        stackCount = 0; if (!TryPush(entryFunctionId, VmReturnKind.Normal, out var reason)) return reason;
        for (var steps = 0; steps < maxSteps; steps++)
        {
            if (stackCount == 0) return VmStopReason.Returned;
            ref var frame = ref stack[stackCount - 1]; var descriptor = GetDescriptor(frame.FunctionId); if (descriptor.State != VmFunctionState.Ready) return StateReason(descriptor.State);
            if ((uint)frame.Pc >= (uint)descriptor.CodeLength) return VmStopReason.InvalidLocalPc;
            var instruction = program.Code[descriptor.CodeStart + frame.Pc]; frame = new(frame.FunctionId, frame.Pc + 1, frame.ReturnKind);
            switch ((VmOpcode)instruction.Opcode)
            {
                case VmOpcode.Nop: break;
                case VmOpcode.Halt: return VmStopReason.Halted;
                case VmOpcode.Branch: if (!SetPc(instruction.Aux)) return VmStopReason.InvalidLocalPc; break;
                case VmOpcode.Call: if (!TryPush(instruction.Aux, VmReturnKind.Normal, out reason)) return reason; break;
                case VmOpcode.Jump: if (!TryPush(instruction.Aux, VmReturnKind.Propagate, out reason)) return reason; break;
                case VmOpcode.Return: if (!Return()) return VmStopReason.StackUnderflow; break;
                case VmOpcode.SemanticBarrier: return VmStopReason.SemanticNotAvailable;
                case VmOpcode.UnsupportedControl: return VmStopReason.UnsupportedControl;
                default: return VmStopReason.SemanticNotAvailable;
            }
        }
        return VmStopReason.StepLimit;
    }
    private bool TryPush(int id, VmReturnKind kind, out VmStopReason reason)
    {
        if ((uint)id >= (uint)program.Descriptors.Length) { reason = VmStopReason.InvalidFunctionId; return false; }
        var d = program.Descriptors[id]; if (d.State != VmFunctionState.Ready) { reason = StateReason(d.State); return false; }
        if (stackCount == stack.Length) Array.Resize(ref stack, checked(stack.Length * 2)); stack[stackCount++] = new(id, 0, kind); reason = VmStopReason.Returned; return true;
    }
    private bool Return()
    {
        if (stackCount == 0) return false; var propagate = stack[stackCount - 1].ReturnKind == VmReturnKind.Propagate; stackCount--;
        while (propagate && stackCount > 0) { propagate = stack[stackCount - 1].ReturnKind == VmReturnKind.Propagate; stackCount--; }
        return true;
    }
    private bool SetPc(int pc) { if ((uint)pc >= (uint)GetDescriptor(stack[stackCount - 1].FunctionId).CodeLength) return false; stack[stackCount - 1] = new(stack[stackCount - 1].FunctionId, pc, stack[stackCount - 1].ReturnKind); return true; }
    private VmFunctionDescriptor GetDescriptor(int id) => (uint)id < (uint)program.Descriptors.Length ? program.Descriptors[id] : new(-1, 0, 0, VmFunctionState.CodeNotAvailable);
    private static VmStopReason StateReason(VmFunctionState state) => state switch { VmFunctionState.CodeNotAvailable => VmStopReason.CodeNotAvailable, VmFunctionState.UnsupportedControl => VmStopReason.UnsupportedControl, VmFunctionState.InvalidStructure => VmStopReason.UnsupportedControl, _ => VmStopReason.SemanticNotAvailable };
}

public sealed class VmSyntheticProgramBuilder
{
    private readonly List<VmInstruction[]> functions = [];
    public int AddFunction(params VmInstruction[] instructions) { functions.Add(instructions); return functions.Count - 1; }
    public LinkedProgram Build()
    {
        var code = functions.SelectMany(x => x).ToArray(); var descriptors = new VmFunctionDescriptor[functions.Count]; var start = 0;
        for (var i = 0; i < functions.Count; i++) { descriptors[i] = new(i, start, functions[i].Length, VmFunctionState.Ready); start += functions[i].Length; }
        return new(code, descriptors, []);
    }
}
