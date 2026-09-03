using MinorShift.Emuera.Next.Compiler;

namespace MinorShift.Emuera.Next.Vm;

// Production-owned readiness data.  The caller supplies only canonical IDs and
// capability facts; TSV formatting and audit files deliberately stay outside this type.
[Flags]
public enum VmRuntimeRequirement : ushort
{
    None = 0,
    Code = 1,
    VariableRead = 2,
    VariableWrite = 4,
    Builtin = 8,
    HostStatement = 16,
    Character = 32,
    Random = 64,
    UnsupportedVariable = 128,
    UnsupportedBuiltin = 256,
    UnsupportedExpressionMethod = 512,
    FrameBridge = 1024,
    EventSemantics = 2048,
}

public enum VmRuntimeVariableUse
{
    Ordinary,
    CsvIndexLabel,
    BareStringLiteral,
}

public readonly record struct VmRuntimeReadinessNode(VmRuntimeRequirement Requirements)
{
    public bool LocalEligible(VmRuntimeRequirement supported) => (Requirements & ~supported) == 0;
}

public sealed record VmRuntimeCapabilitySnapshot(
    VmRuntimeRequirement SupportedRequirements,
    Func<SemanticHostIdentity, bool>? VariableAvailable = null,
    Func<SemanticHostIdentity, bool>? BuiltinAvailable = null,
    Func<int, SemanticPayload, SemanticHostIdentity, bool>? FrameVariableAvailable = null,
    Func<SemanticHostIdentity, bool>? CharacterVariable = null,
    Func<SemanticHostIdentity, bool>? VariableWriteAvailable = null,
    Func<int, SemanticPayload, SemanticHostIdentity, VmRuntimeVariableUse, bool>? VariableAvailableInContext = null,
    Func<int, SemanticPayload, SemanticHostIdentity, VmRuntimeVariableUse, bool>? CharacterVariableInContext = null,
    Func<int, SemanticPayload, int, bool>? StringAssignmentTarget = null,
    Func<int, SemanticPayload, SemanticHostIdentity, VmRuntimeVariableUse, VmRuntimeVariableObservation>? VariableObservation = null,
    Func<SemanticHostIdentity, VmRuntimeCallObservation>? CallObservation = null)
{
    public static VmRuntimeCapabilitySnapshot KernelOnly { get; } = new(VmRuntimeRequirement.None);
}

public sealed class VmRuntimePreparationCounters
{
    public int SemanticNodeVisitCount { get; set; }
    public int SemanticContextIndexBuildCount { get; set; }
    public int SemanticContextFullPayloadScanCount { get; set; }
    public int CsvContextLookupCount { get; set; }
    public int CharacterContextLookupCount { get; set; }
    public int VariableClassifierCallCount { get; set; }
    public int VariableClassifierCacheHitCount { get; set; }
    public int CallGraphBuildCount { get; set; }
    public int CallEdgeVisitCount { get; set; }
    public int SccBuildCount { get; set; }
    public int SharedReadinessEvaluationCount { get; set; }
    public int ProductionReadinessEvaluationCount { get; set; }
    public int ReadinessEvaluationPasses { get; set; }
}

public readonly record struct VmRuntimeReadinessRow(
    RuntimeFunctionId RuntimeId,
    bool LocalEligible,
    bool TransitiveEligible,
    VmRuntimeRequirement TransitiveRequirements,
    VmRuntimeRequirement BlockingRequirements,
    int SccId);

public sealed record VmRuntimeReadinessResult(
    VmRuntimeReadinessRow[] Rows,
    int[][] SccGroups,
    int CondensationEdgeCount,
    int FixedPointIterations)
{
    public int EligibleCount => Rows.Count(row => row.TransitiveEligible);
    public int BlockedCount => Rows.Length - EligibleCount;

    public VmRuntimeReadinessResult RestrictEligibleTo(IReadOnlySet<int> allowedIds) =>
        this with { Rows = Rows.Select(row => allowedIds.Contains(row.RuntimeId.Value) ? row : row with { TransitiveEligible = false }).ToArray() };
}

public readonly record struct VmRuntimeReadinessEdgeRecord(
    int RecordOrdinal,
    RuntimeFunctionId Caller,
    RuntimeFunctionId Callee,
    string EdgeKind,
    string SourceKind,
    bool ValidCaller,
    bool ValidTarget);

public readonly record struct VmRuntimeExpressionUserMethodRecord(
    RuntimeFunctionId Caller,
    int SemanticRecordIndex,
    RuntimeFunctionId Target,
    bool Resolved,
    bool EdgeAdded,
    bool UnsupportedExpressionMethodAdded,
    string Reason);

public readonly record struct VmRuntimeRequirementOriginRecord(
    RuntimeFunctionId RuntimeId,
    VmRuntimeRequirement Requirement,
    string FirstArenaKind,
    int FirstRecordIndex,
    int FirstNodeIndex,
    int OriginCount,
    string OriginCategory);

public readonly record struct VmRuntimeVariableObservation(
    bool TokenLookupAttempted,
    bool TokenResolved,
    string TokenCode,
    string TokenName,
    string TokenFamily,
    string StorageShape,
    bool TokenIsCharacter,
    bool TokenIsConst,
    bool TokenIsCalc,
    bool TokenReadSupported,
    bool TokenWriteSupported,
    bool ContextAvailable,
    bool CacheLookupPerformed,
    bool CacheHit,
    string CacheKey,
    string ResolutionReason);

public readonly record struct VmRuntimeCallObservation(
    bool RegistryLookupAttempted,
    bool Resolved,
    string BuiltinIdentity,
    string ResolutionReason);

