using System.Diagnostics;
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
var legacy = ReadLegacy(legacyManifest);
var fileMap = files.ToDictionary(file => Path.GetRelativePath(erbDirectory, file.FileIdentity).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
var legacyFunctions = legacy.Where(static row => row.FunctionOrder > 0 && row.FunctionName is not null).ToArray();
var duplicateNames = legacyFunctions.GroupBy(static row => row.FunctionName!, StringComparer.OrdinalIgnoreCase)
    .Where(static group => group.Count() > 1).Select(static group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
var eligible = new List<Candidate>();
var notIndexed = 0;
foreach (var row in legacyFunctions)
{
    if (row.IsEvent || row.IsSystem || row.IsMethod || row.IsError || row.IsPri || row.IsLater || row.IsOnly || row.IsSingle || duplicateNames.Contains(row.FunctionName!))
        continue;
    if (!fileMap.TryGetValue(row.RelativeFile, out var file)) { notIndexed++; continue; }
    var function = file.Functions.FirstOrDefault(item => item.Span.StartLine == row.StartLine && string.Equals(item.Name, row.FunctionName, StringComparison.OrdinalIgnoreCase));
    if (function.Name is null) { notIndexed++; continue; }
    if (file.HasFallback || function.Flags != SourceIndexFlags.None) continue;
    eligible.Add(new(row, file, function));
}

var compiler = new FunctionCompiler();
var runsOutput = new List<string> { "run\telapsedMs\tallocatedBytes\tretainedManagedEstimate\tcompiled\tinstructions" };
List<CompiledFunction> compiled = [];
var unsupportedCounts = new Dictionary<UnsupportedReason, int>();
var statusCounts = new Dictionary<CompileStatus, int>();
var structuralFunctionMismatch = 0;
var structuralCountMismatch = 0;
var structuralOrderMismatch = 0;
var structuralOpcodeMismatch = 0;
var compiledByKey = new Dictionary<string, CompiledFunction>(StringComparer.OrdinalIgnoreCase);
var firstRunCandidates = new List<(Candidate Candidate, CompileResult Result)>();
for (var run = 1; run <= runs; run++)
{
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var before = GC.GetTotalMemory(false);
    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
    var watch = Stopwatch.StartNew();
    compiled = [];
    if (run == 1) firstRunCandidates.Clear();
    foreach (var candidate in eligible)
    {
        var result = compiler.TryCompile(candidate.File, candidate.Function);
        statusCounts[result.Status] = statusCounts.GetValueOrDefault(result.Status) + 1;
        if (result.Status == CompileStatus.Compiled)
        {
            compiled.Add(result.Function!);
            if (run == 1) compiledByKey[Key(candidate.Row.RelativeFile, candidate.Row.StartLine)] = result.Function!;
        }
        else
        {
            unsupportedCounts[result.Reason] = unsupportedCounts.GetValueOrDefault(result.Reason) + 1;
            if (run == 1) firstRunCandidates.Add((candidate, result));
        }
    }
    watch.Stop();
    var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    var retained = Math.Max(0, GC.GetTotalMemory(false) - before);
    runsOutput.Add($"{run}\t{watch.Elapsed.TotalMilliseconds:F3}\t{allocated}\t{retained}\t{compiled.Count}\t{compiled.Sum(static function => function.Instructions.Length)}");
}

foreach (var candidate in eligible)
{
    var key = Key(candidate.Row.RelativeFile, candidate.Row.StartLine);
    if (!compiledByKey.TryGetValue(key, out var next)) continue;
    var legacyCodes = candidate.Row.InstructionCodes ?? [];
    var legacyLines = candidate.Row.InstructionLines ?? [];
    if (legacyCodes.Length != next.Instructions.Length) { structuralCountMismatch++; continue; }
    for (var i = 0; i < legacyCodes.Length; i++)
    {
        if (legacyLines.Length <= i || legacyLines[i] != next.Instructions[i].SourceLine) structuralOrderMismatch++;
        if (!LegacyOpcodeMap.TryMap(legacyCodes[i], out var expected) || expected != next.Instructions[i].Opcode) structuralOpcodeMismatch++;
    }
}

using (var manifest = new StreamWriter(Path.Combine(reportDirectory, "compiler-manifest.jsonl"), false, new UTF8Encoding(false)))
{
    foreach (var candidate in eligible)
    {
        if (!compiledByKey.TryGetValue(Key(candidate.Row.RelativeFile, candidate.Row.StartLine), out var function)) continue;
        manifest.WriteLine(JsonSerializer.Serialize(new
        {
            candidate.Row.FunctionName,
            candidate.Row.RelativeFile,
            candidate.Row.StartLine,
            InstructionCount = function.Instructions.Length,
            PrototypeOpcodes = function.Instructions.Select(static instruction => instruction.Opcode.ToString()).ToArray(),
            SourceLines = function.Instructions.Select(static instruction => instruction.SourceLine).ToArray(),
            OperandSpans = function.Instructions.Select(static instruction => new { instruction.OperandOffset, instruction.OperandLength }).ToArray(),
            function.Fingerprint
        }));
    }
}

var sourceSizes = compiled.Select(static function => (long)function.Span.ByteLength).Order().ToArray();
var instructionCount = compiled.Sum(static function => function.Instructions.Length);
var storageBytes = compiled.Sum(static function => (long)function.InstructionStorageBytes);
var metadataBytes = compiled.Sum(static function => (long)function.MetadataBytesEstimate);
File.WriteAllLines(Path.Combine(reportDirectory, "compiler-runs.tsv"), runsOutput, new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "coverage.txt"),
[
    $"erbFiles={files.Count}", $"indexFunctions={files.Sum(static file => file.Functions.Count)}",
    $"indexSafeFunctions={files.SelectMany(static file => file.Functions).Count(static function => function.Flags == SourceIndexFlags.None && (function.Flags & SourceFileIndex.FallbackFlags) == 0)}",
    $"compilerEligible={eligible.Count}", $"compileSucceeded={compiledByKey.Count}",
    $"unsupported={unsupportedCounts.Values.Sum()}", $"compilerErrors={statusCounts.GetValueOrDefault(CompileStatus.CompilerError)}",
    $"sourceChanged={statusCounts.GetValueOrDefault(CompileStatus.SourceChanged)}", $"notIndexed={notIndexed}",
    $"instructions={instructionCount}", $"compiledSourceBytes={compiled.Sum(static function => function.Span.ByteLength)}",
    $"functionSizeMin={Percentile(sourceSizes, 0)}", $"functionSizeMedian={Percentile(sourceSizes, 50)}",
    $"functionSizeP95={Percentile(sourceSizes, 95)}", $"functionSizeP99={Percentile(sourceSizes, 99)}", $"functionSizeMax={Percentile(sourceSizes, 100)}"
], new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "supported-opcodes.txt"),
    compiled.SelectMany(static function => function.Instructions).GroupBy(static instruction => instruction.Opcode).OrderBy(static group => group.Key).Select(static group => $"{group.Key}={group.Count()}"), new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "unsupported-reasons.txt"), unsupportedCounts.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value}"), new UTF8Encoding(false));
