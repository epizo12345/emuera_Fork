using System.Diagnostics;
using System.Text.Json;
using MinorShift.Emuera.Next.Core;

if (args.Length == 0 || args.Any(static a => a is "-h" or "--help"))
{
    Console.Error.WriteLine("Usage: Emuera.Next.IndexAudit <erb-directory> [--json <manifest.json>]");
    return args.Length == 0 ? 2 : 0;
}

var directory = Path.GetFullPath(args[0]);
string? jsonPath = null;
for (var i = 1; i < args.Length; i++)
    if (args[i] == "--json" && ++i < args.Length) jsonPath = Path.GetFullPath(args[i]);

var indexAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
var indexManagedBefore = GC.GetTotalMemory(false);
var indexWatch = Stopwatch.StartNew();
IReadOnlyList<SourceFileIndex> files;
try
{
    files = ErbSourceIndexer.IndexDirectory(directory);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}
indexWatch.Stop();
var indexAllocated = GC.GetTotalAllocatedBytes(precise: true) - indexAllocatedBefore;
var indexManagedAfter = GC.GetTotalMemory(false);

var postAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
var postWatch = Stopwatch.StartNew();
var functions = files.SelectMany(static f => f.Functions.Select(function => (File: f, Function: function))).ToArray();
var sizes = functions.Select(static x => x.Function.Span.ByteLength).Order().ToArray();
var totalBytes = files.Sum(static f => f.SourceBytes);
var indexedBytes = functions.Sum(static x => x.Function.Span.ByteLength);
var fallbackFiles = files.Count(static f => f.HasFallback);
var fallbackFunctions = functions.Count(static x => (x.Function.Flags & SourceFileIndex.FallbackFlags) != 0);
var preprocessorFiles = files.Count(static f => (f.Flags & SourceIndexFlags.Preprocessor) != 0);
var preprocessorFunctions = functions.Count(static x => (x.Function.Flags & SourceIndexFlags.Preprocessor) != 0);
var declarationFiles = files.Count(static f => (f.Flags & SourceIndexFlags.DeclarationDirective) != 0);
var declarationFunctions = functions.Count(static x => (x.Function.Flags & SourceIndexFlags.DeclarationDirective) != 0);
var metadataFiles = files.Count(static f => (f.Flags & SourceIndexFlags.FunctionMetadata) != 0);
var metadataFunctions = functions.Count(static x => (x.Function.Flags & SourceIndexFlags.FunctionMetadata) != 0);
var renameFiles = files.Count(static f => (f.Flags & SourceIndexFlags.Rename) != 0);
var renameFunctions = functions.Count(static x => (x.Function.Flags & SourceIndexFlags.Rename) != 0);
var continuationFiles = files.Count(static f => (f.Flags & SourceIndexFlags.LineContinuation) != 0);
var continuationFunctions = functions.Count(static x => (x.Function.Flags & SourceIndexFlags.LineContinuation) != 0);
var parenthesizedHeaders = files.Sum(static f => f.ParenthesizedFunctionHeaderCount);
var quotedAtSigns = files.Sum(static f => f.QuotedAtSignLineCount);
var invalidCandidates = files.Sum(static f => f.InvalidFunctionCandidateCount);
var rejectedCandidates = files.Sum(static f => f.RejectedAtCandidateCount);
var reasonCounts = functions.SelectMany(static x => Reasons(x.Function.Flags))
    .GroupBy(static reason => reason).OrderBy(static group => group.Key)
    .ToDictionary(static group => group.Key, static group => group.Count());
var largest = functions.OrderByDescending(static x => x.Function.Span.ByteLength).FirstOrDefault();
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
var retainedIndexManaged = GC.GetTotalMemory(false);
var postWatchElapsed = postWatch.Elapsed;
postWatch.Stop();
var postAllocated = GC.GetTotalAllocatedBytes(precise: true) - postAllocatedBefore;
var indexSeconds = Math.Max(indexWatch.Elapsed.TotalSeconds, double.Epsilon);
var errors = files.Count(static f => (f.Flags & (SourceIndexFlags.MissingBom | SourceIndexFlags.InvalidUtf8 | SourceIndexFlags.ScanError)) != 0);