public readonly record struct VmRuntimeOccurrenceDiagnostic(
    int RuntimeFunctionId,
    int Pc,
    int OperandIndex,
    string StatementKind,
    string ArenaKind,
    int RecordIndex,
    int NodeIndex,
    int ParentNodeIndex,
    string ParentNodeKind,
    string NodeKind,
    string Role,
    VmRuntimeVariableUse Use,
    bool IsRead,
    bool IsWrite,
    bool IsAssignmentDestination,
    bool IsTimesDestination,
    bool IsVariableRoot,
    bool IsIndexChild,
    bool IsBareStringLiteralContext,
    bool IsCharacterContext,
    bool IsFrameContext,
    bool IsCsvIndexContext,
    bool StatementTargetIsString,
    ulong HostIdentityStableId,
    int NameSymbolIndex,
    int SubkeySymbolIndex,
    int IndexArity,
    string TokenCode,
    string TokenName,
    string TokenFamily,
    string StorageShape,
    bool TokenResolved,
    string TokenResolutionReason,
    bool CacheLookupPerformed,
    bool CacheHit,
    string CacheKey,
    bool ContextAvailable,
    bool RequirementVariableRead,
    bool RequirementVariableWrite,
    bool RequirementUnsupportedVariable,
    bool RequirementCharacter,
    string RequirementContribution);

public readonly record struct VmRuntimeNonVariableBlockingDiagnostic(
    int RuntimeFunctionId,
    int Pc,
    int OperandIndex,
    string StatementKind,
    string ArenaKind,
    int RecordIndex,
    int NodeIndex,
    int ParentNodeIndex,
    string ParentNodeKind,
    string NodeKind,
    string Role,
    ulong HostIdentityStableId,
    int TargetRuntimeFunctionId,
    string TargetKind,
    bool TargetResolved,
    bool Callable,
    bool BuiltinResolved,
    bool UnsupportedExpressionMethod,
    bool UnsupportedBuiltin,
    string RequirementContribution,
    string ResolvedBuiltinIdentity,
    string Reason);

public sealed record VmRuntimeReadinessDiagnostics(
    VmRuntimeReadinessNode[] SharedLocalNodes,
    VmRuntimeReadinessEdgeRecord[] RawEdges,
    VmRuntimeExpressionUserMethodRecord[] ExpressionUserMethods,
    VmRuntimeRequirementOriginRecord[] LocalRequirementOrigins,
    VmRuntimeOccurrenceDiagnostic[] OccurrenceDiagnostics,
    VmRuntimeNonVariableBlockingDiagnostic[] NonVariableBlockingDiagnostics);

public sealed class VmRuntimeReadinessGraph
{
    internal VmRuntimeReadinessGraph(int nodeCount, int[][] groups, int[] ids, HashSet<int>[] condensation)
    {
        NodeCount = nodeCount;
        SccGroups = groups;
        SccIds = ids;
        Condensation = condensation;
    }

    public int NodeCount { get; }
    public int[][] SccGroups { get; }
    public int[] SccIds { get; }
    internal HashSet<int>[] Condensation { get; }

    public static VmRuntimeReadinessGraph Build(
        int nodeCount,
        IReadOnlyList<(RuntimeFunctionId Caller, RuntimeFunctionId Callee)> edges,
        VmRuntimePreparationCounters? counters = null)
    {
        if (nodeCount < 0) throw new ArgumentOutOfRangeException(nameof(nodeCount));
        ArgumentNullException.ThrowIfNull(edges);
        if (counters is not null) counters.CallGraphBuildCount++;
        var graph = Enumerable.Range(0, nodeCount).Select(_ => new HashSet<int>()).ToArray();
        foreach (var (caller, callee) in edges)
        {
            if (counters is not null) counters.CallEdgeVisitCount++;
            if ((uint)caller.Value < (uint)nodeCount && (uint)callee.Value < (uint)nodeCount)
                graph[caller.Value].Add(callee.Value);
        }
        var (ids, groups) = ComputeScc(graph);
        if (counters is not null) counters.SccBuildCount++;
        var condensation = Enumerable.Range(0, groups.Length).Select(_ => new HashSet<int>()).ToArray();
        foreach (var (caller, callee) in edges)
            if ((uint)caller.Value < (uint)nodeCount && (uint)callee.Value < (uint)nodeCount && ids[caller.Value] != ids[callee.Value])
                condensation[ids[caller.Value]].Add(ids[callee.Value]);
        return new(nodeCount, groups, ids, condensation);
    }

    private static (int[] Ids, int[][] Groups) ComputeScc(IReadOnlyList<HashSet<int>> graph)
    {
        var index = 0; var stack = new Stack<int>(); var onStack = new bool[graph.Count]; var indices = Enumerable.Repeat(-1, graph.Count).ToArray(); var low = new int[graph.Count]; var ids = new int[graph.Count]; var groups = new List<int[]>();
        void Visit(int node)
        {
            indices[node] = low[node] = index++; stack.Push(node); onStack[node] = true;
            foreach (var next in graph[node])
                if (indices[next] < 0) { Visit(next); low[node] = Math.Min(low[node], low[next]); }
                else if (onStack[next]) low[node] = Math.Min(low[node], indices[next]);
            if (low[node] != indices[node]) return;
            var group = new List<int>(); int member;
            do { member = stack.Pop(); onStack[member] = false; ids[member] = groups.Count; group.Add(member); } while (member != node);
            groups.Add(group.ToArray());
        }
        for (var node = 0; node < graph.Count; node++) if (indices[node] < 0) Visit(node);
        return (ids, groups.ToArray());
    }
}

public static class VmRuntimeReadinessEvaluator
{
    public static VmRuntimeReadinessResult Evaluate(
        IReadOnlyList<VmRuntimeReadinessNode> nodes,
        IReadOnlyList<(RuntimeFunctionId Caller, RuntimeFunctionId Callee)> edges,
        VmRuntimeCapabilitySnapshot capabilities,
        VmRuntimePreparationCounters? counters = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var graph = VmRuntimeReadinessGraph.Build(nodes.Count, edges, counters);
        return Evaluate(graph, nodes, capabilities, counters);
    }

