using System.Globalization;
using MinorShift.Emuera.Next.Compiler;

namespace MinorShift.Emuera.Next.Vm;

public enum VmSemanticValueKind : byte { Unavailable, Missing, Integer, String }

public readonly struct VmSemanticValue
{
    private readonly long integer;
    private readonly string? text;
    public VmSemanticValueKind Kind { get; }
    private VmSemanticValue(VmSemanticValueKind kind, long integer = 0, string? text = null) => (Kind, this.integer, this.text) = (kind, integer, text);
    public static VmSemanticValue Unavailable => new(VmSemanticValueKind.Unavailable);
    public static VmSemanticValue Missing => new(VmSemanticValueKind.Missing);
    public static VmSemanticValue From(long value) => new(VmSemanticValueKind.Integer, value);
    public static VmSemanticValue From(string value) => new(VmSemanticValueKind.String, text: value);
    public bool TryGetInteger(out long value) { value = integer; return Kind == VmSemanticValueKind.Integer; }
    public bool TryGetString(out string value) { value = text ?? string.Empty; return Kind == VmSemanticValueKind.String; }
    public override string ToString() => Kind switch { VmSemanticValueKind.Integer => integer.ToString(CultureInfo.InvariantCulture), VmSemanticValueKind.String => text ?? string.Empty, VmSemanticValueKind.Missing => string.Empty, _ => "<unavailable>" };
}

// This is the NextRuntime boundary for future Legacy/runtime-state adapters.  The executor never
// reaches into Legacy globals; the host owns variable identity, comparison policy, and calls.
public interface IVmSemanticHost
{
    bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value);
    bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value);
    bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value);
    int CompareStrings(string left, string right);
}

public sealed class VmSemanticExecutor : IVmStructuralSemantics
{
    private readonly LinkedProgram program;
    private readonly IVmSemanticHost host;
    private readonly int[][] structuralByFunctionPc;
    private readonly long[] repeatCounters;
    public VmSemanticStatus LastStatus { get; private set; }
    public VmSemanticFault LastFault { get; private set; }

    public VmSemanticExecutor(LinkedProgram program, IVmSemanticHost host)
    {
        this.program = program ?? throw new ArgumentNullException(nameof(program));
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        structuralByFunctionPc = program.Descriptors.Select(d => Enumerable.Repeat(-1, d.CodeLength).ToArray()).ToArray();
        for (var index = 0; index < program.StructuralLinks.Length; index++)
        {
            var link = program.StructuralLinks[index];
            if ((uint)link.FunctionId < (uint)structuralByFunctionPc.Length && (uint)link.Pc < (uint)structuralByFunctionPc[link.FunctionId].Length)
                structuralByFunctionPc[link.FunctionId][link.Pc] = index;
        }
        repeatCounters = new long[program.Loops.Length];
    }

    public bool TryEvaluateRecord(int recordIndex, out VmSemanticValue value)
    {
        ResetStatus();
        var success = TryEvaluateRecordCore(recordIndex, out value);
        LastStatus = success ? VmSemanticStatus.Success : LastFault == VmSemanticFault.None ? VmSemanticStatus.Unavailable : VmSemanticStatus.Fault;
        return success;
    }

    public VmSemanticIntResult EvaluateInt(RuntimeFunctionId functionId, int pc, VmStructuralKind kind)
    {
        ResetStatus();
        if (TryGetRecord(functionId, pc, out var record) && TryEvaluateRecordCore(record, out var value) && value.TryGetInteger(out var integer)) return VmSemanticIntResult.From(integer);
        return IntFailure();
    }

    public VmSemanticCaseResult SelectCase(RuntimeFunctionId functionId, int groupIndex, ReadOnlySpan<SelectCaseRecord> cases)
    {
        ResetStatus();
        if ((uint)groupIndex >= (uint)program.SelectGroups.Length) return VmSemanticCaseResult.NotAvailable;
        var group = program.SelectGroups[groupIndex];
        if (group.FunctionId != functionId.Value || !TryGetRecord(functionId, group.OpenerPc, out var selectorRecord) || !TryEvaluateRecordCore(selectorRecord, out var selector)) return CaseFailure();
        for (var ordinal = 0; ordinal < cases.Length; ordinal++)
        {
            var candidate = cases[ordinal];
            if (!TryGetRecord(functionId, candidate.Pc, out var record) || !TryMatchCase(selector, record, out var match)) return CaseFailure();
            if (match) return VmSemanticCaseResult.From(ordinal);
        }
        return VmSemanticCaseResult.From(-1);
    }

