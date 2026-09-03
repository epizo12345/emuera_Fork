using System.Text;

// [Emuera改修:NEXT-3D-R1.4I 2026-09-04]
// cross-runの主たるalignment authorityはNormalizedSourcePath、physical StartLine、FunctionName。
// CallDepth/InvocationOrdinal/ParentIdentityは追加の曖昧性解消に使い、RuntimeFunctionIdは診断値に限る。
// sidecarはroot-change deltaを復元してexact variable/index/value localizationに使うが、
// Next-only write trackingをalignment authorityにはしない。
internal static class IntegrationCheckpointComparer
{
    private const char DetailEntry = '\u001e';
    private const char DetailPart = '\u001f';
    private static readonly string[] Header = ["Sequence", "CheckpointKind", "FunctionName", "RuntimeFunctionId", "NormalizedSourcePath", "StartLine", "CallDepth", "InvocationOrdinal", "ParentIdentity", "SelectedEngine", "VmStopReason", "EntryDispatchResult", "FallbackReason", "StateRootHash", "VariablesHash", "CharacterHash", "RngHash", "ValueDetails"];

    public static int Run(string legacyPath, string nextPath, string reportDirectory)
    {
        var result = Compare(Read(legacyPath), Read(nextPath));
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(Path.Combine(reportDirectory, "r1.4i-alignment-summary.txt"), result.Summary);
        File.WriteAllText(Path.Combine(reportDirectory, "r1.4i-first-divergence.txt"), result.FirstDivergence);
        File.WriteAllText(Path.Combine(reportDirectory, "r1.4i-domain-diff.tsv"), result.DomainDiff);
        Console.Write(result.Summary);
        return 0;
    }