    public static VmRuntimeReadinessResult Evaluate(
        VmRuntimeReadinessGraph graph,
        IReadOnlyList<VmRuntimeReadinessNode> nodes,
        VmRuntimeCapabilitySnapshot capabilities,
        VmRuntimePreparationCounters? counters = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (nodes.Count != graph.NodeCount) throw new ArgumentException("readiness graph/node count mismatch", nameof(nodes));
        if (counters is not null) counters.ReadinessEvaluationPasses++;
        var groups = graph.SccGroups;
        var ids = graph.SccIds;
        var local = groups.Select(group => group.All(id => nodes[id].LocalEligible(capabilities.SupportedRequirements))).ToArray();
        var transitive = groups.Select(group => group.Aggregate(VmRuntimeRequirement.None, (all, id) => all | nodes[id].Requirements)).ToArray();
        var blocking = groups.Select(group => group.Aggregate(VmRuntimeRequirement.None, (all, id) => all | (nodes[id].Requirements & ~capabilities.SupportedRequirements))).ToArray();
        var condensation = graph.Condensation;
        var eligible = local.ToArray();
        var iterations = 0;
        bool changed;
        do
        {
            changed = false;
            iterations++;
            for (var scc = 0; scc < eligible.Length; scc++)
            {
                var next = local[scc] && condensation[scc].All(target => eligible[target]);
                if (next != eligible[scc]) { eligible[scc] = next; changed = true; }
                var nextTransitive = transitive[scc];
                foreach (var target in condensation[scc]) nextTransitive |= transitive[target];
                if (nextTransitive != transitive[scc]) { transitive[scc] = nextTransitive; changed = true; }
                var nextBlocking = blocking[scc];
                foreach (var target in condensation[scc]) nextBlocking |= blocking[target];
                if (nextBlocking != blocking[scc]) { blocking[scc] = nextBlocking; changed = true; }
            }
        } while (changed);
        var rows = Enumerable.Range(0, nodes.Count).Select(id => new VmRuntimeReadinessRow(new(id), nodes[id].LocalEligible(capabilities.SupportedRequirements), eligible[ids[id]], transitive[ids[id]], blocking[ids[id]], ids[id])).ToArray();
        return new(rows, groups, condensation.Sum(group => group.Count), iterations);
    }
}

// Shared production-side requirement derivation.  Audit/reporting remains in
// Emuera.Next.VmAudit; this value-only pass is usable by the Legacy host.
public static class VmRuntimeRequirementAnalyzer
{
    public sealed record StagedResult(VmRuntimeReadinessResult Shared, VmRuntimeReadinessResult Production, VmRuntimeReadinessDiagnostics? Diagnostics = null);

    private sealed record StagedBuild(
        VmRuntimeReadinessNode[] SharedNodes,
        VmRuntimeReadinessNode[] ProductionNodes,
        List<(RuntimeFunctionId Caller, RuntimeFunctionId Callee)> Edges,
        List<VmRuntimeReadinessEdgeRecord> RawEdges,
        List<VmRuntimeExpressionUserMethodRecord> ExpressionUserMethods,
        List<VmRuntimeOccurrenceDiagnostic> OccurrenceDiagnostics,
        List<VmRuntimeNonVariableBlockingDiagnostic> NonVariableBlockingDiagnostics,
        VmRuntimeRequirementOriginRecord[] Origins);

    private sealed class OriginAccumulator
    {
        private readonly Dictionary<(int FunctionId, VmRuntimeRequirement Requirement), VmRuntimeRequirementOriginRecord> rows = [];
        public void Add(int functionId, VmRuntimeRequirement requirement, string arena, int record, int node, string category)
        {
            if (rows.TryGetValue((functionId, requirement), out var existing))
            {
                rows[(functionId, requirement)] = existing with { OriginCount = existing.OriginCount + 1 };
                return;
            }
            rows[(functionId, requirement)] = new(new RuntimeFunctionId(functionId), requirement, arena, record, node, 1, category);
        }
        public VmRuntimeRequirementOriginRecord[] ToArray() => rows.Values.OrderBy(row => row.RuntimeId.Value).ThenBy(row => row.Requirement).ToArray();
    }

    public static VmRuntimeReadinessResult Analyze(
        LinkedProgram program,
        IReadOnlyList<FunctionKind> kinds,
        VmRuntimeCapabilitySnapshot capabilities,
        bool includeFrame = true,
        bool includeEventGate = true,
        VmRuntimePreparationCounters? counters = null)
    {
        var state = BuildRequirements(program, kinds, capabilities, includeFrame, includeEventGate, counters);
        var graph = VmRuntimeReadinessGraph.Build(program.Descriptors.Length, state.Edges, counters);
        return VmRuntimeReadinessEvaluator.Evaluate(graph, state.Nodes, capabilities, counters);
    }