Console.WriteLine($"erbFileCount: {files.Count}");
Console.WriteLine($"sourceBytes: {totalBytes}");
Console.WriteLine($"indexedFunctionCount: {functions.Length}");
Console.WriteLine($"indexedFunctionSourceBytes: {indexedBytes}");
Console.WriteLine($"fallbackFileCount: {fallbackFiles}");
Console.WriteLine($"fallbackFunctionCount: {fallbackFunctions}");
Console.WriteLine($"preprocessorFiles: {preprocessorFiles}");
Console.WriteLine($"preprocessorFunctions: {preprocessorFunctions}");
Console.WriteLine($"declarationDirectiveFiles: {declarationFiles}");
Console.WriteLine($"declarationDirectiveFunctions: {declarationFunctions}");
Console.WriteLine($"functionMetadataFiles: {metadataFiles}");
Console.WriteLine($"functionMetadataFunctions: {metadataFunctions}");
Console.WriteLine($"renameFiles: {renameFiles}");
Console.WriteLine($"renameFunctions: {renameFunctions}");
Console.WriteLine($"lineContinuationFiles: {continuationFiles}");
Console.WriteLine($"lineContinuationFunctions: {continuationFunctions}");
Console.WriteLine($"parenthesizedFunctionHeaderCount: {parenthesizedHeaders}");
Console.WriteLine($"quotedAtSignLineCount: {quotedAtSigns}");
Console.WriteLine($"invalidFunctionCandidateCount: {invalidCandidates}");
Console.WriteLine($"rejectedAtCandidateCount: {rejectedCandidates}");
Console.WriteLine($"fallbackReasonBreakdown: {JsonSerializer.Serialize(reasonCounts)}");
Console.WriteLine($"largestFunction: {(largest == default ? "none" : $"{largest.Function.Name} {largest.Function.Span.ByteLength} bytes / {largest.Function.Span.LineCount} lines")}");
Console.WriteLine($"medianFunctionBytes: {Percentile(sizes, .50)}");
Console.WriteLine($"p95FunctionBytes: {Percentile(sizes, .95)}");
Console.WriteLine($"p99FunctionBytes: {Percentile(sizes, .99)}");
Console.WriteLine($"errors: {errors}");
Console.WriteLine($"indexBuildElapsedMs: {indexWatch.Elapsed.TotalMilliseconds:F3}");
Console.WriteLine($"indexBuildAllocatedBytes: {indexAllocated}");
Console.WriteLine($"indexBuildManagedBefore: {indexManagedBefore}");
Console.WriteLine($"indexBuildManagedAfter: {indexManagedAfter}");
Console.WriteLine($"indexBuildManagedDelta: {indexManagedAfter - indexManagedBefore}");
Console.WriteLine($"retainedIndexManagedBytesEstimate: {retainedIndexManaged}");
Console.WriteLine($"auditPostProcessElapsedMs: {postWatchElapsed.TotalMilliseconds:F3}");
Console.WriteLine($"auditPostProcessAllocatedBytes: {postAllocated}");
Console.WriteLine($"MB/sec: {totalBytes / 1024d / 1024d / indexSeconds:F3}");
Console.WriteLine($"functions/sec: {functions.Length / indexSeconds:F3}");

if (jsonPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { directory, files }, new JsonSerializerOptions { WriteIndented = true }));
}

return 0;

static IEnumerable<string> Reasons(SourceIndexFlags flags)
{
    if ((flags & SourceIndexFlags.Preprocessor) != 0) yield return nameof(SourceIndexFlags.Preprocessor);
    if ((flags & SourceIndexFlags.DeclarationDirective) != 0) yield return nameof(SourceIndexFlags.DeclarationDirective);
    if ((flags & SourceIndexFlags.FunctionMetadata) != 0) yield return nameof(SourceIndexFlags.FunctionMetadata);
    if ((flags & SourceIndexFlags.Rename) != 0) yield return nameof(SourceIndexFlags.Rename);
    if ((flags & SourceIndexFlags.LineContinuation) != 0) yield return nameof(SourceIndexFlags.LineContinuation);
    if ((flags & SourceIndexFlags.OtherSemanticFallback) != 0) yield return nameof(SourceIndexFlags.OtherSemanticFallback);
}

static long Percentile(long[] values, double percentile)
    => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * percentile) - 1)];
