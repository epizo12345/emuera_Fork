namespace MinorShift.Emuera.Next.Vm;

// Immutable, generation-scoped structural link lookup. The LinkedProgram reference is the
// generation identity; callers must not reuse this lookup with another program instance.
public sealed class VmSemanticStructuralLookup
{
    public LinkedProgram Program { get; }
    private readonly int[][] structuralByFunctionPc;

    private VmSemanticStructuralLookup(LinkedProgram program)
    {
        Program = program ?? throw new ArgumentNullException(nameof(program));
        structuralByFunctionPc = program.Descriptors.Select(d => Enumerable.Repeat(-1, d.CodeLength).ToArray()).ToArray();
        for (var index = 0; index < program.StructuralLinks.Length; index++)
        {
            var link = program.StructuralLinks[index];
            if ((uint)link.FunctionId < (uint)structuralByFunctionPc.Length && (uint)link.Pc < (uint)structuralByFunctionPc[link.FunctionId].Length)
                structuralByFunctionPc[link.FunctionId][link.Pc] = index;
        }
    }

    public static VmSemanticStructuralLookup Build(LinkedProgram program) => new(program);

    public int GetStructuralLinkIndex(RuntimeFunctionId functionId, int pc)
    {
        if ((uint)functionId.Value >= (uint)structuralByFunctionPc.Length || (uint)pc >= (uint)structuralByFunctionPc[functionId.Value].Length) return -1;
        return structuralByFunctionPc[functionId.Value][pc];
    }

    internal void EnsureFor(LinkedProgram program)
    {
        if (!ReferenceEquals(Program, program)) throw new ArgumentException("structural lookup belongs to another linked program", nameof(program));
    }
}