    public static StagedResult AnalyzeStaged(
        LinkedProgram program,
        IReadOnlyList<FunctionKind> kinds,
        VmRuntimeCapabilitySnapshot sharedCapabilities,
        VmRuntimeCapabilitySnapshot productionCapabilities,
        VmRuntimePreparationCounters? counters = null,
        Action<string>? phaseBoundary = null)
    {
        phaseBoundary?.Invoke("Readiness.Start");
        var state = BuildStagedRequirements(program, kinds, sharedCapabilities, productionCapabilities, counters);
        phaseBoundary?.Invoke("Readiness.AfterRequirements");
        var graph = VmRuntimeReadinessGraph.Build(program.Descriptors.Length, state.Edges, counters);
        phaseBoundary?.Invoke("Readiness.AfterGraph");
        if (counters is not null) counters.SharedReadinessEvaluationCount++;
        var shared = VmRuntimeReadinessEvaluator.Evaluate(graph, state.SharedNodes, sharedCapabilities, counters);
        phaseBoundary?.Invoke("Readiness.AfterSharedEvaluation");
        if (counters is not null) counters.ProductionReadinessEvaluationCount++;
        var production = VmRuntimeReadinessEvaluator.Evaluate(graph, state.ProductionNodes, productionCapabilities, counters);
        phaseBoundary?.Invoke("Readiness.AfterProductionEvaluation");
        var diagnostics = new VmRuntimeReadinessDiagnostics(
            state.SharedNodes,
            state.RawEdges.ToArray(),
            state.ExpressionUserMethods.ToArray(),
            state.Origins,
            state.OccurrenceDiagnostics.ToArray(),
            state.NonVariableBlockingDiagnostics.ToArray());
        phaseBoundary?.Invoke("Readiness.AfterDiagnostics");
        return new(shared, production, diagnostics);
    }