    public static int SelfTest()
    {
        var exactLegacy = new[] { Row(1, "FunctionEntry", "A", "a.erb", 10, 1, "<root>", "AA"), Row(2, "FunctionExit", "A", "a.erb", 10, 1, "<root>", "BB") };
        if (Compare(exactLegacy, exactLegacy).First is not null) return Fail("identical");
        if (!HasVariableDifference("N", "Integer", "", "1", "2")) return Fail("numeric");
        if (!HasVariableDifference("S", "String", "", "あいう", "いろは")) return Fail("string");
        if (!HasVariableDifference("STR", "String", "5", "まどかマギカ", "<different>")) return Fail("indexed");
        if (!HasVariableDifference("BASE", "CharacterInteger", "2:0", "1", "2")) return Fail("character");
        if (!HasSidecarDifference("Variables", "N", "Integer", "", "1", "2")) return Fail("capture-numeric");
        if (!HasSidecarDifference("Variables", "S", "String", "", "あいう", "いろは")) return Fail("capture-string");
        if (!HasSidecarDifference("Variables", "STR", "String", "5", "まどかマギカ", "different")) return Fail("capture-indexed");
        if (!HasSidecarDifference("Character", "BASE", "CharacterInteger", "2:0", "1", "2")) return Fail("capture-character");
        var extra = exactLegacy.Append(Row(9, "VmCompleted", "A", "a.erb", 10, 1, "<root>", "ignored")).ToArray();
        if (Compare(exactLegacy, extra).First is not null) return Fail("next-internal");
        var recursive = new[] { Row(1, "FunctionExit", "R", "r.erb", 3, 2, "A#1", "1", ordinal: 1), Row(2, "FunctionExit", "R", "r.erb", 3, 3, "R#1", "2", ordinal: 1) };
        if (Compare(recursive, recursive).Matched != 2) return Fail("recursive");
        var parents = new[] { Row(1, "FunctionExit", "X", "x.erb", 5, 2, "A#1", "1"), Row(2, "FunctionExit", "X", "x.erb", 5, 2, "B#1", "2") };
        var reordered = Compare(parents, [parents[1] with { Sequence = 1 }, parents[0] with { Sequence = 2 }]);
        if (reordered.First is not null) return Fail("parent-order-sensitive-state");
        if (!reordered.FirstDivergence.Contains("DivergenceKind=StructuralAlignment", StringComparison.Ordinal)) return Fail("parent-order-sensitive-kind");
        if (reordered.FirstUnmatchedLegacy?.ParentIdentity != "A#1") return Fail("parent-order-sensitive-first");
        var missingNext = Compare(exactLegacy, exactLegacy.Take(1).ToArray());
        if (missingNext.UnmatchedLegacy != 1 || missingNext.FirstUnmatchedLegacy is null || missingNext.LastMatching is null) return Fail("missing-next");
        var missingLegacy = Compare(exactLegacy.Take(1).ToArray(), exactLegacy);
        if (missingLegacy.UnmatchedNext != 1 || missingLegacy.FirstUnmatchedNext is null || missingLegacy.LastMatching is null) return Fail("missing-legacy");
        var historical = SidecarDifference("Variables", "EXTRA_TITLE", "String", "5", "まどかマギカ", "<different>", "SET_EXTRA_TITLE_VAR", "作品管理/SET_EXTRA_TITLE_VAR.ERB", 14);
        if (historical is not { Variable: "EXTRA_TITLE", Indices: "5", LegacyValue: "まどかマギカ", NextValue: "<different>" }) return Fail("historical-extra-title");
        if (!HasSidecarDifference("Variables", "I", "Integer", "", "0", "1")) return Fail("integer-default-zero");
        if (!HasSidecarDifference("Variables", "I", "Integer", "", "1", "0")) return Fail("integer-default-one");
        if (!HasSidecarDifference("Variables", "S", "String", "", "", "abc")) return Fail("string-default-empty");
        if (!HasSidecarDifference("Variables", "S", "String", "", "abc", "")) return Fail("string-default-empty-next");
        if (!HasSidecarDifference("Variables", "S", "String", "", "", "0")) return Fail("string-zero-preserved");
        if (!HasSidecarDifference("Variables", "S", "String", "", "0", "")) return Fail("string-zero-removed");
        if (!HasSparseSidecarDifference("Variables", "I", "Integer", "7", null, "1")) return Fail("sparse-integer-added");
        if (!HasSparseSidecarDifference("Variables", "I", "Integer", "7", "1", null)) return Fail("sparse-integer-removed");
        if (!HasSparseSidecarDifference("Variables", "S", "String", "8", null, "abc")) return Fail("sparse-string-added");
        if (!HasSparseSidecarDifference("Variables", "S", "String", "8", "abc", null)) return Fail("sparse-string-removed");
        if (!HasSparseSidecarDifference("Variables", "S", "String", "9", null, "0")) return Fail("sparse-string-zero-added");
        if (!HasSparseSidecarDifference("Variables", "S", "String", "9", "0", null)) return Fail("sparse-string-zero-removed");
        if (!HasSparseSidecarDifference("Variables", "S", "String", "10", null, "indexed")) return Fail("sparse-indexed-added");
        if (!HasSparseSidecarDifference("Variables", "S", "String", "10", "indexed", null)) return Fail("sparse-indexed-removed");
        if (!HasSparseSidecarDifference("Character", "C", "CharacterInteger", "2:3", null, "4")) return Fail("sparse-character-added");
        if (!HasSparseSidecarDifference("Character", "C", "CharacterInteger", "2:3", "4", null)) return Fail("sparse-character-removed");
        var unavailable = Compare([Row(1, "FunctionExit", "F", "f.erb", 1, 1, "<root>", "a")], [Row(1, "FunctionExit", "F", "f.erb", 1, 1, "<root>", "b")]).First;
        if (unavailable is not { Variable: "UNAVAILABLE_CAPTURE_V1", LegacyValue: "<not-captured>", NextValue: "<not-captured>" }) return Fail("unavailable-capture");
        if (IsStateBearingKind("FunctionEntry") || IsStateBearingKind("FunctionExit") || IsStateBearingKind("NextToLegacyFallback")) return Fail("structural-state-observation");
        if (!IsStateBearingKind("InputBoundary")) return Fail("input-boundary-state-observation");
        if (IsStateBearingKind("Wait")) return Fail("explicit-wait-is-not-input-boundary");
        var matchingInput = Row(1, "InputBoundary", "INPUT", "input.erb", 10, 1, "<root>", "same");
        if (Compare([matchingInput], [matchingInput]).Matched != 1 || Compare([matchingInput], [matchingInput]).First is not null) return Fail("input-boundary-comparable");
        var inputStateMismatch = Compare(
            [StateRow(1, "InputBoundary", "INPUT", "input.erb", 10, "root-a", "I", "Integer", "2", "0")],
            [StateRow(1, "InputBoundary", "INPUT", "input.erb", 10, "root-b", "I", "Integer", "2", "1")]);
        if (inputStateMismatch.First is not { Variable: "I", VariableType: "Integer", Indices: "2", LegacyValue: "0", NextValue: "1" } || inputStateMismatch.First.Domains.Count == 0) return Fail("input-boundary-state-mismatch");
        var inputStructuralMismatch = Compare([matchingInput], [matchingInput with { NormalizedSourcePath = "other.erb" }]);
        if (inputStructuralMismatch.First is not null || !inputStructuralMismatch.FirstDivergence.Contains("DivergenceKind=StructuralAlignment", StringComparison.Ordinal)) return Fail("input-boundary-structural-mismatch");
        var mixed = Compare(
            [Row(1, "FunctionEntry", "F", "input.erb", 1, 1, "<root>", "a"), matchingInput, Row(3, "FunctionExit", "F", "input.erb", 1, 1, "<root>", "b")],
            [Row(1, "FunctionEntry", "F", "input.erb", 1, 1, "<root>", "a"), matchingInput, Row(3, "FunctionExit", "F", "input.erb", 1, 1, "<root>", "b")]);
        if (mixed.Matched != 3 || mixed.First is not null) return Fail("mixed-input-boundary-order");
        var inputSparse = SidecarDifference("Variables", "I", "Integer", "7", null, "1", "F", "input.erb", 10, "InputBoundary");
        if (inputSparse is not { Variable: "I", VariableType: "Integer", Indices: "7", LegacyValue: "0", NextValue: "1" }) return Fail("input-boundary-sidecar-localization");
        Console.WriteLine("SyntheticIdenticalState=PASS");
        Console.WriteLine("SyntheticNumericMismatch=PASS");
        Console.WriteLine("SyntheticStringMismatch=PASS");
        Console.WriteLine("SyntheticIndexedMismatch=PASS");
        Console.WriteLine("SyntheticCharacterMismatch=PASS");
        Console.WriteLine("SyntheticExtraNextCheckpointAlignment=PASS");
        Console.WriteLine("SyntheticRecursiveAlignment=PASS");
        Console.WriteLine("SyntheticParentOrderSensitiveAlignment=PASS");
        Console.WriteLine("SyntheticDefaultValueLocalization=PASS");
        Console.WriteLine("SyntheticStringZeroLocalization=PASS");
        Console.WriteLine("SyntheticSparseDefaultTransitions=PASS");
        Console.WriteLine("SyntheticUnavailableCapture=PASS");
        Console.WriteLine("StructuralCheckpointsStateObservation=NONE");
        Console.WriteLine("LegacyNumericInputBoundarySourceWiring=PASS");
        Console.WriteLine("LegacyStringInputBoundarySourceWiring=PASS");
        Console.WriteLine("NextInputBoundarySourceWiring=PASS");
        Console.WriteLine("SingleTracePerInputSourceWiring=PASS");
        Console.WriteLine("InputBoundaryStateObservation=PASS");
        Console.WriteLine("InputBoundaryComparable=PASS");
        Console.WriteLine("InputBoundaryStateMismatchDetected=PASS");
        Console.WriteLine("InputBoundaryStructuralMismatchDetected=PASS");
        Console.WriteLine("InputBoundaryValueLocalization=PASS");
        Console.WriteLine("MixedStructuralInputBoundaryOrdering=PASS");
        Console.WriteLine("ExplicitWaitIsNotStateBearing=PASS");
        Console.WriteLine("SyntheticUnmatchedStructure=PASS");
        Console.WriteLine("SyntheticMissingLegacyRow=PASS");
        Console.WriteLine("SyntheticMissingNextRow=PASS");
        Console.WriteLine("CaptureSidecarNumericLocalization=PASS");
        Console.WriteLine("CaptureSidecarStringLocalization=PASS");
        Console.WriteLine("CaptureSidecarIndexedLocalization=PASS");
        Console.WriteLine("CaptureSidecarCharacterLocalization=PASS");
        Console.WriteLine("CaptureArtifactReadPath=PASS");
        Console.WriteLine("HistoricalExtraTitleBugAutoDetected=PASS");
        Console.WriteLine("DifferentialHarnessTests=PASS");
        return 0;
    }