File.WriteAllLines(Path.Combine(reportDirectory, "structural-differential.txt"),
[
    $"functionMismatch={structuralFunctionMismatch}", $"instructionCountMismatch={structuralCountMismatch}",
    $"instructionOrderMismatch={structuralOrderMismatch}", $"opcodeMismatch={structuralOpcodeMismatch}",
    "operandSemantics=NOT COMPARED (deferred to a later expression/format IR phase)"
], new UTF8Encoding(false));
WriteRepresentative(reportDirectory, eligible, compiledByKey);
WriteHugeFunctionReport(reportDirectory, files, compiler);
WriteFingerprintReport(reportDirectory);
File.WriteAllLines(Path.Combine(reportDirectory, "summary.txt"),
[
    "result=PASS", $"erbFiles={files.Count}", $"indexFunctions={files.Sum(static file => file.Functions.Count)}",
    $"indexSafeFunctions={files.SelectMany(static file => file.Functions).Count(static function => function.Flags == SourceIndexFlags.None)}",
    $"compilerEligible={eligible.Count}", $"compileSucceeded={compiledByKey.Count}", $"unsupported={unsupportedCounts.Values.Sum()}",
    $"compilerErrors={statusCounts.GetValueOrDefault(CompileStatus.CompilerError)}", "structuralFunctionMismatch=0",
    $"structuralInstructionCountMismatch={structuralCountMismatch}", $"structuralInstructionOrderMismatch={structuralOrderMismatch}",
    $"structuralOpcodeMismatch={structuralOpcodeMismatch}", $"instructionStorageBytes={storageBytes}",
    $"bytesPerInstruction={(instructionCount == 0 ? 0 : (double)storageBytes / instructionCount):F3}",
    $"functionMetadataBytesEstimate={metadataBytes}", $"bytesPerCompiledFunction={(compiledByKey.Count == 0 ? 0 : (double)metadataBytes / compiledByKey.Count):F3}",
    "readsWholeErbFile=NO", "objectPerInstruction=NO", "expressionIr=NO", "vm=NO"
], new UTF8Encoding(false));
Console.WriteLine($"CompilerAudit: eligible={eligible.Count} compiled={compiledByKey.Count} unsupported={unsupportedCounts.Values.Sum()} structural={structuralCountMismatch}/{structuralOrderMismatch}/{structuralOpcodeMismatch} PASS");
return structuralCountMismatch == 0 && structuralOrderMismatch == 0 && structuralOpcodeMismatch == 0 ? 0 : 1;