    private static (VmRuntimeReadinessNode[] Nodes, List<(RuntimeFunctionId Caller, RuntimeFunctionId Callee)> Edges) BuildRequirements(
        LinkedProgram program,
        IReadOnlyList<FunctionKind> kinds,
        VmRuntimeCapabilitySnapshot capabilities,
        bool includeFrame,
        bool includeEventGate,
        VmRuntimePreparationCounters? counters)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Count != program.Descriptors.Length) throw new ArgumentException("function kind count mismatch", nameof(kinds));
        var nodes = new VmRuntimeReadinessNode[program.Descriptors.Length];
        for (var id = 0; id < nodes.Length; id++) nodes[id] = new(LocalRequirements(program, id, kinds[id], includeFrame, includeEventGate));
        var edges = new List<(RuntimeFunctionId Caller, RuntimeFunctionId Callee)>(program.CallSites.Length + program.ExpressionFunctionTargets.Length);
        edges.AddRange(program.CallSites.Select(site => (new RuntimeFunctionId(site.FunctionId), site.Target)));
        var expressionTargets = program.ExpressionFunctionTargets.ToDictionary(target => target.StableId);
        var callSites = program.CallSites.ToDictionary(site => (site.FunctionId, site.Pc));
        foreach (var site in program.CallSites)
            if ((uint)site.FunctionId < (uint)program.Descriptors.Length && (uint)site.Target.Value < (uint)program.Descriptors.Length &&
                program.Descriptors[site.Target.Value].State == VmFunctionState.CodeNotAvailable)
                nodes[site.FunctionId] = new(nodes[site.FunctionId].Requirements | VmRuntimeRequirement.Code);
        for (var id = 0; id < nodes.Length; id++)
            CollectSemanticRequirements(program, id, nodes, edges, capabilities, expressionTargets, callSites, counters);
        return (nodes, edges);
    }

    private static StagedBuild BuildStagedRequirements(
        LinkedProgram program,
        IReadOnlyList<FunctionKind> kinds,
        VmRuntimeCapabilitySnapshot sharedCapabilities,
        VmRuntimeCapabilitySnapshot productionCapabilities,
        VmRuntimePreparationCounters? counters)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Count != program.Descriptors.Length) throw new ArgumentException("function kind count mismatch", nameof(kinds));
        var nodes = new VmRuntimeReadinessNode[program.Descriptors.Length];
        var productionNodes = new VmRuntimeReadinessNode[program.Descriptors.Length];
        var origins = new OriginAccumulator();
        var occurrences = new List<VmRuntimeOccurrenceDiagnostic>();
        var nonVariable = new List<VmRuntimeNonVariableBlockingDiagnostic>();
        for (var id = 0; id < nodes.Length; id++)
        {
            nodes[id] = new(LocalRequirements(program, id, kinds[id], includeFrame: false, includeEventGate: false, (requirement, arena, record, node, category) => origins.Add(id, requirement, arena, record, node, category)));
            productionNodes[id] = new(LocalRequirements(program, id, kinds[id], includeFrame: true, includeEventGate: true));
            for (var pc = 0; pc < program.Descriptors[id].CodeLength; pc++)
            {
                var instruction = program.Code[program.Descriptors[id].CodeStart + pc];
                if ((VmOpcode)instruction.Opcode == VmOpcode.Statement && (uint)instruction.Aux < (uint)program.RuntimeStatements.Records.Length &&
                    program.RuntimeStatements.Records[instruction.Aux].Kind == VmRuntimeStatementKind.Host)
                    nonVariable.Add(new(id, pc, -1, "Host", "RuntimeStatementArena", instruction.Aux, -1, -1, "Root", "HostStatement", "HostStatement", 0, -1, "HostStatement", false, false, false, false, false, "HostStatement", "", "Host statement requirement"));
            }
        }
        var edges = new List<(RuntimeFunctionId Caller, RuntimeFunctionId Callee)>(program.CallSites.Length + program.ExpressionFunctionTargets.Length);
        var rawEdges = new List<VmRuntimeReadinessEdgeRecord>(program.CallSites.Length + program.ExpressionFunctionTargets.Length);
        var expressionUserMethods = new List<VmRuntimeExpressionUserMethodRecord>();
        foreach (var site in program.CallSites)
        {
            var caller = new RuntimeFunctionId(site.FunctionId);
            edges.Add((caller, site.Target));
            rawEdges.Add(new(rawEdges.Count, caller, site.Target, site.Kind == VmInvocationKind.Jump ? "JUMP" : "CALL", "FixedCallSite",
                (uint)caller.Value < (uint)program.Descriptors.Length, (uint)site.Target.Value < (uint)program.Descriptors.Length));
        }
        var expressionTargets = program.ExpressionFunctionTargets.ToDictionary(target => target.StableId);
        var callSites = program.CallSites.ToDictionary(site => (site.FunctionId, site.Pc));
        foreach (var site in program.CallSites)
            if ((uint)site.FunctionId < (uint)program.Descriptors.Length && (uint)site.Target.Value < (uint)program.Descriptors.Length &&
                program.Descriptors[site.Target.Value].State == VmFunctionState.CodeNotAvailable)
            {
                nodes[site.FunctionId] = new(nodes[site.FunctionId].Requirements | VmRuntimeRequirement.Code);
                productionNodes[site.FunctionId] = new(productionNodes[site.FunctionId].Requirements | VmRuntimeRequirement.Code);
                origins.Add(site.FunctionId, VmRuntimeRequirement.Code, "CallSite", site.Pc, -1, "CallTargetAvailability");
            }
        for (var id = 0; id < nodes.Length; id++)
            CollectSemanticRequirements(program, id, nodes, edges, sharedCapabilities, expressionTargets, callSites, counters, productionNodes, productionCapabilities, rawEdges, expressionUserMethods, origins, occurrences, nonVariable);
        return new(nodes, productionNodes, edges, rawEdges, expressionUserMethods, occurrences, nonVariable, origins.ToArray());
    }

    private static void CollectSemanticRequirements(
        LinkedProgram program,
        int functionId,
        VmRuntimeReadinessNode[] nodes,
        List<(RuntimeFunctionId Caller, RuntimeFunctionId Callee)> edges,
        VmRuntimeCapabilitySnapshot capabilities,
        IReadOnlyDictionary<ulong, VmExpressionFunctionTarget> expressionTargets,
        IReadOnlyDictionary<(int FunctionId, int Pc), VmCallSiteRecord> callSites,
        VmRuntimePreparationCounters? counters,
        VmRuntimeReadinessNode[]? secondaryNodes = null,
        VmRuntimeCapabilitySnapshot? secondaryCapabilities = null,
        List<VmRuntimeReadinessEdgeRecord>? rawEdges = null,
        List<VmRuntimeExpressionUserMethodRecord>? expressionUserMethods = null,
        OriginAccumulator? origins = null,
        List<VmRuntimeOccurrenceDiagnostic>? occurrences = null,
        List<VmRuntimeNonVariableBlockingDiagnostic>? nonVariable = null)
    {
        var visited = new HashSet<(SemanticPayload Arena, int Index)>();
        var activeRecordIndex = -1;
        var activePc = -1;
        var activeOperandIndex = -1;
        var activeStatementKind = "Structural";
        var activeRole = "StructuralOperand";
        var activeAssignmentDestination = false;
        var activeTimesDestination = false;
        var activeStatementTargetIsString = false;
        string ArenaKind(SemanticPayload payload) => ReferenceEquals(payload, program.SemanticArena) ? "SemanticArena" : ReferenceEquals(payload, program.RuntimeStatements.OperandArena) ? "RuntimeStatementOperandArena" : ReferenceEquals(payload, program.CallArgumentArena) ? "CallArgumentArena" : "SemanticPayload";
        void ApplyVariable(VmRuntimeReadinessNode[] targetNodes, VmRuntimeCapabilitySnapshot targetCapabilities, SemanticPayload payload, SemanticHostIdentity identity, VmRuntimeVariableUse use, bool write, int recordIndex, int nodeIndex, bool isIndexChild, SemanticNodeKind nodeKind, int parentNodeIndex, string parentNodeKind)
        {
            var requirement = write ? VmRuntimeRequirement.VariableWrite : VmRuntimeRequirement.VariableRead;
            var unsupported = false;
            targetNodes[functionId] = new(targetNodes[functionId].Requirements | requirement);
            if (ReferenceEquals(targetNodes, nodes)) origins?.Add(functionId, requirement, ArenaKind(payload), recordIndex, nodeIndex, write ? "VariableWrite" : "VariableRead");
            var frameAvailable = targetCapabilities.FrameVariableAvailable?.Invoke(functionId, payload, identity) ?? false;
            var available = targetCapabilities.VariableAvailableInContext?.Invoke(functionId, payload, identity, use)
                ?? targetCapabilities.VariableAvailable?.Invoke(identity)
                ?? false;
            if (use != VmRuntimeVariableUse.BareStringLiteral && !available && !frameAvailable)
            {
                unsupported = true;
                targetNodes[functionId] = new(targetNodes[functionId].Requirements | VmRuntimeRequirement.UnsupportedVariable);
                if (ReferenceEquals(targetNodes, nodes)) origins?.Add(functionId, VmRuntimeRequirement.UnsupportedVariable, ArenaKind(payload), recordIndex, nodeIndex, "UnsupportedVariable");
            }
            var character = targetCapabilities.CharacterVariableInContext?.Invoke(functionId, payload, identity, use)
                ?? targetCapabilities.CharacterVariable?.Invoke(identity)
                ?? false;
            if (character)
            {
                targetNodes[functionId] = new(targetNodes[functionId].Requirements | VmRuntimeRequirement.Character);
                if (ReferenceEquals(targetNodes, nodes)) origins?.Add(functionId, VmRuntimeRequirement.Character, ArenaKind(payload), recordIndex, nodeIndex, "Character");
            }
            if (write && use != VmRuntimeVariableUse.CsvIndexLabel && targetCapabilities.VariableWriteAvailable is { } variableWriteAvailable && !variableWriteAvailable(identity))
            {
                unsupported = true;
                targetNodes[functionId] = new(targetNodes[functionId].Requirements | VmRuntimeRequirement.UnsupportedVariable);
                if (ReferenceEquals(targetNodes, nodes)) origins?.Add(functionId, VmRuntimeRequirement.UnsupportedVariable, ArenaKind(payload), recordIndex, nodeIndex, "UnsupportedVariable");
            }
            if (ReferenceEquals(targetNodes, nodes) && occurrences is not null)
            {
                var observation = capabilities.VariableObservation?.Invoke(functionId, payload, identity, use) ?? new(false, false, "", "", "Unknown", "Unknown", character, false, false, available, false, available, false, false, "", "NoObserver");
                occurrences.Add(new(functionId, activePc, activeOperandIndex, activeStatementKind, ArenaKind(payload), recordIndex, nodeIndex, parentNodeIndex, parentNodeKind, nodeKind.ToString(), activeRole, use,
                    !write, write, activeAssignmentDestination, activeTimesDestination, !isIndexChild, isIndexChild, use == VmRuntimeVariableUse.BareStringLiteral,
                    character, frameAvailable, use == VmRuntimeVariableUse.CsvIndexLabel, activeStatementTargetIsString, identity.StableId, identity.NameSymbolIndex, identity.SubkeySymbolIndex, identity.IndexArity,
                    observation.TokenCode, observation.TokenName, observation.TokenFamily, observation.StorageShape, observation.TokenResolved, observation.ResolutionReason,
                    observation.CacheLookupPerformed, observation.CacheHit, observation.CacheKey, available || frameAvailable,
                    !write, write, unsupported, character, write ? VmRuntimeRequirement.VariableWrite.ToString() : VmRuntimeRequirement.VariableRead.ToString()));
            }
        }
        void Visit(SemanticPayload payload, int nodeIndex, VmRuntimeVariableUse use = VmRuntimeVariableUse.Ordinary, bool write = false, int parentNodeIndex = -1, string parentNodeKind = "Root", bool isIndexChild = false)
        {
            if ((uint)nodeIndex >= (uint)payload.Nodes.Length) return;
            var node = payload.Nodes[nodeIndex];
            if (!visited.Add((payload, nodeIndex))) return;
            if (counters is not null) counters.SemanticNodeVisitCount++;
            if (node.Kind is SemanticNodeKind.Symbol or SemanticNodeKind.Variable or SemanticNodeKind.VariableSubkey)
            {
                if (payload.TryGetHostIdentity(nodeIndex, SemanticHostIdentityKind.Variable, out var identity))
                {
                    ApplyVariable(nodes, capabilities, payload, identity, use, write, activeRecordIndex, nodeIndex, isIndexChild, node.Kind, parentNodeIndex, parentNodeKind);
                    if (secondaryNodes is not null && secondaryCapabilities is { } secondary)
                        ApplyVariable(secondaryNodes, secondary, payload, identity, use, write, activeRecordIndex, nodeIndex, isIndexChild, node.Kind, parentNodeIndex, parentNodeKind);
                }
            }
            if (node.Kind == SemanticNodeKind.Call && payload.TryGetHostIdentity(nodeIndex, SemanticHostIdentityKind.Call, out var call))
            {
                var callObservation = capabilities.CallObservation?.Invoke(call) ?? new(false, false, "", "NoObserver");
                if (expressionTargets.TryGetValue(call.StableId, out var target))
                {
                    var resolved = target.Target.Value >= 0;
                    var edgeAdded = target.Callable && resolved && (uint)target.Target.Value < (uint)program.Descriptors.Length;
                    if (edgeAdded)
                    {
                        edges.Add((new RuntimeFunctionId(functionId), target.Target));
                        rawEdges?.Add(new(rawEdges.Count, new RuntimeFunctionId(functionId), target.Target, "ExpressionUserMethod", "ExpressionSemantic",
                            (uint)functionId < (uint)program.Descriptors.Length, (uint)target.Target.Value < (uint)program.Descriptors.Length));
                    }
                    else
                    {
                        nodes[functionId] = new(nodes[functionId].Requirements | VmRuntimeRequirement.UnsupportedExpressionMethod);
                        origins?.Add(functionId, VmRuntimeRequirement.UnsupportedExpressionMethod, ArenaKind(payload), activeRecordIndex, nodeIndex, "UnsupportedExpressionMethod");
                        if (secondaryNodes is not null)
                            secondaryNodes[functionId] = new(secondaryNodes[functionId].Requirements | VmRuntimeRequirement.UnsupportedExpressionMethod);
                    }
                    nonVariable?.Add(new(functionId, activePc, activeOperandIndex, activeStatementKind, ArenaKind(payload), activeRecordIndex, nodeIndex, parentNodeIndex, parentNodeKind, node.Kind.ToString(), activeRole,
                        call.StableId, target.Target.Value, "ExpressionUserMethod", resolved, target.Callable, false, !edgeAdded, false, !edgeAdded ? "UnsupportedExpressionMethod" : "", callObservation.BuiltinIdentity, edgeAdded ? "ResolvedCallable" : resolved ? "ResolvedNonCallable" : "UnresolvedTarget"));
                    expressionUserMethods?.Add(new(new RuntimeFunctionId(functionId), activeRecordIndex, target.Target, resolved, edgeAdded,
                        !edgeAdded, edgeAdded ? "ResolvedCallable" : resolved ? "ResolvedNonCallable" : "UnresolvedTarget"));
                }
                else
                {
                    var builtinAvailableResult = capabilities.BuiltinAvailable is { } builtinAvailable && builtinAvailable(call);
                    if (capabilities.BuiltinAvailable is { } && !builtinAvailableResult)
                    {
                        nodes[functionId] = new(nodes[functionId].Requirements | VmRuntimeRequirement.UnsupportedBuiltin);
                        origins?.Add(functionId, VmRuntimeRequirement.UnsupportedBuiltin, ArenaKind(payload), activeRecordIndex, nodeIndex, "UnsupportedBuiltin");
                    }
                    if (secondaryNodes is not null && secondaryCapabilities?.BuiltinAvailable is { } secondaryBuiltinAvailable && !secondaryBuiltinAvailable(call))
                        secondaryNodes[functionId] = new(secondaryNodes[functionId].Requirements | VmRuntimeRequirement.UnsupportedBuiltin);
                    nonVariable?.Add(new(functionId, activePc, activeOperandIndex, activeStatementKind, ArenaKind(payload), activeRecordIndex, nodeIndex, parentNodeIndex, parentNodeKind, node.Kind.ToString(), activeRole,
                        call.StableId, -1, "Builtin", false, false, builtinAvailableResult, false, !builtinAvailableResult, !builtinAvailableResult ? "UnsupportedBuiltin" : "", callObservation.BuiltinIdentity, builtinAvailableResult ? "ResolvedBuiltin" : "UnresolvedBuiltin"));
                }
            }
            foreach (var (child, childIsIndexLabel) in Children(payload, node))
                Visit(payload, child, childIsIndexLabel ? VmRuntimeVariableUse.CsvIndexLabel : VmRuntimeVariableUse.Ordinary, write, nodeIndex, node.Kind.ToString(), childIsIndexLabel);
        }

        void VisitRecord(SemanticPayload payload, int recordIndex, bool write = false, VmRuntimeVariableUse use = VmRuntimeVariableUse.Ordinary, int pc = -1, int operandIndex = -1, string statementKind = "Structural", string role = "StructuralOperand", bool assignmentDestination = false, bool timesDestination = false, bool statementTargetIsString = false)
        {
            if ((uint)recordIndex >= (uint)payload.Records.Length) return;
            var previousRecordIndex = activeRecordIndex;
            var previousPc = activePc; var previousOperandIndex = activeOperandIndex; var previousStatementKind = activeStatementKind; var previousRole = activeRole;
            var previousAssignmentDestination = activeAssignmentDestination; var previousTimesDestination = activeTimesDestination; var previousStatementTargetIsString = activeStatementTargetIsString;
            activeRecordIndex = recordIndex;
            activePc = pc; activeOperandIndex = operandIndex; activeStatementKind = statementKind; activeRole = role;
            activeAssignmentDestination = assignmentDestination; activeTimesDestination = timesDestination; activeStatementTargetIsString = statementTargetIsString;
            Visit(payload, payload.Records[recordIndex].RootNodeIndex, use, write);
            activeRecordIndex = previousRecordIndex;
            activePc = previousPc; activeOperandIndex = previousOperandIndex; activeStatementKind = previousStatementKind; activeRole = previousRole;
            activeAssignmentDestination = previousAssignmentDestination; activeTimesDestination = previousTimesDestination; activeStatementTargetIsString = previousStatementTargetIsString;
        }

        var descriptor = program.Descriptors[functionId];
        for (var pc = 0; pc < descriptor.CodeLength; pc++)
        {
            var instruction = program.Code[descriptor.CodeStart + pc];
            switch ((VmOpcode)instruction.Opcode)
            {
                case VmOpcode.Structural:
                    if ((uint)instruction.Aux < (uint)program.StructuralSemanticRecordIndices.Length)
                    {
                        var record = program.StructuralSemanticRecordIndices[instruction.Aux];
                        if (record >= 0) VisitRecord(program.SemanticArena, record, pc: pc, operandIndex: 0, role: "StructuralOperand");
                    }
                    break;
                case VmOpcode.Statement:
                    if ((uint)instruction.Aux < (uint)program.RuntimeStatements.Records.Length)
                    {
                        var statement = program.RuntimeStatements.Records[instruction.Aux];
                        if (statement.OperandRecord >= 0) VisitRecord(program.RuntimeStatements.OperandArena, statement.OperandRecord,
                            statement.Kind == VmRuntimeStatementKind.Set || statement.Kind == VmRuntimeStatementKind.Times,
                            pc: pc, operandIndex: 0, statementKind: statement.Kind.ToString(), role: "PrimaryOperand",
                            assignmentDestination: statement.Kind == VmRuntimeStatementKind.Set || statement.Kind == VmRuntimeStatementKind.Times,
                            timesDestination: statement.Kind == VmRuntimeStatementKind.Times);
                        if (statement.SecondaryOperandRecord >= 0)
                        {
                            var bareString = statement.Assignment == VmAssignmentOperator.AssignString ||
                                capabilities.StringAssignmentTarget?.Invoke(functionId, program.RuntimeStatements.OperandArena, statement.OperandRecord) == true;
                            VisitRecord(program.RuntimeStatements.OperandArena, statement.SecondaryOperandRecord,
                                use: bareString ? VmRuntimeVariableUse.BareStringLiteral : VmRuntimeVariableUse.Ordinary,
                                pc: pc, operandIndex: 1, statementKind: statement.Kind.ToString(), role: "SecondaryOperand", statementTargetIsString: bareString);
                        }
                    }
                    break;
                case VmOpcode.Call:
                case VmOpcode.Jump:
                    if (callSites.TryGetValue((functionId, pc), out var site))
                        for (var i = 0; i < site.ArgumentCount; i++)
                        {
                            var record = program.CallArgumentRecords[site.ArgumentRecordStart + i];
                            if (record >= 0) VisitRecord(program.CallArgumentArena, record, pc: pc, operandIndex: i, statementKind: ((VmOpcode)instruction.Opcode).ToString(), role: "ActualArgument");
                        }
                    break;
            }
        }
    }

    private static IEnumerable<(int Index, bool IndexLabel)> Children(SemanticPayload payload, SemanticNode node)
    {
        switch (node.Kind)
        {
            case SemanticNodeKind.Variable:
            case SemanticNodeKind.Call:
                if (node.B >= 0 && node.C >= 0 && node.B <= payload.Edges.Length - node.C)
                    for (var i = 0; i < node.C; i++) yield return (payload.Edges[node.B + i].To, true);
                break;
            case SemanticNodeKind.VariableSubkey:
                if (node.C >= 0 && node.D >= 0 && node.C <= payload.Edges.Length - node.D)
                    for (var i = 0; i < node.D; i++) yield return (payload.Edges[node.C + i].To, true);
                break;
            case SemanticNodeKind.Unary:
                yield return (node.A, false);
                break;
            case SemanticNodeKind.Binary:
            case SemanticNodeKind.Ternary:
                yield return (node.A, false); yield return (node.B, false); yield return (node.C, false);
                break;
            case SemanticNodeKind.Format:
                yield return (node.A, false); if (node.B >= 0) yield return (node.B, false);
                break;
            case SemanticNodeKind.ConditionalFormat:
                yield return (node.A, false); yield return (node.B, false); if (node.C >= 0) yield return (node.C, false);
                break;
            case SemanticNodeKind.FormattedSequence:
                if (node.A >= 0 && node.B >= 0 && node.A <= payload.Edges.Length - node.B)
                    for (var i = 0; i < node.B; i++) yield return (payload.Edges[node.A + i].To, false);
                break;
            case SemanticNodeKind.Case:
                if (node.A >= 0 && node.B >= 0 && node.A <= payload.CaseArms.Length - node.B)
                    for (var i = 0; i < node.B; i++) { yield return (payload.CaseArms[node.A + i].ValueNode, false); if (payload.CaseArms[node.A + i].ToNode >= 0) yield return (payload.CaseArms[node.A + i].ToNode, false); }
                break;
            case SemanticNodeKind.CountedLoop:
                yield return (node.A, false); yield return (node.B, false); yield return (node.C, false); yield return (node.D, false);
                break;
        }
    }

    private static VmRuntimeRequirement LocalRequirements(LinkedProgram program, int id, FunctionKind kind, bool includeFrame, bool includeEventGate, Action<VmRuntimeRequirement, string, int, int, string>? origin = null)
    {
        var descriptor = program.Descriptors[id];
        var result = VmRuntimeRequirement.None;
        if (descriptor.State == VmFunctionState.CodeNotAvailable)
        {
            result |= VmRuntimeRequirement.Code;
            origin?.Invoke(VmRuntimeRequirement.Code, "Descriptor", -1, -1, "CodeAvailability");
        }
        for (var pc = 0; pc < descriptor.CodeLength; pc++)
        {
            var instruction = program.Code[descriptor.CodeStart + pc];
            if ((VmOpcode)instruction.Opcode is VmOpcode.SemanticBarrier or VmOpcode.UnsupportedControl)
            {
                result |= VmRuntimeRequirement.Code;
                origin?.Invoke(VmRuntimeRequirement.Code, "LinkedCode", pc, -1, "CodeBarrier");
            }
            if ((VmOpcode)instruction.Opcode != VmOpcode.Statement || (uint)instruction.Aux >= (uint)program.RuntimeStatements.Records.Length) continue;
            var statement = program.RuntimeStatements.Records[instruction.Aux];
            if (statement.Kind == VmRuntimeStatementKind.Host)
            {
                result |= VmRuntimeRequirement.HostStatement;
                origin?.Invoke(VmRuntimeRequirement.HostStatement, "RuntimeStatementArena", instruction.Aux, -1, "HostStatement");
            }
        }
        if (includeFrame && program.RuntimeMetadata[id] != FunctionRuntimeMetadata.Empty)
        {
            result |= VmRuntimeRequirement.FrameBridge;
            origin?.Invoke(VmRuntimeRequirement.FrameBridge, "RuntimeMetadata", -1, -1, "FrameBridge");
        }
        if (includeEventGate && kind == FunctionKind.Event)
        {
            result |= VmRuntimeRequirement.EventSemantics;
            origin?.Invoke(VmRuntimeRequirement.EventSemantics, "FunctionCatalog", -1, -1, "EventSemantics");
        }
        return result;
    }
}