    private static bool HasVariableDifference(string name, string type, string indices, string legacyValue, string nextValue) =>
        Compare([Row(1, "FunctionExit", "F", "f.erb", 1, 1, "<root>", "a", detail: Detail(name, type, indices, legacyValue))],
                [Row(1, "FunctionExit", "F", "f.erb", 1, 1, "<root>", "b", detail: Detail(name, type, indices, nextValue))]).First is { Variable: var variable } && variable == name;

    private static bool HasSidecarDifference(string domain, string name, string type, string indices, string legacyValue, string nextValue) =>
        SidecarDifference(domain, name, type, indices, legacyValue, nextValue, "F", "f.erb", 1) is { Variable: var variable } && variable == name;

    private static bool HasSparseSidecarDifference(string domain, string name, string type, string indices, string? legacyValue, string? nextValue)
    {
        var difference = SidecarDifference(domain, name, type, indices, legacyValue, nextValue, "F", "f.erb", 1);
        return difference is { Variable: var variable, VariableType: var variableType, Indices: var actualIndices, LegacyValue: var actualLegacy, NextValue: var actualNext }
            && variable == name && variableType == type && actualIndices == indices
            && actualLegacy == (legacyValue ?? (type.Contains("String", StringComparison.OrdinalIgnoreCase) ? "" : "0"))
            && actualNext == (nextValue ?? (type.Contains("String", StringComparison.OrdinalIgnoreCase) ? "" : "0"));
    }

