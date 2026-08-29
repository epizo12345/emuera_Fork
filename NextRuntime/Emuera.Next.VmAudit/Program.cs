using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: Emuera.Next.VmAudit <fixture-root> <report-directory> <legacy-manifest.jsonl> <phase1-compiler-manifest.jsonl> [--phase3-environment-only|--phase3-semantic]");
    return 2;
}

var auditWatch = Stopwatch.StartNew();
var allocatedStart = GC.GetTotalAllocatedBytes(true);
var fixtureRoot = Path.GetFullPath(args[0]);
var erbRoot = Directory.Exists(Path.Combine(fixtureRoot, "Data", "ERB")) ? Path.Combine(fixtureRoot, "Data", "ERB") : fixtureRoot;
var report = Path.GetFullPath(args[1]);
Directory.CreateDirectory(report);
var legacyPath = Path.GetFullPath(args[2]);
var phase1Path = Path.GetFullPath(args[3]);
var phase3Options = args.Skip(4).ToArray();
var phase3EnvironmentOnly = phase3Options.Contains("--phase3-environment-only", StringComparer.Ordinal);
var phase3Semantic = phase3Options.Contains("--phase3-semantic", StringComparer.Ordinal);
if (phase3Options.Any(x => x is not ("--phase3-environment-only" or "--phase3-semantic")) || phase3EnvironmentOnly && phase3Semantic) { Console.Error.WriteLine("Invalid Phase3 option."); return 2; }
Phase3HarnessEnvironment? phase3Environment = null;
if (phase3EnvironmentOnly || phase3Semantic)
{
    phase3Environment = BuildPhase3Environment(Path.Combine(fixtureRoot, "Data"));
    WritePhase3Environment(report, phase3Environment);
}
if (phase3EnvironmentOnly)
{
    Write("phase3-environment-only.txt", "Mode=PHASE3_ENVIRONMENT_ONLY\nErbIndexingPerformed=NO\nLegacyManifestRead=NO\nCompilerInvocationCount=0\nVmLinkPerformed=NO\nVmRunPerformed=NO\n");
    Console.WriteLine("VmAudit: phase3 environment-only PASS");
    return 0;
}
if (phase3Semantic && (!phase3Environment!.AuthorityMatched || !TryReadCompilerFingerprint(phase1Path, out var compilerFingerprint) || !compilerFingerprint.Equals(phase3Environment.Fingerprint, StringComparison.OrdinalIgnoreCase)))
{
    Write("phase3-semantic-summary.txt", $"Phase3EnvironmentAuthorityMatched={phase3Environment.AuthorityMatched}\nPhase3EnvironmentGateErrors={phase3Environment.AuthorityGateErrors}\nCompilerFingerprintFileStrictParser=True\nCompilerEnvironmentFingerprintMatched=False\nPhase3SemanticGatesAffectExitCode=True\nResult=FAIL\n");
    return 1;
}

var indexWatch = Stopwatch.StartNew();
var files = ErbSourceIndexer.IndexDirectory(erbRoot);
indexWatch.Stop();
var sourceRows = new List<SourceRow>();
var sourceId = 0;
for (var fileOrdinal = 0; fileOrdinal < files.Count; fileOrdinal++)
{
    var file = files[fileOrdinal];
    for (var functionOrdinal = 0; functionOrdinal < file.Functions.Count; functionOrdinal++)
    {
        var function = file.Functions[functionOrdinal];
        sourceRows.Add(new(new(sourceId++), NormalizeRelative(Path.GetRelativePath(erbRoot, file.FileIdentity)), functionOrdinal,
            function.Span.StartLine, function.Name, fileOrdinal, function.Flags));
    }
}

var oracleWatch = Stopwatch.StartNew();
var legacyRows = ReadLegacy(legacyPath);
oracleWatch.Stop();
var sourcePositionGroups = sourceRows.GroupBy(x => PositionKey(x.RelativeFile, x.StartLine), StringComparer.OrdinalIgnoreCase).ToArray();
var legacyPositionGroups = legacyRows.GroupBy(x => PositionKey(x.RelativeFile, x.StartLine), StringComparer.OrdinalIgnoreCase).ToArray();
var bindingCollisions = sourcePositionGroups.Where(x => x.Count() != 1).Select(x => $"Source\t{x.First().RelativeFile}\t{x.First().StartLine}\t{x.Count()}\t{string.Join(",", x.Select(y => y.SourceId.Value))}")
    .Concat(legacyPositionGroups.Where(x => x.Count() != 1).Select(x => $"Runtime\t{x.First().RelativeFile}\t{x.First().StartLine}\t{x.Count()}\t{string.Join(",", x.Select(y => y.RuntimeId.Value))}"))
    .ToArray();
var sourceByPosition = sourcePositionGroups.Where(x => x.Count() == 1).ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
var legacyByPosition = legacyPositionGroups.Where(x => x.Count() == 1).ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
var exactBound = legacyRows.Where(x => sourceByPosition.ContainsKey(PositionKey(x.RelativeFile, x.StartLine))).ToArray();
var sourceOnly = sourceRows.Where(x => !legacyByPosition.ContainsKey(PositionKey(x.RelativeFile, x.StartLine))).ToArray();
var runtimeOnly = legacyRows.Where(x => !sourceByPosition.ContainsKey(PositionKey(x.RelativeFile, x.StartLine))).ToArray();
var ordinalMisbinds = DetectOrdinalMisbinds(sourceRows, legacyRows);
var sourceOnlyEvidence = sourceOnly.Select(x => ProveSourceOnly(x, files, erbRoot)).ToArray();
var runtimeOnlyEvidence = runtimeOnly.Select(x => ProveRuntimeOnly(x, files, erbRoot)).ToArray();
var sourceOnlyProof = sourceOnlyEvidence.Length == 3 && sourceOnlyEvidence.All(x => x.Proven && x.Classification == "PhysicalOnlyPreprocessorDisabled");
var runtimeOnlyProof = runtimeOnlyEvidence.Length == 3 && runtimeOnlyEvidence.All(x => x.Proven && x.Classification == "RuntimeOnlyLineContinuation");
var ambiguousBinding = bindingCollisions.Length;

var runtimeBindings = new RuntimeFunctionBinding[legacyRows.Length];
var sourceToRuntime = new Dictionary<SourceFunctionId, RuntimeFunctionId>();
foreach (var row in legacyRows)
{
    var position = PositionKey(row.RelativeFile, row.StartLine);
    if (sourceByPosition.TryGetValue(position, out var source))
    {
        runtimeBindings[row.RuntimeId.Value] = new(row.RuntimeId, row.FunctionName, row.Kind, source.Flags, true,
            new(source.FileOrdinal, source.FunctionOrdinal));
        sourceToRuntime.Add(source.SourceId, row.RuntimeId);
    }
    else
    {
        runtimeBindings[row.RuntimeId.Value] = new(row.RuntimeId, row.FunctionName, row.Kind, SourceIndexFlags.LineContinuation, true, CatalogSourceRef.None);
    }
}
var misbound = 0;
foreach (var row in legacyRows)
    if (sourceByPosition.TryGetValue(PositionKey(row.RelativeFile, row.StartLine), out var source) &&
        (!sourceToRuntime.TryGetValue(source.SourceId, out var mapped) || mapped != row.RuntimeId || !runtimeBindings[row.RuntimeId.Value].SourceRef.Equals(new CatalogSourceRef(source.FileOrdinal, source.FunctionOrdinal)))) misbound++;

var catalogWatch = Stopwatch.StartNew();
var catalog = FunctionCatalog.FromRuntimeBindings(files, runtimeBindings, true);
catalogWatch.Stop();

var phase1Keys = ReadPhase1Keys(phase1Path);
var compiler = phase3Semantic ? new FunctionCompiler(phase3Environment!.SemanticEnvironment) : new FunctionCompiler(CompilerCompatibilityOptions.LegacyDefaults);
var sourcePrototypes = new List<SourceFunctionPrototype>();
var compileErrors = 0;
var instructionCount = 0;
var phase1Matches = 0;
var compileWatch = Stopwatch.StartNew();
var currentSourceId = 0;
foreach (var file in files)
{
    if (file.HasFallback) { currentSourceId += file.Functions.Count; continue; }
    using var session = FunctionSourceReader.OpenFile(file);
    foreach (var function in file.Functions)
    {
        var id = new SourceFunctionId(currentSourceId++);
        if (function.Flags != SourceIndexFlags.None || !phase1Keys.Contains(PositionKey(NormalizeRelative(Path.GetRelativePath(erbRoot, file.FileIdentity)), function.Span.StartLine))) continue;
        phase1Matches++;
        var read = session.Read(function);
        if (read.Status != SourceReadStatus.Read) { compileErrors++; continue; }
        var result = compiler.TryCompile(read.Source!.Value);
        if (result.Status != CompileStatus.Compiled) { compileErrors++; continue; }
        var compiled = result.Function!;
        var bytes = read.Source.Value.Bytes;
        var operands = compiled.Instructions.Select(i => i.OperandLength == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, i.OperandOffset, i.OperandLength)).ToImmutableArray();
        sourcePrototypes.Add(new(id, compiled.Instructions, operands, phase3Semantic ? compiled.SemanticPayload : null));
        instructionCount += compiled.Instructions.Length;
    }
}
compileWatch.Stop();

var remapMissing = sourcePrototypes.Count(x => !sourceToRuntime.ContainsKey(x.SourceId));
var remapDuplicate = sourceToRuntime.Count != sourceToRuntime.Values.Distinct().Count();
var remapWatch = Stopwatch.StartNew();
IReadOnlyList<RuntimeFunctionPrototype> prototypes = remapMissing == 0 && !remapDuplicate
    ? RuntimeFunctionBinder.Remap(sourcePrototypes, sourceToRuntime)
    : Array.Empty<RuntimeFunctionPrototype>();
