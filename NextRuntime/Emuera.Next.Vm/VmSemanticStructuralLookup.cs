namespace MinorShift.Emuera.Next.Vm;

// Immutable, generation-scoped structural link lookup. The LinkedProgram reference is the
// generation identity; callers must not reuse this lookup with another program instance.
public sealed class VmSemanticStructuralLookup
{
    public LinkedProgram Program { get; }
    private readonly int[][] structuralByFunctionPc;
    private readonly Dictionary<int, int>? descriptorSlotByFunction;

    private VmSemanticStructuralLookup(LinkedProgram program)
    {
        Program = program ?? throw new ArgumentNullException(nameof(program));
        structuralByFunctionPc = program.Descriptors.Select(d => Enumerable.Repeat(-1, d.CodeLength).ToArray()).ToArray();
        if (program.Descriptors.Where((descriptor, slot) => descriptor.FunctionId != slot).Any())
            descriptorSlotByFunction = program.Descriptors.Select((descriptor, slot) => (descriptor.FunctionId, slot))
                .ToDictionary(item => item.FunctionId, item => item.slot);
        for (var index = 0; index < program.StructuralLinks.Length; index++)
        {
            var link = program.StructuralLinks[index];
            var slot = descriptorSlotByFunction?.GetValueOrDefault(link.FunctionId, -1) ?? link.FunctionId;
            if ((uint)slot < (uint)structuralByFunctionPc.Length && (uint)link.Pc < (uint)structuralByFunctionPc[slot].Length)
                structuralByFunctionPc[slot][link.Pc] = index;
        }
    }

    public static VmSemanticStructuralLookup Build(LinkedProgram program) => new(program);

    public int GetStructuralLinkIndex(RuntimeFunctionId functionId, int pc)
    {
        var slot = descriptorSlotByFunction?.GetValueOrDefault(functionId.Value, -1) ?? functionId.Value;
        if ((uint)slot >= (uint)structuralByFunctionPc.Length || (uint)pc >= (uint)structuralByFunctionPc[slot].Length) return -1;
        return structuralByFunctionPc[slot][pc];
    }

    internal void EnsureFor(LinkedProgram program)
    {
        if (!ReferenceEquals(Program, program)) throw new ArgumentException("structural lookup belongs to another linked program", nameof(program));
    }
}
