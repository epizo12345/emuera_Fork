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
                Kind = label.GetType().Name
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
            $"erbManagedAfter={baseline.ManagedAfter}"
        ], new UTF8Encoding(false));
    }
}
#endif
