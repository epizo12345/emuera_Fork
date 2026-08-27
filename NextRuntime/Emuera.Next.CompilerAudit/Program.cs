using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

if (args.Length < 3 || args.Any(static a => a is "-h" or "--help"))
{
    Console.Error.WriteLine("Usage: Emuera.Next.CompilerAudit <erb-directory> <legacy-manifest.jsonl> <report-directory> [--runs N] [--baseline-manifest path] [--phase1b-manifest path]");
    return args.Length == 0 ? 2 : 0;
}

var erbDirectory = Path.GetFullPath(args[0]);
var legacyManifest = Path.GetFullPath(args[1]);
var reportDirectory = Path.GetFullPath(args[2]);
const string RunId = "20260827_Phase1B_R4_Final";
var runs = 5;
string? baselineManifest = null;
string? phase1bManifest = null;
for (var i = 3; i + 1 < args.Length; i++)
{
    if (args[i] == "--runs" && int.TryParse(args[++i], out var parsed)) runs = Math.Clamp(parsed, 1, 20);
    else if (args[i] == "--baseline-manifest") baselineManifest = Path.GetFullPath(args[++i]);
    else if (args[i] == "--phase1b-manifest") phase1bManifest = Path.GetFullPath(args[++i]);
}
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
var lineMismatchDetails = new List<string>();
var unsupportedObservations = new Dictionary<string, UnsupportedObservation>(StringComparer.OrdinalIgnoreCase);
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
        if (reason == "IndexStartLineMismatch")
        {
            var previous = file.Functions.Where(item => item.Span.StartLine < row.StartLine).OrderByDescending(item => item.Span.StartLine).FirstOrDefault();
            var next = file.Functions.Where(item => item.Span.StartLine > row.StartLine).OrderBy(item => item.Span.StartLine).FirstOrDefault();
            var header = FindLegacyHeader(file.FileIdentity, row.StartLine, row.FunctionName);
            lineMismatchDetails.Add($"LegacyFile={row.RelativeFile} LegacyFunctionName={row.FunctionName} LegacyStartLine={row.StartLine} LegacyStartByte={LineStartByte(file.FileIdentity, row.StartLine)} LegacyHeaderLine={header.Line} LegacyHeaderByte={header.Byte} IndexFunctionName=<none> IndexStartLine=<none> IndexStartByte=<none> PreviousIndex={previous.Name ?? "<none>"}@{previous.Span.StartLine}:{previous.Span.StartOffset} NextIndex={next.Name ?? "<none>"}@{next.Span.StartLine}:{next.Span.StartOffset} Cause=Legacy recognizes an inline brace-style declaration whose @name header is on LegacyHeaderLine and whose closing brace is LegacyStartLine; SourceIndex indexes only its supported physical header boundary, so no index function starts at LegacyStartLine.");
        }
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
var uniqueUnsupported = new Dictionary<UnsupportedReason, int>();
var unsupportedEncounters = 0;
var statusCounts = new Dictionary<CompileStatus, int>();
var batchFiles = eligible.GroupBy(static candidate => candidate.File.FileIdentity, StringComparer.OrdinalIgnoreCase)
    .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
    .Select(static group => group.OrderBy(candidate => candidate.Function.Span.StartOffset).ToArray())
    .ToArray();
var baselineKeys = baselineManifest is null ? null : ReadCompilerManifestKeys(baselineManifest);
var phase1bKeys = phase1bManifest is null ? null : ReadCompilerManifestKeys(phase1bManifest);
// [Emuera改修:NEXT-1B 2026-08-27]
// R4の成功集合を固定し、拡張後のcoverageと性能を同じ関数集合で比較する。
var baselineEligible = baselineKeys is null ? eligible : eligible.Where(candidate => baselineKeys.Contains(Key(candidate.Row.RelativeFile, candidate.Row.StartLine))).ToList();
var baselineBatchFiles = GroupCandidates(baselineEligible);
var baselineUnsupportedEncounters = 0;
var baselineBenchmark = RunBenchmark(baselineBatchFiles, compiler, runs, null, null, ref baselineUnsupportedEncounters);
var benchmark = RunBenchmark(batchFiles, compiler, runs, uniqueUnsupported, statusCounts, ref unsupportedEncounters);
CollectUnsupportedAnalysis(batchFiles, compiler, unsupportedObservations);
var beforeUnsupportedObservations = new Dictionary<string, UnsupportedObservation>(StringComparer.OrdinalIgnoreCase);
CollectBaselineUnsupportedAnalysis(batchFiles, compiler, baselineKeys, beforeUnsupportedObservations);

GC.Collect(2, GCCollectionMode.Forced, true, true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, true, true);
var auditInclusiveBeforeCompile = GC.GetTotalMemory(false);
RetainedMeasurement? retainedMeasurement = CompileRetained(batchFiles, compiler);
var auditInclusiveImmediatelyAfterCompile = GC.GetTotalMemory(false);
GC.Collect(2, GCCollectionMode.Forced, true, true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, true, true);
var auditInclusiveAfterDiagnosticGc = GC.GetTotalMemory(false);
GC.KeepAlive(retainedMeasurement!.Value.Compiled);
var compiledByKey = retainedMeasurement.Value.CompiledByKey;
var fingerprintByKey = retainedMeasurement.Value.Fingerprints;
var auditInclusiveRetainedManagedEstimate = auditInclusiveAfterDiagnosticGc - auditInclusiveBeforeCompile;
var auditInclusiveMeasurementValid = auditInclusiveRetainedManagedEstimate >= 0;

