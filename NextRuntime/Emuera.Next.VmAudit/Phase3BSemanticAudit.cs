using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Vm;

namespace MinorShift.Emuera.Next.VmAudit;

internal sealed class Phase3BSemanticCoverage
{
    public int SemanticRecordsTotal { get; init; }
    public int MappingErrors { get; init; }
    public int OutOfRange { get; init; }
    public int Invalid { get; init; }
    public int UnknownNodeKinds { get; init; }
    public int UnknownOperators { get; init; }
    public int UnknownRequiredShapes { get; init; }
    public int ConstantEvaluable { get; init; }
    public int HostDependent { get; init; }
    public int ContextEvaluable { get; init; }
    public int TrueDeferred { get; init; }
    public int DeferredRequired { get; init; }
    public int StructuralRecords { get; init; }
    public int UnmappedRequired { get; init; }
    public IReadOnlyDictionary<string, int> StructuralKinds { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> DeferredReasons { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> NodeKinds { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> Operators { get; init; } = new Dictionary<string, int>();
    public bool ClassificationPartitionMatch => ConstantEvaluable + HostDependent + ContextEvaluable + TrueDeferred == SemanticRecordsTotal;
    public bool Passed => SemanticRecordsTotal == 37366 && MappingErrors == 0 && OutOfRange == 0 && Invalid == 0 && UnknownRequiredShapes == 0 && UnmappedRequired == 0 && DeferredRequired == 0 && ClassificationPartitionMatch;
}

internal static class Phase3BSemanticAudit
{
    public static Phase3BSemanticCoverage Analyze(LinkedProgram program)
    {
        var payload = program.SemanticArena;
        var nodeKinds = payload.Nodes.GroupBy(x => x.Kind.ToString()).ToDictionary(x => x.Key, x => x.Count());
        var operators = payload.Nodes.Where(x => x.Operator != SemanticOperator.None).GroupBy(x => x.Operator.ToString()).ToDictionary(x => x.Key, x => x.Count());
        var unknownNodes = payload.Nodes.Count(x => !SupportsNode(x.Kind));
        var unknownOperators = payload.Nodes.Count(x => !SupportsOperator(x.Operator));
        var mappingErrors = 0;
        var outOfRange = 0;
        var seen = new int[payload.Records.Length];
        var requiredByRecord = new bool[payload.Records.Length];
        var structuralKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var mappedRequired = 0;
        foreach (var (link, structuralIndex) in program.StructuralLinks.Select((x, i) => (x, i)))
        {
            if ((uint)structuralIndex >= (uint)program.StructuralSemanticRecordIndices.Length) { mappingErrors++; continue; }
            var record = program.StructuralSemanticRecordIndices[structuralIndex];
            if (record < 0) continue;
            if ((uint)record >= (uint)payload.Records.Length) { outOfRange++; continue; }
            seen[record]++;
            structuralKinds[link.Kind.ToString()] = structuralKinds.GetValueOrDefault(link.Kind.ToString()) + 1;
            if (RequiresSemantic(link.Kind)) { mappedRequired++; requiredByRecord[record] = true; }
        }
        mappingErrors += seen.Count(x => x > 1) + seen.Count(x => x == 0);
        var unmapped = program.StructuralLinks.Count(link => RequiresSemantic(link.Kind)) - mappedRequired;
        var executor = new VmSemanticExecutor(program, UnavailableHost.Instance);
        var deferredReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var constant = 0; var host = 0; var context = 0; var deferred = 0; var invalid = 0; var unknownRequired = 0; var deferredRequired = 0;
        for (var record = 0; record < payload.Records.Length; record++)
        {
            var root = payload.Records[record].RootNodeIndex;
            if ((uint)root >= (uint)payload.Nodes.Length) { invalid++; continue; }
            var shape = InspectShape(payload, root);
            if (shape.Invalid) { invalid++; continue; }
            if (!shape.Supported)
            {
                deferred++;
                deferredReasons[shape.Reason] = deferredReasons.GetValueOrDefault(shape.Reason) + 1;
                if (requiredByRecord[record]) { unknownRequired++; deferredRequired++; }
                continue;
            }
            if (shape.Context) { context++; continue; }
            if (executor.TryEvaluateRecord(record, out _) || executor.LastStatus == VmSemanticStatus.Fault) { constant++; continue; }
            if (shape.Host) { host++; continue; }
            deferred++;
            deferredReasons["ScalarEvaluatorUnavailable"] = deferredReasons.GetValueOrDefault("ScalarEvaluatorUnavailable") + 1;
            if (requiredByRecord[record]) deferredRequired++;
        }
        return new()
        {
            SemanticRecordsTotal = payload.Records.Length, MappingErrors = mappingErrors, OutOfRange = outOfRange, Invalid = invalid,
            UnknownNodeKinds = unknownNodes, UnknownOperators = unknownOperators, UnknownRequiredShapes = unknownRequired,
            ConstantEvaluable = constant, HostDependent = host, ContextEvaluable = context, TrueDeferred = deferred, DeferredRequired = deferredRequired,
            StructuralRecords = seen.Count(x => x > 0), UnmappedRequired = unmapped,
            StructuralKinds = structuralKinds, DeferredReasons = deferredReasons, NodeKinds = nodeKinds, Operators = operators,
        };
    }

    public static string NodeTsv(Phase3BSemanticCoverage coverage) => "NodeKind\tCount\tEvaluatorSupported\tHostDependency\tDeferredReason\n" + string.Join('\n', coverage.NodeKinds.OrderBy(x => x.Key).Select(x =>
    {
        var kind = Enum.Parse<SemanticNodeKind>(x.Key);
        var supported = SupportsNode(kind);
        var host = kind is SemanticNodeKind.Symbol or SemanticNodeKind.Variable or SemanticNodeKind.VariableSubkey or SemanticNodeKind.Call;
        return $"{x.Key}\t{x.Value}\t{supported}\t{host}\t{(supported ? "" : "TrueDeferred")}";
    })) + "\n";
    public static string OperatorTsv(Phase3BSemanticCoverage coverage) => "Operator\tCount\tEvaluatorSupported\tDeferredReason\n" + string.Join('\n', coverage.Operators.OrderBy(x => x.Key).Select(x =>
    {
        var op = Enum.Parse<SemanticOperator>(x.Key);
        var supported = SupportsOperator(op);
        return $"{x.Key}\t{x.Value}\t{supported}\t{(supported ? "" : "TrueDeferred")}";
    })) + "\n";
    public static string DeferredTsv(Phase3BSemanticCoverage coverage) => "Category\tCount\tReason\tNextPhaseRequirement\n" + string.Join('\n', coverage.DeferredReasons.OrderBy(x => x.Key).Select(x => $"{x.Key}\t{x.Value}\tTrue implementation deferral\tLegacy/runtime-state adapter or formatting adapter")) + "\n";
    public static string StructuralTsv(Phase3BSemanticCoverage coverage) => "StructuralKind\tMappedRecords\n" + string.Join('\n', coverage.StructuralKinds.OrderBy(x => x.Key).Select(x => $"{x.Key}\t{x.Value}")) + "\n";

    private static bool RequiresSemantic(VmStructuralKind kind) => kind is VmStructuralKind.Sif or VmStructuralKind.If or VmStructuralKind.ElseIf or VmStructuralKind.SelectCase or VmStructuralKind.Case or VmStructuralKind.For or VmStructuralKind.Repeat or VmStructuralKind.While or VmStructuralKind.Loop;
    private static bool SupportsNode(SemanticNodeKind kind) => kind is SemanticNodeKind.IntegerLiteral or SemanticNodeKind.StringLiteral or SemanticNodeKind.Symbol or SemanticNodeKind.Variable or SemanticNodeKind.VariableSubkey or SemanticNodeKind.Call or SemanticNodeKind.Unary or SemanticNodeKind.Binary or SemanticNodeKind.Ternary or SemanticNodeKind.Format or SemanticNodeKind.ConditionalFormat or SemanticNodeKind.Case or SemanticNodeKind.FormattedSequence or SemanticNodeKind.CountedLoop or SemanticNodeKind.MissingArgument;
    private static bool SupportsOperator(SemanticOperator op) => op is SemanticOperator.None or SemanticOperator.Plus or SemanticOperator.Minus or SemanticOperator.Multiply or SemanticOperator.Divide or SemanticOperator.Modulo or SemanticOperator.Equal or SemanticOperator.Greater or SemanticOperator.Less or SemanticOperator.GreaterEqual or SemanticOperator.LessEqual or SemanticOperator.NotEqual or SemanticOperator.BitAnd or SemanticOperator.BitOr or SemanticOperator.BitXor or SemanticOperator.LogicalAnd or SemanticOperator.LogicalOr or SemanticOperator.LogicalXor or SemanticOperator.Nand or SemanticOperator.Nor or SemanticOperator.ShiftLeft or SemanticOperator.ShiftRight or SemanticOperator.Not or SemanticOperator.BitNot or SemanticOperator.PrefixIncrement or SemanticOperator.PrefixDecrement or SemanticOperator.PostfixIncrement or SemanticOperator.PostfixDecrement or SemanticOperator.CountedFor or SemanticOperator.CountedRepeat;
    private static SemanticShape InspectShape(SemanticPayload payload, int root)
    {
        var stack = new Stack<int>(); stack.Push(root); var host = false; var context = false;
        while (stack.Count > 0)
        {
            var index = stack.Pop(); if ((uint)index >= (uint)payload.Nodes.Length) return new(true, false, false, false, "InvalidNodeReference");
            var node = payload.Nodes[index];
            if (!SupportsNode(node.Kind)) return new(false, false, false, false, node.Kind.ToString());
            if (!SupportsOperator(node.Operator)) return new(false, false, false, false, node.Operator.ToString());
            host |= node.Kind is SemanticNodeKind.Symbol or SemanticNodeKind.Variable or SemanticNodeKind.VariableSubkey or SemanticNodeKind.Call;
            context |= node.Kind is SemanticNodeKind.Case or SemanticNodeKind.CountedLoop;
            foreach (var child in Children(payload, node)) stack.Push(child);
        }
        return new(false, true, host, context, string.Empty);
    }
    private static IEnumerable<int> Children(SemanticPayload p, SemanticNode n)
    {
        if (n.Kind is SemanticNodeKind.Unary && n.A >= 0) yield return n.A;
        if (n.Kind is SemanticNodeKind.Binary or SemanticNodeKind.Ternary or SemanticNodeKind.CountedLoop) { if (n.A >= 0) yield return n.A; if (n.B >= 0) yield return n.B; if (n.C >= 0) yield return n.C; if (n.Kind == SemanticNodeKind.CountedLoop && n.D >= 0) yield return n.D; }
        if (n.Kind is SemanticNodeKind.Format) { yield return n.A; if (n.B >= 0) yield return n.B; }
        if (n.Kind is SemanticNodeKind.ConditionalFormat) { yield return n.A; yield return n.B; if (n.C >= 0) yield return n.C; }
        if (n.Kind is SemanticNodeKind.Variable or SemanticNodeKind.Call)
            for (var i = 0; i < n.C; i++) yield return p.Edges[n.B + i].To;
        if (n.Kind == SemanticNodeKind.VariableSubkey && n.C >= 0 && n.D > 0)
            for (var i = 0; i < n.D; i++) yield return p.Edges[n.C + i].To;
        if (n.Kind == SemanticNodeKind.FormattedSequence)
            for (var i = 0; i < n.B; i++) yield return p.Edges[n.A + i].To;
        if (n.Kind == SemanticNodeKind.Case)
            for (var i = 0; i < n.B; i++) { var arm = p.CaseArms[n.A + i]; yield return arm.ValueNode; if (arm.ToNode >= 0) yield return arm.ToNode; }
    }

    private readonly record struct SemanticShape(bool Invalid, bool Supported, bool Host, bool Context, string Reason);
    private sealed class UnavailableHost : IVmSemanticHost
    {
        public static UnavailableHost Instance { get; } = new();
        public bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
        public bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => false;
        public bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
        public int CompareStrings(string left, string right) => string.CompareOrdinal(left, right);
    }
}