    public VmCountedLoopResult BeginCounted(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop)
    {
        ResetStatus();
        if (!TryGetRecord(functionId, loop.HeaderPc, out var record) || !TryGetCountedLoop(record, out var node)) return CountedFailure();
        if (!TryEvaluateInt(node.B, out var start)) return CountedFailure();
        if (node.Operator == SemanticOperator.CountedRepeat)
        {
            if (!TryEvaluateInt(node.C, out var repeatEnd) || !TryEvaluateInt(node.D, out var repeatStep) || (uint)loopIndex >= (uint)repeatCounters.Length) return CountedFailure();
            repeatCounters[loopIndex] = start;
            return VmCountedLoopResult.From(new(start, repeatEnd, repeatStep));
        }
        // Legacy FOR order: Start, resolve/set counter, End, Step, resolve/read counter.
        if (!TryResolveLValue(node.A, out var counter) || !host.TryWrite(counter.Name, counter.Subkey, counter.Indices, VmSemanticValue.From(start))) return CountedFailure();
        if (!TryEvaluateInt(node.C, out var end) || !TryEvaluateInt(node.D, out var step)) return CountedFailure();
        if (!TryResolveLValue(node.A, out counter) || !host.TryRead(counter.Name, counter.Subkey, counter.Indices, out var current) || !current.TryGetInteger(out var initialCounter)) return CountedFailure();
        return VmCountedLoopResult.From(new(initialCounter, end, step));
    }

    public VmSemanticIntResult AdvanceCounted(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop, long step)
    {
        ResetStatus();
        if (!TryGetRecord(functionId, loop.HeaderPc, out var record) || !TryGetCountedLoop(record, out var node)) return IntFailure();
        if (node.Operator == SemanticOperator.CountedRepeat)
        {
            if ((uint)loopIndex >= (uint)repeatCounters.Length) return IntFailure();
            return VmSemanticIntResult.From(repeatCounters[loopIndex] = unchecked(repeatCounters[loopIndex] + step));
        }
        if (!TryResolveLValue(node.A, out var counter) || !host.TryRead(counter.Name, counter.Subkey, counter.Indices, out var current) || !current.TryGetInteger(out var integer)) return IntFailure();
        var next = unchecked(integer + step);
        return host.TryWrite(counter.Name, counter.Subkey, counter.Indices, VmSemanticValue.From(next)) ? VmSemanticIntResult.From(next) : IntFailure();
    }

    private bool TryEvaluateRecordCore(int recordIndex, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        return (uint)recordIndex < (uint)program.SemanticArena.Records.Length && TryEvaluateNode(program.SemanticArena.Records[recordIndex].RootNodeIndex, out value);
    }
    private void ResetStatus() => (LastStatus, LastFault) = (VmSemanticStatus.Unavailable, VmSemanticFault.None);
    private bool Fault(VmSemanticFault fault) { LastFault = fault; return false; }
    private VmSemanticIntResult IntFailure() => LastFault == VmSemanticFault.None ? VmSemanticIntResult.NotAvailable : VmSemanticIntResult.Faulted(LastFault);
    private VmSemanticCaseResult CaseFailure() => LastFault == VmSemanticFault.None ? VmSemanticCaseResult.NotAvailable : VmSemanticCaseResult.Faulted(LastFault);
    private VmCountedLoopResult CountedFailure() => LastFault == VmSemanticFault.None ? VmCountedLoopResult.NotAvailable : VmCountedLoopResult.Faulted(LastFault);

    private bool TryGetRecord(RuntimeFunctionId functionId, int pc, out int record)
    {
        record = -1;
        if ((uint)functionId.Value >= (uint)structuralByFunctionPc.Length || (uint)pc >= (uint)structuralByFunctionPc[functionId.Value].Length) return false;
        var structural = structuralByFunctionPc[functionId.Value][pc];
        if (structural < 0 || (uint)structural >= (uint)program.StructuralSemanticRecordIndices.Length) return false;
        record = program.StructuralSemanticRecordIndices[structural];
        return record >= 0;
    }

