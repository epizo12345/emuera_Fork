#if R0_F4D3
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const int F4D3DepthLimit = 60;
        private static readonly string[] F4D3ClassCNames = ["装備箇所_3203", "装備箇所_4601", "装備箇所_4602"];

        private sealed record F4D3Cell(int Index, long Value, string CellSha256, string SchemaIdentity,
            string SchemaSha256, string TokenIdentity, string TokenSha256, int OwnerGeneration, int ResetGeneration);
        private sealed record F4D3State(string StateSha256, long[] BaseEquipment, string BaseEquipmentSha256,
            long[] EquipPart, string EquipPartSha256, long[] Result, string ResultSha256, string[] Results,
            string ResultsSha256, string GetEquipNumArgs, long HelperArg, string SetGameplayStartFrame,
            string EventCursor, string SystemState, bool PendingBegin, string RngSha256, long RngCallCount,
            long ClockCount, string ClockSha256, string SourceGenerationSha256, string ConfigSha256,
            string ErhSchemaSha256, int OwnerGeneration, int ResetGeneration);
        private sealed record F4D3Edge(int RootIndex, string RootWrapper, int Depth, string PhysicalDefinition,
            int BoundFormal, long CellValue, string GeneratedTarget, string Classification, string? RelativePath,
            int? Line, string? SourceSha256, string? BodySha256, string StateKey);
        private sealed record F4D3Root(int RootIndex, string RootWrapper, long ActualCellValue,
            string GeneratedTarget, int Depth, string[] VisitedStates, string TerminalClass,
            string CertificateResult, string[] CyclePath);

        internal object F4D3StateBefore { get; private set; } = null!;
        internal object F4D3StateAfter { get; private set; } = null!;
        internal object F4D3Cells { get; private set; } = null!;
        internal object F4D3ClosureRoots { get; private set; } = null!;
        internal object F4D3ClosureEdges { get; private set; } = null!;
        internal object F4D3ClosureSummary { get; private set; } = null!;
        internal object F4D3CertificateEffects { get; private set; } = null!;
        internal object F4D3Gate { get; private set; } = null!;

        internal void CaptureF4D3ActualClosure(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1022" || SetEquipVarMaterialized != 0
                || compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START"
                || compactFrames[0].Pc != 1022)
                throw new InvalidOperationException("R0-F4D3 requires the actual F4C PC1022 boundary");

            f4d1ClassA = AdmitClassA(Path.Combine(DataRoot, "..", "evidence", "family-target-inventory.json"));
            f4d2ClassB = AdmitClassB(Path.Combine(DataRoot, "..", "evidence", "class-b-authority.json"));
            f4d2Helper = AdmitF4D2Helper();
            f4d2Saved = BindF4D2Saved(process, F4D2SavedName, 60);
            var classC = AdmitF4D3ClassC(Path.Combine(DataRoot, "..", "evidence", "family-target-inventory.json"));
            var equipPart = BindF4D3Array(process, "EQUIP_PART", 4500);

            // Allocate diagnostic-only candidate banks before the effect-free interval.
            if (Candidate)
            {
                _ = EnsureF4D1Bank(GetEquipNumName);
                _ = EnsureF4D1Bank(F4D2HelperName);
            }

            var cells = Enumerable.Range(0, 60).Select(i => f4d2Saved.Read(process, i)).ToArray();
            var schema = $"{f4d2Saved.Name}:Int64:SAVEDATA:rank1:length={f4d2Saved.Length}";
            var token = $"{f4d2Saved.Token.Name}:{f4d2Saved.Token.Code}:{f4d2Saved.Token.Dimension}:{f4d2Saved.Token.GetLength()}";
            var cellRows = cells.Select((value, index) => new F4D3Cell(index, value,
                Hash($"{index}:{value.ToString(CultureInfo.InvariantCulture)}"), schema, Hash(schema), token,
                Hash(token), OwnerGeneration, 1)).ToArray();

            var sourceGeneration = F4D3SourceGeneration();
            var configIdentity = F4D3ConfigIdentity();
            var schemaIdentity = F4D3SchemaIdentity(f4d2Saved.Token, equipPart.Token);
            var beforeGuards = R0E1AProof.Counters.Sum();
            var before = CaptureF4D3State(process, cells, equipPart, sourceGeneration, configIdentity, schemaIdentity);
            var edges = new List<F4D3Edge>();
            var roots = f4d2ClassB.Values.OrderBy(value => value.Argument)
                .Select(wrapper => WalkF4D3Root(wrapper, cells, classC, edges)).ToArray();
            var after = CaptureF4D3State(process,
                Enumerable.Range(0, 60).Select(i => f4d2Saved.Read(process, i)).ToArray(), equipPart,
                F4D3SourceGeneration(), F4D3ConfigIdentity(), F4D3SchemaIdentity(f4d2Saved.Token, equipPart.Token));
            var afterGuards = R0E1AProof.Counters.Sum();

            var stateStable = F4D3StateStable(before, after);
            var ready = roots.Count(row => row.CertificateResult == "Ready");
            var categories = edges.GroupBy(row => row.Classification)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            int Count(string name) => categories.GetValueOrDefault(name);
            var cycles = Count("Cycle");
            var maxDepth = edges.Count == 0 ? 0 : edges.Max(row => row.Depth);
            var scriptEffects = new
            {
                ScriptVariableWrites=0, PersistentFormalWrites=0, ResultWrites=0, FileIO=0,
                Display=0, Input=0, RNG=after.RngCallCount-before.RngCallCount,
                Clock=after.ClockCount-before.ClockCount, ColdMetadataReads="excluded from script-visible effects",
                Total=(after.RngCallCount-before.RngCallCount)+(after.ClockCount-before.ClockCount)
            };
            var pass = cells.Length == 60 && f4d1ClassA.Count == 1163 && f4d2ClassB.Count == 60
                && classC.Count == 3 && roots.Length == 60 && stateStable && scriptEffects.Total == 0
                && beforeGuards == afterGuards && (!Candidate || afterGuards == 0);

            F4D3StateBefore = before;
            F4D3StateAfter = after;
            F4D3Cells = new { Count=cellRows.Length, Rows=cellRows };
            F4D3ClosureRoots = roots;
            F4D3ClosureEdges = edges;
            F4D3ClosureSummary = new
            {
                Roots=roots.Length, ReadyRoots=ready, BlockedRoots=roots.Length-ready,
                KnownMissingEdges=Count("KnownMissing"), ClassAEdges=Count("KnownReadyClassA"),
                ClassBEdges=Count("KnownReadyClassB"), ClassCEdges=Count("KnownClassCNotReady"),
                OtherKnownEdges=Count("KnownUnsupported")+Count("WrongKind"),
                UnresolvedEdges=Count("UnresolvedIdentity"), SourceMismatchEdges=Count("SourceMismatch"),
                Cycles=cycles, DepthExceeded=Count("DepthExceeded"), MaxDepth=maxDepth,
                ActualClosureCycleFree=cycles==0, ClassBActualClosureReady=ready==60
            };
            F4D3CertificateEffects = scriptEffects;
            F4D3Gate = new
            {
                Pass=pass, ReachedF4CPC1022=true, BaseEquipmentCellsCaptured=cellRows.Length,
                StateStable=stateStable, SourceGenerationStable=before.SourceGenerationSha256==after.SourceGenerationSha256,
                ConfigStable=before.ConfigSha256==after.ConfigSha256,
                ErhSchemaStable=before.ErhSchemaSha256==after.ErhSchemaSha256,
                BaseEquipmentStable=before.BaseEquipmentSha256==after.BaseEquipmentSha256,
                OwnerGenerationStable=before.OwnerGeneration==after.OwnerGeneration,
                SetEquipVarEntries=0, EquipPartF4DWrites=0, ClassAAdmitted=f4d1ClassA.Count,
                ClassBWrapperSemanticReady=f4d2ClassB.Count, ClassCAdmitted=0,
                ActualSetEquipVarExecuted=false, Whole4500FamilyReady=false,
                CandidateGuardDelta=afterGuards-beforeGuards, Result=pass?"PASS":"FAIL"
            };
        }

        private Dictionary<string, R0F1Definition> AdmitF4D3ClassC(string inventoryPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
            var expected = document.RootElement.GetProperty("Entries").EnumerateArray()
                .Where(row => F4D3ClassCNames.Contains(row.GetProperty("Name").GetString(), StringComparer.Ordinal))
                .ToDictionary(row => row.GetProperty("Name").GetString()!, row => row, StringComparer.Ordinal);
            var result = new Dictionary<string, R0F1Definition>(StringComparer.Ordinal);
            foreach (var name in F4D3ClassCNames)
            {
                if (!expected.TryGetValue(name, out var row) || !functions.TryGetValue(name, out var matches) || matches.Length != 1)
                    throw new InvalidOperationException("F4D3 Class C identity missing/ambiguous: " + name);
                var definition = matches[0];
                var body = Executable(definition);
                if (definition.IsEvent || definition.RelativePath != row.GetProperty("RelativePath").GetString()
                    || definition.Line != row.GetProperty("Line").GetInt32()
                    || FileHash(definition.File.FileIdentity) != row.GetProperty("SourceSha256").GetString()
                    || BodyHash(body) != row.GetProperty("BodySha256").GetString()
                    || row.GetProperty("BodyKind").GetString() != "UnsupportedMultipleReturn")
                    throw new InvalidOperationException("F4D3 Class C source mismatch: " + name);
                result.Add(name, definition);
            }
            return result;
        }

        private F4D3Root WalkF4D3Root(ClassBDescriptor root, long[] cells,
            IReadOnlyDictionary<string, R0F1Definition> classC, List<F4D3Edge> edges)
        {
            var active = new Dictionary<ClosureState, int>();
            var visited = new List<string>();
            var cycle = Array.Empty<string>();
            var current = root;
            var terminal = "DepthExceeded";
            var generated = F4D2TargetName(cells[root.Argument]);
            var depth = 0;
            while (depth < F4D3DepthLimit)
            {
                var state = new ClosureState(current.Name, current.Argument);
                var stateText = $"{state.PhysicalDefinition}(ARG={state.BoundFormal})";
                if (active.TryGetValue(state, out var cycleStart))
                {
                    cycle = visited.Skip(cycleStart).Append(stateText).ToArray();
                    edges.Add(F4D3EdgeFor(root, ++depth, current, cells[current.Argument], generated, "Cycle", stateText));
                    terminal = "Cycle";
                    break;
                }
                active.Add(state, visited.Count);
                visited.Add(stateText);
                var value = cells[current.Argument];
                generated = F4D2TargetName(value);
                var classification = ClassifyF4D3(generated, classC);
                edges.Add(F4D3EdgeFor(root, ++depth, current, value, generated, classification, stateText));
                if (classification == "KnownReadyClassB")
                {
                    current = f4d2ClassB[generated];
                    continue;
                }
                terminal = classification == "KnownMissing" ? "InnerMissingReturnsZero" : classification;
                break;
            }
            if (depth == F4D3DepthLimit && terminal == "DepthExceeded")
                edges.Add(F4D3EdgeFor(root, depth, current, cells[current.Argument], generated, "DepthExceeded", visited[^1]));
            var ready = terminal is "KnownReadyClassA" or "InnerMissingReturnsZero";
            return new(root.Argument, root.Name, cells[root.Argument], F4D2TargetName(cells[root.Argument]),
                depth, visited.ToArray(), terminal, ready?"Ready":"Blocked", cycle);
        }

        private string ClassifyF4D3(string target, IReadOnlyDictionary<string, R0F1Definition> classC)
        {
            if (f4d1ClassA.ContainsKey(target)) return "KnownReadyClassA";
            if (f4d2ClassB.ContainsKey(target)) return "KnownReadyClassB";
            if (classC.ContainsKey(target)) return "KnownClassCNotReady";
            return Resolve(target, false) switch
            {
                R0F1Resolution.KnownMissing => "KnownMissing",
                R0F1Resolution.WrongKind => "WrongKind",
                R0F1Resolution.Unknown => "UnresolvedIdentity",
                _ => "KnownUnsupported"
            };
        }

        private F4D3Edge F4D3EdgeFor(ClassBDescriptor root, int depth, ClassBDescriptor current,
            long value, string target, string classification, string stateKey)
        {
            R0F1Definition? targetDefinition = null;
            if (functions.TryGetValue(target, out var matches) && matches.Length == 1) targetDefinition = matches[0];
            return new(root.Argument, root.Name, depth, current.Name, current.Argument, value, target,
                classification, targetDefinition?.RelativePath, targetDefinition?.Line,
                targetDefinition is null ? null : FileHash(targetDefinition.File.FileIdentity),
                targetDefinition is null ? null : BodyHash(Executable(targetDefinition)), stateKey);
        }

        private sealed record F4D3BoundArray(string Name, VariableToken Token, int Length);

        private static F4D3BoundArray BindF4D3Array(Process process, string name, int length)
        {
            var token = process.idDic.GetVariableToken(name, null, false);
            if (token is null || !token.IsInteger || token.IsPrivate || token.Dimension != 1 || token.GetLength() != length)
                throw new InvalidOperationException($"R0-F4D3 {name} schema mismatch");
            return new(name, token, length);
        }

        private F4D3State CaptureF4D3State(Process process, long[] cells, F4D3BoundArray equipPart,
            string sourceGeneration, string configIdentity, string schemaIdentity)
        {
            var equip = Enumerable.Range(0, equipPart.Length)
                .Select(i => equipPart.Token.GetIntValue(process.exm, [i])).ToArray();
            var result = (long[])process.vEvaluator.RESULT_ARRAY.Clone();
            var results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            var getArgs = Candidate ? EnsureF4D1Bank(GetEquipNumName).Args[0] : ReadLegacyPersistentArgs(process);
            var helperArg = ReadPersistentArg(process);
            var clock = DifferentialDeterminism.ObservationCount;
            return new(process.GetBenchmarkStateHash(), cells, HashLongs(cells), equip, HashLongs(equip), result,
                HashLongs(result), results, HashStrings(results), getArgs, helperArg, FrameText(compactFrames[0]),
                EventCursor, process.state.SystemState.ToString(), process.state.isBegun,
                process.vEvaluator.GetR0C2RngHash(), process.vEvaluator.GetR0F4D3RandomCallCount(),
                clock, Hash($"{Program.NextRuntimeDifferentialClockBase}|{Program.NextRuntimeDifferentialClockStepMs}|{clock}"),
                sourceGeneration, configIdentity, schemaIdentity, OwnerGeneration, 1);
        }

        private static bool F4D3StateStable(F4D3State before, F4D3State after) =>
            before.StateSha256==after.StateSha256 && before.BaseEquipmentSha256==after.BaseEquipmentSha256
            && before.EquipPartSha256==after.EquipPartSha256 && before.ResultSha256==after.ResultSha256
            && before.ResultsSha256==after.ResultsSha256 && before.GetEquipNumArgs==after.GetEquipNumArgs
            && before.HelperArg==after.HelperArg && before.SetGameplayStartFrame==after.SetGameplayStartFrame
            && before.EventCursor==after.EventCursor && before.SystemState==after.SystemState
            && before.PendingBegin==after.PendingBegin && before.RngSha256==after.RngSha256
            && before.RngCallCount==after.RngCallCount && before.ClockCount==after.ClockCount
            && before.ClockSha256==after.ClockSha256 && before.SourceGenerationSha256==after.SourceGenerationSha256
            && before.ConfigSha256==after.ConfigSha256 && before.ErhSchemaSha256==after.ErhSchemaSha256
            && before.OwnerGeneration==after.OwnerGeneration && before.ResetGeneration==after.ResetGeneration;

        private string F4D3SourceGeneration() => Hash(string.Join('\n', functions.Values.SelectMany(value => value)
            .OrderBy(value => value.RelativePath, StringComparer.Ordinal).ThenBy(value => value.Line)
            .Select(value => $"{value.RelativePath}:{value.Line}:{value.Name}:{value.Function.Flags}")));

        private string F4D3ConfigIdentity() => Hash(string.Join('|',
            $"{Program.NextRuntimeDifferentialSeed}",
            Program.NextRuntimeDifferentialClockBase ?? "",
            Program.NextRuntimeDifferentialClockStepMs.ToString(CultureInfo.InvariantCulture),
            Program.NextRuntimeMode.ToString(), sourceComplete.ToString()));

        private static string F4D3SchemaIdentity(VariableToken saved, VariableToken equip) => Hash(
            $"{saved.Name}:{saved.Code}:{saved.Dimension}:{saved.GetLength()}|{equip.Name}:{equip.Code}:{equip.Dimension}:{equip.GetLength()}");

        private static string HashLongs(IEnumerable<long> values) => Hash(string.Join(',',
            values.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        private static string HashStrings(IEnumerable<string?> values) => Hash(string.Join('\u001f', values.Select(value => value ?? "")));
    }
}
#endif
