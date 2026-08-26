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
File.WriteAllText(Path.Combine(reportDirectory, "duplicate-functions.txt"), DuplicateReport(legacy, next));
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
    var legacyFileRows = legacy.Where(static row => row.FunctionOrder == 0 && row.Kind == "File").ToDictionary(static row => row.RelativeFile, StringComparer.OrdinalIgnoreCase);
    var nextFileRows = next.Where(static row => row.FunctionOrder == 0).ToDictionary(static row => row.RelativeFile, StringComparer.OrdinalIgnoreCase);
    var missingFiles = legacyFileRows.Keys.Except(nextFileRows.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
    var extraFiles = nextFileRows.Keys.Except(legacyFileRows.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
    var fileOrder = legacyFileRows.Keys.Intersect(nextFileRows.Keys, StringComparer.OrdinalIgnoreCase)
        .Where(file => legacyFileRows[file].FileOrder != nextFileRows[file].FileOrder)
        .Select(file => $"{file} legacy={legacyFileRows[file].FileOrder} next={nextFileRows[file].FileOrder}").ToArray();

    var legacyFunctions = legacy.Where(static row => row.FunctionOrder > 0).ToArray();
    var nextFunctions = next.Where(static row => row.FunctionOrder > 0).ToArray();
    var legacyByPosition = legacyFunctions.ToDictionary(PositionKey, StringComparer.OrdinalIgnoreCase);
    var nextByPosition = nextFunctions.ToDictionary(PositionKey, StringComparer.OrdinalIgnoreCase);
    var legacyByLine = legacyFunctions.GroupBy(LineKey, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
    var legacyPreprocessorRanges = legacy.Where(static row => row.Kind == "PreprocessorRange")
        .GroupBy(static row => row.RelativeFile, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.Select(row => new ContinuationBlock(row.StartLine, row.EndLine)).ToArray(), StringComparer.OrdinalIgnoreCase);
    var ranges = next.Where(static row => row.ContinuationBlocks is not null)
        .SelectMany(static row => row.ContinuationBlocks!.Select(block => (row.RelativeFile, Block: block)))
        .GroupBy(static item => item.RelativeFile, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.Select(item => item.Block).ToArray(), StringComparer.OrdinalIgnoreCase);
    var expectedFallbackKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var fallbackLines = new List<string>();
    var unexplainedFallback = new List<string>();
    foreach (var row in nextFunctions.Where(static row => row.Fallback))
    {
        if (legacyByLine.TryGetValue(LineKey(row), out var candidates))
        {
            var oracle = candidates[0];
            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate.FunctionName, row.FunctionName, StringComparison.OrdinalIgnoreCase))
                {
                    oracle = candidate;
                    break;
                }
            }
            expectedFallbackKeys.Add(PositionKey(oracle));
            fallbackLines.Add($"expected {PositionKey(row)} reason={row.Flags} legacy={PositionKey(oracle)}");
        }
        else if (row.Flags.Contains("Preprocessor", StringComparison.Ordinal)
            && legacyPreprocessorRanges.TryGetValue(row.RelativeFile, out var disabledRanges)
            && disabledRanges.Any(range => row.StartLine >= range.StartLine && row.StartLine <= range.EndLine))
        {
            var range = disabledRanges.First(range => row.StartLine >= range.StartLine && row.StartLine <= range.EndLine);
            fallbackLines.Add($"expected {PositionKey(row)} reason=LegacyPPState Disabled range={range.StartLine}-{range.EndLine}");
        }
        else
            unexplainedFallback.Add($"next-only {PositionKey(row)} reason={row.Flags}");
    }

    var missing = new List<Row>();
    foreach (var row in legacyFunctions.Where(row => !expectedFallbackKeys.Contains(PositionKey(row)) && !nextByPosition.ContainsKey(PositionKey(row))))
    {
        if (ranges.TryGetValue(row.RelativeFile, out var fileRanges) && fileRanges.Any(block => row.StartLine >= block.StartLine && row.StartLine <= block.EndLine))
        {
            expectedFallbackKeys.Add(PositionKey(row));
            var block = fileRanges.First(block => row.StartLine >= block.StartLine && row.StartLine <= block.EndLine);
            fallbackLines.Add($"expected {PositionKey(row)} reason=LineContinuation block={block.StartLine}-{block.EndLine}");
        }
        else
            missing.Add(row);
    }

    var extra = nextFunctions.Where(row => !legacyByPosition.ContainsKey(PositionKey(row)) && !row.Fallback).ToArray();
    var names = new List<string>();
    var order = new List<string>();
    var safeLegacy = legacyFunctions.Where(row => !expectedFallbackKeys.Contains(PositionKey(row)))
        .GroupBy(static row => row.RelativeFile, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.OrderBy(static row => row.StartLine).ToArray(), StringComparer.OrdinalIgnoreCase);
    var safeNext = nextFunctions.Where(static row => !row.Fallback)
        .GroupBy(static row => row.RelativeFile, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.OrderBy(static row => row.StartLine).ToArray(), StringComparer.OrdinalIgnoreCase);
    foreach (var file in safeLegacy.Keys.Union(safeNext.Keys, StringComparer.OrdinalIgnoreCase))
    {
        var left = safeLegacy.GetValueOrDefault(file) ?? [];
        var right = safeNext.GetValueOrDefault(file) ?? [];
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            if (!string.Equals(left[i].FunctionName, right[i].FunctionName, StringComparison.Ordinal))
                names.Add($"{file} index={i + 1} next={right[i].FunctionName} legacy={left[i].FunctionName}");
            else if (left[i].StartLine != right[i].StartLine)
                order.Add($"{file} index={i + 1} nextLine={right[i].StartLine} legacyLine={left[i].StartLine}");
        }
    }
    var invalid = nextFunctions.Where(static row => !row.Fallback)
        .Where(row => row.IsError || (legacyByPosition.TryGetValue(PositionKey(row), out var oracle) && oracle.IsError))
        .Select(row => $"{PositionKey(row)} legacyIsError={legacyByPosition.GetValueOrDefault(PositionKey(row)).IsError} nextIsError={row.IsError}").ToArray();
    var duplicates = AnalyzeDuplicates(legacyFunctions, nextFunctions);
    var unexpected = string.Join(Environment.NewLine,
        missingFiles.Select(file => $"missing-file {file}")
        .Concat(extraFiles.Select(file => $"extra-file {file}"))
        .Concat(fileOrder.Select(line => $"file-order {line}"))
        .Concat(missing.Select(row => $"missing {PositionKey(row)}"))
        .Concat(extra.Select(row => $"extra {PositionKey(row)}"))
        .Concat(names.Select(line => $"name {line}"))
        .Concat(order.Select(line => $"order {line}"))
        .Concat(invalid.Select(line => $"invalid {line}"))
        .Concat(unexplainedFallback.Select(line => $"unexplained-fallback {line}"))
        .Concat(duplicates.PriorityMismatches.Select(line => $"priority {line}")));
    var fallback = string.Join(Environment.NewLine, fallbackLines.Concat(unexplainedFallback.Select(line => $"unexplained {line}")));
    var preprocessorExpected = fallbackLines.Count(line => line.Contains("LegacyPPState Disabled", StringComparison.Ordinal));
    var summary = $"Legacy file rows: {legacyFileRows.Count}\nNext file rows: {nextFileRows.Count}\nLegacy function rows: {legacyFunctions.Length}\nNext function rows: {nextFunctions.Length}\nSafe Next functions: {nextFunctions.Count(static row => !row.Fallback)}\nFallback Next functions: {nextFunctions.Count(static row => row.Fallback)}\nExpected fallback differences: {fallbackLines.Count}\nExpected preprocessor-disabled differences: {preprocessorExpected}\nUnexplained fallback differences: {unexplainedFallback.Count}\nUnexpected missing: {missing.Count + missingFiles.Length}\nUnexpected extra: {extra.Length + extraFiles.Length}\nFileOrder mismatch: {fileOrder.Length}\nName mismatch: {names.Count}\nOrder mismatch: {order.Count}\nInvalid/error mismatch: {invalid.Length}\nDuplicate function names: {duplicates.Groups.Count}\nDuplicate definitions: {duplicates.Groups.Sum(static group => group.Count())}\nMax duplicate count: {(duplicates.Groups.Count == 0 ? 0 : duplicates.Groups.Max(static group => group.Count()))}\nDuplicate definition order mismatches: {duplicates.PriorityMismatches.Count}\nEvent priority semantic verification: DEFERRED / LEGACY FALLBACK\n";
    var blockers = missing.Count + missingFiles.Length + extra.Length + extraFiles.Length + fileOrder.Length + names.Count + order.Count + invalid.Length + unexplainedFallback.Count + duplicates.PriorityMismatches.Count;
    return new(summary, unexpected.Length == 0 ? "none\n" : unexpected + Environment.NewLine,
        fallback.Length == 0 ? "none\n" : fallback + Environment.NewLine, blockers);
}

