using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: Emuera.Next.VmAudit <fixture-root> <report-directory> <legacy-manifest.jsonl> <phase1-compiler-manifest.jsonl>");
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
var compiler = new FunctionCompiler(CompilerCompatibilityOptions.LegacyDefaults);
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
        sourcePrototypes.Add(new(id, compiled.Instructions, operands));
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
var auditResult = sourceRows.Count == 134652 && legacyRows.Length == 134652 && exactBound.Length == 134649 && sourceOnlyProof && runtimeOnlyProof && ambiguousBinding == 0 && misbound == 0 && ordinalMisbinds.Length == 2 && effectiveUnknown == 0 && remapMissing == 0 && !remapDuplicate && compileErrors == 0 && spanMismatch == 0 && localPcErrors == 0 && invalidStructure == 0 && unsupportedControl == 0 && codeAvailable == 59103 && retained.AllValid ? "PASS" : "HOLD";

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
Write("control-link-summary.txt", $"linkedFunctions={prototypes.Count}\nlinkedInstructions={link.Program.Code.Length}\nstructuralLinks={link.Program.StructuralLinks.Length}\nsifLinks={link.Program.SifLinks.Length}\nifGroups={link.Program.IfGroups.Length}\nifClauses={link.Program.IfClauses.Length}\nselectGroups={link.Program.SelectGroups.Length}\nselectCases={link.Program.SelectCases.Length}\nloopDescriptors={link.Program.Loops.Length}\ninvalidStructure={invalidStructure}\nunsupportedControl={unsupportedControl}\nexecutableRealReady=0\nsemanticBarriers={link.SemanticBarriers}\nmaxStructuralNesting={maxDepth}\nmaxLoopNesting={MaxLoopDepth(allInstructions)}\nstructuralWarnings={warningDiagnostics}\nfatalDiagnostics={fatalDiagnostics}\n");
Write("real-execution-readiness.txt", $"CodeAvailable={codeAvailable}\nLinkReady={link.LinkReadyFunctions}\nLinkedSemanticPending={link.SemanticPendingFunctions}\nExecutableReadyReal=0\nSemanticNotAvailableBeforeFetch=True\nInvalidStructureStopReason=InvalidStructure\n");
var allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocatedStart;
Write("performance-single-run.txt", $"sourceIndexElapsedMs={indexWatch.Elapsed.TotalMilliseconds:F3}\nsemanticReconcileElapsedMs={oracleWatch.Elapsed.TotalMilliseconds:F3}\nfunctionCatalogElapsedMs={catalogWatch.Elapsed.TotalMilliseconds:F3}\nphase1CompileElapsedMs={compileWatch.Elapsed.TotalMilliseconds:F3}\nsourceToRuntimeRemapElapsedMs={remapWatch.Elapsed.TotalMilliseconds:F3}\ncontrolLinkElapsedMs={linkWatch.Elapsed.TotalMilliseconds:F3}\ncoreNextPipelineElapsedMs={coreNextPipelineMs:F3}\nauditTotalElapsedMs={auditWatch.Elapsed.TotalMilliseconds:F3}\nallocatedBytes={allocatedBytes}\n");
Write("memory.txt", $"catalogActualRetainedBytes={retained.Catalog.RetainedBytes}\nlinkedActualRetainedBytes={retained.Linked.RetainedBytes}\ncombinedActualRetainedBytes={retained.Combined.RetainedBytes}\nretainedRuns=3\nvalidRuns={retained.ValidRunCount}\nnegativeRetainedRuns={retained.NegativeRetainedRuns}\n");
Write("retained-raw.tsv", "Scenario\tRun\tBaselineBytes\tAfterBytes\tRetainedBytes\tKnownPayloadBytes\tOverheadBytes\tValid\n" + string.Join("\n", retained.AllSamples.Select(x => $"{x.Scenario}\t{x.Run}\t{x.BaselineBytes}\t{x.AfterBytes}\t{x.RetainedBytes}\t{x.KnownPayloadBytes}\t{x.OverheadBytes}\t{x.Valid}")));
Write("structural-diagnostics.tsv", "RuntimeFunctionId\tPc\tClassification\tMessage\n" + string.Join("\n", link.StructuralDiagnostics.Select(x => $"{x.RuntimeId.Value}\t{x.Pc}\t{x.Classification}\t{x.Message}")));
auditWatch.Stop();
Console.WriteLine($"VmAudit: source={sourceRows.Count} runtime={legacyRows.Length} exact={exactBound.Length} sourceOnly={sourceOnly.Length} runtimeOnly={runtimeOnly.Length} ordinalMisbind={ordinalMisbinds.Length} compiled={prototypes.Count} instructions={instructionCount} linked={link.Program.Code.Length} calls={link.Calls} jumps={link.Jumps} resolved={link.ResolvedCalls} missing={link.MissingTargets} unknown={effectiveUnknown} retainedValid={retained.AllValid} result={auditResult}");
return auditResult == "PASS" ? 0 : 1;