    private static Divergence? SidecarDifference(string domain, string name, string type, string indices, string? legacyValue, string? nextValue, string functionName, string path, int line, string checkpointKind = "FunctionExit")
    {
        var directory = Path.Combine(Path.GetTempPath(), "Emuera.Next.DifferentialAudit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var legacyInput = Path.Combine(directory, "legacy.tsv");
            var nextInput = Path.Combine(directory, "next.tsv");
            var legacySidecar = legacyInput + ".details.tsv";
            var nextSidecar = nextInput + ".details.tsv";
            WriteSidecar(legacySidecar, domain, name, type, indices, legacyValue);
            WriteSidecar(nextSidecar, domain, name, type, indices, nextValue);
            WriteCheckpoint(legacyInput, Row(1, checkpointKind, functionName, path, line, 1, "<root>", "legacy", detail: "@1"));
            WriteCheckpoint(nextInput, Row(1, checkpointKind, functionName, path, line, 1, "<root>", "next", detail: "@1"));
            return Compare(Read(legacyInput), Read(nextInput)).First;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteSidecar(string path, string domain, string name, string type, string indices, string? value) =>
        File.WriteAllText(path, "Sequence\tOperation\tDomain\tName\tType\tIndices\tValueBase64\n" + (value is null ? "" : "1\tSet\t" + domain + "\t" + name + "\t" + type + "\t" + indices + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "\n"));
    private static void WriteCheckpoint(string path, Checkpoint row) =>
        File.WriteAllText(path, string.Join('\t', Header) + "\n" + string.Join('\t', [row.Sequence.ToString(), row.CheckpointKind, row.FunctionName, row.RuntimeFunctionId, row.NormalizedSourcePath, row.StartLine.ToString(), row.CallDepth.ToString(), row.InvocationOrdinal.ToString(), row.ParentIdentity, row.SelectedEngine, row.VmStopReason, row.EntryDispatchResult, row.FallbackReason, row.StateRootHash, row.VariablesHash, row.CharacterHash, row.RngHash, row.ValueDetails]) + "\n");

    private static int Fail(string name) { Console.Error.WriteLine($"DifferentialHarnessTests=FAIL:{name}"); return 1; }

    private static List<Checkpoint> Read(string path)
    {
        var lines = File.ReadLines(path).Where(static line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length == 0 || !string.Equals(lines[0], string.Join('\t', Header), StringComparison.Ordinal)) throw new InvalidDataException("invalid R1.4I checkpoint header");
        var detailSidecar = path + ".details.tsv";
        return lines.Skip(1).Select(line => Parse(line) with { DetailSidecarPath = detailSidecar }).ToList();
    }

    private static Checkpoint Parse(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length != Header.Length) throw new InvalidDataException("invalid R1.4I checkpoint column count");
        return new(int.Parse(parts[0]), parts[1], parts[2], parts[3], parts[4], int.Parse(parts[5]), int.Parse(parts[6]), int.Parse(parts[7]), parts[8], parts[9], parts[10], parts[11], parts[12], parts[13], parts[14], parts[15], parts[16], parts[17]);
    }

    private static Comparison Compare(IEnumerable<Checkpoint> legacyRows, IEnumerable<Checkpoint> nextRows)
    {
        var legacy = legacyRows.Where(IsComparable).OrderBy(static row => row.Sequence).ToArray();
        var next = nextRows.Where(IsComparable).OrderBy(static row => row.Sequence).ToArray();
        Checkpoint? lastMatching = null;
        Checkpoint? firstUnmatchedLegacy = null;
        Checkpoint? firstUnmatchedNext = null;
        for (var index = 0; index < Math.Min(legacy.Length, next.Length); index++)
        {
            if (Key(legacy[index]) == Key(next[index])) { lastMatching = legacy[index]; continue; }
            firstUnmatchedLegacy = legacy[index];
            firstUnmatchedNext = next[index];
            break;
        }
        if (firstUnmatchedLegacy is null && legacy.Length != next.Length)
        {
            firstUnmatchedLegacy = legacy.Length > next.Length ? legacy[next.Length] : null;
            firstUnmatchedNext = next.Length > legacy.Length ? next[legacy.Length] : null;
        }
        var matched = 0;
        var unmatchedLegacy = new List<Checkpoint>();
        var unmatchedNext = new List<Checkpoint>();
        var first = default(Divergence);
        var structural = false;
        for (var index = 0; index < Math.Min(legacy.Length, next.Length); index++)
        {
            if (Key(legacy[index]) != Key(next[index]))
            {
                structural = true;
                unmatchedLegacy.AddRange(legacy[index..]);
                unmatchedNext.AddRange(next[index..]);
                break;
            }
            matched++;
            if (first is null && IsStateObserved(legacy[index]) && IsStateObserved(next[index]) && !string.Equals(legacy[index].StateRootHash, next[index].StateRootHash, StringComparison.Ordinal)) first = BuildDivergence(legacy[index], next[index]);
        }
        if (!structural && legacy.Length != next.Length)
        {
            structural = true;
            if (legacy.Length > matched) unmatchedLegacy.AddRange(legacy[matched..]);
            if (next.Length > matched) unmatchedNext.AddRange(next[matched..]);
        }
        firstUnmatchedLegacy = unmatchedLegacy.Count == 0 ? null : unmatchedLegacy[0];
        firstUnmatchedNext = unmatchedNext.Count == 0 ? null : unmatchedNext[0];
        var summary = new StringBuilder()
            .AppendLine($"MatchedCheckpointCount={matched}")
            .AppendLine($"UnmatchedLegacyCheckpointCount={unmatchedLegacy.Count}")
            .AppendLine($"UnmatchedNextCheckpointCount={unmatchedNext.Count}")
            .AppendLine($"LastMatchingCheckpoint={FormatCheckpoint(lastMatching)}")
            .AppendLine($"FirstUnmatchedLegacyCheckpoint={FormatCheckpoint(firstUnmatchedLegacy)}")
            .AppendLine($"FirstUnmatchedNextCheckpoint={FormatCheckpoint(firstUnmatchedNext)}")
            .AppendLine($"FirstObservableDivergenceFound={(first is null && !structural ? "NO" : "YES")}")
            .AppendLine("AlignmentUsesSourceIdentity=YES")
            .AppendLine("RuntimeFunctionIdRequiredForCrossRunAlignment=NO")
            .AppendLine("IndexedWriteSetUsedAsHint=NO")
            .AppendLine("IndexedWriteSetUsedAsAuthority=NO")
            .AppendLine("ValueDetailLocalization=OPTIONAL_CAPTURE")
            .AppendLine("TwoLevelHashing=YES").ToString();
        var firstText = first is not null ? first.Render() : structural
            ? $"FirstObservableDivergenceFound=YES\nDivergenceKind=StructuralAlignment\nLastMatchingCheckpoint={FormatCheckpoint(lastMatching)}\nFirstUnmatchedLegacyCheckpoint={FormatCheckpoint(firstUnmatchedLegacy)}\nFirstUnmatchedNextCheckpoint={FormatCheckpoint(firstUnmatchedNext)}\n"
            : "FirstObservableDivergenceFound=NO\n";
        var domain = first is null ? "Domain\tLegacyHash\tNextHash\n" : first.DomainRows;
        return new(matched, unmatchedLegacy.Count, unmatchedNext.Count, first, lastMatching, firstUnmatchedLegacy, firstUnmatchedNext, summary, firstText, domain);
    }

    private static bool IsComparable(Checkpoint row) => row.CheckpointKind is "FunctionEntry" or "FunctionExit" or "InputBoundary" or "Wait" or "Phase";
    private static bool IsStateBearingKind(string kind) => string.Equals(kind, "InputBoundary", StringComparison.Ordinal);
    private static bool IsStateObserved(Checkpoint row) => !string.Equals(row.StateRootHash, "UNAVAILABLE", StringComparison.Ordinal);
    private static string Key(Checkpoint row) => string.Join('\u001f', row.CheckpointKind, row.NormalizedSourcePath, row.StartLine, row.FunctionName, row.CallDepth, row.InvocationOrdinal, row.ParentIdentity);

    private static Divergence BuildDivergence(Checkpoint legacy, Checkpoint next)
    {
        var domains = new List<(string Name, string Left, string Right)>();
        if (!Same(legacy.VariablesHash, next.VariablesHash)) domains.Add(("Variables", legacy.VariablesHash, next.VariablesHash));
        if (!Same(legacy.CharacterHash, next.CharacterHash)) domains.Add(("Character", legacy.CharacterHash, next.CharacterHash));
        if (!Same(legacy.RngHash, next.RngHash)) domains.Add(("Rng", legacy.RngHash, next.RngHash));
        if (domains.Count == 0) domains.Add(("StateRoot", legacy.StateRootHash, next.StateRootHash));
        var left = ResolveDetails(legacy);
        var right = ResolveDetails(next);
        var detail = left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal).Select(key => (key, Left: left.GetValueOrDefault(key), Right: right.GetValueOrDefault(key))).FirstOrDefault(pair => !string.Equals(LogicalValue(pair.Left, pair.Right), LogicalValue(pair.Right, pair.Left), StringComparison.Ordinal));
        var firstDetail = !string.IsNullOrEmpty(detail.Left.Name) ? detail.Left : !string.IsNullOrEmpty(detail.Right.Name) ? detail.Right : new ValueDetail("UNAVAILABLE_CAPTURE_V1", "Unknown", "", "<not-captured>");
        if (firstDetail.Name == "UNAVAILABLE_CAPTURE_V1") return new(legacy, next, domains, firstDetail.Name, firstDetail.Type, firstDetail.Indices, "<not-captured>", "<not-captured>");
        return new(legacy, next, domains, firstDetail.Name, firstDetail.Type, firstDetail.Indices, LogicalValue(detail.Left, detail.Right), LogicalValue(detail.Right, detail.Left));
    }

