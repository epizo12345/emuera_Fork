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

var beforeAllocated = GC.GetTotalAllocatedBytes(false);
var beforeManaged = GC.GetTotalMemory(false);
var stopwatch = Stopwatch.StartNew();
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
stopwatch.Stop();

var functions = files.SelectMany(static f => f.Functions).ToArray();
var sizes = functions.Select(static f => f.Span.ByteLength).Order().ToArray();
var totalBytes = files.Sum(static f => f.SourceBytes);
var indexedBytes = functions.Sum(static f => f.Span.ByteLength);
var fallbackFiles = files.Count(static f => f.HasFallback);
var fallbackFunctions = functions.Count(static f => f.Flags != SourceIndexFlags.None);
var reasonCounts = functions.SelectMany(static f => (f.FallbackReason ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
    .GroupBy(static reason => reason).OrderBy(static group => group.Key)
    .ToDictionary(static group => group.Key, static group => group.Count());
var largest = functions.OrderByDescending(static f => f.Span.ByteLength).FirstOrDefault();
var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, double.Epsilon);
var summary = new
{
    erbFileCount = files.Count,
    totalSourceBytes = totalBytes,
    indexedFunctionCount = functions.Length,
    indexedFunctionSourceBytes = indexedBytes,
    fallbackFileCount = fallbackFiles,
    fallbackFunctionCount = fallbackFunctions,
    fallbackReasonBreakdown = reasonCounts,
    largestFunction = largest is null ? null : new { largest.FileIdentity, largest.Name, bytes = largest.Span.ByteLength, lines = largest.Span.LineCount },
    medianFunctionBytes = Percentile(sizes, .50),
    p95FunctionBytes = Percentile(sizes, .95),
    p99FunctionBytes = Percentile(sizes, .99),
    errorCount = files.Count(static f => (f.Flags & (SourceIndexFlags.MissingBom | SourceIndexFlags.InvalidUtf8 | SourceIndexFlags.ScanError)) != 0),
    elapsedScanMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
    allocatedBytes = GC.GetTotalAllocatedBytes(false) - beforeAllocated,
    managedMemoryDeltaBytes = GC.GetTotalMemory(false) - beforeManaged,
    megabytesPerSecond = totalBytes / 1024d / 1024d / seconds,
    functionsPerSecond = functions.Length / seconds,
};

Console.WriteLine($"ERB file count: {summary.erbFileCount}");
Console.WriteLine($"total source bytes: {summary.totalSourceBytes}");
Console.WriteLine($"indexed function count: {summary.indexedFunctionCount}");
Console.WriteLine($"indexed function source bytes: {summary.indexedFunctionSourceBytes}");
Console.WriteLine($"fallback file count: {summary.fallbackFileCount}");
Console.WriteLine($"fallback function count: {summary.fallbackFunctionCount}");
Console.WriteLine($"fallback reason breakdown: {JsonSerializer.Serialize(summary.fallbackReasonBreakdown)}");
Console.WriteLine($"largest function: {(summary.largestFunction is null ? "none" : $"{summary.largestFunction.Name} {summary.largestFunction.bytes} bytes / {summary.largestFunction.lines} lines")}");
Console.WriteLine($"median function bytes: {summary.medianFunctionBytes}");
Console.WriteLine($"p95 function bytes: {summary.p95FunctionBytes}");
Console.WriteLine($"p99 function bytes: {summary.p99FunctionBytes}");
Console.WriteLine($"error count: {summary.errorCount}");
Console.WriteLine($"elapsed scan milliseconds: {summary.elapsedScanMilliseconds:F3}");
Console.WriteLine($"allocated bytes: {summary.allocatedBytes}");
Console.WriteLine($"managed memory delta bytes: {summary.managedMemoryDeltaBytes}");
Console.WriteLine($"MB/sec: {summary.megabytesPerSecond:F3}");
Console.WriteLine($"functions/sec: {summary.functionsPerSecond:F3}");

if (jsonPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { directory, files, summary }, new JsonSerializerOptions { WriteIndented = true }));
}

return 0;

static long Percentile(long[] values, double percentile)
    => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * percentile) - 1)];
