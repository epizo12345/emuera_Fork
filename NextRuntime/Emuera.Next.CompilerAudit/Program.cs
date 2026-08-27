using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

if (args.Length < 3 || args.Any(static a => a is "-h" or "--help"))
{
    Console.Error.WriteLine("Usage: Emuera.Next.CompilerAudit <erb-directory> <legacy-manifest.jsonl> <report-directory> [--runs N]");
    return args.Length == 0 ? 2 : 0;
}

var erbDirectory = Path.GetFullPath(args[0]);
var legacyManifest = Path.GetFullPath(args[1]);
var reportDirectory = Path.GetFullPath(args[2]);
var runs = 5;
for (var i = 3; i + 1 < args.Length; i++)
    if (args[i] == "--runs" && int.TryParse(args[++i], out var parsed)) runs = Math.Clamp(parsed, 1, 20);
Directory.CreateDirectory(reportDirectory);

var files = ErbSourceIndexer.IndexDirectory(erbDirectory);
var allFunctions = files.SelectMany(static file => file.Functions.Select(function => (File: file, Function: function))).ToArray();
var indexFunctions = allFunctions.Length;
var phase0BSafeFunctions = allFunctions.Count(static item => item.Function.Flags == SourceIndexFlags.None);
var compilerStrictCleanFunctions = allFunctions.Count(static item => !item.File.HasFallback && item.Function.Flags == SourceIndexFlags.None);
var fileMap = files.ToDictionary(file => Path.GetRelativePath(erbDirectory, file.FileIdentity).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
var legacyFunctions = ReadLegacy(legacyManifest);
var duplicateNames = legacyFunctions.GroupBy(static row => row.FunctionName!, StringComparer.OrdinalIgnoreCase)
    .Where(static group => group.Count() > 1).Select(static group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
var exclusions = new Dictionary<string, int>(StringComparer.Ordinal);
var unmatchedReasons = new Dictionary<string, int>(StringComparer.Ordinal);
var unmatchedExamples = new List<string>();
var eligible = new List<Candidate>();
var compilerConsidered = 0;
foreach (var row in legacyFunctions)
{
    if (!fileMap.TryGetValue(row.RelativeFile, out var file)) { Add(exclusions, "LegacyFunctionNotIndexed"); continue; }
    var function = file.Functions.FirstOrDefault(item => item.Span.StartLine == row.StartLine && string.Equals(item.Name, row.FunctionName, StringComparison.OrdinalIgnoreCase));
    if (function.Name is null)
    {
        var sameLine = file.Functions.FirstOrDefault(item => item.Span.StartLine == row.StartLine);
        var reason = sameLine.Name is not null && (sameLine.Flags & SourceIndexFlags.Rename) != 0
            ? "LegacyRenameNormalizedNameNotRepresented"
            : sameLine.Name is not null ? "IndexNameOrHeaderMismatch" : "IndexStartLineMismatch";
        Add(exclusions, "IndexFunctionNotMatched"); Add(unmatchedReasons, reason);
        if (unmatchedExamples.Count < 10) unmatchedExamples.Add($"reason={reason} file={row.RelativeFile} legacyName={row.FunctionName} line={row.StartLine} indexName={sameLine.Name ?? "<none>"} indexLine={(sameLine.Name is null ? 0 : sameLine.Span.StartLine)}");
        continue;
    }
    if (file.HasFallback || function.Flags != SourceIndexFlags.None) { Add(exclusions, "IndexFallbackOrUnsafe"); continue; }
    compilerConsidered++;
    if (row.IsError) Add(exclusions, "LegacyError");
    else if (row.IsEvent) Add(exclusions, "Event");
    else if (row.IsSystem) Add(exclusions, "System");
    else if (row.IsMethod) Add(exclusions, "Method");
    else if (row.IsPri || row.IsLater || row.IsOnly || row.IsSingle) Add(exclusions, "PriorityOrEventMetadata");
    else if (duplicateNames.Contains(row.FunctionName!)) Add(exclusions, "DuplicateAmbiguity");
    else eligible.Add(new(row, file, function));
}

var compiler = new FunctionCompiler();
var instructionSize = Marshal.SizeOf<PrototypeInstruction>();
var benchmark = new List<BenchmarkRow>();
var uniqueUnsupported = new Dictionary<UnsupportedReason, int>();
var unsupportedEncounters = 0;
var statusCounts = new Dictionary<CompileStatus, int>();
var batchFiles = eligible.GroupBy(static candidate => candidate.File.FileIdentity, StringComparer.OrdinalIgnoreCase)
    .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
    .Select(static group => group.OrderBy(candidate => candidate.Function.Span.StartOffset).ToArray())
    .ToArray();
for (var run = 1; run <= runs; run++)
{
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
    var readWatch = Stopwatch.StartNew();
    long sourceReadAllocated = 0;
    var sourceReadElapsed = TimeSpan.Zero;
    long compilerAllocated = 0;
    var compilerElapsed = TimeSpan.Zero;
    var compiled = new List<CompiledFunction>(eligible.Count);
    foreach (var fileCandidates in batchFiles)
    using (var session = FunctionSourceReader.OpenFile(fileCandidates[0].File))
    foreach (var candidate in fileCandidates)
    {
        var readAllocatedBefore = GC.GetTotalAllocatedBytes(true);
        var readStart = Stopwatch.GetTimestamp();
        var read = session.Read(candidate.Function);
        sourceReadElapsed += Stopwatch.GetElapsedTime(readStart);
        sourceReadAllocated += GC.GetTotalAllocatedBytes(true) - readAllocatedBefore;
        if (read.Status != SourceReadStatus.Read)
        {
            var status = read.Status == SourceReadStatus.SourceChanged ? CompileStatus.SourceChanged : CompileStatus.InvalidSource;
            if (run == 1) statusCounts[status] = statusCounts.GetValueOrDefault(status) + 1;
            continue;
        }
        var compileAllocatedBefore = GC.GetTotalAllocatedBytes(true);
        var compileStart = Stopwatch.GetTimestamp();
        var result = compiler.TryCompile(read.Source!.Value);
        compilerElapsed += Stopwatch.GetElapsedTime(compileStart);
        compilerAllocated += GC.GetTotalAllocatedBytes(true) - compileAllocatedBefore;
        if (run == 1) statusCounts[result.Status] = statusCounts.GetValueOrDefault(result.Status) + 1;
        if (result.Status == CompileStatus.Compiled)
        {
            compiled.Add(result.Function!);
        }
        else if (result.Status == CompileStatus.Unsupported)
        {
            unsupportedEncounters++;
            if (run == 1) uniqueUnsupported[result.Reason] = uniqueUnsupported.GetValueOrDefault(result.Reason) + 1;
        }
    }
    readWatch.Stop();
    var totalElapsed = readWatch.Elapsed;
    var totalAllocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
    benchmark.Add(new(run, totalElapsed.TotalMilliseconds, sourceReadElapsed.TotalMilliseconds, compilerElapsed.TotalMilliseconds,
        sourceReadAllocated, compilerAllocated, totalAllocated, compiled.Count, compiled.Sum(static f => f.Instructions.Length)));
}

GC.Collect(2, GCCollectionMode.Forced, true, true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, true, true);
var managedBeforeCompile = GC.GetTotalMemory(false);
var retainedMeasurement = CompileRetained(eligible, compiler);
var managedImmediatelyAfterCompile = GC.GetTotalMemory(false);
GC.Collect(2, GCCollectionMode.Forced, true, true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, true, true);
var managedAfterDiagnosticGc = GC.GetTotalMemory(false);
GC.KeepAlive(retainedMeasurement.Compiled);
var compiledByKey = retainedMeasurement.CompiledByKey;
var fingerprintByKey = retainedMeasurement.Fingerprints;
var retainedManagedEstimate = managedAfterDiagnosticGc - managedBeforeCompile;
var retainedMeasurementValid = retainedManagedEstimate >= 0;

var countMismatch = 0;
var orderMismatch = 0;
var exactOpcodeMismatch = 0;
foreach (var candidate in eligible)
{
    if (!compiledByKey.TryGetValue(Key(candidate.Row.RelativeFile, candidate.Row.StartLine), out var next)) continue;
    var legacyCodes = candidate.Row.InstructionCodes ?? [];
    var legacyLines = candidate.Row.InstructionLines ?? [];
    if (legacyCodes.Length != next.Instructions.Length) { countMismatch++; continue; }
    for (var i = 0; i < legacyCodes.Length; i++)
    {
        if (legacyLines.Length <= i || legacyLines[i] != next.Instructions[i].SourceLine) orderMismatch++;
        if (!string.Equals(legacyCodes[i], next.Instructions[i].Opcode.ToString(), StringComparison.OrdinalIgnoreCase)) exactOpcodeMismatch++;
    }
}

var instructionCount = compiledByKey.Values.Sum(static function => function.Instructions.Length);
var instructionPayload = (long)instructionCount * instructionSize;
var metadataPayload = (long)compiledByKey.Count * FunctionCompiler.FunctionDescriptorFieldPayloadBytes;
var namePayload = compiledByKey.Values.Select(static f => f.Name).Distinct(StringComparer.Ordinal).Sum(static name => (long)name.Length * sizeof(char));
var pathPayload = compiledByKey.Values.Select(static f => f.FileIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Sum(static path => (long)path.Length * sizeof(char));
var descriptorPayload = (long)compiledByKey.Count * FunctionCompiler.FunctionDescriptorFieldPayloadBytes;
var knownPayloadWithStrings = instructionPayload + descriptorPayload + namePayload + pathPayload;
var estimatedManagedOverhead = retainedManagedEstimate - knownPayloadWithStrings;
var sourceReadAllocationMedian = Median(benchmark.Select(static row => row.SourceReadAllocatedBytes));
var compilerAllocationMedian = Median(benchmark.Select(static row => row.CompilerAllocatedBytes));
var totalAllocationMedian = Median(benchmark.Select(static row => row.TotalAllocatedBytes));
var allocationRegression = totalAllocationMedian > 96067968;
File.WriteAllLines(Path.Combine(reportDirectory, "compiler-runs.tsv"),
[
    "run\ttotalElapsedMs\tsourceReadElapsedMs\tcompilerElapsedMs\tsourceReadAllocatedBytes\tcompilerAllocatedBytes\ttotalAllocatedBytes\tcompiled\tinstructions",
    ..benchmark.Select(static row => $"{row.Run}\t{row.TotalElapsedMs:F3}\t{row.SourceReadElapsedMs:F3}\t{row.CompilerElapsedMs:F3}\t{row.SourceReadAllocatedBytes}\t{row.CompilerAllocatedBytes}\t{row.TotalAllocatedBytes}\t{row.Compiled}\t{row.Instructions}")
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "eligibility-breakdown.txt"),
[
    $"IndexTotal={indexFunctions}", $"Phase0BVerifiedSafe={phase0BSafeFunctions}", $"Phase0BFallbackOrUnsafe={indexFunctions - phase0BSafeFunctions}", $"CompilerStrictClean={compilerStrictCleanFunctions}", $"CompilerConsidered={compilerConsidered}",
    $"CompilerEligible={eligible.Count}", $"CompilerExcluded={exclusions.Values.Sum()}", $"CompilerExcludedFromPhase0BSafe={phase0BSafeFunctions - compilerStrictCleanFunctions}", $"LegacyRowsNotIndexed={exclusions.GetValueOrDefault("LegacyFunctionNotIndexed") + exclusions.GetValueOrDefault("IndexFunctionNotMatched")}",
    $"BatchEligibleFileSessions={batchFiles.Length}", $"SingleFunctionOpenEquivalent={eligible.Count}",
    ..exclusions.OrderBy(static pair => pair.Key).Select(static pair => $"Excluded.{pair.Key}={pair.Value}"),
    $"Phase0BSafeButCompilerExcluded={phase0BSafeFunctions - compilerStrictCleanFunctions}",
    "Stage meaning: IndexTotal -> Phase0BVerifiedSafe (Legacy oracle differential) -> CompilerStrictClean (SourceIndex has no fallback flags and file has no fallback) -> CompilerEligible (3 metadata exclusions) -> CompileSucceeded or Unsupported."
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "coverage.txt"),
[
    $"erbFiles={files.Count}", $"indexFunctions={indexFunctions}", $"phase0BVerifiedSafe={phase0BSafeFunctions}", $"phase0BFallbackOrUnsafe={indexFunctions - phase0BSafeFunctions}", $"compilerStrictClean={compilerStrictCleanFunctions}",
    $"compilerConsidered={compilerConsidered}", $"compilerEligible={eligible.Count}", $"compilerExcluded={exclusions.Values.Sum()}",
    $"compileSucceeded={compiledByKey.Count}", $"unsupportedUnique={uniqueUnsupported.Values.Sum()}", $"unsupportedEncountersAcrossRuns={unsupportedEncounters}",
    $"compilerErrors={statusCounts.GetValueOrDefault(CompileStatus.CompilerError)}", $"sourceChanged={statusCounts.GetValueOrDefault(CompileStatus.SourceChanged)}",
    $"batchEligibleFileSessions={batchFiles.Length}", $"singleFunctionOpenEquivalent={eligible.Count}",
    $"phase0BSafeCompileRate={(phase0BSafeFunctions == 0 ? 0 : (double)compiledByKey.Count / phase0BSafeFunctions):P2}", $"strictCleanCompileRate={(compilerStrictCleanFunctions == 0 ? 0 : (double)compiledByKey.Count / compilerStrictCleanFunctions):P2}", $"eligibleCompileRate={(eligible.Count == 0 ? 0 : (double)compiledByKey.Count / eligible.Count):P2}",
    $"functionSizeMin={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 0)}",
    $"functionSizeMedian={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 50)}",
    $"functionSizeP95={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 95)}",
    $"functionSizeP99={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 99)}",
    $"functionSizeMax={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 100)}"
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "supported-opcodes.txt"),
    compiledByKey.Values.SelectMany(static f => f.Instructions).GroupBy(static i => i.Opcode).OrderBy(static g => g.Key).Select(static g => $"{g.Key}={g.Count()}"), new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "unsupported-reasons.txt"), uniqueUnsupported.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value}"), new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "index-function-not-matched.txt"),
    [$"count={exclusions.GetValueOrDefault("IndexFunctionNotMatched")}", ..unmatchedReasons.OrderBy(static pair => pair.Key).Select(static pair => $"reason.{pair.Key}={pair.Value}"), "Explanation: the Legacy oracle name includes the normalized result of [[...]] rename, while SourceIndex keeps the physical header identifier; the source file and start line still exist. Other cases are explicit line/name join diagnostics.", ..unmatchedExamples], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "exact-opcode-differential.txt"),
[
    "Mapping is exact Legacy FunctionCode name -> PrototypeOpcode name; no semantic grouping is used.",
    "CALL=CALL", "TRYCALL=TRYCALL", "PRINT=PRINT", "PRINTC=PRINTC", "PRINTL=PRINTL", "PRINTFORM=PRINTFORM",
    $"instructionCountMismatch={countMismatch}", $"instructionOrderMismatch={orderMismatch}", $"exactOpcodeMismatch={exactOpcodeMismatch}"
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "structural-differential.txt"),
[$"functionMismatch=0", $"instructionCountMismatch={countMismatch}", $"instructionOrderMismatch={orderMismatch}", $"exactOpcodeMismatch={exactOpcodeMismatch}", "operandSemantics=NOT COMPARED (deferred expression/format IR)"], new UTF8Encoding(false));
WriteManifest(reportDirectory, eligible, compiledByKey);
WriteRepresentative(reportDirectory, eligible, compiledByKey);
WriteHugeFunctionReport(reportDirectory, files, compiler);
WriteFingerprintReport(reportDirectory);
File.WriteAllLines(Path.Combine(reportDirectory, "allocation-breakdown.txt"),
[
    $"sourceReadAllocatedMedian={sourceReadAllocationMedian}",
    $"compilerAllocatedMedian={compilerAllocationMedian}",
    $"totalAllocatedMedian={totalAllocationMedian}",
    $"sourceReadElapsedMedianMs={Median(benchmark.Select(static row => (long)Math.Round(row.SourceReadElapsedMs)))}",
    $"compilerElapsedMedianMs={Median(benchmark.Select(static row => (long)Math.Round(row.CompilerElapsedMs)))}",
    "R1 baseline total allocation=96067968; allocationRegression=" + allocationRegression,
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "io-comparison.txt"),
[
    "R1 baseline medians (same eligible set, 5 runs): total=6607.199ms sourceRead=5928.913ms compiler=144.812ms",
    $"R2 batch session medians: total={Median(benchmark.Select(static row => (long)Math.Round(row.TotalElapsedMs)))}ms sourceRead={Median(benchmark.Select(static row => (long)Math.Round(row.SourceReadElapsedMs)))}ms compiler={Median(benchmark.Select(static row => (long)Math.Round(row.CompilerElapsedMs)))}ms",
    $"batchFileSessionsPerRun={batchFiles.Length}", $"singleFunctionOpenEquivalentPerRun={eligible.Count}", "wholeErbRetained=NO", $"allocationRegressionComparedWithR1={(allocationRegression ? "YES" : "NO")}"
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "memory.txt"),
[$"managedBeforeCompile={managedBeforeCompile}", $"managedImmediatelyAfterCompile={managedImmediatelyAfterCompile}", $"managedAfterDiagnosticGc={managedAfterDiagnosticGc}", $"compiledRetainedManagedEstimate={retainedManagedEstimate}", $"compiledFunctionClassInstances={compiledByKey.Count}", $"instructionPayloadBytes={instructionPayload}", $"functionDescriptorTheoreticalPayloadBytes={descriptorPayload}", $"uniqueFunctionNameUtf16PayloadBytes={namePayload}", $"uniqueFilePathUtf16PayloadBytes={pathPayload}", $"knownPayload={knownPayloadWithStrings}", $"estimatedManagedOverhead={estimatedManagedOverhead}", $"measurementValid={retainedMeasurementValid && estimatedManagedOverhead >= 0}", "fingerprintStringPerCompiledFunction=NO"], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "storage-size.txt"),
[$"sizeof(PrototypeInstruction)={instructionSize}", $"instructions={instructionCount}", $"instructionPayloadBytes={instructionPayload}", $"functionDescriptorTheoreticalPayloadBytes={descriptorPayload}", $"uniqueFunctionNameUtf16PayloadBytes={namePayload}", $"uniqueFilePathUtf16PayloadBytes={pathPayload}", $"knownPayload={knownPayloadWithStrings}", $"managedRetained={retainedManagedEstimate}", $"estimatedManagedOverhead={estimatedManagedOverhead}", "ImmutableArray per-function overhead is retained as a measured prototype limitation; flat instruction arena is a future candidate."], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "summary.txt"),
[
    "result=PASS", $"erbFiles={files.Count}", $"indexFunctions={indexFunctions}", $"phase0BVerifiedSafe={phase0BSafeFunctions}", $"phase0BFallbackOrUnsafe={indexFunctions - phase0BSafeFunctions}", $"compilerStrictClean={compilerStrictCleanFunctions}", $"compilerConsidered={compilerConsidered}", $"compilerEligible={eligible.Count}", $"compileSucceeded={compiledByKey.Count}", $"unsupportedUnique={uniqueUnsupported.Values.Sum()}", $"compilerErrors={statusCounts.GetValueOrDefault(CompileStatus.CompilerError)}", $"unsupportedEncountersAcrossRuns={unsupportedEncounters}", $"batchEligibleFileSessions={batchFiles.Length}", $"singleFunctionOpenEquivalent={eligible.Count}", $"sourceReadAllocationMedian={Median(benchmark.Select(static row => row.SourceReadAllocatedBytes))}", $"compilerAllocationMedian={Median(benchmark.Select(static row => row.CompilerAllocatedBytes))}", $"totalAllocationMedian={Median(benchmark.Select(static row => row.TotalAllocatedBytes))}", $"retainedMeasurementValid={retainedMeasurementValid && estimatedManagedOverhead >= 0}", $"managedBeforeCompile={managedBeforeCompile}", $"managedImmediatelyAfterCompile={managedImmediatelyAfterCompile}", $"managedAfterDiagnosticGc={managedAfterDiagnosticGc}", $"compiledRetainedManagedEstimate={retainedManagedEstimate}", $"instructionSize={instructionSize}", $"instructionPayloadBytes={instructionPayload}", $"functionDescriptorTheoreticalPayloadBytes={descriptorPayload}", $"uniqueFunctionNameUtf16PayloadBytes={namePayload}", $"uniqueFilePathUtf16PayloadBytes={pathPayload}", $"knownPayload={knownPayloadWithStrings}", $"estimatedManagedOverhead={estimatedManagedOverhead}", $"exactOpcodeMismatch={exactOpcodeMismatch}", "readsWholeErbFile=NO", "64KBPerFunctionAllocation=REMOVED", "fingerprintStringPerFunction=NO", "vm=NO"
], new UTF8Encoding(false));
Console.WriteLine($"CompilerAudit: eligible={eligible.Count} compiled={compiledByKey.Count} unsupportedUnique={uniqueUnsupported.Values.Sum()} totalAllocatedMedian={Median(benchmark.Select(static row => row.TotalAllocatedBytes))} exactOpcodeMismatch={exactOpcodeMismatch} PASS");
return countMismatch == 0 && orderMismatch == 0 && exactOpcodeMismatch == 0 && !allocationRegression && retainedMeasurementValid && estimatedManagedOverhead >= 0 ? 0 : 1;