static string PositionKey(Row row) => $"{row.RelativeFile}:{row.FunctionName}:{row.StartLine}";

static string LineKey(Row row) => $"{row.RelativeFile}:{row.StartLine}";

static List<IGrouping<string, Row>> DuplicateGroups(IEnumerable<Row> rows) => rows
    .GroupBy(static row => row.FunctionName ?? "", StringComparer.OrdinalIgnoreCase)
    .Where(static group => group.Count() > 1).OrderByDescending(static group => group.Count()).ToList();

static DuplicateResult AnalyzeDuplicates(Row[] legacy, Row[] next)
{
    var groups = DuplicateGroups(legacy);
    var mismatches = new List<string>();
    foreach (var group in groups)
    {
        var left = group.OrderBy(static row => row.FileOrder).ThenBy(static row => row.StartLine).ToArray();
        var right = next.Where(row => string.Equals(row.FunctionName, group.Key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static row => row.FileOrder).ThenBy(static row => row.StartLine).ToArray();
        if (left.Length != right.Length || left.Zip(right).Any(pair => DefinitionKey(pair.First) != DefinitionKey(pair.Second)))
            mismatches.Add($"{group.Key} definition order/set differs");
        if (left.Any(static row => !row.IsEvent) && left.Count(static row => row.LegacyFinalPriority) != 1)
            mismatches.Add($"{group.Key} non-event final priority count={left.Count(static row => row.LegacyFinalPriority)}");
    }
    return new(groups, mismatches);
}

static string DefinitionKey(Row row) => $"{row.FileOrder}:{row.RelativeFile}:{row.StartLine}:{row.FunctionName}";

static string DuplicateReport(List<Row> legacy, List<Row> next)
{
    var result = AnalyzeDuplicates(legacy.Where(static row => row.FunctionOrder > 0).ToArray(), next.Where(static row => row.FunctionOrder > 0).ToArray());
    var lines = new List<string>
    {
        $"duplicateFunctionNames={result.Groups.Count}",
        $"duplicateDefinitions={result.Groups.Sum(static group => group.Count())}",
        $"maxDuplicateCount={(result.Groups.Count == 0 ? 0 : result.Groups.Max(static group => group.Count()))}",
        $"definitionOrderMismatches={result.PriorityMismatches.Count}",
        "eventPrioritySemanticVerification=DEFERRED / LEGACY FALLBACK"
    };
    foreach (var group in result.Groups)
    {
        lines.Add($"[{group.Key}]");
        foreach (var row in group.OrderBy(static row => row.FileOrder).ThenBy(static row => row.StartLine))
            lines.Add($"legacy order={row.FunctionOrder} fileOrder={row.FileOrder} file={row.RelativeFile} line={row.StartLine} event={row.IsEvent} system={row.IsSystem} method={row.IsMethod} pri={row.IsPri} later={row.IsLater} only={row.IsOnly} single={row.IsSingle} final={row.LegacyFinalPriority}");
        foreach (var row in next.Where(row => string.Equals(row.FunctionName, group.Key, StringComparison.OrdinalIgnoreCase)).OrderBy(static row => row.FileOrder).ThenBy(static row => row.StartLine))
            lines.Add($"next order={row.FunctionOrder} fileOrder={row.FileOrder} file={row.RelativeFile} line={row.StartLine}");
    }
    lines.AddRange(result.PriorityMismatches.Select(static line => $"definition-order-mismatch {line}"));
    return string.Join(Environment.NewLine, lines) + Environment.NewLine;
}

static int SelfTest()
{
    var legacy = new List<Row> { new(1, "a.ERB", 1, "A", 1, 0, 0, 0, "None", false, false), new(1, "a.ERB", 2, "A", 3, 0, 0, 0, "None", false, false) };
    var next = new List<Row> { new(1, "a.ERB", 1, "A", 1, 1, 0, 0, "None", false, false), new(1, "a.ERB", 2, "A", 3, 3, 0, 0, "None", false, false) };
    legacy[0] = legacy[0] with { LegacyFinalPriority = true };
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
    var fileLegacy = new List<Row>
    {
        new(1, "A.ERB", 0, null, 0, 0, 0, 0, "None", false, false, Kind: "File"),
        new(2, "B.ERB", 0, null, 0, 0, 0, 0, "None", false, false, Kind: "File"),
        new(1, "A.ERB", 1, "A", 1, 1, 0, 0, "None", false, false)
    };
    var fileExact = Compare(fileLegacy, fileLegacy.ToList());
    if (fileExact.BlockerCount != 0) return Fail("file order exact");
    var fileSwapped = fileLegacy.Select(row => row.RelativeFile == "A.ERB" ? row with { FileOrder = 2 } : row with { FileOrder = 1 }).ToList();
    if (Compare(fileLegacy, fileSwapped).BlockerCount == 0) return Fail("file order mismatch");
    var fallbackLegacy = new List<Row>
    {
        new(1, "C.ERB", 0, null, 0, 0, 0, 0, "None", false, false, Kind: "File"),
        new(1, "C.ERB", 1, "BEFORE", 1, 1, 0, 0, "None", false, false),
        new(1, "C.ERB", 2, "INSIDE", 3, 3, 0, 0, "None", false, false),
        new(1, "C.ERB", 3, "AFTER", 5, 5, 0, 0, "None", false, false)
    };
    var fallbackNext = new List<Row>
    {
        new(1, "C.ERB", 0, null, 0, 0, 0, 0, "LineContinuation", true, false, Kind: "File", ContinuationBlocks: [new(2, 4)]),
        new(1, "C.ERB", 1, "BEFORE", 1, 1, 0, 0, "None", false, false),
        new(1, "C.ERB", 2, "AFTER", 5, 5, 0, 0, "LineContinuation", true, false, ContinuationBlocks: [new(2, 4)])
    };
    var fallbackExact = Compare(fallbackLegacy, fallbackNext);
    if (fallbackExact.BlockerCount != 0 || !fallbackExact.Fallback.Contains("block=2-4", StringComparison.Ordinal)) return Fail("fallback source range");
    var unexplained = fallbackNext.Append(fallbackNext[^1] with { FunctionName = "UNEXPLAINED", StartLine = 99 }).ToList();
    if (Compare(fallbackLegacy, unexplained).BlockerCount == 0) return Fail("unexplained fallback");
    var ppLegacy = new List<Row>
    {
        new(1, "BIT_SETTING.ERB", 0, null, 0, 0, 0, 0, "None", false, false, Kind: "File"),
        new(1, "BIT_SETTING.ERB", -1, null, 140, 185, 0, 0, "Preprocessor", true, false, Kind: "PreprocessorRange"),
        new(1, "BIT_SETTING.ERB", 1, "ENABLED", 190, 190, 0, 0, "None", false, false)
    };
    var ppNext = new List<Row>
    {
        new(1, "BIT_SETTING.ERB", 0, null, 0, 0, 0, 0, "Preprocessor", true, false, Kind: "File"),
        new(1, "BIT_SETTING.ERB", 1, "DISABLED", 150, 155, 0, 0, "Preprocessor", true, false),
        new(1, "BIT_SETTING.ERB", 2, "ENABLED", 190, 190, 0, 0, "None", false, false)
    };
    var ppExact = Compare(ppLegacy, ppNext);
    if (ppExact.BlockerCount != 0 || !ppExact.Fallback.Contains("LegacyPPState Disabled range=140-185", StringComparison.Ordinal)) return Fail("preprocessor disabled range");
    if (Compare(ppLegacy, ppNext.Select(row => row with { StartLine = row.FunctionName == "DISABLED" ? 99 : row.StartLine }).ToList()).BlockerCount == 0) return Fail("preprocessor range guard");
    Console.WriteLine("DifferentialSelfTest: exact/fallback/extra/missing/name/order/duplicate/invalid/case/preprocessor-range PASS");
    return 0;

    static int Fail(string name) { Console.Error.WriteLine($"DifferentialSelfTest: FAIL {name}"); return 1; }
}

readonly record struct Row(int FileOrder, string RelativeFile, int FunctionOrder, string? FunctionName,
    int StartLine, int EndLine, long StartByte, long EndByte, string Flags, bool Fallback, bool IsError,
    string? Kind = null, bool IsEvent = false, bool IsSystem = false, bool IsMethod = false,
    bool IsPri = false, bool IsLater = false, bool IsOnly = false, bool IsSingle = false,
    bool LegacyFinalPriority = false, ContinuationBlock[]? ContinuationBlocks = null);
readonly record struct ComparisonResult(string Summary, string Unexpected, string Fallback, int BlockerCount);
readonly record struct DuplicateResult(List<IGrouping<string, Row>> Groups, List<string> PriorityMismatches);