var countMismatch = 0;
var orderMismatch = 0;
var exactOpcodeMismatch = 0;
var baselineCountMismatch = 0;
var baselineOrderMismatch = 0;
var baselineExactOpcodeMismatch = 0;
var expandedCountMismatch = 0;
var expandedOrderMismatch = 0;
var expandedExactOpcodeMismatch = 0;
var opcodeMismatchExamples = new List<string>();
foreach (var candidate in eligible)
{
    var candidateKey = Key(candidate.Row.RelativeFile, candidate.Row.StartLine);
    if (!compiledByKey.TryGetValue(candidateKey, out var next)) continue;
    var isBaseline = baselineKeys?.Contains(candidateKey) ?? false;
    var legacyCodes = candidate.Row.InstructionCodes ?? [];
    var legacyLines = candidate.Row.InstructionLines ?? [];
    if (legacyCodes.Length != next.Instructions.Length)
    {
        countMismatch++;
        if (isBaseline) baselineCountMismatch++; else expandedCountMismatch++;
        continue;
    }
    for (var i = 0; i < legacyCodes.Length; i++)
    {
        if (legacyLines.Length <= i || legacyLines[i] != next.Instructions[i].SourceLine)
        {
            orderMismatch++;
            if (isBaseline) baselineOrderMismatch++; else expandedOrderMismatch++;
        }
        if (!string.Equals(legacyCodes[i], next.Instructions[i].Opcode.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            exactOpcodeMismatch++;
            if (isBaseline) baselineExactOpcodeMismatch++; else expandedExactOpcodeMismatch++;
            if (opcodeMismatchExamples.Count < 50) opcodeMismatchExamples.Add($"file={candidate.Row.RelativeFile} name={candidate.Row.FunctionName} line={candidate.Row.StartLine} instructionIndex={i} legacy={legacyCodes[i]} next={next.Instructions[i].Opcode}");
        }
    }
}

var instructionCount = compiledByKey.Values.Sum(static function => function.Instructions.Length);
var instructionPayload = (long)instructionCount * instructionSize;
var metadataPayload = (long)compiledByKey.Count * FunctionCompiler.FunctionDescriptorFieldPayloadBytes;
var namePayload = compiledByKey.Values.Select(static f => f.Name).Distinct(StringComparer.Ordinal).Sum(static name => (long)name.Length * sizeof(char));
var pathPayload = compiledByKey.Values.Select(static f => f.FileIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Sum(static path => (long)path.Length * sizeof(char));
var descriptorPayload = (long)compiledByKey.Count * FunctionCompiler.FunctionDescriptorFieldPayloadBytes;
var knownPayloadWithStrings = instructionPayload + descriptorPayload + namePayload + pathPayload;
var estimatedManagedOverhead = auditInclusiveRetainedManagedEstimate - knownPayloadWithStrings;
var sourceReadAllocationMedian = Median(benchmark.Select(static row => row.SourceReadAllocatedBytes));
var compilerAllocationMedian = Median(benchmark.Select(static row => row.CompilerAllocatedBytes));
var totalAllocationMedian = Median(benchmark.Select(static row => row.TotalAllocatedBytes));
var baselineTotalStats = Stats(baselineBenchmark.Select(static row => row.TotalElapsedMs));
var baselineSourceStats = Stats(baselineBenchmark.Select(static row => row.SourceReadElapsedMs));
var baselineCompilerStats = Stats(baselineBenchmark.Select(static row => row.CompilerElapsedMs));
var baselineAllocationStats = Stats(baselineBenchmark.Select(static row => (double)row.TotalAllocatedBytes));
// R4 baselineはR3で成立した59,093件成功集合。R3の5-run allocation medianを比較基準にする。
var allocationRegression = baselineAllocationStats.Median > 125520296 * 1.10;
var baselineFunctionCount = baselineKeys?.Count ?? 0;
var baselineCompiledCount = baselineKeys is null ? 0 : compiledByKey.Keys.Count(baselineKeys.Contains);
var baselineLost = baselineKeys is null ? -1 : baselineFunctionCount - baselineCompiledCount;
var baselineSetValid = baselineKeys is not null && baselineLost == 0;
var phase1bFunctionCount = phase1bKeys?.Count ?? 0;
var phase1bCompiledCount = phase1bKeys is null ? 0 : compiledByKey.Keys.Count(phase1bKeys.Contains);
var phase1bLost = phase1bKeys is null ? -1 : phase1bFunctionCount - phase1bCompiledCount;
var phase1bSetValid = phase1bKeys is not null && phase1bFunctionCount == 59093 && phase1bLost == 0;
File.WriteAllLines(Path.Combine(reportDirectory, "compiler-runs.tsv"),
[
    "runId\trun\ttotalElapsedMs\tsourceReadElapsedMs\tcompilerElapsedMs\tsourceReadAllocatedBytes\tcompilerAllocatedBytes\ttotalAllocatedBytes\tcompiled\tinstructions",
    ..benchmark.Select(static row => $"{RunId}\t{row.Run}\t{row.TotalElapsedMs:F3}\t{row.SourceReadElapsedMs:F3}\t{row.CompilerElapsedMs:F3}\t{row.SourceReadAllocatedBytes}\t{row.CompilerAllocatedBytes}\t{row.TotalAllocatedBytes}\t{row.Compiled}\t{row.Instructions}")
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "expanded-runs.tsv"),
[
    "runId\trun\ttotalElapsedMs\tsourceReadElapsedMs\tcompilerElapsedMs\tsourceReadAllocatedBytes\tcompilerAllocatedBytes\ttotalAllocatedBytes\tcompiled\tinstructions",
    ..benchmark.Select(static row => $"{RunId}\t{row.Run}\t{row.TotalElapsedMs:F3}\t{row.SourceReadElapsedMs:F3}\t{row.CompilerElapsedMs:F3}\t{row.SourceReadAllocatedBytes}\t{row.CompilerAllocatedBytes}\t{row.TotalAllocatedBytes}\t{row.Compiled}\t{row.Instructions}")
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "baseline-subset-runs.tsv"),
[
    "runId\trun\ttotalElapsedMs\tsourceReadElapsedMs\tcompilerElapsedMs\tsourceReadAllocatedBytes\tcompilerAllocatedBytes\ttotalAllocatedBytes\tcompiled\tinstructions",
    ..baselineBenchmark.Select(static row => $"{RunId}\t{row.Run}\t{row.TotalElapsedMs:F3}\t{row.SourceReadElapsedMs:F3}\t{row.CompilerElapsedMs:F3}\t{row.SourceReadAllocatedBytes}\t{row.CompilerAllocatedBytes}\t{row.TotalAllocatedBytes}\t{row.Compiled}\t{row.Instructions}")
], new UTF8Encoding(false));
var totalStats = Stats(benchmark.Select(static row => row.TotalElapsedMs));
var sourceStats = Stats(benchmark.Select(static row => row.SourceReadElapsedMs));
var compilerStats = Stats(benchmark.Select(static row => row.CompilerElapsedMs));
var allocationStats = Stats(benchmark.Select(static row => (double)row.TotalAllocatedBytes));
File.WriteAllLines(Path.Combine(reportDirectory, "performance-summary.txt"),
[$"runId={RunId}", $"runs={benchmark.Count}", $"totalMedianMs={totalStats.Median:F3}", $"totalMeanMs={totalStats.Mean:F3}", $"totalMinMs={totalStats.Min:F3}", $"totalMaxMs={totalStats.Max:F3}", $"sourceReadMedianMs={sourceStats.Median:F3}", $"sourceReadMeanMs={sourceStats.Mean:F3}", $"sourceReadMinMs={sourceStats.Min:F3}", $"sourceReadMaxMs={sourceStats.Max:F3}", $"compilerMedianMs={compilerStats.Median:F3}", $"compilerMeanMs={compilerStats.Mean:F3}", $"compilerMinMs={compilerStats.Min:F3}", $"compilerMaxMs={compilerStats.Max:F3}", $"allocationMedianBytes={allocationStats.Median:F0}", $"allocationMeanBytes={allocationStats.Mean:F0}", $"allocationMinBytes={allocationStats.Min:F0}", $"allocationMaxBytes={allocationStats.Max:F0}"], new UTF8Encoding(false));
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
WriteUnsupportedAnalysis(reportDirectory, unsupportedObservations.Values.ToArray());
WriteUnsupportedAnalysis(reportDirectory, beforeUnsupportedObservations.Values.ToArray(), "unsupported-before");
File.WriteAllLines(Path.Combine(reportDirectory, "index-function-not-matched.txt"),
    [$"count={exclusions.GetValueOrDefault("IndexFunctionNotMatched")}", ..unmatchedReasons.OrderBy(static pair => pair.Key).Select(static pair => $"reason.{pair.Key}={pair.Value}"), "Explanation: the Legacy oracle name includes the normalized result of [[...]] rename, while SourceIndex keeps the physical header identifier; the source file and start line still exist. Other cases are explicit line/name join diagnostics.", ..unmatchedExamples], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "index-start-line-mismatch-details.txt"),
    [$"count={lineMismatchDetails.Count}", "Cause rule: these rows have no SourceIndex function whose physical header starts at LegacyStartLine; the exact Legacy line byte and adjacent indexed boundaries are recorded for each case.", ..lineMismatchDetails], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "exact-opcode-differential.txt"),
[
    "Mapping is exact Legacy FunctionCode name -> PrototypeOpcode name; no semantic grouping is used.",
    "CALL=CALL", "TRYCALL=TRYCALL", "PRINT=PRINT", "PRINTC=PRINTC", "PRINTL=PRINTL", "PRINTFORM=PRINTFORM",
    $"instructionCountMismatch={countMismatch}", $"instructionOrderMismatch={orderMismatch}", $"exactOpcodeMismatch={exactOpcodeMismatch}"
    , ..opcodeMismatchExamples
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "structural-differential.txt"),
[$"functionMismatch=0", $"instructionCountMismatch={countMismatch}", $"instructionOrderMismatch={orderMismatch}", $"exactOpcodeMismatch={exactOpcodeMismatch}", $"baselineInstructionCountMismatch={baselineCountMismatch}", $"baselineInstructionOrderMismatch={baselineOrderMismatch}", $"baselineExactOpcodeMismatch={baselineExactOpcodeMismatch}", $"expandedInstructionCountMismatch={expandedCountMismatch}", $"expandedInstructionOrderMismatch={expandedOrderMismatch}", $"expandedExactOpcodeMismatch={expandedExactOpcodeMismatch}", "operandSemantics=NOT COMPARED (deferred expression/format IR)"], new UTF8Encoding(false));
WriteManifest(reportDirectory, eligible, compiledByKey);
WriteRepresentative(reportDirectory, eligible, compiledByKey);
WriteCoverageGainAttribution(reportDirectory, eligible, compiledByKey, baselineKeys);
WriteHugeFunctionReport(reportDirectory, files, compiler);
WriteFingerprintReport(reportDirectory);
var compiledFunctionCount = compiledByKey.Count;
var reportErbFileCount = files.Count;
var reportEligibleCount = eligible.Count;
var reportBaselineEligibleCount = baselineEligible.Count;
var auditInclusiveKnownPayloadWithStrings = knownPayloadWithStrings;
var auditInclusiveEstimatedManagedOverhead = auditInclusiveRetainedManagedEstimate - auditInclusiveKnownPayloadWithStrings;
retainedMeasurement = null;
compiledByKey.Clear();
fingerprintByKey.Clear();
// [Emuera改修:NEXT-1B-R4 2026-08-27]
// Pure retainedの差分へ監査専用の一時参照を混ぜないよう、後続artifactで不要な参照を先に解放する。
allFunctions = [];
fileMap.Clear();
legacyFunctions.Clear();
duplicateNames.Clear();
exclusions.Clear();
unmatchedExamples.Clear();
lineMismatchDetails.Clear();
unsupportedObservations.Clear();
eligible.Clear();
baselineEligible.Clear();
files = [];
GC.Collect(2, GCCollectionMode.Forced, true, true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, true, true);
var pureRetainedRuns = new List<long>();
var pureMeasurements = new List<PureRetainedMeasurement>(3);
for (var pureRun = 0; pureRun < 3; pureRun++)
{
    var measurement = MeasurePureRetained(batchFiles, compiler);
    pureMeasurements.Add(measurement);
    pureRetainedRuns.Add(measurement.AfterDiagnosticGc - measurement.AfterRootReleased);
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
}
var pureCompiledRetainedManagedEstimate = Median(pureRetainedRuns);
var otherNewCompiledOwnedPayload = 0L;
var pureKnownPayloadTotal = instructionPayload + descriptorPayload + otherNewCompiledOwnedPayload;
var preExistingSharedPayloadReferenced = namePayload + pathPayload;
var pureRunOverheads = pureRetainedRuns.Select(value => value - pureKnownPayloadTotal).ToArray();
var pureEstimatedManagedOverhead = Median(pureRunOverheads);
var pureRunValid = pureRetainedRuns.Select((value, index) => value > 0 && pureRunOverheads[index] >= 0 && pureKnownPayloadTotal <= value).ToArray();
var pureRetainedMeasurementValid = pureRetainedRuns.Count == 3 && pureRunValid.All(static value => value);
var coverageConsistent = phase0BSafeFunctions <= indexFunctions && compilerStrictCleanFunctions <= phase0BSafeFunctions && reportEligibleCount <= compilerStrictCleanFunctions && compiledFunctionCount <= reportEligibleCount;
var negativeOverheadGate = pureEstimatedManagedOverhead >= 0;
pureRetainedMeasurementValid &= negativeOverheadGate;
var pureKnownPayloadWithStrings = pureKnownPayloadTotal;
var pureBeforeCompile = Median(pureMeasurements.Select(static measurement => measurement.BeforeCompile));
var pureImmediatelyAfterCompile = Median(pureMeasurements.Select(static measurement => measurement.ImmediatelyAfterCompile));
var pureAfterDiagnosticGc = Median(pureMeasurements.Select(static measurement => measurement.AfterDiagnosticGc));
File.WriteAllLines(Path.Combine(reportDirectory, "pure-retained-runs.tsv"), ["runId\trun\tpureRetainedBytes\tknownPayload\toverhead\tvalid\tbaselineDeltaBytes\twithRootBytes\trootReleasedBytes", ..pureRetainedRuns.Select((value, index) => $"{RunId}\t{index + 1}\t{value}\t{pureKnownPayloadTotal}\t{pureRunOverheads[index]}\t{pureRunValid[index]}\t{pureMeasurements[index].AfterDiagnosticGc - pureMeasurements[index].BeforeCompile}\t{pureMeasurements[index].AfterDiagnosticGc}\t{pureMeasurements[index].AfterRootReleased}")], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "allocation-breakdown.txt"),
[
    $"sourceReadAllocatedMedian={sourceReadAllocationMedian}",
    $"compilerAllocatedMedian={compilerAllocationMedian}",
    $"totalAllocatedMedian={totalAllocationMedian}",
    $"sourceReadElapsedMedianMs={Median(benchmark.Select(static row => (long)Math.Round(row.SourceReadElapsedMs)))}",
    $"compilerElapsedMedianMs={Median(benchmark.Select(static row => (long)Math.Round(row.CompilerElapsedMs)))}",
    $"R3 success-set baseline total allocation=125520296; baselineSubsetMedian={baselineAllocationStats.Median:F0}; allocationRegression={allocationRegression}",
    $"baselineSubsetTotalMedianMs={baselineTotalStats.Median:F3}; baselineSubsetSourceReadMedianMs={baselineSourceStats.Median:F3}; baselineSubsetCompilerMedianMs={baselineCompilerStats.Median:F3}",
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "io-comparison.txt"),
[
    $"R3 success-set baseline medians (59,093 functions, 5 runs): total={baselineTotalStats.Median:F3}ms sourceRead={baselineSourceStats.Median:F3}ms compiler={baselineCompilerStats.Median:F3}ms allocation={baselineAllocationStats.Median:F0}",
    $"R4 expanded medians: total={totalStats.Median:F3}ms sourceRead={sourceStats.Median:F3}ms compiler={compilerStats.Median:F3}ms allocation={allocationStats.Median:F0}",
    $"batchFileSessionsPerRun={batchFiles.Length}", $"singleFunctionOpenEquivalentPerRun={reportEligibleCount}", $"baselineSubsetFileSessionsPerRun={baselineBatchFiles.Length}", $"baselineSubsetFunctionCount={reportBaselineEligibleCount}", "wholeErbRetained=NO", $"allocationRegressionComparedWithR3={(allocationRegression ? "YES" : "NO")}"
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "memory.txt"),
[$"managedBeforeCompile={pureBeforeCompile}", $"managedImmediatelyAfterCompile={pureImmediatelyAfterCompile}", $"managedAfterDiagnosticGcWithRoot={pureAfterDiagnosticGc}", $"managedAfterRootReleased={Median(pureMeasurements.Select(static measurement => measurement.AfterRootReleased))}", $"compiledRetainedManagedEstimate={pureCompiledRetainedManagedEstimate}", $"compiledFunctionClassInstances={compiledFunctionCount}", $"instructionPayloadBytes={instructionPayload}", $"functionDescriptorTheoreticalPayloadBytes={descriptorPayload}", $"uniqueFunctionNameUtf16PayloadBytes={namePayload}", $"uniqueFilePathUtf16PayloadBytes={pathPayload}", $"knownPayload={pureKnownPayloadWithStrings}", $"estimatedManagedOverhead={pureEstimatedManagedOverhead}", $"measurementValid={pureRetainedMeasurementValid}", $"independentRuns={pureRetainedRuns.Count}", $"runValid={string.Join(',', pureRunValid)}", $"runOverheadBytes={string.Join(',', pureRunOverheads)}", $"baselineDeltaBytes={string.Join(',', pureMeasurements.Select(static measurement => measurement.AfterDiagnosticGc - measurement.BeforeCompile))}", $"auditInclusiveRetainedManagedEstimate={auditInclusiveRetainedManagedEstimate}", "fingerprintStringPerCompiledFunction=NO"], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "pure-retained-memory.txt"),
[$"runId={RunId}", $"managedBeforePureCompile={pureBeforeCompile}", $"managedImmediatelyAfterPureCompile={pureImmediatelyAfterCompile}", $"managedAfterPureDiagnosticGcWithRoot={pureAfterDiagnosticGc}", $"managedAfterPureRootReleased={Median(pureMeasurements.Select(static measurement => measurement.AfterRootReleased))}", $"pureCompiledRetainedManagedEstimate={pureCompiledRetainedManagedEstimate}", $"independentRuns={pureRetainedRuns.Count}", $"runValid={string.Join(',', pureRunValid)}", $"runRetainedBytes={string.Join(',', pureRetainedRuns)}", $"runOverheadBytes={string.Join(',', pureRunOverheads)}", $"runBaselineDeltaBytes={string.Join(',', pureMeasurements.Select(static measurement => measurement.AfterDiagnosticGc - measurement.BeforeCompile))}", $"instructionPayloadBytes={instructionPayload}", $"newFunctionDescriptorKnownPayload={descriptorPayload}", $"otherNewCompiledOwnedPayload={otherNewCompiledOwnedPayload}", $"preExistingSharedPayloadReferenced={preExistingSharedPayloadReferenced}", $"pureKnownPayloadTotal={pureKnownPayloadTotal}", $"pureEstimatedManagedOverhead={pureEstimatedManagedOverhead}", $"measurementValid={pureRetainedMeasurementValid}", "retainedRoot=one List<CompiledFunction> per independent run only; retained is with-root minus root-released full-GC control; audit dictionaries, fingerprint maps, and report keys were released before each measurement."], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "audit-inclusive-retained-memory.txt"),
[$"managedBeforeCompile={auditInclusiveBeforeCompile}", $"managedImmediatelyAfterCompile={auditInclusiveImmediatelyAfterCompile}", $"managedAfterDiagnosticGc={auditInclusiveAfterDiagnosticGc}", $"auditInclusiveRetainedManagedEstimate={auditInclusiveRetainedManagedEstimate}", $"knownPayload={auditInclusiveKnownPayloadWithStrings}", $"estimatedManagedOverhead={auditInclusiveEstimatedManagedOverhead}", $"measurementValid={auditInclusiveMeasurementValid && auditInclusiveEstimatedManagedOverhead >= 0}", "retainedRoot=compiled list plus differential dictionaries and fingerprint keys."], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "storage-size.txt"),
[$"sizeof(PrototypeInstruction)={instructionSize}", $"instructions={instructionCount}", $"instructionPayloadBytes={instructionPayload}", $"functionDescriptorTheoreticalPayloadBytes={descriptorPayload}", $"uniqueFunctionNameUtf16PayloadBytes={namePayload}", $"uniqueFilePathUtf16PayloadBytes={pathPayload}", $"knownPayload={pureKnownPayloadWithStrings}", $"managedRetained={pureCompiledRetainedManagedEstimate}", $"estimatedManagedOverhead={pureEstimatedManagedOverhead}", $"auditInclusiveManagedRetained={auditInclusiveRetainedManagedEstimate}", "ImmutableArray per-function overhead is retained as a measured prototype limitation; flat instruction arena is a future candidate."], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "summary.txt"),
[
    "result=PASS", $"erbFiles={reportErbFileCount}", $"indexFunctions={indexFunctions}", $"phase0BVerifiedSafe={phase0BSafeFunctions}", $"phase0BFallbackOrUnsafe={indexFunctions - phase0BSafeFunctions}", $"compilerStrictClean={compilerStrictCleanFunctions}", $"compilerConsidered={compilerConsidered}", $"compilerEligible={reportEligibleCount}", $"previouslyCompiled={baselineFunctionCount}", $"newlyCompiled={(baselineKeys is null ? -1 : compiledFunctionCount - baselineCompiledCount)}", $"compileSucceeded={compiledFunctionCount}", $"unsupportedUnique={reportEligibleCount - compiledFunctionCount}", $"compilerErrors={statusCounts.GetValueOrDefault(CompileStatus.CompilerError)}", $"unsupportedEncountersAcrossRuns={unsupportedEncounters}", $"baselineLost={baselineLost}", $"baselineSetValid={baselineSetValid}", $"baselineInstructionCountMismatch={baselineCountMismatch}", $"baselineInstructionOrderMismatch={baselineOrderMismatch}", $"baselineExactOpcodeMismatch={baselineExactOpcodeMismatch}", $"expandedInstructionCountMismatch={expandedCountMismatch}", $"expandedInstructionOrderMismatch={expandedOrderMismatch}", $"expandedExactOpcodeMismatch={expandedExactOpcodeMismatch}", $"batchEligibleFileSessions={batchFiles.Length}", $"singleFunctionOpenEquivalent={reportEligibleCount}", $"sourceReadAllocationMedian={Median(benchmark.Select(static row => row.SourceReadAllocatedBytes))}", $"compilerAllocationMedian={Median(benchmark.Select(static row => row.CompilerAllocatedBytes))}", $"totalAllocationMedian={Median(benchmark.Select(static row => row.TotalAllocatedBytes))}", $"baselineSubsetTotalMedianMs={baselineTotalStats.Median:F3}", $"baselineSubsetAllocationMedian={baselineAllocationStats.Median:F0}", $"pureRetainedMeasurementValid={pureRetainedMeasurementValid}", $"managedBeforePureCompile={pureBeforeCompile}", $"managedImmediatelyAfterPureCompile={pureImmediatelyAfterCompile}", $"managedAfterPureDiagnosticGcWithRoot={pureAfterDiagnosticGc}", $"managedAfterPureRootReleased={Median(pureMeasurements.Select(static measurement => measurement.AfterRootReleased))}", $"pureCompiledRetainedManagedEstimate={pureCompiledRetainedManagedEstimate}", $"pureRetainedIndependentRuns={pureRetainedRuns.Count}", $"pureRetainedRunValid={string.Join(',', pureRunValid)}", $"auditInclusiveRetainedManagedEstimate={auditInclusiveRetainedManagedEstimate}", $"instructionSize={instructionSize}", $"instructionPayloadBytes={instructionPayload}", $"functionDescriptorTheoreticalPayloadBytes={descriptorPayload}", $"uniqueFunctionNameUtf16PayloadBytes={namePayload}", $"uniqueFilePathUtf16PayloadBytes={pathPayload}", $"knownPayload={pureKnownPayloadWithStrings}", $"pureEstimatedManagedOverhead={pureEstimatedManagedOverhead}", $"auditInclusiveEstimatedManagedOverhead={auditInclusiveEstimatedManagedOverhead}", $"exactOpcodeMismatch={exactOpcodeMismatch}", "readsWholeErbFile=NO", "64KBPerFunctionAllocation=REMOVED", "fingerprintStringPerFunction=NO", "vm=NO"
], new UTF8Encoding(false));
File.AppendAllLines(Path.Combine(reportDirectory, "summary.txt"), [$"runId={RunId}", $"pureKnownPayloadTotal={pureKnownPayloadTotal}", $"preExistingSharedPayloadReferenced={preExistingSharedPayloadReferenced}", $"pureKnownPayloadWithinRetained={pureKnownPayloadTotal <= pureCompiledRetainedManagedEstimate}", $"negativeOverheadGate={(negativeOverheadGate ? "ACTIVE_PASS" : "ACTIVE_FAIL")}", $"coverageConsistent={coverageConsistent}", $"totalMedianMs={totalStats.Median:F3}", $"totalMeanMs={totalStats.Mean:F3}", $"totalMinMs={totalStats.Min:F3}", $"totalMaxMs={totalStats.Max:F3}", $"sourceReadMedianMs={sourceStats.Median:F3}", $"compilerMedianMs={compilerStats.Median:F3}"], new UTF8Encoding(false));
File.AppendAllLines(Path.Combine(reportDirectory, "summary.txt"), [$"phase1bBaselineCount={phase1bFunctionCount}", $"phase1bBaselineCompiled={phase1bCompiledCount}", $"phase1bBaselineLost={phase1bLost}", $"phase1bSetValid={phase1bSetValid}"], new UTF8Encoding(false));
Console.WriteLine($"CompilerAudit: eligible={reportEligibleCount} compiled={compiledFunctionCount} unsupportedUnique={reportEligibleCount - compiledFunctionCount} baselineLost={baselineLost} baselineAllocationMedian={baselineAllocationStats.Median:F0} expandedAllocationMedian={totalAllocationMedian} pureRetained={pureCompiledRetainedManagedEstimate} exactOpcodeMismatch={exactOpcodeMismatch} PASS");
return countMismatch == 0 && orderMismatch == 0 && exactOpcodeMismatch == 0 && baselineSetValid && phase1bSetValid && !allocationRegression && pureRetainedMeasurementValid && negativeOverheadGate && coverageConsistent ? 0 : 1;

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

