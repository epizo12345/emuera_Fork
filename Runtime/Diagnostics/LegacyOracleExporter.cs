#if LEGACY_ORACLE
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Loader;
using MinorShift.Emuera.Runtime.Script.Statements;
using LegacyConfig = MinorShift.Emuera.Runtime.Config.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal static class LegacyOracleExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    internal static void Write(LabelDictionary labels, string outputPath, string erbRoot, LegacyErbBaseline baseline,
        IReadOnlyDictionary<string, LegacyPreprocessorTrace> preprocessorDiagnostics)
    {
        if (labels is null || string.IsNullOrWhiteSpace(outputPath))
            return;

        var ordered = labels.GetAllLabels(true)
            .Where(static label => label.Position is not null)
            .OrderBy(static label => label.FileIndex == 0 ? int.MaxValue : label.FileIndex)
            .ThenBy(static label => label.Position!.Value.Filename, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static label => label.Position!.Value.LineNo)
            .ToArray();
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var writer = new StreamWriter(fullPath, false, new UTF8Encoding(false));
        for (var fileOrder = 0; fileOrder < Program.AnalysisFiles.Count; fileOrder++)
        {
            var relativeFile = Path.GetRelativePath(erbRoot, Program.AnalysisFiles[fileOrder]).Replace('\\', '/');
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                FileOrder = fileOrder + 1,
                RelativeFile = relativeFile,
                FunctionOrder = 0,
                FunctionName = (string?)null,
                StartLine = 0,
                IsEvent = false,
                IsSystem = false,
                IsMethod = false,
                IsError = false,
                Kind = "File"
            }, JsonOptions));
            if (preprocessorDiagnostics.TryGetValue(Program.AnalysisFiles[fileOrder], out var trace))
                foreach (var range in trace.DisabledRanges)
                    writer.WriteLine(JsonSerializer.Serialize(new
                    {
                        FileOrder = fileOrder + 1,
                        RelativeFile = relativeFile,
                        FunctionOrder = -1,
                        FunctionName = (string?)null,
                        StartLine = range.StartLine,
                        EndLine = range.EndLine,
                        StartByte = 0L,
                        EndByte = 0L,
                        Flags = "Preprocessor",
                        Fallback = true,
                        IsError = false,
                        Kind = "PreprocessorRange"
                    }, JsonOptions));

        }
        string? previousFile = null;
        var functionOrder = 0;
        foreach (var label in ordered)
        {
            var position = label.Position!.Value;
            var relativeFile = Path.GetRelativePath(erbRoot, position.Filename).Replace('\\', '/');
            if (!string.Equals(previousFile, relativeFile, StringComparison.OrdinalIgnoreCase))
            {
                previousFile = relativeFile;
                functionOrder = 0;
            }
            var instructions = ReadInstructions(label);
            var row = new
            {
                FileOrder = label.FileIndex,
                RelativeFile = relativeFile,
                FunctionOrder = ++functionOrder,
                FunctionName = label.LabelName,
                StartLine = position.LineNo,
                IsEvent = label.IsEvent,
                IsSystem = label.IsSystem,
                IsMethod = label.IsMethod,
                IsError = label.IsError,
                Kind = label.GetType().Name,
                IsPri = label.IsPri,
                IsLater = label.IsLater,
                IsOnly = label.IsOnly,
                IsSingle = label.IsSingle,
                LegacyFinalPriority = !label.IsEvent && ReferenceEquals(labels.GetNonEventLabel(label.LabelName), label),
                InstructionCount = instructions.Count,
                InstructionCodes = instructions.Select(static instruction => instruction.Code).ToArray(),
                InstructionLines = instructions.Select(static instruction => instruction.Line).ToArray()
            };
            writer.WriteLine(JsonSerializer.Serialize(row, JsonOptions));
        }
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(fullPath)!, "legacy-summary.txt"),
        [
            "source=actual Legacy ErbLoader/LogicalLineParser/LabelDictionary",
            $"functionRows={ordered.Length}",
            $"filesEnumerated={Program.AnalysisFiles.Count}",
            $"filesWithLabels={ordered.Select(static label => label.Position!.Value.Filename).Distinct(StringComparer.OrdinalIgnoreCase).Count()}",
            $"errorRows={ordered.Count(static label => label.IsError)}",
            $"ignoreCase={LegacyConfig.IgnoreCase}",
            $"stringComparison={LegacyConfig.StringComparison}",
            $"systemAllowFullSpace={LegacyConfig.SystemAllowFullSpace}",
            $"erbElapsedMilliseconds={baseline.ElapsedMilliseconds:F3}",
            $"erbAllocatedBytes={baseline.AllocatedBytes}",
            $"erbManagedBefore={baseline.ManagedBefore}",
            $"erbManagedImmediatelyAfter={baseline.ManagedImmediatelyAfter}",
            $"erbManagedAfterDiagnosticGc={baseline.ManagedAfterDiagnosticGc}",
            $"legacyRetainedManagedEstimate={Math.Max(0, baseline.ManagedAfterDiagnosticGc - baseline.ManagedBefore)}"
        ], new UTF8Encoding(false));
        WritePreprocessorReports(Path.GetDirectoryName(fullPath)!, erbRoot, preprocessorDiagnostics);
    }

    private static List<(string Code, int Line)> ReadInstructions(FunctionLabelLine label)
    {
        var instructions = new List<(string Code, int Line)>();
        for (var line = label.NextLine; line is not null && line is not NullLine && line is not FunctionLabelLine; line = line.NextLine)
        {
            if (line is InstructionLine instruction && instruction.Position is not null)
                instructions.Add((instruction.FunctionCode.ToString(), instruction.Position.Value.LineNo));
        }
        return instructions;
    }

    private static void WritePreprocessorReports(string directory, string erbRoot,
        IReadOnlyDictionary<string, LegacyPreprocessorTrace> traces)
    {
        var ranges = new List<string>();
        var bit = new List<string>();
        foreach (var pair in traces.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var relativeFile = Path.GetRelativePath(erbRoot, pair.Key).Replace('\\', '/');
            ranges.Add($"file={relativeFile}");
            ranges.AddRange(pair.Value.Directives.Select(static d =>
                $"directive line={d.PhysicalLine} token={d.Directive} before={(d.DisabledBefore ? "Disabled" : "Enabled")} after={(d.DisabledAfter ? "Disabled" : "Enabled")}"));
            ranges.AddRange(pair.Value.DisabledRanges.Select(static r =>
                $"disabledRange start={r.StartLine} end={r.EndLine} reason={r.Reason}"));

            if (!relativeFile.EndsWith("BIT_SETTING.ERB", StringComparison.OrdinalIgnoreCase))
                continue;
            var path = pair.Key;
            if (!File.Exists(path))
                continue;
            var sourceLines = File.ReadAllLines(path);
            var candidates = sourceLines
                .Select((line, index) => (Name: CandidateName(line), Line: index + 1))
                .Where(static item => item.Name is not null)
                .Select(static item => (Name: item.Name!, item.Line))
                .ToArray();
            bit.Add($"file={pair.Key}");
            foreach (var candidate in candidates)
            {
                var endLine = candidates.FirstOrDefault(next => next.Line > candidate.Line).Line - 1;
                if (endLine < candidate.Line)
                    endLine = sourceLines.Length;
                var range = pair.Value.DisabledRanges.FirstOrDefault(r => candidate.Line >= r.StartLine && candidate.Line <= r.EndLine);
                var disabled = range.EndLine >= candidate.Line && range.StartLine <= candidate.Line;
                bit.Add($"function={candidate.Name} physicalStart={candidate.Line} physicalEnd={endLine} LegacyState={(disabled ? "Disabled" : "Enabled")}" +
                    (disabled ? $" reason={range.Reason}" : ""));
            }
        }
        File.WriteAllLines(Path.Combine(directory, "preprocessor-ranges.txt"), ranges, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "bit-setting-check.txt"), bit, new UTF8Encoding(false));
    }

    private static string CandidateName(string line)
    {
        var text = line.TrimStart(' ', '\t');
        if (text.Length < 2 || text[0] != '@' || text[1] is '"' or '\'')
            return null;
        var end = 1;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '\\'))
            end++;
        return end == 1 ? null : text[1..end];
    }
}
#endif
