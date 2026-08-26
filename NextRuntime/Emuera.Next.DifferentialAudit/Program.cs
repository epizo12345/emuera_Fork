using System.Text.Json;
using MinorShift.Emuera.Next.Core;

if (args.Any(static a => a is "-h" or "--help"))
{
    Console.WriteLine("Usage: Emuera.Next.DifferentialAudit <legacy.jsonl> <next.jsonl> <report-directory> [--self-test]");
    return 0;
}

if (args.Length == 1 && args[0] == "--self-test")
    return SelfTest();
if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: Emuera.Next.DifferentialAudit <legacy.jsonl> <next.jsonl> <report-directory>");
    return 2;
}

var legacy = Read(args[0]);
var next = Read(args[1]);
var reportDirectory = Path.GetFullPath(args[2]);
Directory.CreateDirectory(reportDirectory);
var result = Compare(legacy, next);
File.WriteAllText(Path.Combine(reportDirectory, "differential-summary.txt"), result.Summary);
File.WriteAllText(Path.Combine(reportDirectory, "unexpected-differences.txt"), result.Unexpected);
File.WriteAllText(Path.Combine(reportDirectory, "fallback-differences.txt"), result.Fallback);
File.WriteAllText(Path.Combine(reportDirectory, "duplicate-functions.txt"), DuplicateReport(legacy));
File.WriteAllText(Path.Combine(reportDirectory, "case-rules.txt"),
    "Legacy function-name comparison rule is recorded by the actual diagnostic build in legacy-summary.txt.\n" +
    "Differential file matching uses Windows-compatible OrdinalIgnoreCase paths; it does not choose a Next rule.\n");
Console.Write(result.Summary);
return result.BlockerCount == 0 ? 0 : 1;

static List<Row> Read(string path)
{
    var rows = new List<Row>();
    foreach (var line in File.ReadLines(path))
        if (!string.IsNullOrWhiteSpace(line))
            rows.Add(JsonSerializer.Deserialize<Row>(line));
    return rows;
}

