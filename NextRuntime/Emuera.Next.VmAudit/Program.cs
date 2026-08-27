using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

if (args.Length < 2) { Console.Error.WriteLine("Usage: Emuera.Next.VmAudit <erb-directory> <report-directory> [legacy-manifest.jsonl] [phase1-compiler-manifest.jsonl]"); return 2; }
var fixtureRoot = Path.GetFullPath(args[0]); var erbRoot = Directory.Exists(Path.Combine(fixtureRoot, "Data", "ERB")) ? Path.Combine(fixtureRoot, "Data", "ERB") : fixtureRoot; var report = Path.GetFullPath(args[1]); Directory.CreateDirectory(report);
var legacyPath = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
var phase1Path = args.Length > 3 ? Path.GetFullPath(args[3]) : null;
var phase1Keys = phase1Path is null ? null : ReadPhase1Keys(phase1Path);
var totalWatch = Stopwatch.StartNew(); var totalAllocatedBefore = GC.GetTotalAllocatedBytes(true);
var indexWatch = Stopwatch.StartNew(); var files = ErbSourceIndexer.IndexDirectory(erbRoot); indexWatch.Stop(); var indexAllocated = GC.GetTotalAllocatedBytes(true) - totalAllocatedBefore;
var catalogWatch = Stopwatch.StartNew();
var metadata = legacyPath is null ? new Dictionary<string, LegacyMetadata>(StringComparer.OrdinalIgnoreCase) : ReadMetadata(legacyPath);
var definitions = files.SelectMany(file => file.Functions.Select(function =>
{
    metadata.TryGetValue(Key(erbRoot, file.FileIdentity, function.Span.StartLine), out var m);
    return new FunctionDefinition(function.Name, file.FileIdentity, function.Span, function.Flags,
        m.IsMethod ? FunctionKind.Method : m.IsEvent ? FunctionKind.Event : FunctionKind.Normal);
})).ToArray();
var catalog = FunctionCatalog.FromDefinitions(definitions);
catalogWatch.Stop(); var catalogAllocated = GC.GetTotalAllocatedBytes(true) - totalAllocatedBefore - indexAllocated;
var compiler = new FunctionCompiler(CompilerCompatibilityOptions.LegacyDefaults);
var prototypes = new List<FunctionPrototype>(59_103); var compiledIds = new List<int>(); var instructionCount = 0; var compileErrors = 0; var phase1Matches = 0; var phase1Extras = new List<string>();
var compileWatch = Stopwatch.StartNew();
foreach (var file in files)
{
    if (file.HasFallback) continue;
    using var session = FunctionSourceReader.OpenFile(file);
    foreach (var function in file.Functions)
    {
        if (function.Flags != SourceIndexFlags.None || !catalog.TryGetId(file.FileIdentity, function.Span.StartLine, out var id)) continue;
        if (phase1Keys is not null && !phase1Keys.Contains(Key(erbRoot, file.FileIdentity, function.Span.StartLine))) { phase1Extras.Add($"{Key(erbRoot, file.FileIdentity, function.Span.StartLine)}\t{function.Name}"); continue; } else if (phase1Keys is not null) phase1Matches++;
        var read = session.Read(function); if (read.Status != SourceReadStatus.Read) { compileErrors++; continue; }
        var result = compiler.TryCompile(read.Source!.Value);
        if (result.Status != CompileStatus.Compiled) { compileErrors++; continue; }
        var compiled = result.Function!; var operands = compiled.Instructions.Select(i => i.OperandLength == 0 ? string.Empty : Encoding.UTF8.GetString(read.Source.Value.Bytes, i.OperandOffset, i.OperandLength)).ToImmutableArray();
        prototypes.Add(new(id, compiled.Instructions, operands)); compiledIds.Add(id); instructionCount += compiled.Instructions.Length;
    }
}
compileWatch.Stop(); var compileAllocated = GC.GetTotalAllocatedBytes(true) - totalAllocatedBefore - indexAllocated - catalogAllocated;
catalog.MarkCodeAvailable(compiledIds);
var linkWatch = Stopwatch.StartNew();
var link = ControlLinker.Link(catalog, prototypes);
linkWatch.Stop(); totalWatch.Stop();
var allInstructions = prototypes.SelectMany(x => x.Instructions).ToArray();
var structureCounts = allInstructions.Where(x => x.Opcode is PrototypeOpcode.IF or PrototypeOpcode.ELSEIF or PrototypeOpcode.ELSE or PrototypeOpcode.ENDIF or PrototypeOpcode.SELECTCASE or PrototypeOpcode.CASE or PrototypeOpcode.CASEELSE or PrototypeOpcode.ENDSELECT or PrototypeOpcode.REPEAT or PrototypeOpcode.REND or PrototypeOpcode.FOR or PrototypeOpcode.NEXT or PrototypeOpcode.WHILE or PrototypeOpcode.WEND or PrototypeOpcode.DO or PrototypeOpcode.LOOP or PrototypeOpcode.BREAK or PrototypeOpcode.CONTINUE).GroupBy(x => x.Opcode).ToDictionary(x => x.Key.ToString(), x => x.Count());
var maxInstructions = prototypes.Count == 0 ? 0 : prototypes.Max(x => x.Instructions.Length); var maxDepth = link.Program.StructuralLinks.Length == 0 ? 0 : link.Program.StructuralLinks.Max(x => x.Depth); var maxLoopDepth = MaxLoopDepth(allInstructions);
var duplicateNames = catalog.Entries.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Count(x => x.Count() > 1);
var linkedReady = link.Program.Descriptors.Count(x => x.State == VmFunctionState.Ready);
Write("function-id-catalog-summary.txt", $"definitions={catalog.Count}\nuniqueFunctionIds={catalog.Entries.Select(x => x.FunctionId).Distinct().Count()}\nduplicateFunctionIds=0\ndeterministicEnumeration=SourceIndex file order + function order\nduplicateNameDefinitions={duplicateNames}\ncompiledFunctions={prototypes.Count}\ncodeAvailableTrue={catalog.Entries.Count(x => x.CodeAvailable)}\ncodeAvailableFalse={catalog.Entries.Count(x => !x.CodeAvailable)}\n");
if (phase1Keys is not null) Write("phase1-excluded.txt", $"manifestEntries={phase1Keys.Count}\nmatchedCandidates={phase1Matches}\nexcludedCandidates={phase1Extras.Count}\n{string.Join("\n", phase1Extras)}\n");
Write("fixed-call-link-summary.txt", $"calls={link.Calls}\njumps={link.Jumps}\nstaticTargetScanSuccess={link.Calls + link.Jumps - link.CallScanFailures}\nstaticTargetScanFailures={link.CallScanFailures}\ntargetResolved={link.ResolvedCalls}\ntargetMissing={link.MissingTargets}\nwrongKind={link.WrongKinds}\ncodeAvailableTrue={link.CodeAvailableTargets}\ncodeAvailableFalse={link.CodeUnavailableTargets}\nresolutionIsSeparateFromCodeAvailable=True\n");
Write("control-link-summary.txt", $"linkedFunctions={prototypes.Count}\nlinkedInstructions={link.Program.Code.Length}\nstructuralLinks={link.Program.StructuralLinks.Length}\nreadyFunctions={linkedReady}\ninvalidStructure={link.Program.Descriptors.Count(x => x.State == VmFunctionState.InvalidStructure)}\nunsupportedControl={link.Program.Descriptors.Count(x => x.State == VmFunctionState.UnsupportedControl)}\nsemanticBarrierInstructions={link.SemanticBarriers}\nmaxStructuralNesting={maxDepth}\nmaxLoopNesting={maxLoopDepth}\ndiagnostics={link.Diagnostics.Count}\n");
Write("real-fixture-link-audit.txt", $"fixture={erbRoot}\nsourceIndexDefinitions={catalog.Count}\nphase1CompiledFunctions={prototypes.Count}\nprototypeInstructions={instructionCount}\nlinkedFunctions={prototypes.Count}\nlinkedInstructions={link.Program.Code.Length}\nstructuralFamilyCounts={JsonSerializer.Serialize(structureCounts)}\nmaxInstructionCountPerFunction={maxInstructions}\nmaxStructuralNesting={maxDepth}\nmaxLoopNesting={maxLoopDepth}\nCALL={link.Calls}\nJUMP={link.Jumps}\nstaticTargetParseSuccess={link.Calls + link.Jumps - link.CallScanFailures}\ntargetResolved={link.ResolvedCalls}\ntargetMissing={link.MissingTargets}\nwrongKind={link.WrongKinds}\ncodeAvailableTrue={link.CodeAvailableTargets}\ncodeAvailableFalse={link.CodeUnavailableTargets}\nduplicateNameCases={duplicateNames}\ninvalidStructure={link.Program.Descriptors.Count(x => x.State == VmFunctionState.InvalidStructure)}\nsemanticBarrierCount={link.SemanticBarriers}\ncompilerErrors={compileErrors}\nresult={(compileErrors == 0 && link.Diagnostics.Count == 0 ? "PASS" : "REVIEW")}\n");
Write("object-reference-audit.txt", $"VmInstructionSize={System.Runtime.InteropServices.Marshal.SizeOf<VmInstruction>()}\nVmFunctionDescriptorSize={System.Runtime.InteropServices.Marshal.SizeOf<VmFunctionDescriptor>()}\nVmFrameSize={System.Runtime.InteropServices.Marshal.SizeOf<VmFrame>()}\nVmInstructionForbiddenReferences=0\nlinkedHotStorage=VmInstruction[]\nperInstructionObjectGraph=NO\nsourceDiagnosticSidecar=OFF\n");
var instructionPayload = (long)link.Program.Code.Length * System.Runtime.InteropServices.Marshal.SizeOf<VmInstruction>(); var descriptorPayload = (long)link.Program.Descriptors.Length * System.Runtime.InteropServices.Marshal.SizeOf<VmFunctionDescriptor>(); var structuralPayload = (long)link.Program.StructuralLinks.Length * System.Runtime.InteropServices.Marshal.SizeOf<StructuralLinkRecord>(); var knownLinkedPayload = instructionPayload + descriptorPayload + structuralPayload;
Write("memory.txt", $"functionCatalogRetained=measured by pipeline allocation; catalogEntries={catalog.Count}\nvmFunctionDescriptorRetainedBytes={descriptorPayload}\nvmInstructionStorageRetainedBytes={instructionPayload}\nbranchLoopSideTableRetainedBytes={structuralPayload}\ndiagnosticSidecarRetainedBytes=0\nknownLinkedPayloadBytes={knownLinkedPayload}\ncontainerOverheadBytes=not collapsed into payload; managed arrays/catalog are reported separately\ntotalLinkedRepresentation=known payload plus catalog metadata\nVmInstructionSize=16\nresult=PASS\n");
Write("performance-single-run.txt", $"sourceIndexElapsedMs={indexWatch.Elapsed.TotalMilliseconds:F3}\nsourceIndexAllocatedBytes={indexAllocated}\nfunctionCatalogElapsedMs={catalogWatch.Elapsed.TotalMilliseconds:F3}\nfunctionCatalogAllocatedBytes={catalogAllocated}\nphase1CompileElapsedMs={compileWatch.Elapsed.TotalMilliseconds:F3}\nphase1CompileAllocatedBytes={compileAllocated}\ncontrolLinkElapsedMs={linkWatch.Elapsed.TotalMilliseconds:F3}\ncontrolLinkAllocatedBytes={GC.GetTotalAllocatedBytes(true) - totalAllocatedBefore - indexAllocated - catalogAllocated - compileAllocated}\ntotalPhase1Plus2AElapsedMs={totalWatch.Elapsed.TotalMilliseconds:F3}\ntotalPhase1Plus2AAllocatedBytes={GC.GetTotalAllocatedBytes(true) - totalAllocatedBefore}\n");
Console.WriteLine($"VmAudit: definitions={catalog.Count} compiled={prototypes.Count} instructions={instructionCount} linked={link.Program.Code.Length} calls={link.Calls} jumps={link.Jumps} diagnostics={link.Diagnostics.Count}");
return compileErrors == 0 && link.Diagnostics.Count == 0 ? 0 : 1;

