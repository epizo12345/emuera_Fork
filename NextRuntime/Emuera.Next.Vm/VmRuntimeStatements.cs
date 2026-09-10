using System.Text;

namespace MinorShift.Emuera.Next.Vm;

// Phase 3C keeps statement operands out of the structural semantic arena. The
// linker copies their UTF-8 value once; the VM never consults source text again.
public enum VmRuntimeStatementKind : byte { Print, PrintLine, PrintWait, Wait, ForceWait, Quit, Set, MultiSet, Split, Times, ReturnValue, LegacyReturnInteger, LegacyReturnValues, Host, TypedHost, NoOp, Expression, PrintButton, Throw, ClearFrame, VarSet, DeclarePrivate, SetBit }
public enum VmAssignmentOperator : byte { Assign, AssignString, Add, Subtract, Multiply, Divide, Modulo, BitOr, BitAnd, BitXor }
public enum VmHostEffectResult : byte { Applied, Unavailable, WaitingForInput, Fault }

public readonly record struct VmRuntimeStatementRecord(VmRuntimeStatementKind Kind, int TextOffset, int TextLength, int OperandRecord = -1, int SecondaryOperandRecord = -1, VmAssignmentOperator Assignment = VmAssignmentOperator.Assign, double NumericValue = 0, ushort HostOpcode = 0, int FormatOperandRecord = -1, int SourceLine = 0, int ValueCount = 0, int TertiaryOperandRecord = -1, int QuaternaryOperandRecord = -1);

public sealed class VmRuntimeStatementArena
{
    public static VmRuntimeStatementArena Empty { get; } = new([], []);
    public VmRuntimeStatementRecord[] Records { get; }
    public byte[] Utf8 { get; }
    public MinorShift.Emuera.Next.Compiler.SemanticPayload OperandArena { get; }
    public VmRuntimeStatementArena(VmRuntimeStatementRecord[] records, byte[] utf8, MinorShift.Emuera.Next.Compiler.SemanticPayload? operandArena = null) => (Records, Utf8, OperandArena) = (records, utf8, operandArena ?? MinorShift.Emuera.Next.Compiler.SemanticPayload.Empty);
    public string ReadText(in VmRuntimeStatementRecord record) => record.TextLength == 0 ? string.Empty : Encoding.UTF8.GetString(Utf8, record.TextOffset, record.TextLength);
}

// UI ownership stays outside the VM so desktop and browser hosts share the same execution core.
public interface IVmRuntimeEffects
{
    void WriteText(string text);
    void NewLine();
    void RequestWait(bool force);
    void Quit();
    // Host-owned UI commands are dispatched explicitly; a missing handler is not treated as execution.
    bool ExecuteHostStatement(MinorShift.Emuera.Next.Compiler.PrototypeOpcode opcode, string operand) => false;
    bool PrintButton(string label, VmSemanticValue value) => false;
    bool SetLegacyReturnValues(ReadOnlySpan<VmSemanticValue> values) => false;
    VmHostEffectResult ExecuteTypedHostStatement(MinorShift.Emuera.Next.Compiler.PrototypeOpcode opcode, ReadOnlySpan<VmSemanticValue> arguments) => VmHostEffectResult.Unavailable;
}

// General-owner input is prepared without publishing. VmMachine publishes only after
// the opcode pump returned and the owner committed Suspended.
public interface IVmOwnedInputRuntimeEffects
{
    bool TryPrepareInput(MinorShift.Emuera.Next.Compiler.PrototypeOpcode opcode,
        ReadOnlySpan<VmSemanticValue> arguments, out VmInputValueKind expectedKind);
    void PublishInput(VmInputContinuation continuation, Action<VmSemanticValue> respond);
    bool TryWriteInputResult(VmSemanticValue value);
}