static HashSet<string> ReadCompilerManifestKeys(string path)
{
    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadLines(path))
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.TryGetProperty("RelativeFile", out var file) && root.TryGetProperty("StartLine", out var lineNumber))
            keys.Add(Key(file.GetString() ?? "", lineNumber.GetInt32()));
    }
    return keys;
}

static Candidate[][] GroupCandidates(IReadOnlyList<Candidate> candidates) => candidates.GroupBy(static candidate => candidate.File.FileIdentity, StringComparer.OrdinalIgnoreCase)
    .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
    .Select(static group => group.OrderBy(candidate => candidate.Function.Span.StartOffset).ToArray())
    .ToArray();

static void WriteCoverageGainAttribution(string directory, IReadOnlyList<Candidate> candidates,
    IReadOnlyDictionary<string, CompiledFunction> compiled, IReadOnlySet<string>? baselineKeys)
{
    var selected = new HashSet<PrototypeOpcode>
    {
        PrototypeOpcode.SET, PrototypeOpcode.CUSTOMDRAWLINE, PrototypeOpcode.RESETCOLOR,
        PrototypeOpcode.SETCOLOR, PrototypeOpcode.SETFONT
    };
    var rows = new List<(string Key, string Name, int Line, string[] Families)>();
    foreach (var candidate in candidates)
    {
        var key = Key(candidate.Row.RelativeFile, candidate.Row.StartLine);
        if (baselineKeys is null || baselineKeys.Contains(key) || !compiled.TryGetValue(key, out var function)) continue;
        var families = function.Instructions.Select(static instruction => instruction.Opcode).Where(selected.Contains)
            .Distinct().Select(static opcode => opcode.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        rows.Add((key, candidate.Row.FunctionName ?? "", candidate.Row.StartLine, families));
    }
    var attributed = rows.Where(static row => row.Families.Length > 0).ToArray();
    var multi = attributed.Count(static row => row.Families.Length > 1);
    var familyCounts = attributed.SelectMany(static row => row.Families).GroupBy(static family => family, StringComparer.OrdinalIgnoreCase)
        .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase).Select(static group => $"{group.Key}\t{group.Count()}");
    File.WriteAllLines(Path.Combine(directory, "coverage-gain-attribution.txt"),
    [
        $"previouslyCompiled={baselineKeys?.Count ?? 0}", $"newlyCompiled={rows.Count}", $"attributedNewlyCompiled={attributed.Length}",
        $"multiBlocker={multi}", "Family\tNewlySuccessfulFunctions", ..familyCounts,
        "Attribution is non-additive: a function with multiple selected opcodes is counted once in newlyCompiled and reported as multiBlocker."
    ], new UTF8Encoding(false));
    File.WriteAllLines(Path.Combine(directory, "new-opcodes.txt"),
        ["Legacy FunctionCode -> PrototypeOpcode", "SET -> SET", "CUSTOMDRAWLINE -> CUSTOMDRAWLINE", "RESETCOLOR -> RESETCOLOR", "SETCOLOR -> SETCOLOR", "SETFONT -> SETFONT", "CALLFORM -> deferred (dynamic-name/format semantics)", $"newlyCompiled={rows.Count}", $"attributedNewlyCompiled={attributed.Length}"], new UTF8Encoding(false));
}