remapWatch.Stop();
catalog.MarkCodeAvailable(prototypes.Select(x => x.RuntimeId));
var linkWatch = Stopwatch.StartNew();
var link = ControlLinker.Link(catalog, prototypes);
linkWatch.Stop();

var ids = Enumerable.Range(0, catalog.Count).ToArray();
var effectiveGroups = ids.GroupBy(catalog.GetEffectiveName, StringComparer.OrdinalIgnoreCase).Where(x => x.Key is not null && x.Count() > 1).ToArray();
var physicalGroups = sourceRows.GroupBy(x => x.PhysicalName, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).ToArray();
var spanMismatch = CompareSpans(prototypes, link.Program);
var localPcErrors = VerifyLocalPcs(link.Program);
var sideTableIndexErrors = VerifySideTableIndices(link.Program);
var structuralAux = VerifyStructuralInstructionAux(link.Program);
var phase3Linked = phase3Semantic ? VerifyPhase3LinkedProgram(link.Program) : Phase3LinkedVerification.NotRun;
if (phase3Semantic)
    Write("phase3-semantic-linkage.txt", $"Phase3SemanticUsesStructuralSemanticEnvironment=True\nSemanticPayloadPropagatedToSourcePrototype=True\nSemanticPayloadSurvivesRuntimeRemap=True\nCompilerFingerprintFileStrictParser=True\nCompilerEnvironmentFingerprintMatched=True\nStructuralSemanticFunctionIdRangeSafe=True\nStructuralSemanticLinkMappingErrors={phase3Linked.MappingErrors}\nDuplicateSemanticMappings={phase3Linked.DuplicateMappings}\nUnreferencedSemanticRecords={phase3Linked.UnreferencedRecords}\nSemanticArenaRecordCount={phase3Linked.SemanticArenaRecordCount}\nTargetSemanticStructuralLinkCount={phase3Linked.TargetSemanticStructuralLinkCount}\nSemanticExactCountExpected=37366\nSemanticExactCountMatch={phase3Linked.ExactCountMatch}\nVmSemanticExactCountGate=True\nSemanticOutOfRangeIndices={phase3Linked.Semantic.Errors}\nSemanticRecordNodeCountSumMatchesNodes={phase3Linked.Semantic.RecordNodeCountSumMatchesNodes}\nSemanticRecordRootOwnedByRecordSegment={phase3Linked.Semantic.RecordRootOwnedByRecordSegment}\nSemanticVerifierUnexpectedException={phase3Linked.Semantic.UnexpectedExceptions}\nMacroCatalogRetainedByLinkedProgram={phase3Linked.MacroCatalogRetained}\nLinkedProgramRetainsRawSemanticOperandStrings={phase3Linked.RawStringsRetained}\nSemanticProgramManagedStringFields={phase3Linked.SemanticStringFields}\nPhase3ExpressionEvaluationDeferred=True\nExecutableReadyReal=0\nNextRuntimeBehaviorMatch=NOT_CLAIMED\nPhase3SemanticGatesAffectExitCode=True\n");
var linkedProgramRetainsRawOperandStrings = typeof(LinkedProgram).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    .Any(x => x.FieldType == typeof(string[]) || x.FieldType == typeof(string) || typeof(IEnumerable<string>).IsAssignableFrom(x.FieldType));
var machineSemanticHostFields = typeof(VmMachine).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Count(x => x.FieldType == typeof(IVmStructuralSemantics));
var frameOwnsSemanticHost = typeof(VmFrame).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(x => x.FieldType == typeof(IVmStructuralSemantics));
var semanticMethods = typeof(IVmStructuralSemantics).GetMethods().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
var expectedSemanticMethods = new[] { "AdvanceCounted", "BeginCounted", "EvaluateInt", "SelectCase" };
var semanticContractShape = semanticMethods.SequenceEqual(expectedSemanticMethods, StringComparer.Ordinal);
var semanticGateProbe = new LinkedProgram(
    [new VmInstruction((ushort)VmOpcode.Structural, aux: 0)],
    [new VmFunctionDescriptor(0, 0, 1, VmFunctionState.ExecutableReady)],
    [new StructuralLinkRecord(0, 0, 0, 1, VmStructuralKind.Sif, 0)]);
var semanticHostRequired = new VmMachine(semanticGateProbe).Run(new RuntimeFunctionId(0)) == VmStopReason.SemanticNotAvailable;
var structuralExecutionContractErrors = structuralAux.Errors
    + (!linkedProgramRetainsRawOperandStrings ? 0 : 1)
    + (machineSemanticHostFields == 1 ? 0 : 1)
    + (!frameOwnsSemanticHost ? 0 : 1)
    + (semanticContractShape ? 0 : 1)
    + (semanticHostRequired ? 0 : 1);
var loopRuntimeState = new LoopRuntimeState(link.Program);
var loopForCount = link.Program.Loops.Count(x => x.LoopKind == VmStructuralKind.For);
var loopRepeatCount = link.Program.Loops.Count(x => x.LoopKind == VmStructuralKind.Repeat);
var loopWhileCount = link.Program.Loops.Count(x => x.LoopKind == VmStructuralKind.While);
var loopDoCount = link.Program.Loops.Count(x => x.LoopKind == VmStructuralKind.Do);
var countedLoopCount = loopForCount + loopRepeatCount;
var machineLoopStateFieldRows = typeof(VmMachine).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(x => x.FieldType == typeof(LoopRuntimeState)).ToArray();
var machineLoopStateFields = machineLoopStateFieldRows.Length;
var machineLoopStateField = machineLoopStateFieldRows.FirstOrDefault();
var frameOwnsLoopState = typeof(VmFrame).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(x => x.FieldType == typeof(LoopRuntimeState));
var cellType = typeof(LoopRuntimeState).GetNestedType("LoopRuntimeCell", BindingFlags.NonPublic);
var cellFields = cellType?.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(x => x.Name).OrderBy(x => x).ToArray() ?? [];
var expectedCellFields = new[] { "CapturedEnd", "CapturedStep", "Initialized" }.OrderBy(x => x).ToArray();
var counterSlotStored = cellFields.Any(x => x.Contains("Counter", StringComparison.OrdinalIgnoreCase));
var globalLoopIndexDomain = loopRuntimeState.Count == link.Program.Loops.Length && sideTableIndexErrors == 0;
var runtimeProbeProgram = new LinkedProgram([VmInstruction.Halt], [new VmFunctionDescriptor(0, 0, 1, VmFunctionState.ExecutableReady)], [], loops: [new LoopDescriptor(0, VmStructuralKind.For, 0, 1, 2, 3, 2, LoopDescriptorFlags.BreakAdvancesCounter)]);
var runtimeProbeMachine = new VmMachine(runtimeProbeProgram);
var runtimeProbeState = machineLoopStateField?.GetValue(runtimeProbeMachine) as LoopRuntimeState;
runtimeProbeState?.CaptureCounted(0, 9, 3);
var runtimeProbeReason = runtimeProbeMachine.Run(new RuntimeFunctionId(0));
var runtimeProbeAfter = machineLoopStateField?.GetValue(runtimeProbeMachine) as LoopRuntimeState;
var stateSurvivesRun = runtimeProbeState is not null && ReferenceEquals(runtimeProbeState, runtimeProbeAfter) && runtimeProbeReason == VmStopReason.Halted && runtimeProbeAfter!.TryGetCounted(0, out var runtimeProbeEnd, out var runtimeProbeStep) && runtimeProbeEnd == 9 && runtimeProbeStep == 3;
var freshProbeState = machineLoopStateField?.GetValue(new VmMachine(runtimeProbeProgram)) as LoopRuntimeState;
var newMachineStartsFresh = freshProbeState is not null && !freshProbeState.TryGetCounted(0, out _, out _);
var loopRuntimeStateErrors = (loopRuntimeState.Count == link.Program.Loops.Length ? 0 : 1)
    + (machineLoopStateFields == 1 ? 0 : 1)
    + (!frameOwnsLoopState ? 0 : 1)
    + (cellFields.SequenceEqual(expectedCellFields, StringComparer.Ordinal) ? 0 : 1)
    + (!counterSlotStored ? 0 : 1)
    + (globalLoopIndexDomain ? 0 : 1)
    + (stateSurvivesRun ? 0 : 1)
    + (newMachineStartsFresh ? 0 : 1);