    private bool TryGetCountedLoop(int record, out SemanticNode node)
    {
        node = default;
        if ((uint)record >= (uint)program.SemanticArena.Records.Length) return false;
        var root = program.SemanticArena.Records[record].RootNodeIndex;
        if ((uint)root >= (uint)program.SemanticArena.Nodes.Length) return false;
        node = program.SemanticArena.Nodes[root];
        return node.Kind == SemanticNodeKind.CountedLoop && node.Operator is SemanticOperator.CountedFor or SemanticOperator.CountedRepeat;
    }

    private bool TryEvaluateInt(int node, out long value)
    {
        value = 0;
        return TryEvaluateNode(node, out var semantic) && semantic.TryGetInteger(out value);
    }

    private bool TryEvaluateNode(int index, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        var nodes = program.SemanticArena.Nodes;
        if ((uint)index >= (uint)nodes.Length) return false;
        var node = nodes[index];
        switch (node.Kind)
        {
            case SemanticNodeKind.IntegerLiteral:
                return TryReadSymbol(node.A, out var integerText) && TryParseInteger(integerText, out value);
            case SemanticNodeKind.StringLiteral:
                return TryReadSymbol(node.A, out var text) && Return(VmSemanticValue.From(text), out value);
            case SemanticNodeKind.Symbol:
                return TryReadSymbol(node.A, out var symbol) && host.TryRead(symbol, null, [], out value);
            case SemanticNodeKind.Variable:
                return TryReadSymbol(node.A, out var variable) && TryEvaluateEdges(node.B, node.C, out var indices) && host.TryRead(variable, null, indices, out value);
            case SemanticNodeKind.VariableSubkey:
                return TryReadSymbol(node.A, out var name) && TryReadSymbol(node.B, out var subkey) && TryEvaluateOptionalEdges(node.C, node.D, out var subkeys) && host.TryRead(name, subkey, subkeys, out value);
            case SemanticNodeKind.Call:
                return TryReadSymbol(node.A, out var call) && TryEvaluateEdges(node.B, node.C, out var arguments) && host.TryCall(call, arguments, out value);
            case SemanticNodeKind.Unary:
                return TryEvaluateUnary(node, out value);
            case SemanticNodeKind.Binary:
                return TryEvaluateBinary(node, out value);
            case SemanticNodeKind.Ternary:
                return TryEvaluateInt(node.A, out var condition) && TryEvaluateNode(condition != 0 ? node.B : node.C, out value);
            case SemanticNodeKind.Format:
                return TryFormat(node, out value);
            case SemanticNodeKind.FormattedSequence:
                return TryFormatSequence(node, out value);
            case SemanticNodeKind.ConditionalFormat:
                if (!TryEvaluateInt(node.A, out var conditional)) return false;
                if (conditional == 0 && node.C < 0) return Return(VmSemanticValue.From(string.Empty), out value);
                return TryEvaluateNode(conditional != 0 ? node.B : node.C, out value) && TryAsString(value, out var formatted) && Return(VmSemanticValue.From(formatted), out value);
            case SemanticNodeKind.MissingArgument:
                value = VmSemanticValue.Missing; return true;
            default:
                return false; // Explicitly deferred: RenameTemplate, TripleLiteral, Case, CountedLoop.
        }
    }

