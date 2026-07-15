using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

// [Emuera改修:TOOLS-04]
// dotnet-traceで採取した.nettraceを読み、「どの型の一時オブジェクトが多いか」を順位表示する。
// Emuera本体やゲームデータを変更するツールではなく、次の高速化候補を探す調査専用プログラム。
// 使い方と依存ツール: プロジェクト資料/06_コード案内.md
if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: TraceAllocationAnalyzer <trace.nettrace> [top-count]");
    return 1;
}

string tracePath = Path.GetFullPath(args[0]);
int topCount = args.Length >= 2 && int.TryParse(args[1], out int parsed) ? parsed : 50;
Dictionary<string, AllocationSummary> allocations = new(StringComparer.Ordinal);
long sampledBytes = 0;
long sampledCount = 0;

string etlxPath = tracePath + ".etlx";
if (!File.Exists(etlxPath) || File.GetLastWriteTimeUtc(etlxPath) < File.GetLastWriteTimeUtc(tracePath))
    TraceLog.CreateFromEventPipeDataFile(tracePath, etlxPath);
using TraceLog traceLog = new(etlxPath);
using TraceLogEventSource source = traceLog.Events.GetSource();
source.Clr.GCAllocationTick += data =>
{
    string typeName = string.IsNullOrEmpty(data.TypeName) ? "<unknown>" : data.TypeName;
    long bytes = data.AllocationAmount64;
    if (!allocations.TryGetValue(typeName, out AllocationSummary? summary))
    {
        summary = new AllocationSummary();
        allocations.Add(typeName, summary);
    }
    summary.Bytes += bytes;
    summary.SampledObjectBytes += data.ObjectSize;
    summary.Count++;
    string applicationFrame = GetApplicationFrame(data.CallStack());
    if (!summary.Frames.TryGetValue(applicationFrame, out AllocationSummary? frameSummary))
    {
        frameSummary = new AllocationSummary();
        summary.Frames.Add(applicationFrame, frameSummary);
    }
    frameSummary.Bytes += bytes;
    frameSummary.SampledObjectBytes += data.ObjectSize;
    frameSummary.Count++;
    sampledBytes += bytes;
    sampledCount++;
};
source.Process();

Console.WriteLine($"Sampled allocation events: {sampledCount:N0}");
Console.WriteLine($"Sampled allocation bytes:  {sampledBytes:N0}");
Console.WriteLine();
Console.WriteLine("Rank\tEstimatedBytes\tEvents\tObjectBytes\tAvgObjectBytes\tPercent\tType");
int rank = 0;
foreach ((string typeName, AllocationSummary summary) in allocations
             .OrderByDescending(pair => pair.Value.Bytes)
             .Take(topCount))
{
    rank++;
    double percent = sampledBytes == 0 ? 0 : summary.Bytes * 100.0 / sampledBytes;
    long averageObjectBytes = summary.Count == 0 ? 0 : summary.SampledObjectBytes / summary.Count;
    Console.WriteLine($"{rank}\t{summary.Bytes}\t{summary.Count}\t{summary.SampledObjectBytes}\t{averageObjectBytes}\t{percent:F2}\t{typeName}");
}

Console.WriteLine();
Console.WriteLine("Top allocation type/call-site pairs");
Console.WriteLine("Rank\tEstimatedBytes\tEvents\tType\tApplicationFrame");
rank = 0;
foreach (var item in allocations
             .SelectMany(type => type.Value.Frames.Select(frame => new
             {
                 TypeName = type.Key,
                 FrameName = frame.Key,
                 Summary = frame.Value,
             }))
             .OrderByDescending(item => item.Summary.Bytes)
             .Take(topCount))
{
    rank++;
    Console.WriteLine($"{rank}\t{item.Summary.Bytes}\t{item.Summary.Count}\t{item.TypeName}\t{item.FrameName}");
}

return 0;

static string GetApplicationFrame(TraceCallStack? stack)
{
    string? firstManagedFrame = null;
    List<string> applicationFrames = [];
    for (TraceCallStack? current = stack; current != null; current = current.Caller)
    {
        string? method = current.CodeAddress?.FullMethodName;
        if (string.IsNullOrEmpty(method))
            continue;
        firstManagedFrame ??= method;
        if (method.Contains("MinorShift.Emuera", StringComparison.Ordinal))
        {
            applicationFrames.Add(method);
            if (applicationFrames.Count == 3)
                return string.Join(" <- ", applicationFrames);
        }
    }
    return applicationFrames.Count > 0
        ? string.Join(" <- ", applicationFrames)
        : firstManagedFrame ?? "<unknown>";
}

sealed class AllocationSummary
{
    public long Bytes;
    public long SampledObjectBytes;
    public long Count;
    public Dictionary<string, AllocationSummary> Frames { get; } = new(StringComparer.Ordinal);
}