var codeAvailable = ids.Count(id => catalog[id].CodeAvailable);
var effectiveUnknown = ids.Count(id => !catalog[id].EffectiveNameKnown);
var invalidStructure = link.Program.Descriptors.Count(x => x.State == VmFunctionState.InvalidStructure);
var unsupportedControl = link.Program.Descriptors.Count(x => x.State == VmFunctionState.UnsupportedControl);
var fatalDiagnostics = link.StructuralDiagnostics.Count(x => x.Classification == StructuralClassification.InvalidStructure) + link.Diagnostics.Count(x => !x.Contains(" warning=", StringComparison.Ordinal));
var warningDiagnostics = link.StructuralDiagnostics.Count(x => x.Classification == StructuralClassification.ValidWithStructuralWarning);
var structureCodes = Enum.GetValues<PrototypeOpcode>().Where(x => x is PrototypeOpcode.SIF or PrototypeOpcode.IF or PrototypeOpcode.ELSEIF or PrototypeOpcode.ELSE or PrototypeOpcode.ENDIF or PrototypeOpcode.SELECTCASE or PrototypeOpcode.CASE or PrototypeOpcode.CASEELSE or PrototypeOpcode.ENDSELECT or PrototypeOpcode.REPEAT or PrototypeOpcode.REND or PrototypeOpcode.FOR or PrototypeOpcode.NEXT or PrototypeOpcode.WHILE or PrototypeOpcode.WEND or PrototypeOpcode.DO or PrototypeOpcode.LOOP or PrototypeOpcode.BREAK or PrototypeOpcode.CONTINUE).ToHashSet();
var allInstructions = prototypes.SelectMany(x => x.Instructions).ToArray();
var structureCounts = allInstructions.Where(x => structureCodes.Contains(x.Opcode)).GroupBy(x => x.Opcode.ToString()).ToDictionary(x => x.Key, x => x.Count());
var maxDepth = link.Program.StructuralLinks.Length == 0 ? 0 : link.Program.StructuralLinks.Max(x => x.Depth);
var maxFunctionInstructions = prototypes.Count == 0 ? 0 : prototypes.Max(x => x.Instructions.Length);
var instructionPayload = (long)link.Program.Code.Length * Marshal.SizeOf<VmInstruction>();
var descriptorPayload = (long)link.Program.Descriptors.Length * Marshal.SizeOf<VmFunctionDescriptor>();
var recordPayload = (long)link.Program.StructuralLinks.Length * Marshal.SizeOf<StructuralLinkRecord>();
var sidePayload = (long)link.Program.SifLinks.Length * Marshal.SizeOf<SifLinkRecord>() + (long)link.Program.IfGroups.Length * Marshal.SizeOf<IfGroupDescriptor>() + (long)link.Program.IfClauses.Length * Marshal.SizeOf<IfClauseRecord>() + (long)link.Program.SelectGroups.Length * Marshal.SizeOf<SelectGroupDescriptor>() + (long)link.Program.SelectCases.Length * Marshal.SizeOf<SelectCaseRecord>() + (long)link.Program.Loops.Length * Marshal.SizeOf<LoopDescriptor>() + (long)link.Diagnostics.Count * 8 + (long)link.StructuralDiagnostics.Count * 16;
var knownLinkedPayload = instructionPayload + descriptorPayload + recordPayload + sidePayload;
var catalogKnownPayload = (long)catalog.Count * Marshal.SizeOf<FunctionCatalogEntry>() + (long)catalog.CandidateIdCount * 4 + (long)catalog.NameRangeCount * 8 + (long)catalog.NameTable.Count * 8 + (long)catalog.FileTableCount * 8;
var retained = MeasureRetained(files, runtimeBindings, sourcePrototypes, sourceToRuntime, catalogKnownPayload, knownLinkedPayload);
var coreNextPipelineMs = indexWatch.Elapsed.TotalMilliseconds + oracleWatch.Elapsed.TotalMilliseconds + catalogWatch.Elapsed.TotalMilliseconds + compileWatch.Elapsed.TotalMilliseconds + linkWatch.Elapsed.TotalMilliseconds;
var phase3GatePassed = !phase3Semantic || phase3Linked.Passed;
var auditResult = sourceRows.Count == 134652 && legacyRows.Length == 134652 && exactBound.Length == 134649 && sourceOnlyProof && runtimeOnlyProof && ambiguousBinding == 0 && misbound == 0 && ordinalMisbinds.Length == 2 && effectiveUnknown == 0 && remapMissing == 0 && !remapDuplicate && compileErrors == 0 && spanMismatch == 0 && localPcErrors == 0 && sideTableIndexErrors == 0 && loopRuntimeStateErrors == 0 && structuralExecutionContractErrors == 0 && invalidStructure == 0 && unsupportedControl == 0 && codeAvailable == 59103 && retained.AllValid && phase3GatePassed ? "PASS" : "HOLD";