    private bool TryEvaluateUnary(SemanticNode node, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (node.Operator is SemanticOperator.PrefixIncrement or SemanticOperator.PrefixDecrement or SemanticOperator.PostfixIncrement or SemanticOperator.PostfixDecrement)
        {
            if (!TryResolveLValue(node.A, out var lvalue) || !host.TryRead(lvalue.Name, lvalue.Subkey, lvalue.Indices, out var current) || !current.TryGetInteger(out var number)) return false;
            var delta = node.Operator is SemanticOperator.PrefixIncrement or SemanticOperator.PostfixIncrement ? 1L : -1L;
            var changed = unchecked(number + delta);
            if (!host.TryWrite(lvalue.Name, lvalue.Subkey, lvalue.Indices, VmSemanticValue.From(changed))) return false;
            value = VmSemanticValue.From(node.Operator is SemanticOperator.PrefixIncrement or SemanticOperator.PrefixDecrement ? changed : number);
            return true;
        }
        if (!TryEvaluateInt(node.A, out var operand)) return false;
        value = node.Operator switch
        {
            SemanticOperator.Plus => VmSemanticValue.From(operand),
            SemanticOperator.Minus => VmSemanticValue.From(unchecked(-operand)),
            SemanticOperator.Not => VmSemanticValue.From(operand == 0 ? 1 : 0),
            SemanticOperator.BitNot => VmSemanticValue.From(~operand),
            _ => VmSemanticValue.Unavailable,
        };
        return value.Kind != VmSemanticValueKind.Unavailable;
    }

    private bool TryEvaluateBinary(SemanticNode node, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (node.A < 0) return false; // CASE IS is evaluated only with its selector.
        if (!TryEvaluateNode(node.A, out var left)) return false;
        if (node.Operator == SemanticOperator.LogicalAnd && left.TryGetInteger(out var andLeft) && andLeft == 0) return Return(VmSemanticValue.From(0), out value);
        if (node.Operator == SemanticOperator.LogicalOr && left.TryGetInteger(out var orLeft) && orLeft != 0) return Return(VmSemanticValue.From(1), out value);
        if (node.Operator == SemanticOperator.Nand && left.TryGetInteger(out var nandLeft) && nandLeft == 0) return Return(VmSemanticValue.From(1), out value);
        if (node.Operator == SemanticOperator.Nor && left.TryGetInteger(out var norLeft) && norLeft != 0) return Return(VmSemanticValue.From(0), out value);
        if (!TryEvaluateNode(node.B, out var right)) return false;
        return TryApplyBinary(node.Operator, left, right, out value);
    }

    private bool TryApplyBinary(SemanticOperator op, VmSemanticValue left, VmSemanticValue right, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (left.TryGetInteger(out var a) && right.TryGetInteger(out var b))
        {
            if (op == SemanticOperator.Divide && b == 0) return Fault(VmSemanticFault.DivideByZero);
            if (op == SemanticOperator.Modulo && b == 0) return Fault(VmSemanticFault.ModuloByZero);
            value = op switch
            {
                SemanticOperator.Plus => VmSemanticValue.From(unchecked(a + b)), SemanticOperator.Minus => VmSemanticValue.From(unchecked(a - b)),
                SemanticOperator.Multiply => VmSemanticValue.From(unchecked(a * b)), SemanticOperator.Divide => VmSemanticValue.From(a / b), SemanticOperator.Modulo => VmSemanticValue.From(a % b),
                SemanticOperator.Equal => Bool(a == b), SemanticOperator.NotEqual => Bool(a != b), SemanticOperator.Greater => Bool(a > b), SemanticOperator.Less => Bool(a < b), SemanticOperator.GreaterEqual => Bool(a >= b), SemanticOperator.LessEqual => Bool(a <= b),
                SemanticOperator.BitAnd => VmSemanticValue.From(a & b), SemanticOperator.BitOr => VmSemanticValue.From(a | b), SemanticOperator.BitXor => VmSemanticValue.From(a ^ b), SemanticOperator.ShiftLeft => VmSemanticValue.From(a << (int)b), SemanticOperator.ShiftRight => VmSemanticValue.From(a >> (int)b),
                SemanticOperator.LogicalAnd => Bool(a != 0 && b != 0), SemanticOperator.LogicalOr => Bool(a != 0 || b != 0), SemanticOperator.LogicalXor => Bool((a == 0) != (b == 0)), SemanticOperator.Nand => Bool(a == 0 || b == 0), SemanticOperator.Nor => Bool(a == 0 && b == 0), _ => VmSemanticValue.Unavailable,
            };
            return value.Kind != VmSemanticValueKind.Unavailable;
        }
        if (left.TryGetString(out var x) && right.TryGetString(out var y))
        {
            var compare = host.CompareStrings(x, y);
            value = op switch { SemanticOperator.Plus => VmSemanticValue.From(x + y), SemanticOperator.Equal => Bool(x == y), SemanticOperator.NotEqual => Bool(x != y), SemanticOperator.Greater => Bool(compare > 0), SemanticOperator.Less => Bool(compare < 0), SemanticOperator.GreaterEqual => Bool(compare >= 0), SemanticOperator.LessEqual => Bool(compare <= 0), _ => VmSemanticValue.Unavailable };
            return value.Kind != VmSemanticValueKind.Unavailable;
        }
        if (op == SemanticOperator.Multiply && TryStringMultiply(left, right, out value)) return true;
        return false;
    }