public sealed record VmRuntimeActivationResult(int Candidates, int Eligible, int Promoted, int Blocked)
{
    public bool AccountingPassed => Candidates == Promoted + Blocked && Eligible == Promoted;
}

public static class VmRuntimeActivator
{
    public static VmRuntimeActivationResult Activate(LinkedProgram program, VmRuntimeReadinessResult readiness)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(readiness);
        if (program.Descriptors.Length != readiness.Rows.Length) throw new ArgumentException("readiness/program length mismatch", nameof(readiness));
        var candidates = 0; var eligible = 0; var promoted = 0; var blocked = 0;
        for (var id = 0; id < program.Descriptors.Length; id++)
        {
            var descriptor = program.Descriptors[id];
            if (descriptor.State != VmFunctionState.LinkedSemanticPending) continue;
            candidates++;
            if (!readiness.Rows[id].TransitiveEligible) { blocked++; continue; }
            eligible++;
            program.Descriptors[id] = new VmFunctionDescriptor(descriptor.FunctionId, descriptor.CodeStart, descriptor.CodeLength, VmFunctionState.ExecutableReady);
            promoted++;
        }
        return new(candidates, eligible, promoted, blocked);
    }
}

// Production dispatch consumes the already-promoted descriptor and the
// production-owned dispatch-entry set.  It never derives eligibility from a
// function name or a diagnostic artifact.
public static class VmRuntimeProductionDispatch
{
    public static bool IsEligible(
        bool nextRuntimeEnabled,
        bool analysisMode,
        bool debugMode,
        int runtimeFunctionId,
        int descriptorCount,
        FunctionKind kind,
        VmFunctionState descriptorState,
        bool productionDispatchEntryReady) =>
        nextRuntimeEnabled && !analysisMode && !debugMode &&
        (uint)runtimeFunctionId < (uint)descriptorCount &&
        kind == FunctionKind.Normal &&
        descriptorState == VmFunctionState.ExecutableReady &&
        productionDispatchEntryReady;
}