internal sealed class VmRuntimeStatementArenaBuilder
{
    private readonly List<VmRuntimeStatementRecord> records = [];
    private readonly List<byte> utf8 = [];
    // [Emuera改修:NEXT-3D-R1.4C2 2026-09-04]
    // 各payloadのrecord数を単調に積み上げ、旧来のoperandParts.Sum(...)によるprefix scanを避ける。
    // これはoperand offsetの意味論を変えず、large fixtureでのリンク時計算量だけを削減する。
    private readonly List<MinorShift.Emuera.Next.Compiler.SemanticPayload> operandParts = [];
    private int operandRecordCount;
    private int AddOperandPart(MinorShift.Emuera.Next.Compiler.SemanticPayload payload)
    {
        var result = operandRecordCount;
        operandParts.Add(payload);
        operandRecordCount += payload.Records.Length;
        return result;
    }
    public int Add(VmRuntimeStatementKind kind, string text = "")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var result = records.Count;
        records.Add(new(kind, utf8.Count, bytes.Length));
        utf8.AddRange(bytes);
        return result;
    }
    public int AddOperand(VmRuntimeStatementKind kind, MinorShift.Emuera.Next.Compiler.SemanticPayload payload)
    {
        var index = records.Count;
        var recordBase = AddOperandPart(payload);
        records.Add(new(kind, 0, 0, recordBase));
        return index;
    }
    public int AddPair(VmRuntimeStatementKind kind, MinorShift.Emuera.Next.Compiler.SemanticPayload first, MinorShift.Emuera.Next.Compiler.SemanticPayload second, int sourceLine = 0)
    {
        var index = records.Count;
        records.Add(new(kind, 0, 0, AddOperandPart(first), AddOperandPart(second), SourceLine: sourceLine));
        return index;
    }
    public int AddOperand(VmRuntimeStatementKind kind, MinorShift.Emuera.Next.Compiler.SemanticPayload payload, int sourceLine)
    {
        var index = records.Count;
        records.Add(new(kind, 0, 0, AddOperandPart(payload), SourceLine: sourceLine));
        return index;
    }
    public int AddDeclaration(string name, MinorShift.Emuera.Next.Compiler.SemanticPayload initializer)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var index = records.Count;
        records.Add(new(VmRuntimeStatementKind.DeclarePrivate, utf8.Count, bytes.Length, AddOperandPart(initializer)));
        utf8.AddRange(bytes);
        return index;
    }
    public int AddAssignment(MinorShift.Emuera.Next.Compiler.SemanticPayload destination, MinorShift.Emuera.Next.Compiler.SemanticPayload source, VmAssignmentOperator assignment, MinorShift.Emuera.Next.Compiler.SemanticPayload? formatSource = null)
    {
        var index = records.Count;
        var destinationBase = AddOperandPart(destination);
        var sourceBase = AddOperandPart(source);
        var formatBase = formatSource is null ? -1 : AddOperandPart(formatSource);
        records.Add(new(VmRuntimeStatementKind.Set, 0, 0, destinationBase, sourceBase, assignment, FormatOperandRecord: formatBase));
        return index;
    }
    public int AddMultiple(VmRuntimeStatementKind kind, MinorShift.Emuera.Next.Compiler.SemanticPayload? destination, IReadOnlyList<MinorShift.Emuera.Next.Compiler.SemanticPayload> values)
    {
        var index = records.Count;
        var destinationRecord = destination is null ? -1 : AddOperandPart(destination);
        var firstValueRecord = values.Count == 0 ? -1 : operandRecordCount;
        foreach (var value in values) AddOperandPart(value);
        records.Add(new(kind, 0, 0, destinationRecord, firstValueRecord, ValueCount: values.Count));
        return index;
    }
    public int AddTypedHost(MinorShift.Emuera.Next.Compiler.PrototypeOpcode opcode, IReadOnlyList<MinorShift.Emuera.Next.Compiler.SemanticPayload> values)
    {
        var index = AddMultiple(VmRuntimeStatementKind.TypedHost, null, values);
        records[index] = records[index] with { HostOpcode = (ushort)opcode };
        return index;
    }
    public int AddSplit(MinorShift.Emuera.Next.Compiler.SemanticPayload source, MinorShift.Emuera.Next.Compiler.SemanticPayload delimiter,
        MinorShift.Emuera.Next.Compiler.SemanticPayload destination, MinorShift.Emuera.Next.Compiler.SemanticPayload count)
    {
        var index = records.Count;
        records.Add(new(VmRuntimeStatementKind.Split, 0, 0, AddOperandPart(source), AddOperandPart(delimiter),
            TertiaryOperandRecord: AddOperandPart(destination), QuaternaryOperandRecord: AddOperandPart(count)));
        return index;
    }
    public int AddVarSet(MinorShift.Emuera.Next.Compiler.SemanticPayload destination, MinorShift.Emuera.Next.Compiler.SemanticPayload value,
        MinorShift.Emuera.Next.Compiler.SemanticPayload start, MinorShift.Emuera.Next.Compiler.SemanticPayload end)
    {
        var index = records.Count;
        records.Add(new(VmRuntimeStatementKind.VarSet, 0, 0, AddOperandPart(destination), AddOperandPart(value),
            TertiaryOperandRecord: AddOperandPart(start), QuaternaryOperandRecord: AddOperandPart(end)));
        return index;
    }
    public int AddTimes(MinorShift.Emuera.Next.Compiler.SemanticPayload destination, double multiplier)
    {
        var index = records.Count;
        var destinationBase = AddOperandPart(destination);
        records.Add(new(VmRuntimeStatementKind.Times, 0, 0, destinationBase, NumericValue: multiplier));
        return index;
    }
    public int AddReturn(MinorShift.Emuera.Next.Compiler.SemanticPayload? value)
    {
        var index = records.Count;
        var recordBase = value is null ? -1 : AddOperandPart(value);
        records.Add(new(VmRuntimeStatementKind.ReturnValue, 0, 0, recordBase));
        return index;
    }
    public int AddLegacyReturn(MinorShift.Emuera.Next.Compiler.SemanticPayload value)
    {
        var index = records.Count;
        records.Add(new(VmRuntimeStatementKind.LegacyReturnInteger, 0, 0, AddOperandPart(value)));
        return index;
    }
    public int AddHost(MinorShift.Emuera.Next.Compiler.PrototypeOpcode opcode, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var result = records.Count;
        records.Add(new(VmRuntimeStatementKind.Host, utf8.Count, bytes.Length, HostOpcode: (ushort)opcode));
        utf8.AddRange(bytes);
        return result;
    }
    public VmRuntimeStatementArena Build() => records.Count == 0 ? VmRuntimeStatementArena.Empty : new(records.ToArray(), utf8.ToArray(), operandParts.Count == 0 ? null : MinorShift.Emuera.Next.Compiler.SemanticPayload.Merge(operandParts));
}