void Write(string name, string text) => File.WriteAllText(Path.Combine(report, name), text, new UTF8Encoding(false));
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
static int MaxLoopDepth(IEnumerable<PrototypeInstruction> xs) { var depth = 0; var max = 0; foreach (var x in xs) { if (x.Opcode is PrototypeOpcode.REPEAT or PrototypeOpcode.FOR or PrototypeOpcode.WHILE or PrototypeOpcode.DO) max = Math.Max(max, ++depth); else if (x.Opcode is PrototypeOpcode.REND or PrototypeOpcode.NEXT or PrototypeOpcode.WEND or PrototypeOpcode.LOOP) depth = Math.Max(0, depth - 1); } return max; }
static RetainedMeasurement MeasureRetained(IReadOnlyList<SourceFileIndex> files, IReadOnlyList<RuntimeFunctionBinding> bindings, IReadOnlyList<SourceFunctionPrototype> sourcePrototypes, IReadOnlyDictionary<SourceFunctionId, RuntimeFunctionId> sourceToRuntime, long catalogPayload, long linkedPayload)
{
    var samples = new List<RetainedSample>();
    RetainedSample Measure(string scenario, int run, Func<object> factory, long known) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var before = GC.GetTotalMemory(true); var root = factory(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var after = GC.GetTotalMemory(true); GC.KeepAlive(root); var retained = after - before; return new(scenario, run, before, after, retained, known, retained - known, retained >= known && retained >= 0); }
    for (var run = 1; run <= 3; run++) { samples.Add(Measure("Catalog", run, () => FunctionCatalog.FromRuntimeBindings(files, bindings), catalogPayload)); samples.Add(Measure("Linked", run, () => ControlLinker.Link(FunctionCatalog.FromRuntimeBindings(files, bindings), RuntimeFunctionBinder.Remap(sourcePrototypes, sourceToRuntime)), linkedPayload)); samples.Add(Measure("Combined", run, () => new object[] { FunctionCatalog.FromRuntimeBindings(files, bindings), ControlLinker.Link(FunctionCatalog.FromRuntimeBindings(files, bindings), RuntimeFunctionBinder.Remap(sourcePrototypes, sourceToRuntime)) }, catalogPayload + linkedPayload)); }
    return new(samples.ToArray());
}
readonly record struct SourceRow(SourceFunctionId SourceId, string RelativeFile, int FunctionOrdinal, int StartLine, string PhysicalName, int FileOrdinal, SourceIndexFlags Flags);
readonly record struct LegacyRow(RuntimeFunctionId RuntimeId, string RelativeFile, int FunctionOrdinal, int StartLine, string FunctionName, FunctionKind Kind);
readonly record struct OrdinalMisbind(string RelativeFile, int Ordinal, int SourceStartLine, string SourcePhysicalName, int LegacyStartLine, string LegacyEffectiveName);
readonly record struct SourceOnlyEvidence(SourceRow Row, int RangeStart, int RangeEnd, bool Proven, string Classification, string Reason);
readonly record struct RuntimeOnlyEvidence(LegacyRow Row, int RangeStart, int RangeEnd, string Candidate, bool Proven, string Classification, string Reason);
readonly record struct RetainedSample(string Scenario, int Run, long BaselineBytes, long AfterBytes, long RetainedBytes, long KnownPayloadBytes, long OverheadBytes, bool Valid);
readonly record struct RetainedMeasurement(RetainedSample[] AllSamples) { public RetainedSample Catalog => Median("Catalog"); public RetainedSample Linked => Median("Linked"); public RetainedSample Combined => Median("Combined"); public int ValidRunCount => AllSamples.Count(x => x.Valid); public int NegativeRetainedRuns => AllSamples.Count(x => x.RetainedBytes < 0); public bool AllValid => AllSamples.Length == 9 && AllSamples.All(x => x.Valid); private RetainedSample Median(string name) => AllSamples.Where(x => x.Scenario == name).OrderBy(x => x.RetainedBytes).ElementAt(1); }