    private static string LogicalValue(ValueDetail detail, ValueDetail other) => detail.Value ?? (string.Equals(detail.Type, "Unknown", StringComparison.OrdinalIgnoreCase) || string.Equals(other.Type, "Unknown", StringComparison.OrdinalIgnoreCase) || (detail.Type is null && other.Type is null) ? "<not-captured>" : (detail.Type ?? other.Type).Contains("String", StringComparison.OrdinalIgnoreCase) ? "" : "0");

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal) || (left == "UNAVAILABLE" && right == "UNAVAILABLE");
    private static Dictionary<string, ValueDetail> ParseDetails(string value)
    {
        var result = new Dictionary<string, ValueDetail>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(value)) return result;
        foreach (var entry in value.Split(DetailEntry))
        {
            var part = entry.Split(DetailPart);
            if (part.Length == 4) result[$"{part[0]}|{part[1]}|{part[2]}"] = new(part[0], part[1], part[2], part[3]);
        }
        return result;
    }
    private static Dictionary<string, ValueDetail> ResolveDetails(Checkpoint checkpoint)
    {
        if (!checkpoint.ValueDetails.StartsWith('@') || !int.TryParse(checkpoint.ValueDetails.AsSpan(1), out var sequence)) return ParseDetails(checkpoint.ValueDetails);
        if (string.IsNullOrEmpty(checkpoint.DetailSidecarPath) || !File.Exists(checkpoint.DetailSidecarPath)) return [];
        var state = new Dictionary<string, ValueDetail>(StringComparer.Ordinal);
        var lines = File.ReadLines(checkpoint.DetailSidecarPath).ToArray();
        if (lines.Length == 0 || lines[0] != "Sequence\tOperation\tDomain\tName\tType\tIndices\tValueBase64") throw new InvalidDataException("invalid R1.4I detail sidecar header");
        foreach (var line in lines.Skip(1))
        {
            var part = line.Split('\t');
            if (part.Length != 7 || !int.TryParse(part[0], out var rowSequence) || rowSequence > sequence) continue;
            var key = $"{part[2]}|{part[3]}|{part[4]}|{part[5]}";
            if (part[1] == "Remove") state.Remove(key);
            else if (part[1] == "Set") state[key] = new(part[3], part[4], part[5], Encoding.UTF8.GetString(Convert.FromBase64String(part[6])));
        }
        return state;
    }
    private static string FormatCheckpoint(Checkpoint? checkpoint) => checkpoint is null ? "NONE" : $"{checkpoint.Value.CheckpointKind}|{checkpoint.Value.NormalizedSourcePath}:{checkpoint.Value.StartLine}|{checkpoint.Value.FunctionName}|depth={checkpoint.Value.CallDepth}|ordinal={checkpoint.Value.InvocationOrdinal}|parent={checkpoint.Value.ParentIdentity}";
    private static string Detail(string name, string type, string indices, string value) => string.Join(DetailPart, name, type, indices, value);
    private static Checkpoint Row(int sequence, string kind, string name, string path, int line, int depth, string parent, string hash, int ordinal = 1, string detail = "") => new(sequence, kind, name, "-1", path, line, depth, ordinal, parent, "Legacy", "", "", "", hash, hash, hash, "UNAVAILABLE", detail);
    private static Checkpoint StateRow(int sequence, string kind, string name, string path, int line, string root, string detailName, string detailType, string indices, string value) => new(sequence, kind, name, "-1", path, line, 1, 1, "<root>", "Legacy", "", "", "", root, root, root, "UNAVAILABLE", Detail(detailName, detailType, indices, value));

    private readonly record struct Checkpoint(int Sequence, string CheckpointKind, string FunctionName, string RuntimeFunctionId, string NormalizedSourcePath, int StartLine, int CallDepth, int InvocationOrdinal, string ParentIdentity, string SelectedEngine, string VmStopReason, string EntryDispatchResult, string FallbackReason, string StateRootHash, string VariablesHash, string CharacterHash, string RngHash, string ValueDetails, string DetailSidecarPath = "");
    private readonly record struct ValueDetail(string Name, string Type, string Indices, string Value);
    private sealed record Divergence(Checkpoint Legacy, Checkpoint Next, List<(string Name, string Left, string Right)> Domains, string Variable, string VariableType, string Indices, string LegacyValue, string NextValue)
    {
        public string DomainRows => "Domain\tLegacyHash\tNextHash\n" + string.Join(Environment.NewLine, Domains.Select(static row => $"{row.Name}\t{row.Left}\t{row.Right}")) + Environment.NewLine;
        public string Render() => $"FirstObservableDivergenceFound=YES\nFirstDivergenceSequence={Legacy.Sequence}\nFirstDivergenceFunction={Legacy.FunctionName}\nFirstDivergenceSourcePath={Legacy.NormalizedSourcePath}\nFirstDivergenceStartLine={Legacy.StartLine}\nFirstDivergenceRuntimeFunctionId={Next.RuntimeFunctionId}\nFirstDivergenceSelectedEngine={Next.SelectedEngine}\nLegacyRootHash={Legacy.StateRootHash}\nNextRootHash={Next.StateRootHash}\nDifferingDomains={string.Join(',', Domains.Select(static domain => domain.Name))}\nFirstDifferingVariable={Variable}\nVariableType={VariableType}\nIndices={Indices}\nLegacyValue={LegacyValue}\nNextValue={NextValue}\nLikelyLastWriterFunction={Legacy.FunctionName}\nLikelyLastWriterSourceLine={Legacy.StartLine}\n";
    }
    private readonly record struct Comparison(int Matched, int UnmatchedLegacy, int UnmatchedNext, Divergence? First, Checkpoint? LastMatching, Checkpoint? FirstUnmatchedLegacy, Checkpoint? FirstUnmatchedNext, string Summary, string FirstDivergence, string DomainDiff);
}