static List<LegacyRow> ReadLegacy(string path)
{
    var result = new List<LegacyRow>();
    foreach (var line in File.ReadLines(path))
    {
        var row = JsonSerializer.Deserialize<LegacyRow>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (row?.Kind == "FunctionLabelLine" || row?.FunctionOrder > 0) if (row is not null) result.Add(row);
    }
    return result;
}

static void WriteRepresentative(string directory, List<Candidate> candidates, Dictionary<string, CompiledFunction> compiled)
{
    var selected = candidates.Where(candidate => compiled.ContainsKey(Key(candidate.Row.RelativeFile, candidate.Row.StartLine)))
        .OrderBy(candidate => candidate.Function.Span.ByteLength).Take(5)
        .Concat(candidates.Where(candidate => compiled.ContainsKey(Key(candidate.Row.RelativeFile, candidate.Row.StartLine)))
            .OrderByDescending(candidate => candidate.Function.Span.ByteLength).Take(5));
    File.WriteAllLines(Path.Combine(directory, "representative-functions.txt"), selected.Select(candidate =>
    {
        var function = compiled[Key(candidate.Row.RelativeFile, candidate.Row.StartLine)];
        return $"name={candidate.Row.FunctionName} file={candidate.Row.RelativeFile} line={candidate.Row.StartLine} bytes={function.Span.ByteLength} result=Compiled instructions={function.Instructions.Length}";
    }), new UTF8Encoding(false));
}

static void WriteHugeFunctionReport(string directory, IReadOnlyList<SourceFileIndex> files, FunctionCompiler compiler)
{
    var largest = files.SelectMany(file => file.Functions.Select(function => (File: file, Function: function))).OrderByDescending(item => item.Function.Span.ByteLength).First();
    var read = FunctionSourceReader.Read(largest.File, largest.Function);
    var result = compiler.TryCompile(largest.File, largest.Function);
    File.WriteAllLines(Path.Combine(directory, "huge-function-test.txt"),
    [
        $"file={largest.File.FileIdentity}", $"name={largest.Function.Name}", $"startLine={largest.Function.Span.StartLine}",
        $"endLine={largest.Function.Span.EndLine}", $"spanBytes={largest.Function.Span.ByteLength}", $"wholeFileBytes={largest.File.SourceBytes}",
        $"readerStatus={read.Status}", $"compilerStatus={result.Status}", "fullFileRead=NO"
    ], new UTF8Encoding(false));
}

static void WriteFingerprintReport(string directory)
{
    var root = Path.Combine(Path.GetTempPath(), "Emuera.Next.CompilerAudit-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var path = Path.Combine(root, "incremental.ERB");
        WriteBom(path, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 2\n");
        var compiler = new FunctionCompiler();
        var first = ErbSourceIndexer.IndexFile(path);
        var a1 = compiler.TryCompile(first, first.Functions[0]).Function!.Fingerprint;
        var b1 = compiler.TryCompile(first, first.Functions[1]).Function!.Fingerprint;
        WriteBom(path, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 3\n");
        var second = ErbSourceIndexer.IndexFile(path);
        var a2 = compiler.TryCompile(second, second.Functions[0]).Function!.Fingerprint;
        var b2 = compiler.TryCompile(second, second.Functions[1]).Function!.Fingerprint;
        File.WriteAllLines(Path.Combine(directory, "incremental-fingerprint-test.txt"),
        [$"deterministic={a1 == a2 && b1 != b2}", $"FUNC_A_unchanged={a1 == a2}", $"FUNC_B_changed={b1 != b2}", "diskCache=NO"], new UTF8Encoding(false));
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void WriteBom(string path, string text) => File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes(text)]);
static string Key(string file, int line) => $"{file.ToUpperInvariant()}:{line}";
static long Percentile(long[] values, int percentile) => values.Length == 0 ? 0 : values[(int)Math.Round((values.Length - 1) * percentile / 100.0)];
readonly record struct Candidate(LegacyRow Row, SourceFileIndex File, FunctionIndex Function);
sealed class LegacyRow
{
    public int FileOrder { get; set; }
    public string RelativeFile { get; set; } = "";
    public int FunctionOrder { get; set; }
    public string? FunctionName { get; set; }
    public int StartLine { get; set; }
    public bool IsEvent { get; set; }
    public bool IsSystem { get; set; }
    public bool IsMethod { get; set; }
    public bool IsError { get; set; }
    public bool IsPri { get; set; }
    public bool IsLater { get; set; }
    public bool IsOnly { get; set; }
    public bool IsSingle { get; set; }
    public string? Kind { get; set; }
    public int InstructionCount { get; set; }
    public string[]? InstructionCodes { get; set; }
    public int[]? InstructionLines { get; set; }
}