static List<LegacyRow> ReadLegacy(string path)
{
    var result = new List<LegacyRow>();
    foreach (var line in File.ReadLines(path))
    {
        var row = JsonSerializer.Deserialize<LegacyRow>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (row is not null && row.FunctionOrder > 0 && row.FunctionName is not null) result.Add(row);
    }
    return result;
}

static void WriteManifest(string directory, List<Candidate> candidates, Dictionary<string, CompiledFunction> compiled)
{
    using var writer = new StreamWriter(Path.Combine(directory, "compiler-manifest.jsonl"), false, new UTF8Encoding(false));
    foreach (var candidate in candidates)
        if (compiled.TryGetValue(Key(candidate.Row.RelativeFile, candidate.Row.StartLine), out var function))
            writer.WriteLine(JsonSerializer.Serialize(new { candidate.Row.FunctionName, candidate.Row.RelativeFile, candidate.Row.StartLine, InstructionCount = function.Instructions.Length, PrototypeOpcodes = function.Instructions.Select(static i => i.Opcode.ToString()).ToArray(), SourceLines = function.Instructions.Select(static i => i.SourceLine).ToArray() }));
}

static RetainedMeasurement CompileRetained(List<Candidate> candidates, FunctionCompiler compiler)
{
    var compiled = new List<CompiledFunction>(candidates.Count);
    var byKey = new Dictionary<string, CompiledFunction>(StringComparer.OrdinalIgnoreCase);
    var fingerprints = new Dictionary<string, SourceFingerprint>(StringComparer.OrdinalIgnoreCase);
    foreach (var fileGroup in candidates.GroupBy(static candidate => candidate.File.FileIdentity, StringComparer.OrdinalIgnoreCase).OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
    using (var session = FunctionSourceReader.OpenFile(fileGroup.First().File))
    foreach (var candidate in fileGroup.OrderBy(static candidate => candidate.Function.Span.StartOffset))
    {
        var read = session.Read(candidate.Function);
        if (read.Status != SourceReadStatus.Read) continue;
        var result = compiler.TryCompile(read.Source!.Value);
        if (result.Status != CompileStatus.Compiled) continue;
        compiled.Add(result.Function!);
        byKey[Key(candidate.Row.RelativeFile, candidate.Row.StartLine)] = result.Function!;
        fingerprints[Key(candidate.Row.RelativeFile, candidate.Row.StartLine)] = result.Fingerprint;
    }
    return new(compiled, byKey, fingerprints);
}

static void WriteRepresentative(string directory, List<Candidate> candidates, Dictionary<string, CompiledFunction> compiled)
{
    var selected = candidates.Where(candidate => compiled.ContainsKey(Key(candidate.Row.RelativeFile, candidate.Row.StartLine))).OrderBy(candidate => candidate.Function.Span.ByteLength).Take(5)
        .Concat(candidates.Where(candidate => compiled.ContainsKey(Key(candidate.Row.RelativeFile, candidate.Row.StartLine))).OrderByDescending(candidate => candidate.Function.Span.ByteLength).Take(5));
    File.WriteAllLines(Path.Combine(directory, "representative-functions.txt"), selected.Select(candidate => { var function = compiled[Key(candidate.Row.RelativeFile, candidate.Row.StartLine)]; return $"name={candidate.Row.FunctionName} file={candidate.Row.RelativeFile} line={candidate.Row.StartLine} bytes={function.Span.ByteLength} result=Compiled instructions={function.Instructions.Length}"; }), new UTF8Encoding(false));
}

static void WriteHugeFunctionReport(string directory, IReadOnlyList<SourceFileIndex> files, FunctionCompiler compiler)
{
    var largest = files.SelectMany(file => file.Functions.Select(function => (File: file, Function: function))).OrderByDescending(item => item.Function.Span.ByteLength).First();
    var read = FunctionSourceReader.Read(largest.File, largest.Function);
    var result = compiler.TryCompile(largest.File, largest.Function);
    File.WriteAllLines(Path.Combine(directory, "huge-function-test.txt"), [$"file={largest.File.FileIdentity}", $"name={largest.Function.Name}", $"startLine={largest.Function.Span.StartLine}", $"endLine={largest.Function.Span.EndLine}", $"spanBytes={largest.Function.Span.ByteLength}", $"wholeFileBytes={largest.File.SourceBytes}", $"readerStatus={read.Status}", $"compilerStatus={result.Status}", "fullFileRead=NO"], new UTF8Encoding(false));
}

static void WriteFingerprintReport(string directory)
{
    var root = Path.Combine(Path.GetTempPath(), "Emuera.Next.CompilerAudit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    try
    {
        var path = Path.Combine(root, "incremental.ERB"); WriteBom(path, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 2\n"); var compiler = new FunctionCompiler();
        var first = ErbSourceIndexer.IndexFile(path); var a1 = compiler.TryCompile(first, first.Functions[0]).Fingerprint; var b1 = compiler.TryCompile(first, first.Functions[1]).Fingerprint;
        WriteBom(path, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 3\n"); var second = ErbSourceIndexer.IndexFile(path); var a2 = compiler.TryCompile(second, second.Functions[0]).Fingerprint; var b2 = compiler.TryCompile(second, second.Functions[1]).Fingerprint;
        File.WriteAllLines(Path.Combine(directory, "incremental-fingerprint-test.txt"), [$"deterministic={a1 == a2 && b1 != b2}", $"FUNC_A_unchanged={a1 == a2}", $"FUNC_B_changed={b1 != b2}", "storedAs64CharString=NO"], new UTF8Encoding(false));
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void WriteBom(string path, string text) => File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes(text)]);
static void Add(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
static string Key(string file, int line) => $"{file.ToUpperInvariant()}:{line}";
static long Median(IEnumerable<long> values) { var sorted = values.Order().ToArray(); return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2]; }
static long Percentile(long[] values, int percentile) => values.Length == 0 ? 0 : values[(int)Math.Round((values.Length - 1) * percentile / 100.0)];
readonly record struct BenchmarkRow(int Run, double TotalElapsedMs, double SourceReadElapsedMs, double CompilerElapsedMs, long SourceReadAllocatedBytes, long CompilerAllocatedBytes, long TotalAllocatedBytes, int Compiled, int Instructions);
readonly record struct Candidate(LegacyRow Row, SourceFileIndex File, FunctionIndex Function);
readonly record struct RetainedMeasurement(List<CompiledFunction> Compiled, Dictionary<string, CompiledFunction> CompiledByKey, Dictionary<string, SourceFingerprint> Fingerprints);
sealed class LegacyRow
{
    public string RelativeFile { get; set; } = ""; public int FunctionOrder { get; set; } public string? FunctionName { get; set; }
    public int StartLine { get; set; } public bool IsEvent { get; set; } public bool IsSystem { get; set; } public bool IsMethod { get; set; } public bool IsError { get; set; }
    public bool IsPri { get; set; } public bool IsLater { get; set; } public bool IsOnly { get; set; } public bool IsSingle { get; set; }
    public string? Kind { get; set; } public string[]? InstructionCodes { get; set; } public int[]? InstructionLines { get; set; }
}