    private bool TryMatchCase(VmSemanticValue selector, int record, out bool match)
    {
        match = false;
        if ((uint)record >= (uint)program.SemanticArena.Records.Length) return false;
        var root = program.SemanticArena.Records[record].RootNodeIndex;
        if ((uint)root >= (uint)program.SemanticArena.Nodes.Length) return false;
        var node = program.SemanticArena.Nodes[root];
        if (node.Kind != SemanticNodeKind.Case) return false;
        for (var i = 0; i < node.B; i++)
        {
            var arm = program.SemanticArena.CaseArms[node.A + i];
            if (!TryMatchArm(selector, arm, out var armMatch)) return false;
            if (armMatch) { match = true; return true; }
        }
        return true;
    }

    private bool TryMatchArm(VmSemanticValue selector, SemanticCaseArm arm, out bool match)
    {
        match = false;
        var node = program.SemanticArena.Nodes[arm.ValueNode];
        if (arm.ToNode >= 0)
        {
            if (!TryEvaluateNode(arm.ValueNode, out var low) || !TryCompare(low, selector, out var lower)) return false;
            if (lower > 0) return true;
            if (!TryEvaluateNode(arm.ToNode, out var high) || !TryCompare(selector, high, out var upper)) return false;
            return Return(upper <= 0, out match);
        }
        if (node.Kind == SemanticNodeKind.Binary && node.A < 0)
        {
            if (!TryEvaluateNode(node.B, out var right)) return false;
            return TryApplyBinary(node.Operator, selector, right, out var result) && result.TryGetInteger(out var integer) && Return(integer != 0, out match);
        }
        return TryEvaluateNode(arm.ValueNode, out var value) && TryApplyBinary(SemanticOperator.Equal, selector, value, out var equality) && equality.TryGetInteger(out var equal) && Return(equal != 0, out match);
    }

    private bool TryFormat(SemanticNode node, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (!TryEvaluateNode(node.A, out var raw) || !TryAsString(raw, out var text)) return false;
        if (node.B < 0) return Return(VmSemanticValue.From(text), out value);
        if (!TryEvaluateInt(node.B, out var width) || width is < int.MinValue or > int.MaxValue || !TryReadSymbol(node.C, out var alignment)) return false;
        var count = Math.Abs((int)width);
        if (count > 1_000_000) return false;
        return Return(VmSemanticValue.From(alignment == "LEFT" ? text.PadRight(count) : text.PadLeft(count)), out value);
    }