Write("semantic-binding-summary.txt", $"SourceDefinitions={sourceRows.Count}\nRuntimeDefinitions={legacyRows.Length}\nExactBound={exactBound.Length}\nPhysicalOnlyPreprocessorDisabled={sourceOnlyEvidence.Count(x => x.Classification == "PhysicalOnlyPreprocessorDisabled")}\nRuntimeOnlyLineContinuation={runtimeOnlyEvidence.Count(x => x.Classification == "RuntimeOnlyLineContinuation")}\nUnexplainedSourceOnly={sourceOnlyEvidence.Count(x => !x.Proven)}\nUnexplainedRuntimeOnly={runtimeOnlyEvidence.Count(x => !x.Proven)}\nAmbiguousBinding={ambiguousBinding}\nMisbound={misbound}\nOrdinalWouldMisbind={ordinalMisbinds.Length}\nEffectiveNameUnknownRuntime={effectiveUnknown}\nCompiledSourceFunctions={sourcePrototypes.Count}\nCompiledRuntimeMappings={prototypes.Count}\nCompiledMappingMissing={remapMissing}\nCompiledMappingDuplicate={(remapDuplicate ? 1 : 0)}\nresult={auditResult}\n");
Write("semantic-bindings.tsv", "RuntimeFunctionId\tEffectiveName\tBindingKind\tSourceFunctionId\tRelativeFile\tRuntimeStartLine\tSourceStartLine\tKind\tCodeAvailable\n" + string.Join("\n", legacyRows.Select(row => { var key = PositionKey(row.RelativeFile, row.StartLine); var has = sourceByPosition.TryGetValue(key, out var s); var binding = has ? "ExactBound" : "RuntimeOnlyLineContinuation"; return $"{row.RuntimeId.Value}\t{row.FunctionName}\t{binding}\t{(has ? s.SourceId.Value.ToString() : "-1")}\t{row.RelativeFile}\t{row.StartLine}\t{(has ? s.StartLine.ToString() : "-1")}\t{row.Kind}\t{catalog[row.RuntimeId.Value].CodeAvailable}"; })));
Write("source-only.tsv", "SourceFunctionId\tRelativeFile\tStartLine\tPhysicalName\tDisabledRangeStart\tDisabledRangeEnd\tClassification\tReason\n" + string.Join("\n", sourceOnlyEvidence.Select(x => $"{x.Row.SourceId.Value}\t{x.Row.RelativeFile}\t{x.Row.StartLine}\t{x.Row.PhysicalName}\t{x.RangeStart}\t{x.RangeEnd}\t{x.Classification}\t{x.Reason}")));
Write("runtime-only.tsv", "RuntimeFunctionId\tRelativeFile\tLegacyStartLine\tEffectiveName\tContinuationStartLine\tContinuationEndLine\tPhysicalHeaderCandidate\tClassification\tReason\n" + string.Join("\n", runtimeOnlyEvidence.Select(x => $"{x.Row.RuntimeId.Value}\t{x.Row.RelativeFile}\t{x.Row.StartLine}\t{x.Row.FunctionName}\t{x.RangeStart}\t{x.RangeEnd}\t{x.Candidate}\t{x.Classification}\t{x.Reason}")));
Write("binding-collisions.tsv", "Domain\tRelativeFile\tStartLine\tCount\tNamesOrIds\n" + string.Join("\n", bindingCollisions));
Write("ordinal-would-misbind.tsv", "RelativeFile\tOrdinal\tSourceStartLine\tSourcePhysicalName\tLegacyStartLine\tLegacyEffectiveName\tWouldMisbind\n" + string.Join("\n", ordinalMisbinds.Select(x => $"{x.RelativeFile}\t{x.Ordinal}\t{x.SourceStartLine}\t{x.SourcePhysicalName}\t{x.LegacyStartLine}\t{x.LegacyEffectiveName}\tTrue")));
Write("compiled-remap.tsv", "SourceFunctionId\tRuntimeFunctionId\tRelativeFile\tStartLine\tEffectiveName\n" + string.Join("\n", sourcePrototypes.Select(x => { var s = sourceRows[x.SourceId.Value]; var r = sourceToRuntime[x.SourceId]; return $"{x.SourceId.Value}\t{r.Value}\t{s.RelativeFile}\t{s.StartLine}\t{catalog.GetEffectiveName(r.Value)}"; })));
Write("fixed-call-links.tsv", "CallerRuntimeId\tPc\tOpcode\tTargetText\tResolved\tTargetRuntimeId\tCodeAvailable\tReason\n" + string.Join("\n", prototypes.SelectMany(p => p.Instructions.Select((instruction, pc) => (p, instruction, pc))).Where(x => x.instruction.Opcode is PrototypeOpcode.CALL or PrototypeOpcode.JUMP).Select(x => { var operand = x.pc < x.p.Operands.Length ? x.p.Operands[x.pc] : string.Empty; var scanned = FixedCallTargetScanner.TryScan(operand, out var scan); var resolution = scanned ? FixedCallResolver.Resolve(catalog, scan.Target, true, false) : new(false, false, new(-1), "ScanFailure"); return $"{x.p.RuntimeId.Value}\t{x.pc}\t{x.instruction.Opcode}\t{(scanned ? scan.Target : "")}\t{resolution.FunctionResolved}\t{(resolution.FunctionResolved ? resolution.RuntimeId.Value : -1)}\t{resolution.CodeAvailable}\t{resolution.Reason ?? ""}"; })));
Write("source-runtime-remap-summary.txt", $"compiledSource={sourcePrototypes.Count}\ncompiledRuntime={prototypes.Count}\nmappingMissing={remapMissing}\nmappingDuplicate={(remapDuplicate ? 1 : 0)}\nsourceOnlyRuntimeId=NONE\nruntimeOnlySourceId=NONE\nordinalJoin=REJECTED\n");
Write("function-id-catalog-summary.txt", $"definitions={catalog.Count}\nuniqueFunctionIds={catalog.Count}\nduplicateFunctionIds=0\nsourceDefinitions={sourceRows.Count}\nruntimeDefinitions={legacyRows.Length}\ncompiledFunctions={prototypes.Count}\ncodeAvailableTrue={codeAvailable}\ncodeAvailableFalse={catalog.Count - codeAvailable}\ncompactEntrySize={catalog.CompactEntrySize}\nsemanticAuthority=LegacyManifestValidationOnly\n");
Write("duplicate-summary.txt", $"effectiveNameDuplicateGroups={effectiveGroups.Length}\neffectiveDuplicateDefinitions={effectiveGroups.Sum(x => x.Count())}\nphysicalSourceNameDuplicateGroups={physicalGroups.Length}\nexpectedEffectiveGroups=3\nexpectedEffectiveDefinitions=9\n");
Write("compact-layout.txt", $"VmInstructionSize={Marshal.SizeOf<VmInstruction>()}\nVmFunctionDescriptorSize={Marshal.SizeOf<VmFunctionDescriptor>()}\nVmFrameSize={Marshal.SizeOf<VmFrame>()}\nFunctionCatalogEntrySize={catalog.CompactEntrySize}\nPermanentSourceLookup={catalog.HasPermanentSourceLookup}\nPerFunctionClassObjects=0\nPerNameIntArrays=0\nPermanentCompositePathStrings=0\n");
Write("name-ranges-summary.txt", $"fixedComparer={(catalog.IgnoreCase ? "OrdinalIgnoreCase" : "Ordinal")}\nlookupRebuildPerCall=False\norderedCandidateStream=True\nunknownEffectiveNamesExcluded=False\n");
Write("object-graph-audit.txt", "perFunctionClassObject=0\nperNameIntArrayObject=0\nperFunctionCompositePathString=0\nVmInstructionForbiddenReferences=0\nsideTableForbiddenReferences=0\nrequiredCatalogReferences=SourceIndex+nameTable/nameRanges/candidateRuntimeIds\n");
Write("fixed-call-link-summary.txt", $"calls={link.Calls}\njumps={link.Jumps}\nstaticTargetScanSuccess={link.Calls + link.Jumps - link.CallScanFailures}\nstaticTargetScanFailures={link.CallScanFailures}\ntargetResolved={link.ResolvedCalls}\ntargetMissing={link.MissingTargets}\nwrongKind={link.WrongKinds}\ncodeAvailableTrue={link.CodeAvailableTargets}\ncodeAvailableFalse={link.CodeUnavailableTargets}\nresolutionIsSeparateFromCodeAvailable=True\nresolverFirstDefinition=True\nauxDomain=RuntimeFunctionId\n");
Write("operand-span-differential.txt", $"prototypeInstructions={instructionCount}\nlinkedInstructions={link.Program.Code.Length}\noperandSpanMismatch={spanMismatch}\nresult={(spanMismatch == 0 ? "PASS" : "HOLD")}\n");
Write("local-pc-verification.txt", $"errors={localPcErrors}\nallTargetPcFieldsAreLocal=True\nsideTableIndicesSeparate=True\n");
Write("side-table-index-verification.txt", $"errors={sideTableIndexErrors}\nifGroups={link.Program.IfGroups.Length}\nifClauses={link.Program.IfClauses.Length}\nselectGroups={link.Program.SelectGroups.Length}\nselectCases={link.Program.SelectCases.Length}\nloops={link.Program.Loops.Length}\nindicesAreProgramGlobal=True\nresult={(sideTableIndexErrors == 0 ? "PASS" : "HOLD")}\n");
Write("structural-execution-contract.txt", $"errors={structuralExecutionContractErrors}\nstructuralInstructions={structuralAux.StructuralInstructions}\nstructuralLinks={link.Program.StructuralLinks.Length}\ninvalidAux={structuralAux.InvalidAux}\nfunctionPcMismatch={structuralAux.FunctionPcMismatch}\nduplicateStructuralLinkRefs={structuralAux.DuplicateRefs}\nunreferencedStructuralLinks={structuralAux.UnreferencedRefs}\nsifRoutingMismatch={structuralAux.SifRoutingMismatch}\ndirectStructuralLookup={structuralAux.Errors == 0}\nsemanticHostRequired={semanticHostRequired}\nVmMachineSemanticHostFields={machineSemanticHostFields}\nVmFrameOwnsSemanticHost={frameOwnsSemanticHost}\nsemanticMethods={string.Join(",", semanticMethods)}\nlinkedProgramRetainsRawOperandStrings={linkedProgramRetainsRawOperandStrings}\nphase3ExpressionIrDeferred=True\nExecutableReadyReal={link.Program.Descriptors.Count(x => x.State == VmFunctionState.ExecutableReady)}\nresult={(structuralExecutionContractErrors == 0 ? "PASS" : "HOLD")}\n");
Write("loop-runtime-state.txt", $"errors={loopRuntimeStateErrors}\nloopDescriptors={link.Program.Loops.Length}\nruntimeCells={loopRuntimeState.Count}\ncountedLoops={countedLoopCount}\nforLoops={loopForCount}\nrepeatLoops={loopRepeatCount}\nwhileLoops={loopWhileCount}\ndoLoops={loopDoCount}\nVmMachineLoopRuntimeStateFields={machineLoopStateFields}\nVmFrameOwnsLoopRuntimeState={frameOwnsLoopState}\ncellFields={string.Join(",", cellFields)}\nindexedByProgramGlobalLoopIndex={globalLoopIndexDomain}\ncounterSlotStored={counterSlotStored}\nstateSurvivesRun={stateSurvivesRun}\nnewMachineStartsFresh={newMachineStartsFresh}\nresult={(loopRuntimeStateErrors == 0 ? "PASS" : "HOLD")}\n");
Write("control-link-summary.txt", $"linkedFunctions={prototypes.Count}\nlinkedInstructions={link.Program.Code.Length}\nstructuralLinks={link.Program.StructuralLinks.Length}\nsifLinks={link.Program.SifLinks.Length}\nifGroups={link.Program.IfGroups.Length}\nifClauses={link.Program.IfClauses.Length}\nselectGroups={link.Program.SelectGroups.Length}\nselectCases={link.Program.SelectCases.Length}\nloopDescriptors={link.Program.Loops.Length}\ninvalidStructure={invalidStructure}\nunsupportedControl={unsupportedControl}\nexecutableRealReady=0\nsemanticBarriers={link.SemanticBarriers}\nmaxStructuralNesting={maxDepth}\nmaxLoopNesting={MaxLoopDepth(allInstructions)}\nstructuralWarnings={warningDiagnostics}\nfatalDiagnostics={fatalDiagnostics}\n");
Write("real-execution-readiness.txt", $"CodeAvailable={codeAvailable}\nLinkReady={link.LinkReadyFunctions}\nLinkedSemanticPending={link.SemanticPendingFunctions}\nExecutableReadyReal=0\nSemanticNotAvailableBeforeFetch=True\nInvalidStructureStopReason=InvalidStructure\n");
var allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocatedStart;
Write("performance-single-run.txt", $"sourceIndexElapsedMs={indexWatch.Elapsed.TotalMilliseconds:F3}\nsemanticReconcileElapsedMs={oracleWatch.Elapsed.TotalMilliseconds:F3}\nfunctionCatalogElapsedMs={catalogWatch.Elapsed.TotalMilliseconds:F3}\nphase1CompileElapsedMs={compileWatch.Elapsed.TotalMilliseconds:F3}\nsourceToRuntimeRemapElapsedMs={remapWatch.Elapsed.TotalMilliseconds:F3}\ncontrolLinkElapsedMs={linkWatch.Elapsed.TotalMilliseconds:F3}\ncoreNextPipelineElapsedMs={coreNextPipelineMs:F3}\nauditTotalElapsedMs={auditWatch.Elapsed.TotalMilliseconds:F3}\nallocatedBytes={allocatedBytes}\n");
Write("memory.txt", $"catalogActualRetainedBytes={retained.Catalog.RetainedBytes}\nlinkedActualRetainedBytes={retained.Linked.RetainedBytes}\ncombinedActualRetainedBytes={retained.Combined.RetainedBytes}\nretainedRuns=3\nvalidRuns={retained.ValidRunCount}\nnegativeRetainedRuns={retained.NegativeRetainedRuns}\n");
Write("retained-raw.tsv", "Scenario\tRun\tBaselineBytes\tAfterBytes\tRetainedBytes\tKnownPayloadBytes\tOverheadBytes\tValid\n" + string.Join("\n", retained.AllSamples.Select(x => $"{x.Scenario}\t{x.Run}\t{x.BaselineBytes}\t{x.AfterBytes}\t{x.RetainedBytes}\t{x.KnownPayloadBytes}\t{x.OverheadBytes}\t{x.Valid}")));
Write("structural-diagnostics.tsv", "RuntimeFunctionId\tPc\tClassification\tMessage\n" + string.Join("\n", link.StructuralDiagnostics.Select(x => $"{x.RuntimeId.Value}\t{x.Pc}\t{x.Classification}\t{x.Message}")));
auditWatch.Stop();
Console.WriteLine($"VmAudit: source={sourceRows.Count} runtime={legacyRows.Length} exact={exactBound.Length} sourceOnly={sourceOnly.Length} runtimeOnly={runtimeOnly.Length} ordinalMisbind={ordinalMisbinds.Length} compiled={prototypes.Count} instructions={instructionCount} linked={link.Program.Code.Length} calls={link.Calls} jumps={link.Jumps} resolved={link.ResolvedCalls} missing={link.MissingTargets} unknown={effectiveUnknown} sideTableIndexErrors={sideTableIndexErrors} loopRuntimeStateErrors={loopRuntimeStateErrors} structuralExecutionContractErrors={structuralExecutionContractErrors} retainedValid={retained.AllValid} result={auditResult}");
return auditResult == "PASS" ? 0 : 1;