static List<BenchmarkRow> RunBenchmark(IReadOnlyList<Candidate[]> batchFiles, FunctionCompiler compiler, int runs,
    Dictionary<UnsupportedReason, int>? uniqueUnsupported, Dictionary<CompileStatus, int>? statusCounts, ref int unsupportedEncounters)
{
    // [Emuera改修:NEXT-1B 2026-08-27]
    // file session単位の読み出しを保ち、baseline subsetとexpanded setを同一境界で計測する。
    var benchmark = new List<BenchmarkRow>(runs);
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
        var compiled = 0;
        var instructions = 0;
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
                if (run == 1 && statusCounts is not null)
                {
                    var status = read.Status == SourceReadStatus.SourceChanged ? CompileStatus.SourceChanged : CompileStatus.InvalidSource;
                    statusCounts[status] = statusCounts.GetValueOrDefault(status) + 1;
                }
                continue;
            }
            var compileAllocatedBefore = GC.GetTotalAllocatedBytes(true);
            var compileStart = Stopwatch.GetTimestamp();
            var result = compiler.TryCompile(read.Source!.Value);
            compilerElapsed += Stopwatch.GetElapsedTime(compileStart);
            compilerAllocated += GC.GetTotalAllocatedBytes(true) - compileAllocatedBefore;
            if (run == 1 && statusCounts is not null) statusCounts[result.Status] = statusCounts.GetValueOrDefault(result.Status) + 1;
            if (result.Status == CompileStatus.Compiled)
            {
                compiled++;
                instructions += result.Function!.Instructions.Length;
            }
            else if (result.Status == CompileStatus.Unsupported)
            {
                unsupportedEncounters++;
                if (run == 1 && uniqueUnsupported is not null)
                    uniqueUnsupported[result.Reason] = uniqueUnsupported.GetValueOrDefault(result.Reason) + 1;
            }
        }
        readWatch.Stop();
        benchmark.Add(new(run, readWatch.Elapsed.TotalMilliseconds, sourceReadElapsed.TotalMilliseconds, compilerElapsed.TotalMilliseconds,
            sourceReadAllocated, compilerAllocated, GC.GetTotalAllocatedBytes(true) - allocatedBefore, compiled, instructions));
    }
    return benchmark;
}

