#if LEGACY_ORACLE
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements;
using LegacyConfig = MinorShift.Emuera.Runtime.Config.Config;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal static class LegacyOracleExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    internal static void Write(LabelDictionary labels, string outputPath, string erbRoot, LegacyErbBaseline baseline)
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
                LegacyFinalPriority = !label.IsEvent && ReferenceEquals(labels.GetNonEventLabel(label.LabelName), label)
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
    }
}
#endif