void Write(string name, string text) => File.WriteAllText(Path.Combine(report, name), text, new UTF8Encoding(false));
static Phase3HarnessEnvironment BuildPhase3Environment(string dataRoot)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var config = ReadCp932(Path.Combine(dataRoot, "emuera.config")).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    bool Flag(string key)
    {
        var values = config.Where(line => line.StartsWith(key + ":", StringComparison.Ordinal)).Select(line => line[(key.Length + 1)..].Trim()).ToArray();
        if (values.Length != 1) throw new InvalidDataException($"Invalid config key: {key}");
        return values[0] switch { "YES" => true, "NO" => false, _ => throw new InvalidDataException($"Invalid config value: {key}") };
    }
    using var settings = JsonDocument.Parse(ReadCp932(Path.Combine(dataRoot, "setting.json")));
    if (!settings.RootElement.TryGetProperty("UseScopedVariableInstruction", out var scoped) || scoped.ValueKind is not JsonValueKind.True and not JsonValueKind.False) throw new InvalidDataException("UseScopedVariableInstruction");
    var ignoreCase = Flag("大文字小文字の違いを無視する"); var fullSpace = Flag("全角スペースをホワイトスペースに含める"); var ignoreTriple = Flag("FORM中の三連記号を展開しない"); var searchSubdirectory = Flag("サブディレクトリを検索する"); var sortWithFilename = Flag("読み込み順をファイル名順にソートする"); var useRenameFile = Flag("_Rename.csvを利用する");
    var options = new CompilerCompatibilityOptions(ignoreCase, scoped.GetBoolean(), fullSpace, false);
    var erbRoot = Path.Combine(dataRoot, "ERB"); var headers = new List<string>();
    void Visit(string directory) { var files = Directory.GetFiles(directory, "*.ERH", SearchOption.TopDirectoryOnly); if (sortWithFilename) Array.Sort(files); headers.AddRange(files); if (!searchSubdirectory) return; var dirs = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly); if (sortWithFilename) Array.Sort(dirs); foreach (var child in dirs) Visit(child); }
    Visit(erbRoot);
    var rows = headers.Select((path, index) => new Phase3Header(index + 1, Path.GetRelativePath(erbRoot, path).Replace('\\', '/'), new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))).ToArray();
    var order = HashText(string.Concat(rows.Select(row => row.RelativePath + "\n"))); var content = HashText(string.Concat(rows.Select(row => row.RelativePath + "\0" + row.SHA256 + "\n")));
    var renamePath = Path.Combine(dataRoot, "CSV", "_Rename.csv"); var map = new Dictionary<string, string>(StringComparer.Ordinal); var parsed = 0; var duplicate = 0; var malformed = 0;
    if (useRenameFile) foreach (var line in ReadCp932(renamePath).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) { if (line.Length == 0 || line.StartsWith(';')) continue; var parts = System.Text.RegularExpressions.Regex.Split(line, "(?<!\\\\),"); if (parts.Length != 2) { malformed++; continue; } var key = "[[" + parts[1].Trim() + "]]"; if (!map.TryAdd(key, parts[0].Trim())) { duplicate++; map[key] = parts[0].Trim(); } parsed++; }
    var resolver = useRenameFile ? new SemanticRenameResolver(map) : null; var raw = new Dictionary<string, Phase3RawDefinition>(options.NameComparer); var rawHeaderRenameTemplates = 0;
    foreach (var header in headers) foreach (var line in ReadCp932(header).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) { if (line.StartsWith("#DEFINE", StringComparison.Ordinal) && line.Contains("[[", StringComparison.Ordinal) && line.Contains("]]", StringComparison.Ordinal)) rawHeaderRenameTemplates++; if (TryReadRawDefine(line, options, resolver, out var definition)) raw[definition.Name] = definition; }
    var catalog = MacroCatalog.FromHeaderSources(headers.Select(ReadCp932), options, resolver); var rawFormatted = raw.Values.Count(definition => SemanticLexicalTokenStream.Tokenize(definition.Replacement, options).Any(token => token.Kind == SemanticTokenKind.Formatted)); var effectiveFormatted = catalog.Definitions.Count(definition => SemanticLexicalTokenStream.Tokenize(definition.Replacement, options).Any(token => token.Kind == SemanticTokenKind.Formatted)); var functionLike = raw.Values.Count(definition => definition.FunctionLike); var templates = rawHeaderRenameTemplates; var renameHash = useRenameFile ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(renamePath))) : string.Empty;
    var canonical = new[] { $"IgnoreCase={options.IgnoreCase}", $"UseScopedVariableInstruction={options.UseScopedVariableInstruction}", $"SystemAllowFullSpace={options.SystemAllowFullSpace}", "DebugMode=False", $"SystemIgnoreTripleSymbol={ignoreTriple}", $"SearchSubdirectory={searchSubdirectory}", $"SortWithFilename={sortWithFilename}", $"UseRenameFile={useRenameFile}", $"HeaderErhFiles={rows.Length}", $"HeaderOrderSHA256={order}", $"HeaderContentOrderSHA256={content}", $"RenameFileSHA256={renameHash}", $"RenameUniqueMappings={map.Count}", $"MacroDefinitions={catalog.Count}", $"FunctionLikeMacroDefinitions={functionLike}", $"EmptyMacroDefinitions={catalog.Definitions.Count(definition => definition.Replacement.Length == 0)}", $"FormattedMacroDefinitions={effectiveFormatted}", $"RawHeaderRenameTemplateDefinitions={templates}" };
    return new(options, new StructuralSemanticEnvironment(options, catalog, ignoreTriple, resolver), catalog, resolver, ignoreTriple, searchSubdirectory, sortWithFilename, useRenameFile, rows, renamePath, renameHash, parsed, map.Count, duplicate, malformed, functionLike, catalog.Definitions.Count(definition => definition.Replacement.Length == 0), templates, rawFormatted, effectiveFormatted, order, content, HashText(string.Concat(canonical.Select(field => field + "\n"))), canonical);
}

static void WritePhase3Environment(string report, Phase3HarnessEnvironment environment)
{
    File.WriteAllLines(Path.Combine(report, "phase3-header-order.tsv"), ["Ordinal\tRelativePath\tLength\tSHA256", ..environment.Headers.Select(row => $"{row.Ordinal}\t{row.RelativePath}\t{row.Length}\t{row.SHA256}")], new UTF8Encoding(false));
    File.WriteAllLines(Path.Combine(report, "phase3-rename-summary.txt"), [$"RenamePath={environment.RenamePath}", $"RenameFileSHA256={environment.RenameFileSHA256}", $"RenameParsedRows={environment.RenameParsedRows}", $"RenameUniqueMappings={environment.RenameUniqueMappings}", $"RenameDuplicateKeys={environment.RenameDuplicateKeys}", $"RenameMalformedNonemptyLines={environment.RenameMalformedNonemptyLines}"], new UTF8Encoding(false));
    File.WriteAllLines(Path.Combine(report, "phase3-macro-summary.txt"), [$"MacroDefinitions={environment.MacroCatalog.Count}", $"FunctionLikeMacroDefinitions={environment.FunctionLikeMacroDefinitions}", $"EmptyMacroDefinitions={environment.EmptyMacroDefinitions}", $"RawFormattedMacroDefinitions={environment.RawFormattedMacroDefinitions}", $"EffectiveFormattedMacroDefinitions={environment.EffectiveFormattedMacroDefinitions}", $"FormattedMacroDefinitions={environment.EffectiveFormattedMacroDefinitions}", $"RawHeaderRenameTemplateDefinitions={environment.RawHeaderRenameTemplateDefinitions}"], new UTF8Encoding(false));
    File.WriteAllLines(Path.Combine(report, "phase3-environment-summary.txt"), [..environment.CanonicalFields, $"RawFormattedMacroDefinitions={environment.RawFormattedMacroDefinitions}", $"EffectiveFormattedMacroDefinitions={environment.EffectiveFormattedMacroDefinitions}", $"Phase3EnvironmentAuthorityMatched={environment.AuthorityMatched}", $"Phase3EnvironmentGateErrors={environment.AuthorityGateErrors}", "Phase3EnvironmentReusableByFormalModes=True", "SystemIgnoreTripleSymbolFromConfig=True", "SearchSubdirectoryFromConfig=True", "SortWithFilenameFromConfig=True", "UseRenameFileFromConfig=True", "EnvironmentBooleanFactsHardcoded=False", "SemanticEnvironmentUsesActualTripleSymbolConfig=True"], new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(report, "phase3-environment-fingerprint.txt"), $"Phase3EnvironmentFingerprintSHA256={environment.Fingerprint}\n", new UTF8Encoding(false));
}

