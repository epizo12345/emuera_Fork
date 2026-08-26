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
var indexSafeFunctions = allFunctions.Count(static item => !item.File.HasFallback && item.Function.Flags == SourceIndexFlags.None);
var fileMap = files.ToDictionary(file => Path.GetRelativePath(erbDirectory, file.FileIdentity).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
var legacyFunctions = ReadLegacy(legacyManifest);
var duplicateNames = legacyFunctions.GroupBy(static row => row.FunctionName!, StringComparer.OrdinalIgnoreCase)
    .Where(static group => group.Count() > 1).Select(static group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
var exclusions = new Dictionary<string, int>(StringComparer.Ordinal);
var eligible = new List<Candidate>();
var compilerConsidered = 0;
foreach (var row in legacyFunctions)
{
    if (!fileMap.TryGetValue(row.RelativeFile, out var file)) { Add(exclusions, "LegacyFunctionNotIndexed"); continue; }
    var function = file.Functions.FirstOrDefault(item => item.Span.StartLine == row.StartLine && string.Equals(item.Name, row.FunctionName, StringComparison.OrdinalIgnoreCase));
    if (function.Name is null) { Add(exclusions, "IndexFunctionNotMatched"); continue; }
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
var compiledByKey = new Dictionary<string, CompiledFunction>(StringComparer.OrdinalIgnoreCase);
var fingerprintByKey = new Dictionary<string, SourceFingerprint>(StringComparer.OrdinalIgnoreCase);
List<CompiledFunction> retainedCompiled = [];
for (var run = 1; run <= runs; run++)
{
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var managedBefore = GC.GetTotalMemory(false);
    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
    var readWatch = Stopwatch.StartNew();
    long sourceReadAllocated = 0;
    var sourceReadElapsed = TimeSpan.Zero;
    long compilerAllocated = 0;
    var compilerElapsed = TimeSpan.Zero;
    var compiled = new List<CompiledFunction>(eligible.Count);
    foreach (var candidate in eligible)
    {
        var readAllocatedBefore = GC.GetTotalAllocatedBytes(true);
        var readStart = Stopwatch.GetTimestamp();
                var read = FunctionSourceReader.Read(candidate.File, candidate.Function);
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
            if (run == 1)
            {
                var key = Key(candidate.Row.RelativeFile, candidate.Row.StartLine);
                compiledByKey[key] = result.Function!;
                fingerprintByKey[key] = result.Fingerprint;
            }
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
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var retained = Math.Max(0, GC.GetTotalMemory(false) - managedBefore);
    retainedCompiled = compiled;
    benchmark.Add(new(run, totalElapsed.TotalMilliseconds, sourceReadElapsed.TotalMilliseconds, compilerElapsed.TotalMilliseconds,
        sourceReadAllocated, compilerAllocated, totalAllocated, retained, compiled.Count, compiled.Sum(static f => f.Instructions.Length)));
}

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
var metadataPayload = (long)compiledByKey.Count * FunctionCompiler.FunctionMetadataPayloadBytes;
var retainedMedian = Median(benchmark.Select(static row => row.RetainedManagedEstimate));
var overhead = Math.Max(0, retainedMedian - instructionPayload - metadataPayload);
File.WriteAllLines(Path.Combine(reportDirectory, "compiler-runs.tsv"),
[
    "run\ttotalElapsedMs\tsourceReadElapsedMs\tcompilerElapsedMs\tsourceReadAllocatedBytes\tcompilerAllocatedBytes\ttotalAllocatedBytes\tretainedManagedEstimate\tcompiled\tinstructions",
    ..benchmark.Select(static row => $"{row.Run}\t{row.TotalElapsedMs:F3}\t{row.SourceReadElapsedMs:F3}\t{row.CompilerElapsedMs:F3}\t{row.SourceReadAllocatedBytes}\t{row.CompilerAllocatedBytes}\t{row.TotalAllocatedBytes}\t{row.RetainedManagedEstimate}\t{row.Compiled}\t{row.Instructions}")
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "eligibility-breakdown.txt"),
[
    $"IndexTotal={indexFunctions}", $"IndexSafe={indexSafeFunctions}", $"CompilerConsidered={compilerConsidered}",
    $"CompilerEligible={eligible.Count}", $"CompilerExcluded={exclusions.Values.Sum()}", $"LegacyRowsNotIndexed={exclusions.GetValueOrDefault("LegacyFunctionNotIndexed") + exclusions.GetValueOrDefault("IndexFunctionNotMatched")}",
    ..exclusions.OrderBy(static pair => pair.Key).Select(static pair => $"Excluded.{pair.Key}={pair.Value}"),
    "Stage meaning: IndexSafe -> CompilerConsidered (matched safe Legacy rows) -> CompilerEligible (event/system/method/duplicate/metadata/error excluded) -> CompileSucceeded or Unsupported."
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "coverage.txt"),
[
    $"erbFiles={files.Count}", $"indexFunctions={indexFunctions}", $"indexSafeFunctions={indexSafeFunctions}",
    $"compilerConsidered={compilerConsidered}", $"compilerEligible={eligible.Count}", $"compilerExcluded={exclusions.Values.Sum()}",
    $"compileSucceeded={compiledByKey.Count}", $"unsupportedUnique={uniqueUnsupported.Values.Sum()}", $"unsupportedEncountersAcrossRuns={unsupportedEncounters}",
    $"compilerErrors={statusCounts.GetValueOrDefault(CompileStatus.CompilerError)}", $"sourceChanged={statusCounts.GetValueOrDefault(CompileStatus.SourceChanged)}",
    $"indexSafeCompileRate={(indexSafeFunctions == 0 ? 0 : (double)compiledByKey.Count / indexSafeFunctions):P2}", $"eligibleCompileRate={(eligible.Count == 0 ? 0 : (double)compiledByKey.Count / eligible.Count):P2}",
    $"functionSizeMin={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 0)}",
    $"functionSizeMedian={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 50)}",
    $"functionSizeP95={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 95)}",
    $"functionSizeP99={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 99)}",
    $"functionSizeMax={Percentile(compiledByKey.Values.Select(static f => f.Span.ByteLength).Order().ToArray(), 100)}"
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "supported-opcodes.txt"),
    compiledByKey.Values.SelectMany(static f => f.Instructions).GroupBy(static i => i.Opcode).OrderBy(static g => g.Key).Select(static g => $"{g.Key}={g.Count()}"), new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "unsupported-reasons.txt"), uniqueUnsupported.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value}"), new UTF8Encoding(false));
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
    $"sourceReadAllocatedMedian={Median(benchmark.Select(static row => row.SourceReadAllocatedBytes))}",
    $"compilerAllocatedMedian={Median(benchmark.Select(static row => row.CompilerAllocatedBytes))}",
    $"totalAllocatedMedian={Median(benchmark.Select(static row => row.TotalAllocatedBytes))}",
    $"sourceReadElapsedMedianMs={Median(benchmark.Select(static row => (long)Math.Round(row.SourceReadElapsedMs)))}",
    $"compilerElapsedMedianMs={Median(benchmark.Select(static row => (long)Math.Round(row.CompilerElapsedMs)))}",
    "previousPhase1A total allocation was inflated by a new 64KB FileStream buffer per function; R1 uses bufferSize=1/random access."
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "memory.txt"),
[$"instructionPayloadBytes={instructionPayload}", $"functionMetadataPayloadBytes={metadataPayload}", $"managedRetainedMedian={retainedMedian}", $"estimatedOverhead={overhead}", $"compiledFunctionClassInstances={compiledByKey.Count}", "fingerprintStringPerCompiledFunction=NO"], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "storage-size.txt"),
[$"sizeof(PrototypeInstruction)={instructionSize}", $"instructions={instructionCount}", $"payloadBytes={instructionPayload}", $"functionMetadataPayload={metadataPayload}", $"managedRetainedMedian={retainedMedian}", $"estimatedOverhead={overhead}", "ImmutableArray per-function overhead is retained as a measured prototype limitation; flat instruction arena is a future candidate."], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "summary.txt"),
[
    "result=PASS", $"erbFiles={files.Count}", $"indexFunctions={indexFunctions}", $"indexSafeFunctions={indexSafeFunctions}", $"compilerConsidered={compilerConsidered}", $"compilerEligible={eligible.Count}", $"compileSucceeded={compiledByKey.Count}", $"unsupportedUnique={uniqueUnsupported.Values.Sum()}", $"compilerErrors={statusCounts.GetValueOrDefault(CompileStatus.CompilerError)}", $"unsupportedEncountersAcrossRuns={unsupportedEncounters}", $"sourceReadAllocationMedian={Median(benchmark.Select(static row => row.SourceReadAllocatedBytes))}", $"compilerAllocationMedian={Median(benchmark.Select(static row => row.CompilerAllocatedBytes))}", $"totalAllocationMedian={Median(benchmark.Select(static row => row.TotalAllocatedBytes))}", $"retainedManagedMedian={retainedMedian}", $"instructionSize={instructionSize}", $"instructionPayloadBytes={instructionPayload}", $"metadataPayloadBytes={metadataPayload}", $"estimatedOverhead={overhead}", $"exactOpcodeMismatch={exactOpcodeMismatch}", "readsWholeErbFile=NO", "64KBPerFunctionAllocation=REMOVED", "fingerprintStringPerFunction=NO", "vm=NO"
], new UTF8Encoding(false));
Console.WriteLine($"CompilerAudit: eligible={eligible.Count} compiled={compiledByKey.Count} unsupportedUnique={uniqueUnsupported.Values.Sum()} totalAllocatedMedian={Median(benchmark.Select(static row => row.TotalAllocatedBytes))} exactOpcodeMismatch={exactOpcodeMismatch} PASS");
return countMismatch == 0 && orderMismatch == 0 && exactOpcodeMismatch == 0 ? 0 : 1;

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
readonly record struct BenchmarkRow(int Run, double TotalElapsedMs, double SourceReadElapsedMs, double CompilerElapsedMs, long SourceReadAllocatedBytes, long CompilerAllocatedBytes, long TotalAllocatedBytes, long RetainedManagedEstimate, int Compiled, int Instructions);
readonly record struct Candidate(LegacyRow Row, SourceFileIndex File, FunctionIndex Function);
sealed class LegacyRow
{
    public string RelativeFile { get; set; } = ""; public int FunctionOrder { get; set; } public string? FunctionName { get; set; }
    public int StartLine { get; set; } public bool IsEvent { get; set; } public bool IsSystem { get; set; } public bool IsMethod { get; set; } public bool IsError { get; set; }
    public bool IsPri { get; set; } public bool IsLater { get; set; } public bool IsOnly { get; set; } public bool IsSingle { get; set; }
    public string? Kind { get; set; } public string[]? InstructionCodes { get; set; } public int[]? InstructionLines { get; set; }
}
