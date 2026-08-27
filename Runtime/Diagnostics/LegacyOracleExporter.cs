#if LEGACY_ORACLE
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Loader;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.Runtime.Utils;
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
        // [Emuera改修:NEXT-1B-R4 2026-08-27]
        // LogicalLineParserが実際に行頭命令検索へ使うinstruction dictionaryの全登録キーを、
        // enum/Method一覧ではなくLegacy実体からA/B/C分類付き診断artifactへ出力する。
        var instructionDictionary = FunctionIdentifier.GetInstructionNameDic();
        var dictionaryDirectory = Path.GetDirectoryName(fullPath)!;
        var lineHeadNames = instructionDictionary.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var statementNames = instructionDictionary.Where(static pair => pair.Value.Method is null).Select(static pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var methodNames = instructionDictionary.Where(static pair => pair.Value.Method is not null).Select(static pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        File.WriteAllLines(Path.Combine(dictionaryDirectory, "legacy-line-head-names.txt"),
            ["source=FunctionIdentifier.GetInstructionNameDic().Keys (LegacyLineHeadIdentifiers A)", ..lineHeadNames], new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(fullPath)!, "legacy-instruction-names.txt"),
            ["source=FunctionIdentifier.GetInstructionNameDic() entries with Method == null (LegacyStatementIdentifiers B)", ..statementNames], new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(dictionaryDirectory, "legacy-method-names.txt"),
            ["source=FunctionIdentifier.GetInstructionNameDic() entries with Method != null (LegacyMethodBackedLineHeads C)", ..methodNames], new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(dictionaryDirectory, "legacy-line-head-oracle.txt"),
        [
            "source=Legacy code-path proof for actual line-head lookup",
            "A=FunctionIdentifier.GetInstructionNameDic().Keys",
            "B=A where FunctionIdentifier.Method == null",
            "C=A where FunctionIdentifier.Method != null",
            "A=B union C; B intersection C=empty",
            "LogicalLineParser.cs:421 firstIdentifier -> GlobalStatic.IdentifierDictionary.GetFunctionIdentifier(firstIdentifier)",
            "IdentifierDictionary.cs:514 GetFunctionIdentifier -> instructionDic.TryGetValue(key, out FunctionIdentifier ret)",
            "IdentifierDictionary.cs:140 instructionDic = FunctionIdentifier.GetInstructionNameDic()",
            "FunctionIdentifier.cs:66 addFunction -> funcDic.Add(key, identifier) for normal FunctionCode registrations",
            "FunctionIdentifier.cs:421 JSONConfig.Game.UseScopedVariableInstruction conditionally registers VARI/VARS",
            "FunctionIdentifier.cs:431 FunctionMethodCreator.GetMethodList() is merged into funcDic when key is absent",
            "FunctionIdentifier.cs:437 method-backed entry uses FunctionCode.__NULL__ and methodInstruction",
            "LogicalLineParser.cs:425-464 non-null func enters instruction path; Method is not checked before assignment is bypassed",
            $"ignoreCase={LegacyConfig.IgnoreCase}",
            $"stringComparison={LegacyConfig.StringComparison}",
            $"useScopedVariableInstruction={JSONConfig.Game.UseScopedVariableInstruction}",
            $"A.count={lineHeadNames.Length}", $"B.count={statementNames.Length}", $"C.count={methodNames.Length}",
            $"A.unionBC.count={statementNames.Concat(methodNames).Distinct(StringComparer.OrdinalIgnoreCase).Count()}",
            $"B.intersectionC.count={statementNames.Intersect(methodNames, StringComparer.OrdinalIgnoreCase).Count()}",
            "SET is registered only as builtInByCode via registerBuiltIn(setFunc), not as a funcDic line-head key."
        ], new UTF8Encoding(false));
        WritePreprocessorReports(Path.GetDirectoryName(fullPath)!, erbRoot, preprocessorDiagnostics);
        WriteLexicalCorpusFromEnvironment();
        WriteCommandSeparatorCorpusFromEnvironment();
        WriteDebugSemicolonHashCorpusFromEnvironment();
    }

    private static void WriteLexicalCorpusFromEnvironment()
    {
        // [Emuera改修:NEXT-1B-R5 2026-08-27]
        // Legacy実parserの区切り文字をoracle化し、Next側のfirst-identifier走査を文字単位で比較可能にする。
        var path = Environment.GetEnvironmentVariable("EMUERA_LEGACY_LEXICAL_CORPUS");
        if (string.IsNullOrWhiteSpace(path)) return;
        var cases = new (string Name, string Input)[]
        {
            ("EOS", "X"), ("space", "X "), ("tab", "X\t"), ("U+3000", "X　"), (".", "X."), ("+", "X+"), ("-", "X-"), ("*", "X*"), ("/", "X/"), ("%", "X%"), ("=", "X="), ("!", "X!"), ("<", "X<"), (">", "X>"), ("|", "X|"), ("&", "X&"), ("^", "X^"), ("~", "X~"), ("?", "X?"), ("#", "X#"), (")", "X)"), ("}", "X}"), ("]", "X]"), (",", "X,"), (":", "X:"), ("(", "X("), ("{", "X{"), ("[", "X["), ("$", "X$"), ("\\", "X\\"), ("'", "X'"), ("\"", "X\""), ("@", "X@"), (";", "X;"), ("VT", "X\v"), ("FF", "X\f")
        };
        var lines = new List<string>();
        foreach (var item in cases)
        {
            var stream = new CharStream(item.Input);
            string identifier;
            try { identifier = LexicalAnalyzer.ReadFirstIdentifier(stream); }
            catch (Exception ex) { identifier = "<error:" + ex.GetType().Name + ">"; }
            lines.Add(JsonSerializer.Serialize(new { item.Name, item.Input, Identifier = identifier, StopPosition = stream.CurrentPosition }));
        }
        File.WriteAllLines(Path.GetFullPath(path), lines, new UTF8Encoding(false));
    }

    private static void WriteCommandSeparatorCorpusFromEnvironment()
    {
        // [Emuera改修:NEXT-1B-R5 2026-08-27]
        // Legacyの行頭keyword後のseparator判定をoracle化し、空白・句読点・//の近似実装を防ぐ。
        var path = Environment.GetEnvironmentVariable("EMUERA_LEGACY_COMMAND_SEPARATOR_CORPUS");
        if (string.IsNullOrWhiteSpace(path)) return;
        var cases = new (string Name, string Input)[]
        {
            ("EOS", "PRINT"), ("space", "PRINT 1"), ("tab", "PRINT\t1"), ("semicolon", "PRINT;comment"), ("U+3000", "PRINT　1"), ("left-parenthesis", "PRINT(1)"), ("comma", "PRINT,1"), ("equals", "PRINT=1"), ("lowercase", "print 1"), ("lowercase-equals", "print=1"), ("VT", "PRINT\v1"), ("FF", "PRINT\f1"), ("line-comment", "// comment")
        };
        var lines = new List<string>();
        foreach (var item in cases)
        {
            var parsed = LogicalLineParser.ParseLine(item.Input, GlobalStatic.Console);
            var instruction = parsed as InstructionLine;
            lines.Add(JsonSerializer.Serialize(new { item.Name, item.Input, Kind = parsed?.GetType().Name, IsError = parsed?.IsError ?? false, FunctionCode = instruction?.FunctionCode.ToString() }));
        }
        File.WriteAllLines(Path.GetFullPath(path), lines, new UTF8Encoding(false));
    }

    private static void WriteDebugSemicolonHashCorpusFromEnvironment()
    {
        // [Emuera改修:NEXT-1B-R6 2026-08-27]
        // DebugModeの;#;はLegacy SkipWhiteSpaceが実際に消費するため、通常comment扱いとの差を別processで残す。
        var path = Environment.GetEnvironmentVariable("EMUERA_DEBUG_SEMICOLON_HASH_CORPUS");
        if (string.IsNullOrWhiteSpace(path)) return;
        var cases = new (string Name, string Input)[]
        {
            ("plain", ";#;PRINTL X"), ("leading-space", " ;#;PRINTL X"), ("leading-tab", "\t;#;PRINTL X"),
            ("double", ";#; ;#;PRINTL X"), ("normal-comment", "; normal comment"), ("inline-comment", "PRINTL X ; comment")
        };
        var lines = new List<string>();
        foreach (var item in cases)
        {
            var parsed = LogicalLineParser.ParseLine(item.Input, GlobalStatic.Console);
            var instruction = parsed as InstructionLine;
            var stream = new CharStream(item.Input);
            string firstIdentifier;
            try
            {
                LexicalAnalyzer.SkipWhiteSpace(stream);
                firstIdentifier = LexicalAnalyzer.ReadFirstIdentifier(stream);
            }
            catch { firstIdentifier = "<none>"; }
            var argument = instruction?.PopArgumentPrimitive();
            lines.Add(JsonSerializer.Serialize(new
            {
                item.Name,
                item.Input,
                DebugMode = Program.DebugMode,
                Kind = parsed?.GetType().Name,
                IsError = parsed?.IsError ?? false,
                FirstIdentifier = firstIdentifier,
                InstructionCount = instruction is null ? 0 : 1,
                FunctionCode = instruction?.FunctionCode.ToString(),
                SourceLine = parsed?.Position?.LineNo ?? 0,
                OperandOffset = argument?.CurrentPosition ?? -1,
                OperandLength = argument?.RowString.Length ?? 0
            }, JsonOptions));
        }
        File.WriteAllLines(Path.GetFullPath(path), lines, new UTF8Encoding(false));
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