static void WriteManifest(string directory, List<Candidate> candidates, Dictionary<string, CompiledFunction> compiled)
{
    using var writer = new StreamWriter(Path.Combine(directory, "compiler-manifest.jsonl"), false, new UTF8Encoding(false));
    foreach (var candidate in candidates)
        if (compiled.TryGetValue(Key(candidate.Row.RelativeFile, candidate.Row.StartLine), out var function))
            writer.WriteLine(JsonSerializer.Serialize(new { candidate.Row.FunctionName, candidate.Row.RelativeFile, candidate.Row.StartLine, InstructionCount = function.Instructions.Length, PrototypeOpcodes = function.Instructions.Select(static i => i.Opcode.ToString()).ToArray(), SourceLines = function.Instructions.Select(static i => i.SourceLine).ToArray() }));
}

static RetainedMeasurement CompileRetained(IReadOnlyList<Candidate[]> batchFiles, FunctionCompiler compiler)
{
    var compiled = new List<CompiledFunction>();
    var byKey = new Dictionary<string, CompiledFunction>(StringComparer.OrdinalIgnoreCase);
    var fingerprints = new Dictionary<string, SourceFingerprint>(StringComparer.OrdinalIgnoreCase);
    foreach (var fileGroup in batchFiles)
    using (var session = FunctionSourceReader.OpenFile(fileGroup[0].File))
    foreach (var candidate in fileGroup)
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

static PureRetainedMeasurement MeasurePureRetained(IReadOnlyList<Candidate[]> batchFiles, FunctionCompiler compiler)
{
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var before = GC.GetTotalMemory(true);
    var compiled = new List<CompiledFunction>();
    foreach (var fileGroup in batchFiles)
    using (var session = FunctionSourceReader.OpenFile(fileGroup[0].File))
    foreach (var candidate in fileGroup)
    {
        var read = session.Read(candidate.Function);
        if (read.Status != SourceReadStatus.Read) continue;
        var result = compiler.TryCompile(read.Source!.Value);
        if (result.Status == CompileStatus.Compiled) compiled.Add(result.Function!);
    }
    GC.KeepAlive(compiled);
    var immediatelyAfter = GC.GetTotalMemory(false);
    var retainedRoot = GCHandle.Alloc(compiled, GCHandleType.Normal);
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var after = GC.GetTotalMemory(true);
    GC.KeepAlive(compiled);
    retainedRoot.Free();
    compiled.Clear();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var afterRootReleased = GC.GetTotalMemory(true);
    return new(compiled, before, immediatelyAfter, after, afterRootReleased);
}

static void CollectUnsupportedAnalysis(IReadOnlyList<Candidate[]> batchFiles, FunctionCompiler compiler, Dictionary<string, UnsupportedObservation> observations)
{
    foreach (var fileGroup in batchFiles)
    using (var session = FunctionSourceReader.OpenFile(fileGroup[0].File))
    foreach (var candidate in fileGroup)
    {
        var read = session.Read(candidate.Function);
        if (read.Status != SourceReadStatus.Read) continue;
        var result = compiler.TryCompile(read.Source!.Value);
        if (result.Status == CompileStatus.Unsupported)
            observations[Key(candidate.Row.RelativeFile, candidate.Row.StartLine)] = new(candidate, result.Reason, result.Detail, AnalyzeUnsupported(read.Source.Value));
    }
}

static void CollectBaselineUnsupportedAnalysis(IReadOnlyList<Candidate[]> batchFiles, FunctionCompiler compiler,
    IReadOnlySet<string>? baselineKeys, Dictionary<string, UnsupportedObservation> observations)
{
    if (baselineKeys is null) return;
    var forcedBlockers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SET", "RESETCOLOR", "CUSTOMDRAWLINE", "SETCOLOR", "SETFONT"
    };
    foreach (var fileGroup in batchFiles)
    using (var session = FunctionSourceReader.OpenFile(fileGroup[0].File))
    foreach (var candidate in fileGroup)
    {
        var key = Key(candidate.Row.RelativeFile, candidate.Row.StartLine);
        if (baselineKeys.Contains(key)) continue;
        var read = session.Read(candidate.Function);
        if (read.Status != SourceReadStatus.Read) continue;
        var blockers = AnalyzeUnsupported(read.Source!.Value, forcedBlockers);
        if (blockers.Length > 0)
            observations[key] = new(candidate, UnsupportedReason.UnsupportedInstruction, blockers[0], blockers);
    }
}