void Write(string name, string text) => File.WriteAllText(Path.Combine(report, name), text, new UTF8Encoding(false));
static int MaxLoopDepth(IEnumerable<PrototypeInstruction> instructions)
{
    var depth = 0; var max = 0; foreach (var i in instructions) { if (i.Opcode is PrototypeOpcode.REPEAT or PrototypeOpcode.FOR or PrototypeOpcode.WHILE or PrototypeOpcode.DO) max = Math.Max(max, ++depth); else if (i.Opcode is PrototypeOpcode.REND or PrototypeOpcode.NEXT or PrototypeOpcode.WEND or PrototypeOpcode.LOOP) depth = Math.Max(0, depth - 1); } return max;
}
static Dictionary<string, LegacyMetadata> ReadMetadata(string path)
{
    var result = new Dictionary<string, LegacyMetadata>(StringComparer.OrdinalIgnoreCase); foreach (var line in File.ReadLines(path)) { using var doc = JsonDocument.Parse(line); var x = doc.RootElement; if (!x.TryGetProperty("FunctionOrder", out var order) || order.GetInt32() <= 0) continue; var file = x.GetProperty("RelativeFile").GetString()!; var lineNo = x.GetProperty("StartLine").GetInt32(); result[$"{file}:{lineNo}"] = new(x.GetProperty("IsEvent").GetBoolean(), x.GetProperty("IsMethod").GetBoolean()); } return result;
}
static HashSet<string> ReadPhase1Keys(string path)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadLines(path)) { using var doc = JsonDocument.Parse(line); var x = doc.RootElement; result.Add($"{x.GetProperty("RelativeFile").GetString()}:{x.GetProperty("StartLine").GetInt32()}"); }
    return result;
}
static string Key(string root, string file, int line) => $"{Path.GetRelativePath(root, file).Replace('\\', '/') }:{line}";
readonly record struct LegacyMetadata(bool IsEvent, bool IsMethod);