    private bool TryFormatSequence(SemanticNode node, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < node.B; i++)
        {
            if (!TryEvaluateNode(program.SemanticArena.Edges[node.A + i].To, out var child) || !TryAsString(child, out var text)) return false;
            builder.Append(text);
        }
        value = VmSemanticValue.From(builder.ToString()); return true;
    }

    private readonly record struct ResolvedLValue(string Name, string? Subkey, VmSemanticValue[] Indices);
    private bool TryResolveLValue(int index, out ResolvedLValue lvalue)
    {
        lvalue = default;
        if ((uint)index >= (uint)program.SemanticArena.Nodes.Length) return false;
        var node = program.SemanticArena.Nodes[index];
        if (node.Kind == SemanticNodeKind.Symbol && TryReadSymbol(node.A, out var symbol)) { lvalue = new(symbol, null, []); return true; }
        if (node.Kind == SemanticNodeKind.Variable && TryReadSymbol(node.A, out var name) && TryEvaluateEdges(node.B, node.C, out var indices)) { lvalue = new(name, null, indices); return true; }
        if (node.Kind == SemanticNodeKind.VariableSubkey && TryReadSymbol(node.A, out name) && TryReadSymbol(node.B, out var subkey) && TryEvaluateOptionalEdges(node.C, node.D, out indices)) { lvalue = new(name, subkey, indices); return true; }
        return false;
    }
    private bool TryEvaluateEdges(int start, int count, out VmSemanticValue[] values)
    {
        values = [];
        if (count < 0 || start < 0 || start > program.SemanticArena.Edges.Length || count > program.SemanticArena.Edges.Length - start) return false;
        values = new VmSemanticValue[count];
        for (var i = 0; i < count; i++) if (!TryEvaluateNode(program.SemanticArena.Edges[start + i].To, out values[i])) return false;
        return true;
    }
    private bool TryEvaluateOptionalEdges(int start, int count, out VmSemanticValue[] values) => start == -1 && count == -1 ? Return([], out values) : TryEvaluateEdges(start, count, out values);
    private bool TryReadSymbol(int index, out string value)
    {
        value = string.Empty;
        if ((uint)index >= (uint)program.SemanticArena.Symbols.Length) return false;
        var slice = program.SemanticArena.Symbols[index];
        if (slice.Offset < 0 || slice.Length < 0 || slice.Offset > program.SemanticArena.Utf8.Length || slice.Length > program.SemanticArena.Utf8.Length - slice.Offset) return false;
        value = System.Text.Encoding.UTF8.GetString(program.SemanticArena.Utf8, slice.Offset, slice.Length); return true;
    }
    private static bool TryParseInteger(string text, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        var marker = text.IndexOfAny(['p','P','e','E']); var significand = marker < 0 ? text : text[..marker]; var exponent = marker < 0 ? "" : text[(marker + 1)..];
        var baseValue = significand.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : significand.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? 2 : 10;
        var digits = baseValue == 10 ? significand : significand[2..];
        if (!long.TryParse(digits, baseValue == 10 ? NumberStyles.Integer : NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var integer))
        {
            if (baseValue == 2) { try { integer = Convert.ToInt64(digits, 2); } catch { return false; } } else return false;
        }
        if (marker >= 0)
        {
            if (!long.TryParse(exponent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var power)) return false;
            var radix = text[marker] is 'p' or 'P' ? 2d : 10d; var result = integer * Math.Pow(radix, power);
            if (double.IsNaN(result) || double.IsInfinity(result) || result > long.MaxValue || result < long.MinValue) return false;
            integer = (long)result;
        }
        value = VmSemanticValue.From(integer); return true;
    }
    private bool TryStringMultiply(VmSemanticValue left, VmSemanticValue right, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        var text = left.TryGetString(out var ls) ? ls : right.TryGetString(out var rs) ? rs : null;
        var number = left.TryGetInteger(out var li) ? li : right.TryGetInteger(out var ri) ? ri : long.MinValue;
        if (text is null) return false;
        if (number < 0 || number >= 10_000) return Fault(VmSemanticFault.StringMultiplierOutOfRange);
        if (number == 0 || text.Length == 0) { value = VmSemanticValue.From(string.Empty); return true; }
        var builder = new System.Text.StringBuilder(text.Length * (int)number);
        for (var i = 0; i < number; i++) builder.Append(text);
        value = VmSemanticValue.From(builder.ToString()); return true;
    }
    private bool TryCompare(VmSemanticValue left, VmSemanticValue right, out int comparison)
    {
        comparison = 0;
        if (left.TryGetInteger(out var a) && right.TryGetInteger(out var b)) { comparison = a.CompareTo(b); return true; }
        if (left.TryGetString(out var x) && right.TryGetString(out var y)) { comparison = host.CompareStrings(x, y); return true; }
        return false;
    }
    private static bool TryAsString(VmSemanticValue value, out string text) { text = value.ToString(); return value.Kind is VmSemanticValueKind.Integer or VmSemanticValueKind.String or VmSemanticValueKind.Missing; }
    private static VmSemanticValue Bool(bool value) => VmSemanticValue.From(value ? 1L : 0L);
    private static bool Return(VmSemanticValue source, out VmSemanticValue value) { value = source; return true; }
    private static bool Return(bool source, out bool value) { value = source; return true; }
    private static bool Return(VmSemanticValue[] source, out VmSemanticValue[] value) { value = source; return true; }
}