static ComparisonResult Compare(List<Row> legacy, List<Row> next)
{
    var legacyFiles = legacy.Where(static row => row.FunctionOrder > 0).Select(static row => row.RelativeFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var nextFiles = next.Where(static row => row.FunctionOrder > 0).Select(static row => row.RelativeFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var nextFallbackFiles = next.Where(static row => row.FunctionOrder > 0 && row.Fallback).Select(static row => row.RelativeFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missingFiles = legacyFiles.Except(nextFiles, StringComparer.OrdinalIgnoreCase).ToArray();
    var extraFiles = nextFiles.Except(legacyFiles, StringComparer.OrdinalIgnoreCase).ToArray();
    var legacyFunctions = legacy.Where(static row => row.FunctionOrder > 0).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
    var nextFunctions = next.Where(static row => row.FunctionOrder > 0).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
    var safeNext = nextFunctions.Values.Where(static row => !row.Fallback).ToArray();
    var fallbackNext = nextFunctions.Values.Where(static row => row.Fallback).ToArray();
    var extra = nextFunctions.Values.Where(row => !legacyFunctions.ContainsKey(Key(row)) && !row.Fallback).ToArray();
    var legacyOnly = legacyFunctions.Values.Where(row => !nextFunctions.ContainsKey(Key(row))).ToArray();
    var missing = legacyOnly.Where(row => !nextFallbackFiles.Contains(row.RelativeFile)).ToArray();
    var fallbackMissing = legacyOnly.Where(row => nextFallbackFiles.Contains(row.RelativeFile)).ToArray();
    var names = new List<string>();
    var order = new List<string>();
    var invalid = new List<string>();
    foreach (var row in safeNext)
    {
        if (!legacyFunctions.TryGetValue(Key(row), out var oracle))
            continue;
        if (!string.Equals(row.FunctionName, oracle.FunctionName, StringComparison.Ordinal))
            names.Add($"{Key(row)} next={row.FunctionName} legacy={oracle.FunctionName}");
        else if (row.StartLine != oracle.StartLine)
            order.Add($"{Key(row)} nextLine={row.StartLine} legacyLine={oracle.StartLine}");
        if (oracle.IsError != row.Fallback)
            invalid.Add($"{Key(row)} legacyIsError={oracle.IsError} nextFallback={row.Fallback}");
    }
    var fallbackLines = fallbackNext.Where(row => legacyFunctions.ContainsKey(Key(row))).Select(row =>
        $"{Key(row)} reason={row.Flags} legacyName={legacyFunctions[Key(row)].FunctionName}").ToArray();
    var unexpected = string.Join(Environment.NewLine,
        missingFiles.Select(file => $"missing-file {file}")
        .Concat(extraFiles.Select(file => $"extra-file {file}"))
        .Concat(missing.Select(row => $"missing {Key(row)}"))
        .Concat(extra.Select(row => $"extra {Key(row)}"))
        .Concat(names.Select(line => $"name {line}"))
        .Concat(order.Select(line => $"order {line}"))
        .Concat(invalid.Select(line => $"invalid {line}")));
    var fallback = string.Join(Environment.NewLine, fallbackLines.Concat(fallbackMissing.Select(row => $"unmatched-fallback {Key(row)}")));
    var duplicates = DuplicateGroups(legacy);
    var summary = $"Legacy function rows: {legacyFunctions.Count}\nNext function rows: {nextFunctions.Count}\nSafe Next functions: {safeNext.Length}\nFallback Next functions: {fallbackNext.Length}\nExpected fallback differences: {fallbackLines.Length + fallbackMissing.Length}\nUnexpected missing: {missing.Length + missingFiles.Length}\nUnexpected extra: {extra.Length + extraFiles.Length}\nName mismatch: {names.Count}\nOrder mismatch: {order.Count}\nInvalid/error mismatch: {invalid.Count}\nDuplicate function names: {duplicates.Count}\nDuplicate definitions: {duplicates.Sum(static group => group.Count())}\nMax duplicate count: {(duplicates.Count == 0 ? 0 : duplicates.Max(static group => group.Count()))}\n";
    return new(summary, unexpected.Length == 0 ? "none\n" : unexpected + Environment.NewLine,
        fallback.Length == 0 ? "none\n" : fallback + Environment.NewLine,
        missing.Length + missingFiles.Length + extra.Length + extraFiles.Length + names.Count + order.Count + invalid.Count);
}

static string Key(Row row) => $"{row.RelativeFile}:{row.FunctionOrder}";

static List<IGrouping<string, Row>> DuplicateGroups(List<Row> rows) => rows.Where(static row => row.FunctionOrder > 0)
    .GroupBy(static row => row.FunctionName ?? "", StringComparer.OrdinalIgnoreCase)
    .Where(static group => group.Count() > 1).OrderByDescending(static group => group.Count()).ToList();

static string DuplicateReport(List<Row> rows)
{
    var groups = DuplicateGroups(rows);
    var lines = new List<string>
    {
        $"duplicateFunctionNames={groups.Count}",
        $"duplicateDefinitions={groups.Sum(static group => group.Count())}",
        $"maxDuplicateCount={(groups.Count == 0 ? 0 : groups.Max(static group => group.Count()))}"
    };
    foreach (var group in groups.Take(20))
        lines.Add($"{group.Key}\t{group.Count()}\t{string.Join(", ", group.Take(5).Select(static row => $"{row.RelativeFile}:{row.StartLine}"))}");
    return string.Join(Environment.NewLine, lines) + Environment.NewLine;
}

static int SelfTest()
{
    var legacy = new List<Row> { new(1, "a.ERB", 1, "A", 1, 0, 0, 0, "None", false, false), new(1, "a.ERB", 2, "A", 3, 0, 0, 0, "None", false, false) };
    var next = new List<Row> { new(1, "a.ERB", 1, "A", 1, 1, 0, 0, "None", false, false), new(1, "a.ERB", 2, "A", 3, 3, 0, 0, "None", false, false) };
    var exact = Compare(legacy, next);
    if (exact.BlockerCount != 0) return Fail("exact match");
    next[1] = next[1] with { StartLine = 99 };
    if (Compare(legacy, next).BlockerCount == 0) return Fail("order mismatch");
    next[1] = next[1] with { StartLine = 3, Fallback = true, Flags = "LineContinuation" };
    if (Compare(legacy, next).BlockerCount != 0) return Fail("expected fallback");
    if (DuplicateGroups(legacy).Count != 1) return Fail("duplicate");
    if (Compare(legacy, next.Append(new(1, "a.ERB", 3, "X", 5, 5, 0, 0, "None", false, false)).ToList()).BlockerCount == 0) return Fail("unexpected extra");
    var safeNext = next.Select(static row => row with { Fallback = false, Flags = "None" }).ToList();
    if (Compare(legacy.Append(new(1, "a.ERB", 3, "A", 5, 5, 0, 0, "None", false, false)).ToList(), safeNext).BlockerCount == 0) return Fail("unexpected missing");
    if (Compare(legacy, next.Select((row, index) => index == 0 ? row with { FunctionName = "B", Fallback = false } : row).ToList()).BlockerCount == 0) return Fail("name mismatch");
    if (!string.Equals(legacy[0].FunctionName, legacy[1].FunctionName, StringComparison.OrdinalIgnoreCase) || legacy[0].StartLine >= legacy[1].StartLine) return Fail("same-name order");
    var invalidLegacy = new List<Row> { legacy[0] with { IsError = true } };
    if (Compare(invalidLegacy, new List<Row> { next[0] }).BlockerCount == 0) return Fail("invalid/error mismatch");
    if (!StringComparer.OrdinalIgnoreCase.Equals("EVENT", "event")) return Fail("case rule");
    Console.WriteLine("DifferentialSelfTest: exact/fallback/extra/missing/name/order/duplicate/invalid/case PASS");
    return 0;

    static int Fail(string name) { Console.Error.WriteLine($"DifferentialSelfTest: FAIL {name}"); return 1; }
}

readonly record struct Row(int FileOrder, string RelativeFile, int FunctionOrder, string? FunctionName,
    int StartLine, int EndLine, long StartByte, long EndByte, string Flags, bool Fallback, bool IsError);
readonly record struct ComparisonResult(string Summary, string Unexpected, string Fallback, int BlockerCount);