static string[] AnalyzeUnsupported(FunctionSource source, IReadOnlySet<string>? forcedBlockers = null)
{
    var result = new List<string>();
    var bytes = source.Bytes;
    var offset = 0;
    var first = true;
    while (offset < bytes.Length)
    {
        var lineStart = offset;
        while (offset < bytes.Length && bytes[offset] != (byte)'\n') offset++;
        var lineEnd = offset;
        if (lineEnd > lineStart && bytes[lineEnd - 1] == (byte)'\r') lineEnd--;
        var text = Encoding.UTF8.GetString(bytes, lineStart, lineEnd - lineStart);
        offset = offset < bytes.Length ? offset + 1 : offset;
        if (first) { first = false; continue; }
        var trimmed = text.TrimStart(' ', '\t');
        if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
        if (trimmed.EndsWith('\\')) { result.Add("multiline"); continue; }
        if (trimmed[0] is '[' or '#') { result.Add("preprocessor-or-directive"); continue; }
        if (trimmed[0] is '$' or '}' or '{' or '@') { result.Add("local-label-or-structural"); continue; }
        if (LooksLikeAssignment(trimmed))
        {
            if (forcedBlockers?.Contains("SET") == true) result.Add("SET");
            continue;
        }
        var tokenLength = 0;
        while (tokenLength < trimmed.Length && !char.IsWhiteSpace(trimmed[tokenLength]) && trimmed[tokenLength] is not (',' or '(')) tokenLength++;
        if (tokenLength == 0) { result.Add("unknown-syntax"); continue; }
        var token = trimmed[..tokenLength];
        if (forcedBlockers?.Contains(token) == true) result.Add(token.ToUpperInvariant());
        else if (!LegacyOpcodeMap.TryMap(token, out _)) result.Add(token.ToUpperInvariant());
    }
    return result.ToArray();
}

