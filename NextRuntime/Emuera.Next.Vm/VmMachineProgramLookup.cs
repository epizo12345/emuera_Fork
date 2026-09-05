namespace MinorShift.Emuera.Next.Vm;

// Immutable, generation-scoped lookup data shared by per-session VmMachine instances.
public sealed class VmMachineProgramLookup
{
    public LinkedProgram Program { get; }
    private readonly Dictionary<(int FunctionId, int Pc), VmCallSiteRecord> callSites;
    private readonly Dictionary<ulong, VmExpressionFunctionTarget> expressionFunctionTargets;

    private VmMachineProgramLookup(LinkedProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        Program = program;
        callSites = program.CallSites.ToDictionary(site => (site.FunctionId, site.Pc));
        expressionFunctionTargets = program.ExpressionFunctionTargets.ToDictionary(target => target.StableId);
    }

    public static VmMachineProgramLookup Build(LinkedProgram program) => new(program);

    internal Dictionary<(int FunctionId, int Pc), VmCallSiteRecord> CallSiteMap => callSites;
    internal Dictionary<ulong, VmExpressionFunctionTarget> ExpressionFunctionTargetMap => expressionFunctionTargets;

    internal void EnsureFor(LinkedProgram program)
    {
        if (!ReferenceEquals(Program, program))
            throw new ArgumentException("machine lookup belongs to another linked program", nameof(program));
    }
}