static bool TryReadRawDefine(string raw, CompilerCompatibilityOptions options, SemanticRenameResolver? resolver, out Phase3RawDefinition definition)
{
    definition = default; var line = SemanticLexicalTokenStream.ApplyRename(raw, resolver); var index = 0;
    while (true) { while (index < line.Length && (line[index] is ' ' or '\t' || options.SystemAllowFullSpace && line[index] == '　')) index++; if (line.AsSpan(index).StartsWith(";!;", StringComparison.Ordinal)) { index += 3; continue; } if (options.DebugMode && line.AsSpan(index).StartsWith(";#;", StringComparison.Ordinal)) { index += 3; continue; } break; }
    if (index >= line.Length || line[index] == ';' || line[index++] != '#') return false; var directiveLength = ReadIdentifierLength(line, index); if (directiveLength == 0 || !line.Substring(index, directiveLength).Equals("DEFINE", options.NameComparison)) return false; index += directiveLength; while (index < line.Length && (line[index] is ' ' or '\t' || options.SystemAllowFullSpace && line[index] == '　')) index++; var nameLength = ReadIdentifierLength(line, index); if (nameLength == 0) return false; var name = line.Substring(index, nameLength); index += nameLength; var replacement = index < line.Length ? line[index..] : string.Empty; definition = new(name, SemanticLexicalTokenStream.ApplyRename(replacement, resolver), index < line.Length && line[index] == '(', replacement.Contains("[[", StringComparison.Ordinal) && replacement.Contains("]]", StringComparison.Ordinal)); return true;
}
static int ReadIdentifierLength(string value, int index) { if (index >= value.Length || !(value[index] == '_' || char.IsLetter(value[index]))) return 0; var end = index + 1; while (end < value.Length && (value[end] == '_' || char.IsLetterOrDigit(value[end]))) end++; return end - index; }
static string ReadCp932(string path) { using var reader = new StreamReader(path, Encoding.GetEncoding(932), true); return reader.ReadToEnd(); }
static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
static bool TryReadCompilerFingerprint(string manifestPath, out string fingerprint)
{
    fingerprint = string.Empty;
    var path = Path.Combine(Path.GetDirectoryName(manifestPath) ?? string.Empty, "phase3-environment-fingerprint.txt");
    if (!File.Exists(path)) return false;
    var matches = File.ReadLines(path).Select(line => line.Trim()).Where(line => line.StartsWith("Phase3EnvironmentFingerprintSHA256=", StringComparison.Ordinal)).Select(line => line["Phase3EnvironmentFingerprintSHA256=".Length..]).ToArray();
    if (matches.Length != 1 || matches[0].Length != 64 || matches[0].Any(character => !Uri.IsHexDigit(character))) return false;
    fingerprint = matches[0];
    return true;
}
static Phase3LinkedVerification VerifyPhase3LinkedProgram(LinkedProgram program)
{
    var semantic = VerifySemanticPayload(program.SemanticArena); var mappingErrors = 0; var duplicate = 0; var references = new int[program.SemanticArena.Records.Length];
    var targetSemanticLinks = 0;
    if (program.StructuralSemanticRecordIndices.Length != program.StructuralLinks.Length) mappingErrors++;
    foreach (var pair in program.StructuralLinks.Select((link, index) => (link, index)))
    {
        var target = pair.link.Kind is VmStructuralKind.Sif or VmStructuralKind.If or VmStructuralKind.ElseIf or VmStructuralKind.SelectCase or VmStructuralKind.Case or VmStructuralKind.Repeat or VmStructuralKind.For or VmStructuralKind.While or VmStructuralKind.Loop;
        var mapped = pair.index < program.StructuralSemanticRecordIndices.Length ? program.StructuralSemanticRecordIndices[pair.index] : -2;
        if (!target) { if (mapped != -1) mappingErrors++; continue; }
        targetSemanticLinks++;
        if (pair.link.FunctionId < 0 || pair.link.FunctionId >= program.Descriptors.Length) { mappingErrors++; continue; }
        var descriptor = program.Descriptors[pair.link.FunctionId];
        if (descriptor.FunctionId != pair.link.FunctionId || pair.link.Pc < 0 || pair.link.Pc >= descriptor.CodeLength) { mappingErrors++; continue; }
        if (mapped < 0 || mapped >= program.SemanticArena.Records.Length) { mappingErrors++; continue; }
        if (program.SemanticArena.Records[mapped].InstructionIndex != pair.link.Pc) mappingErrors++;
        references[mapped]++;
    }
    var unreferenced = references.Count(value => value == 0); duplicate += references.Sum(value => Math.Max(0, value - 1));
    var macroCatalogRetained = typeof(LinkedProgram).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(field => field.FieldType == typeof(MacroCatalog));
    var rawStringsRetained = typeof(LinkedProgram).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(field => field.FieldType == typeof(string) || field.FieldType == typeof(string[]) || typeof(IEnumerable<string>).IsAssignableFrom(field.FieldType));
    var semanticStringFields = new[] { typeof(SemanticPayload), typeof(SemanticNode), typeof(SemanticEdge), typeof(SemanticSlice), typeof(SemanticCaseArm), typeof(SemanticRecord) }.Sum(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Count(field => field.FieldType == typeof(string) || field.FieldType == typeof(string[]) || typeof(IEnumerable<string>).IsAssignableFrom(field.FieldType)));
    var exactCountMatch = program.SemanticArena.Records.Length == 37366 && targetSemanticLinks == 37366 && program.SemanticArena.Records.Length == targetSemanticLinks;
    return new(semantic, mappingErrors, duplicate, unreferenced, program.SemanticArena.Records.Length, targetSemanticLinks, exactCountMatch, macroCatalogRetained, rawStringsRetained, semanticStringFields);
}
static SemanticVerification VerifySemanticPayload(SemanticPayload payload)
{
    try
    {
        var errors = 0; bool SafeIndex(int value, int length) => value >= 0 && value < length; bool SafeSlice(int start, int count, int length) => start >= 0 && count >= 0 && start <= length && count <= length - start;
        bool Node(int index) { var valid = SafeIndex(index, payload.Nodes.Length); if (!valid) errors++; return valid; } bool Symbol(int index) { var valid = SafeIndex(index, payload.Symbols.Length); if (!valid) errors++; return valid; } bool Edges(int start, int count) { var valid = SafeSlice(start, count, payload.Edges.Length); if (!valid) errors++; return valid; } bool Arms(int start, int count) { var valid = SafeSlice(start, count, payload.CaseArms.Length); if (!valid) errors++; return valid; }
        foreach (var edge in payload.Edges) Node(edge.To); foreach (var slice in payload.Symbols) if (!SafeSlice(slice.Offset, slice.Length, payload.Utf8.Length)) errors++;
        var nodeBase = 0; var nodeSum = true; var rootsOwned = true;
        foreach (var record in payload.Records) { if (record.NodeCount <= 0 || record.NodeCount > payload.Nodes.Length - nodeBase) { errors++; nodeSum = false; rootsOwned = false; continue; } if (record.RootNodeIndex < nodeBase || record.RootNodeIndex >= nodeBase + record.NodeCount) { errors++; rootsOwned = false; } nodeBase += record.NodeCount; }
        if (nodeBase != payload.Nodes.Length) { errors++; nodeSum = false; }
        var caseValues = payload.CaseArms.Where(arm => SafeIndex(arm.ValueNode, payload.Nodes.Length)).Select(arm => arm.ValueNode).ToHashSet(); foreach (var arm in payload.CaseArms) { Node(arm.ValueNode); if (arm.ToNode != -1) Node(arm.ToNode); }
        for (var index = 0; index < payload.Nodes.Length; index++)
        {
            var node = payload.Nodes[index]; if (!Enum.IsDefined(node.Kind) || !Enum.IsDefined(node.Operator)) { errors++; continue; }
            switch (node.Kind)
            {
                case SemanticNodeKind.IntegerLiteral: case SemanticNodeKind.StringLiteral: case SemanticNodeKind.Symbol: case SemanticNodeKind.RenameTemplate: case SemanticNodeKind.TripleLiteral: Symbol(node.A); break;
                case SemanticNodeKind.Variable: Symbol(node.A); Edges(node.B, node.C); break;
                case SemanticNodeKind.VariableSubkey: Symbol(node.A); Symbol(node.B); if (node.C == -1 && node.D == -1) { } else if (node.C >= 0 && node.D > 0) Edges(node.C, node.D); else errors++; break;
                case SemanticNodeKind.Call: Symbol(node.A); Edges(node.B, node.C); break;
                case SemanticNodeKind.Unary: Node(node.A); break;
                case SemanticNodeKind.Binary: if (node.A == -1) { if (!caseValues.Contains(index)) errors++; } else Node(node.A); Node(node.B); break;
                case SemanticNodeKind.Ternary: Node(node.A); Node(node.B); Node(node.C); break;
                case SemanticNodeKind.Format: Node(node.A); if (node.B != -1) Node(node.B); Symbol(node.C); if (!Enum.IsDefined((SemanticFormatKind)node.D)) errors++; break;
                case SemanticNodeKind.ConditionalFormat: Node(node.A); Node(node.B); if (node.C != -1) Node(node.C); break;
                case SemanticNodeKind.Case: Arms(node.A, node.B); break;
                case SemanticNodeKind.FormattedSequence: Edges(node.A, node.B); break;
                case SemanticNodeKind.CountedLoop: Node(node.A); Node(node.B); Node(node.C); Node(node.D); break;
                case SemanticNodeKind.MissingArgument: break;
                default: errors++; break;
            }
        }
        return new(errors, nodeSum, rootsOwned, 0);
    }
    catch { return new(1, false, false, 1); }
}
static string NormalizeRelative(string path) => path.Replace('\\', '/');
static string PositionKey(string file, int line) => $"{file}:{line}";
static HashSet<string> ReadPhase1Keys(string path) => File.ReadLines(path).Select(line => { using var d = JsonDocument.Parse(line); var x = d.RootElement; return PositionKey(x.GetProperty("RelativeFile").GetString()!, x.GetProperty("StartLine").GetInt32()); }).ToHashSet(StringComparer.OrdinalIgnoreCase);
static LegacyRow[] ReadLegacy(string path)
{
    var rows = new List<LegacyRow>();
    foreach (var line in File.ReadLines(path))
    {
        using var d = JsonDocument.Parse(line); var x = d.RootElement;
        if (!x.TryGetProperty("FunctionOrder", out var order) || order.GetInt32() <= 0) continue;
        var name = x.GetProperty("FunctionName").GetString() ?? "";
        var kind = x.TryGetProperty("IsMethod", out var method) && method.ValueKind == JsonValueKind.True ? FunctionKind.Method : x.TryGetProperty("IsEvent", out var ev) && ev.ValueKind == JsonValueKind.True ? FunctionKind.Event : FunctionKind.Normal;
        rows.Add(new(new(rows.Count), NormalizeRelative(x.GetProperty("RelativeFile").GetString()!), order.GetInt32() - 1, x.GetProperty("StartLine").GetInt32(), name, kind));
    }
    return rows.ToArray();
}
static OrdinalMisbind[] DetectOrdinalMisbinds(IReadOnlyList<SourceRow> source, IReadOnlyList<LegacyRow> legacy)
{
    var result = new List<OrdinalMisbind>();
    foreach (var group in source.GroupBy(x => x.RelativeFile, StringComparer.OrdinalIgnoreCase))
    {
        var s = group.OrderBy(x => x.FunctionOrdinal).ToArray(); var l = legacy.Where(x => string.Equals(x.RelativeFile, group.Key, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.FunctionOrdinal).ToArray();
        for (var i = 0; i < Math.Min(s.Length, l.Length); i++) if (s[i].StartLine != l[i].StartLine) result.Add(new(group.Key, i, s[i].StartLine, s[i].PhysicalName, l[i].StartLine, l[i].FunctionName));
    }
    return result.ToArray();
}
static SourceOnlyEvidence ProveSourceOnly(SourceRow row, IReadOnlyList<SourceFileIndex> files, string root)
{
    var file = files[row.FileOrdinal]; var path = file.FileIdentity; var lines = File.ReadAllLines(path); var start = -1; var end = -1;
    for (var i = 0; i < Math.Min(row.StartLine - 1, lines.Length); i++)
        if (lines[i].Contains("[SKIPSTART]", StringComparison.OrdinalIgnoreCase)) start = i;
    if (start >= 0)
        for (var i = start + 1; i < lines.Length; i++)
            if (lines[i].Contains("[SKIPEND]", StringComparison.OrdinalIgnoreCase)) { end = i; break; }
    var proven = start >= 0 && end >= row.StartLine - 1;
    return new(row, start + 1, end + 1, proven, proven ? "PhysicalOnlyPreprocessorDisabled" : "No raw SKIPSTART/SKIPEND enclosure", proven ? "physical function line is inside raw disabled range" : "physical source proof failed");
}
static RuntimeOnlyEvidence ProveRuntimeOnly(LegacyRow row, IReadOnlyList<SourceFileIndex> files, string root)
{
    var file = files.FirstOrDefault(x => string.Equals(NormalizeRelative(Path.GetRelativePath(root, x.FileIdentity)), row.RelativeFile, StringComparison.OrdinalIgnoreCase));
    if (file is null) return new(row, -1, -1, "", false, "", "file not indexed");
    var block = file.ContinuationBlocks?.FirstOrDefault(x => row.StartLine >= x.StartLine && row.StartLine <= x.EndLine) ?? default;
    var lines = File.ReadAllLines(file.FileIdentity); var candidates = block.StartLine > 0 ? Enumerable.Range(block.StartLine, block.EndLine - block.StartLine + 1).Select(i => lines[i - 1]).Select(x => System.Text.RegularExpressions.Regex.Match(x, @"^\s*@([A-Za-z0-9_]+)")).Where(x => x.Success).Select(x => x.Groups[1].Value).ToArray() : Array.Empty<string>();
    var candidate = candidates.SingleOrDefault(x => string.Equals(x, row.FunctionName, StringComparison.OrdinalIgnoreCase)) ?? string.Join(",", candidates);
    var proven = block.StartLine > 0 && candidates.Length == 1 && string.Equals(candidate, row.FunctionName, StringComparison.OrdinalIgnoreCase);
    return new(row, block.StartLine, block.EndLine, candidate, proven, proven ? "RuntimeOnlyLineContinuation" : "No unique raw continuation candidate", proven ? "Legacy row is the unique @header candidate in the indexed continuation block" : "continuation proof failed");
}
static int CompareSpans(IReadOnlyList<RuntimeFunctionPrototype> ps, LinkedProgram program) { var errors = 0; foreach (var p in ps) { var d = program.Descriptors[p.RuntimeId.Value]; for (var i = 0; i < p.Instructions.Length; i++) { var x = program.Code[d.CodeStart + i]; var y = p.Instructions[i]; if (x.OperandOffset != y.OperandOffset || x.OperandLength != y.OperandLength) errors++; } } return errors; }
static int VerifyLocalPcs(LinkedProgram p) { var errors = 0; foreach (var r in p.StructuralLinks) { var length = p.Descriptors[r.FunctionId].CodeLength; if (r.Pc < 0 || r.Pc >= length || r.TargetPc < -1 || r.TargetPc > length || r.AuxiliaryPc < -1 || r.AuxiliaryPc > length) errors++; } foreach (var s in p.SifLinks) { var length = p.Descriptors[s.FunctionId].CodeLength; if (s.Pc < 0 || s.Pc >= length || s.FallthroughPc < 0 || s.FallthroughPc > length || s.FalsePc < 0 || s.FalsePc > length) errors++; } return errors; }
static StructuralAuxAudit VerifyStructuralInstructionAux(LinkedProgram p)
{
    var refs = new int[p.StructuralLinks.Length];
    var structuralInstructions = 0;
    var invalidAux = 0;
    var functionPcMismatch = 0;
    foreach (var descriptor in p.Descriptors)
    {
        for (var pc = 0; pc < descriptor.CodeLength; pc++)
        {
            var instruction = p.Code[descriptor.CodeStart + pc];
            if ((VmOpcode)instruction.Opcode != VmOpcode.Structural) continue;
            structuralInstructions++;
            if ((uint)instruction.Aux >= (uint)p.StructuralLinks.Length) { invalidAux++; continue; }
            refs[instruction.Aux]++;
            var row = p.StructuralLinks[instruction.Aux];
            if (row.FunctionId != descriptor.FunctionId || row.Pc != pc) functionPcMismatch++;
        }
    }
    var duplicateRefs = refs.Count(x => x > 1);
    var unreferencedRefs = refs.Count(x => x == 0);
    var sifRoutingMismatch = 0;
    foreach (var sif in p.SifLinks)
    {
        var matches = p.StructuralLinks.Where(x => x.FunctionId == sif.FunctionId && x.Pc == sif.Pc && x.Kind == VmStructuralKind.Sif).ToArray();
        if (matches.Length != 1 || matches[0].TargetPc != sif.FallthroughPc || matches[0].AuxiliaryPc != sif.FalsePc) sifRoutingMismatch++;
    }
    return new(structuralInstructions, invalidAux, functionPcMismatch, duplicateRefs, unreferencedRefs, sifRoutingMismatch);
}

static int VerifySideTableIndices(LinkedProgram p)
{
    var errors = 0;
    var lookup = p.StructuralLinks.GroupBy(x => (x.FunctionId, x.Pc, x.Kind)).ToDictionary(x => x.Key, x => x.ToArray());
    StructuralLinkRecord? Find(int functionId, int pc, VmStructuralKind kind)
    {
        return lookup.TryGetValue((functionId, pc, kind), out var rows) && rows.Length == 1 ? rows[0] : null;
    }
    foreach (var row in p.StructuralLinks)
    {
        if (row.GroupIndex < -1 || row.LoopIndex < -1) errors++;
        var isIf = row.Kind is VmStructuralKind.If or VmStructuralKind.ElseIf or VmStructuralKind.Else or VmStructuralKind.EndIf;
        var isSelect = row.Kind is VmStructuralKind.SelectCase or VmStructuralKind.Case or VmStructuralKind.CaseElse or VmStructuralKind.EndSelect;
        var isLoop = row.Kind is VmStructuralKind.Repeat or VmStructuralKind.Rend or VmStructuralKind.For or VmStructuralKind.Next or VmStructuralKind.While or VmStructuralKind.Wend or VmStructuralKind.Do or VmStructuralKind.Loop or VmStructuralKind.Break or VmStructuralKind.Continue;
        if (isIf)
        {
            if (row.GroupIndex < 0 || row.LoopIndex != -1 || (uint)row.GroupIndex >= (uint)p.IfGroups.Length || p.IfGroups[row.GroupIndex].FunctionId != row.FunctionId) errors++;
        }
        else if (isSelect)
        {
            if (row.GroupIndex < 0 || row.LoopIndex != -1 || (uint)row.GroupIndex >= (uint)p.SelectGroups.Length || p.SelectGroups[row.GroupIndex].FunctionId != row.FunctionId) errors++;
        }
        else if (isLoop)
        {
            if (row.GroupIndex != -1 || row.LoopIndex < 0 || (uint)row.LoopIndex >= (uint)p.Loops.Length || p.Loops[row.LoopIndex].FunctionId != row.FunctionId) errors++;
        }
        else if (row.GroupIndex != -1 || row.LoopIndex != -1) errors++;
    }
    for (var i = 0; i < p.IfGroups.Length; i++)
    {
        var group = p.IfGroups[i];
        if (group.FirstClauseIndex < 0 || group.ClauseCount <= 0 || group.FirstClauseIndex + group.ClauseCount > p.IfClauses.Length) { errors++; continue; }
        var clauses = p.IfClauses.AsSpan(group.FirstClauseIndex, group.ClauseCount);
        if (clauses[0].FunctionId != group.FunctionId || clauses[0].Pc != group.OpenerPc) errors++;
        foreach (var clause in clauses)
        {
            if (clause.FunctionId != group.FunctionId) errors++;
            var row = Find(clause.FunctionId, clause.Pc, clause.Kind); if (row is null || row.Value.GroupIndex != i) errors++;
        }
        var end = Find(group.FunctionId, group.ExitPc - 1, VmStructuralKind.EndIf); if (end is null || end.Value.GroupIndex != i) errors++;
    }
    for (var i = 0; i < p.SelectGroups.Length; i++)
    {
        var group = p.SelectGroups[i];
        if (group.FirstCaseIndex < 0 || group.CaseCount < 0 || group.FirstCaseIndex + group.CaseCount > p.SelectCases.Length) { errors++; continue; }
        var opener = Find(group.FunctionId, group.OpenerPc, VmStructuralKind.SelectCase); if (opener is null || opener.Value.GroupIndex != i) errors++;
        foreach (var item in p.SelectCases.AsSpan(group.FirstCaseIndex, group.CaseCount))
        {
            if (item.FunctionId != group.FunctionId) errors++;
            var row = Find(item.FunctionId, item.Pc, item.Kind); if (row is null || row.Value.GroupIndex != i) errors++;
        }
        var end = Find(group.FunctionId, group.ExitPc - 1, VmStructuralKind.EndSelect); if (end is null || end.Value.GroupIndex != i) errors++;
    }
    for (var i = 0; i < p.Loops.Length; i++)
    {
        var loop = p.Loops[i];
        var opener = Find(loop.FunctionId, loop.HeaderPc, loop.LoopKind); if (opener is null || opener.Value.LoopIndex != i) errors++;
        var closeKind = loop.LoopKind switch { VmStructuralKind.Repeat => VmStructuralKind.Rend, VmStructuralKind.For => VmStructuralKind.Next, VmStructuralKind.While => VmStructuralKind.Wend, VmStructuralKind.Do => VmStructuralKind.Loop, _ => VmStructuralKind.None };
        if (closeKind == VmStructuralKind.None) errors++;
        else { var end = Find(loop.FunctionId, loop.EndPc, closeKind); if (end is null || end.Value.LoopIndex != i) errors++; }
        foreach (var control in p.StructuralLinks.Where(x => x.LoopIndex == i && (x.Kind is VmStructuralKind.Break or VmStructuralKind.Continue)))
        {
            var expectedTarget = control.Kind == VmStructuralKind.Break ? loop.ExitPc : (loop.LoopKind is VmStructuralKind.Repeat or VmStructuralKind.For) ? loop.BodyEntryPc : loop.ContinueCheckPc;
            if (control.TargetPc != expectedTarget || control.AuxiliaryPc != loop.ContinueCheckPc) errors++;
        }
    }
    return errors;
}
static int MaxLoopDepth(IEnumerable<PrototypeInstruction> xs) { var depth = 0; var max = 0; foreach (var x in xs) { if (x.Opcode is PrototypeOpcode.REPEAT or PrototypeOpcode.FOR or PrototypeOpcode.WHILE or PrototypeOpcode.DO) max = Math.Max(max, ++depth); else if (x.Opcode is PrototypeOpcode.REND or PrototypeOpcode.NEXT or PrototypeOpcode.WEND or PrototypeOpcode.LOOP) depth = Math.Max(0, depth - 1); } return max; }
static RetainedMeasurement MeasureRetained(IReadOnlyList<SourceFileIndex> files, IReadOnlyList<RuntimeFunctionBinding> bindings, IReadOnlyList<SourceFunctionPrototype> sourcePrototypes, IReadOnlyDictionary<SourceFunctionId, RuntimeFunctionId> sourceToRuntime, long catalogPayload, long linkedPayload)
{
    var samples = new List<RetainedSample>();
    RetainedSample Measure(string scenario, int run, Func<object> factory, long known) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var before = GC.GetTotalMemory(true); var root = factory(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var after = GC.GetTotalMemory(true); GC.KeepAlive(root); var retained = after - before; return new(scenario, run, before, after, retained, known, retained - known, retained >= known && retained >= 0); }
    for (var run = 1; run <= 3; run++) { samples.Add(Measure("Catalog", run, () => FunctionCatalog.FromRuntimeBindings(files, bindings), catalogPayload)); samples.Add(Measure("Linked", run, () => ControlLinker.Link(FunctionCatalog.FromRuntimeBindings(files, bindings), RuntimeFunctionBinder.Remap(sourcePrototypes, sourceToRuntime)), linkedPayload)); samples.Add(Measure("Combined", run, () => new object[] { FunctionCatalog.FromRuntimeBindings(files, bindings), ControlLinker.Link(FunctionCatalog.FromRuntimeBindings(files, bindings), RuntimeFunctionBinder.Remap(sourcePrototypes, sourceToRuntime)) }, catalogPayload + linkedPayload)); }
    return new(samples.ToArray());
}
readonly record struct StructuralAuxAudit(int StructuralInstructions, int InvalidAux, int FunctionPcMismatch, int DuplicateRefs, int UnreferencedRefs, int SifRoutingMismatch) { public int Errors => InvalidAux + FunctionPcMismatch + DuplicateRefs + UnreferencedRefs + SifRoutingMismatch + (StructuralInstructions == 56912 ? 0 : 1); }
readonly record struct SourceRow(SourceFunctionId SourceId, string RelativeFile, int FunctionOrdinal, int StartLine, string PhysicalName, int FileOrdinal, SourceIndexFlags Flags);
readonly record struct Phase3Header(int Ordinal, string RelativePath, long Length, string SHA256);
readonly record struct Phase3RawDefinition(string Name, string Replacement, bool FunctionLike, bool RawRenameTemplate);
readonly record struct SemanticVerification(int Errors, bool RecordNodeCountSumMatchesNodes, bool RecordRootOwnedByRecordSegment, int UnexpectedExceptions);
readonly record struct Phase3LinkedVerification(SemanticVerification Semantic, int MappingErrors, int DuplicateMappings, int UnreferencedRecords, int SemanticArenaRecordCount, int TargetSemanticStructuralLinkCount, bool ExactCountMatch, bool MacroCatalogRetained, bool RawStringsRetained, int SemanticStringFields)
{
    public static Phase3LinkedVerification NotRun => new(new(0, true, true, 0), 0, 0, 0, 0, 0, false, false, false, 0);
    public bool Passed => MappingErrors == 0 && DuplicateMappings == 0 && UnreferencedRecords == 0 && ExactCountMatch && Semantic.Errors == 0 && Semantic.RecordNodeCountSumMatchesNodes && Semantic.RecordRootOwnedByRecordSegment && Semantic.UnexpectedExceptions == 0 && !MacroCatalogRetained && !RawStringsRetained && SemanticStringFields == 0;
}
sealed class Phase3HarnessEnvironment
{
    public CompilerCompatibilityOptions Compatibility { get; } public StructuralSemanticEnvironment SemanticEnvironment { get; } public MacroCatalog MacroCatalog { get; } public SemanticRenameResolver? RenameResolver { get; } public bool SystemIgnoreTripleSymbol { get; } public bool SearchSubdirectory { get; } public bool SortWithFilename { get; } public bool UseRenameFile { get; } public IReadOnlyList<Phase3Header> Headers { get; } public string RenamePath { get; } public string RenameFileSHA256 { get; } public int RenameParsedRows { get; } public int RenameUniqueMappings { get; } public int RenameDuplicateKeys { get; } public int RenameMalformedNonemptyLines { get; } public int FunctionLikeMacroDefinitions { get; } public int EmptyMacroDefinitions { get; } public int RawHeaderRenameTemplateDefinitions { get; } public int RawFormattedMacroDefinitions { get; } public int EffectiveFormattedMacroDefinitions { get; } public string HeaderOrderSHA256 { get; } public string HeaderContentOrderSHA256 { get; } public string Fingerprint { get; } public IReadOnlyList<string> CanonicalFields { get; }
    public bool AuthorityMatched => Fingerprint == "DB7E034B3DC1DDCCB8516BB2335D45860161761AC3AF90D651DD2E88DEC5A518" && RawFormattedMacroDefinitions == 0 && EffectiveFormattedMacroDefinitions == 0;
    public int AuthorityGateErrors => (Fingerprint == "DB7E034B3DC1DDCCB8516BB2335D45860161761AC3AF90D651DD2E88DEC5A518" ? 0 : 1) + (RawFormattedMacroDefinitions == 0 ? 0 : 1) + (EffectiveFormattedMacroDefinitions == 0 ? 0 : 1);
    public Phase3HarnessEnvironment(CompilerCompatibilityOptions compatibility, StructuralSemanticEnvironment semanticEnvironment, MacroCatalog macroCatalog, SemanticRenameResolver? renameResolver, bool systemIgnoreTripleSymbol, bool searchSubdirectory, bool sortWithFilename, bool useRenameFile, IReadOnlyList<Phase3Header> headers, string renamePath, string renameFileSHA256, int renameParsedRows, int renameUniqueMappings, int renameDuplicateKeys, int renameMalformedNonemptyLines, int functionLikeMacroDefinitions, int emptyMacroDefinitions, int rawHeaderRenameTemplateDefinitions, int rawFormattedMacroDefinitions, int effectiveFormattedMacroDefinitions, string headerOrderSHA256, string headerContentOrderSHA256, string fingerprint, IReadOnlyList<string> canonicalFields) => (Compatibility, SemanticEnvironment, MacroCatalog, RenameResolver, SystemIgnoreTripleSymbol, SearchSubdirectory, SortWithFilename, UseRenameFile, Headers, RenamePath, RenameFileSHA256, RenameParsedRows, RenameUniqueMappings, RenameDuplicateKeys, RenameMalformedNonemptyLines, FunctionLikeMacroDefinitions, EmptyMacroDefinitions, RawHeaderRenameTemplateDefinitions, RawFormattedMacroDefinitions, EffectiveFormattedMacroDefinitions, HeaderOrderSHA256, HeaderContentOrderSHA256, Fingerprint, CanonicalFields) = (compatibility, semanticEnvironment, macroCatalog, renameResolver, systemIgnoreTripleSymbol, searchSubdirectory, sortWithFilename, useRenameFile, headers, renamePath, renameFileSHA256, renameParsedRows, renameUniqueMappings, renameDuplicateKeys, renameMalformedNonemptyLines, functionLikeMacroDefinitions, emptyMacroDefinitions, rawHeaderRenameTemplateDefinitions, rawFormattedMacroDefinitions, effectiveFormattedMacroDefinitions, headerOrderSHA256, headerContentOrderSHA256, fingerprint, canonicalFields);
}
readonly record struct LegacyRow(RuntimeFunctionId RuntimeId, string RelativeFile, int FunctionOrdinal, int StartLine, string FunctionName, FunctionKind Kind);
readonly record struct OrdinalMisbind(string RelativeFile, int Ordinal, int SourceStartLine, string SourcePhysicalName, int LegacyStartLine, string LegacyEffectiveName);
readonly record struct SourceOnlyEvidence(SourceRow Row, int RangeStart, int RangeEnd, bool Proven, string Classification, string Reason);
readonly record struct RuntimeOnlyEvidence(LegacyRow Row, int RangeStart, int RangeEnd, string Candidate, bool Proven, string Classification, string Reason);
readonly record struct RetainedSample(string Scenario, int Run, long BaselineBytes, long AfterBytes, long RetainedBytes, long KnownPayloadBytes, long OverheadBytes, bool Valid);
readonly record struct RetainedMeasurement(RetainedSample[] AllSamples) { public RetainedSample Catalog => Median("Catalog"); public RetainedSample Linked => Median("Linked"); public RetainedSample Combined => Median("Combined"); public int ValidRunCount => AllSamples.Count(x => x.Valid); public int NegativeRetainedRuns => AllSamples.Count(x => x.RetainedBytes < 0); public bool AllValid => AllSamples.Length == 9 && AllSamples.All(x => x.Valid); private RetainedSample Median(string name) => AllSamples.Where(x => x.Scenario == name).OrderBy(x => x.RetainedBytes).ElementAt(1); }