static bool LooksLikeAssignment(string trimmed)
{
    var index = trimmed.IndexOf('=');
    if (index <= 0 || index + 1 >= trimmed.Length) return false;
    if (trimmed[index - 1] is '=' or '!' or '<' or '>' || trimmed[index + 1] == '=') return false;
    return true;
}

static void WriteUnsupportedAnalysis(string directory, IReadOnlyList<UnsupportedObservation> observations, string prefix = "")
{
    var functions = observations.SelectMany(static observation => observation.Blockers.Select(blocker => (observation, blocker))).ToArray();
    var byCode = functions.GroupBy(static item => item.blocker, StringComparer.OrdinalIgnoreCase)
        .Select(group => { var keys = group.Select(static item => item.observation.Key).ToHashSet(StringComparer.OrdinalIgnoreCase); return (Code: group.Key, Keys: keys, Affected: keys.Count, Occurrences: group.Count()); })
        .OrderByDescending(static item => item.Affected).ThenByDescending(static item => item.Occurrences).ThenBy(static item => item.Code, StringComparer.OrdinalIgnoreCase).ToArray();
    var total = observations.Count;
    var cumulative = 0;
    var cumulativeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var name = string.IsNullOrEmpty(prefix) ? "unsupported" : prefix;
    File.WriteAllLines(Path.Combine(directory, $"{name}-by-functioncode.tsv"),
        ["FunctionCode\tAffectedFunctionCount\tOccurrenceCount\tPercentageOfUnsupportedFunctions\tCumulativeCoverageOpportunity",
        ..byCode.Select(item => { cumulativeKeys.UnionWith(item.Keys); cumulative = cumulativeKeys.Count; return $"{item.Code}\t{item.Affected}\t{item.Occurrences}\t{(total == 0 ? 0 : (double)item.Affected / total):P2}\t{cumulative}"; })], new UTF8Encoding(false));
    var byReason = observations.GroupBy(static observation => observation.FirstReason).OrderByDescending(static group => group.Count()).Select(static group => $"{group.Key}\t{group.Count()}");
    File.WriteAllLines(Path.Combine(directory, $"{name}-by-reason.tsv"), ["FirstUnsupportedReason\tAffectedFunctionCount", ..byReason], new UTF8Encoding(false));
    File.WriteAllLines(Path.Combine(directory, $"{name}-pareto.txt"),
        [$"unsupportedFunctions={total}", $"unsupportedOccurrences={observations.Sum(static observation => observation.Blockers.Length)}", "Top blockers are sorted by distinct affected functions; cumulative opportunity de-duplicates functions across rows.", ..byCode.Take(20).Select(static item => $"{item.Code}: affected={item.Affected} occurrences={item.Occurrences}"), "theoreticalOpportunity=not additive across overlapping blockers"], new UTF8Encoding(false));
    var selectedTierA = new[] { "SET", "RESETCOLOR", "CUSTOMDRAWLINE", "SETCOLOR", "SETFONT" };
    var tierA = byCode.Where(item => selectedTierA.Contains(item.Code, StringComparer.OrdinalIgnoreCase)).ToArray();
    var tierB = byCode.Where(static item => item.Code is "CHKFONT" or "GETFONT" or "RESULT" or "RESET_STAIN" or "RESTART").ToArray();
    var tierC = byCode.Except(tierA).Except(tierB).ToArray();
    if (string.IsNullOrEmpty(prefix))
    {
        File.WriteAllLines(Path.Combine(directory, "risk-classification.txt"),
            ["Tier A: implemented exact opcode + raw operand span; no expression/format/control-flow lowering.", "Implemented: SET, RESETCOLOR, CUSTOMDRAWLINE, SETCOLOR, SETFONT.", $"Tier A remaining blockers={tierA.Length} families affected={tierA.Sum(static item => item.Affected)}", "Tier B: command-specific operand or state boundary review required; deferred.", $"Tier B remaining blockers={tierB.Length} families affected={tierB.Sum(static item => item.Affected)}", "Tier C: dynamic call/try-catch, local variables, expression-sensitive, macro/preprocessor, or unknown syntax; deferred.", $"Tier C remaining blockers={tierC.Length} families affected={tierC.Sum(static item => item.Affected)}"], new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "phase1b-selection.txt"), ["Selected family: variable assignment and exact command scanner extensions.", "Selected: SET, RESETCOLOR, CUSTOMDRAWLINE, SETCOLOR, SETFONT", "Risk tier=Tier A", "Why safe now=opcode enum plus source operand span only; no Legacy parser reuse and no semantic lowering.", "Why VM/Expression IR不要=compiler stores source line and operand offset/length; it does not execute or evaluate the operand.", "Expected coverage gain=measured after implementation; exact attribution is in coverage-gain-attribution.txt", "Explicitly deferred: CALLFORM because dynamic target/format semantics need a later phase."], new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "deferred-families.txt"), ["Deferred families remain Unsupported; no coverage inflation by unsafe acceptance.", ..tierB.Concat(tierC).Take(30).Select(static item => $"{item.Code}\taffected={item.Affected}\toccurrences={item.Occurrences}\treason=needs command/state, expression, control-flow, macro, or dynamic-name review\tneeded=Phase 1C/Phase 2/Phase 3")], new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "unsupported-representatives.txt"), observations.Take(40).Select(static observation => $"file={observation.Candidate.Row.RelativeFile} name={observation.Candidate.Row.FunctionName} line={observation.Candidate.Row.StartLine} firstReason={observation.FirstReason} detail={observation.Detail ?? ""} blockers={string.Join(",", observation.Blockers.Distinct(StringComparer.OrdinalIgnoreCase))}"), new UTF8Encoding(false));
    }
    else
    {
        File.WriteAllLines(Path.Combine(directory, $"{name}-risk-classification.txt"), ["Pre-Phase 1B unsupported distribution; selected families are forced blockers for before/after comparison.", $"unsupportedFunctions={total}", $"tierASelectedFamilies={string.Join(",", selectedTierA)}", "Tier B/C exact classification is based on the post-implementation residual report."], new UTF8Encoding(false));
    }
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
static long LineStartByte(string path, int targetLine)
{
    if (targetLine <= 1) return 0;
    var bytes = File.ReadAllBytes(path);
    var line = 1;
    for (var offset = 0; offset < bytes.Length; offset++)
        if (bytes[offset] == (byte)'\n' && ++line == targetLine) return offset + 1;
    return -1;
}
static (int Line, long Byte) FindLegacyHeader(string path, int endLine, string? functionName)
{
    if (functionName is null) return (0, -1);
    var lines = File.ReadAllLines(path);
    for (var line = Math.Min(endLine, lines.Length) - 1; line >= 0; line--)
        if (lines[line].Contains("@" + functionName, StringComparison.OrdinalIgnoreCase)) return (line + 1, LineStartByte(path, line + 1));
    return (0, -1);
}
static void Add(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
static string Key(string file, int line) => $"{file.ToUpperInvariant()}:{line}";
static long Median(IEnumerable<long> values) { var sorted = values.Order().ToArray(); return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2]; }
static long Percentile(long[] values, int percentile) => values.Length == 0 ? 0 : values[(int)Math.Round((values.Length - 1) * percentile / 100.0)];
static StatsRow Stats(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted.Length == 0 ? new(0, 0, 0, 0) : new(sorted[sorted.Length / 2], sorted.Average(), sorted[0], sorted[^1]); }
readonly record struct BenchmarkRow(int Run, double TotalElapsedMs, double SourceReadElapsedMs, double CompilerElapsedMs, long SourceReadAllocatedBytes, long CompilerAllocatedBytes, long TotalAllocatedBytes, int Compiled, int Instructions);
readonly record struct StatsRow(double Median, double Mean, double Min, double Max);
readonly record struct Candidate(LegacyRow Row, SourceFileIndex File, FunctionIndex Function);
readonly record struct RetainedMeasurement(List<CompiledFunction> Compiled, Dictionary<string, CompiledFunction> CompiledByKey, Dictionary<string, SourceFingerprint> Fingerprints);
readonly record struct PureRetainedMeasurement(List<CompiledFunction> Compiled, long BeforeCompile, long ImmediatelyAfterCompile, long AfterDiagnosticGc, long AfterRootReleased);
readonly record struct UnsupportedObservation(Candidate Candidate, UnsupportedReason FirstReason, string? Detail, string[] Blockers)
{
    public string Key => $"{Candidate.Row.RelativeFile}:{Candidate.Row.StartLine}";
}
sealed class LegacyRow
{
    public string RelativeFile { get; set; } = ""; public int FunctionOrder { get; set; } public string? FunctionName { get; set; }
    public int StartLine { get; set; } public bool IsEvent { get; set; } public bool IsSystem { get; set; } public bool IsMethod { get; set; } public bool IsError { get; set; }
    public bool IsPri { get; set; } public bool IsLater { get; set; } public bool IsOnly { get; set; } public bool IsSingle { get; set; }
    public string? Kind { get; set; } public string[]? InstructionCodes { get; set; } public int[]? InstructionLines { get; set; }
}
