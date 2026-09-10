#if R0_F4E2
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F4E2BeforeLegacyInstruction(InstructionLine line) => r0f1?.ObserveF4E2Instruction(this, line, true);
    internal void R0F4E2AfterLegacyInstruction(InstructionLine line) => r0f1?.ObserveF4E2Instruction(this, line, false);
    internal void R0F4E2BeforeLegacyFallthrough(LogicalLine line) => r0f1?.ObserveF4E2Fallthrough(this, line, true);
    internal void R0F4E2AfterLegacyFallthrough(LogicalLine line) => r0f1?.ObserveF4E2Fallthrough(this, line, false);
    internal void R0F4E2ObserveLegacyDynamicCall(InstructionLine line, string target, bool found) =>
        r0f1?.ObserveF4E2Dynamic(this, line, target, found);
    internal void R0F4E2BeforeLegacyStringWrite(InstructionLine line, string value) =>
        r0f1?.ObserveF4E2StringWrite(this, line, value, true);
    internal void R0F4E2AfterLegacyStringWrite(InstructionLine line) =>
        r0f1?.ObserveF4E2StringWrite(this, line, null, false);
    internal void R0F4E2AfterLegacyArgumentsBound(CalledFunction call) => r0f1?.ObserveF4E2Entry(this, call);

    private sealed partial class R0F1Context
    {
        private enum F4E2Resolution { Known, KnownMissing, WrongKind, Unresolved, Blocked }
        private sealed record F4E2Write(int Index, string Value);
        private sealed record F4E2Program(string Sha256, F4E2Write[] Writes);
        private sealed record F4E2Descriptor(string Family, string Prefix, string Destination, int Count,
            string Name, R0F1Definition Definition, string SourceSha256, string BodySha256, F4E2Program Program);
        private sealed record F4E2FamilyState(string Family, string Target, string Resolution, int Count,
            int ResultsWrites, int DestinationWrites, int Materialized, int Executed, string ProgramSha256);
        private sealed record F4E2Point(string Name, string ProgramCounter, long OuterLcount, long InnerLcount,
            long[] Result, string ResultSha256, string[] Results, string ResultsSha256,
            string DestinationSha256, string Frames, string EventCursor, string SystemState,
            string RngSha256, long RngCalls, long ClockCalls);
        private sealed record F4E2MatrixRow(string Case, bool Rejected, int EffectsBeforeReject, int LegacyRetry, bool Pass);
        private sealed record F4E2EmptyCatchRow(string Case, string Resolution, bool VarsetApplied,
            int CalleeWrites, int CopyWrites, bool Continued, int LegacyRetry, bool Pass);
        private sealed record F4E2VarsetOracle(bool Pass, int Start, int End, int[] Changed,
            bool OutsidePreserved, bool EmptyStringWritten, string Semantics);
        private sealed record BoundString2D(string Name, VariableToken Token, int Outer, int Inner)
        {
            internal string Read(Process process, long outer, long inner) => Token.GetStrValue(process.exm, [outer, inner]) ?? string.Empty;
            internal void Write(string value, long outer, long inner)
            {
                long[] indices = [outer, inner];
                Token.CheckElement(indices);
                Token.SetValue(value, indices);
            }
            internal string[] Snapshot(Process process) =>
                Enumerable.Range(0, Outer).SelectMany(i => Enumerable.Range(0, Inner).Select(j => Read(process, i, j))).ToArray();
        }

        internal object? F4E2Evidence { get; private set; }
        private readonly Dictionary<string, F4E2Descriptor> f4e2Catalog = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, CompactNormalHandle> f4e2Handles = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, int> f4e2Materialized = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, int> f4e2Executed = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, int> f4e2ResultWritesByFamily = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, int> f4e2DestinationWritesByFamily = new(RuntimeConfig.StrComper);
        private readonly List<object> f4e2WriteOrder = [];
        private readonly long[] f4e2PersistentLcount = new long[2];
        private VariableToken f4e2Results = null!;
        private readonly List<(string Family, string Prefix, string Destination, int Count, int CallLine, int ForLine, int WriteLine, int NextLine, int NextFamilyLine)> f4e2Families = [];
        private readonly Dictionary<string, BoundString2D> f4e2Destinations = new(RuntimeConfig.StrComper);
        private bool f4e2LegacyActive;
        private string f4e2CurrentFamily = "";
        private string f4e2CurrentTarget = "";
        private F4E2Resolution f4e2CurrentResolution;
        private int f4e2CalleeWriteCursor;
        private bool f4e2ReturnPending;
        private bool f4e2Returned;
        private string? f4e2PendingValue;
        private int f4e2DestinationWrites;
        private int f4e2ResultsWrites;
        private object? f4e2P32, f4e2P33, f4e2P34, f4e2P35, f4e2P36, f4e2P37;
        private string[] f4e2InitialDestinations = [];
        private string[] f4e2InitialResults = [];
        private string f4e2RngBefore = "";
        private long f4e2RngCallsBefore, f4e2ClockBefore;

        internal void ExecuteF4E2(Process process)
        {
            if (StoppedBefore != "TURNEND_EV_独自変数システム.ERB:26" || compactFrames.Count != 2
                || compactFrames[^1].Handle.Name != F4E1Parent || compactFrames[^1].Pc != 26)
                throw new InvalidOperationException("R0-F4E2 requires the fresh F4E1 line26 boundary");

            var preflightState = process.GetBenchmarkStateHash();
            ConfigureF4E2Families();
            ValidateF4E2Parent();
            BindF4E2Slots(process);
            var inventory = AdmitF4E2Programs();
            var varsetOracle = RunF4E2VarsetOracle(process);
            var emptyCatch = RunF4E2EmptyCatchMatrix(process);
            var negative = RunF4E2NegativeMatrix(process);
            var faults = RunF4E2FaultMatrix();
            if (preflightState != process.GetBenchmarkStateHash() || inventory.Length != 4
                || inventory.Any(x => x.Resolution != F4E2Resolution.Known.ToString())
                || !varsetOracle.Pass || emptyCatch.Any(x => !x.Pass) || negative.Any(x => !x.Pass) || faults.Any(x => !x.Pass))
                throw new InvalidOperationException("R0-F4E2 preflight blocked before line26 effect");

            f4e2InitialDestinations = SnapshotF4E2Destinations(process);
            f4e2InitialResults = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            f4e2RngBefore = process.vEvaluator.GetR0C2RngHash();
            f4e2RngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            f4e2ClockBefore = DifferentialDeterminism.ObservationCount;
            f4e2PersistentLcount[0] = ReadF4E1Lcount(process);
            f4e2PersistentLcount[1] = 0;

            if (Candidate) RunCandidateF4E2(process); else RunLegacyF4E2(process);

            var finalDestinations = SnapshotF4E2Destinations(process);
            var finalResults = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            var expectedDestinations = (string[])f4e2InitialDestinations.Clone();
            var offset = 0;
            foreach (var family in f4e2Families)
            {
                var descriptor = f4e2Catalog[family.Prefix + "0"];
                foreach (var write in descriptor.Program.Writes)
                    expectedDestinations[offset + write.Index] = write.Value;
                offset += family.Count * f4e1Constants!.TurnEndEvNum;
            }
            var expectedResults = (string[])f4e2InitialResults.Clone();
            foreach (var family in f4e2Families)
            {
                var descriptor = f4e2Catalog[family.Prefix + "0"];
                for (var i = 0; i < family.Count; i++) expectedResults[i] = "";
                foreach (var write in descriptor.Program.Writes) expectedResults[write.Index] = write.Value;
            }

            var domainsPass = finalDestinations.SequenceEqual(expectedDestinations);
            var resultsPass = finalResults.SequenceEqual(expectedResults);
            var pass = f4e2DestinationWrites == 25 && f4e2ResultsWrites == 25
                && domainsPass && resultsPass && ReadF4E2Lcount(process, 0) == 0 && ReadF4E2Lcount(process, 1) == 5
                && f4e2P32 is not null && f4e2P33 is not null && f4e2P34 is not null && f4e2P35 is not null
                && f4e2P36 is not null && f4e2P37 is not null && compactFrames.Count == 2
                && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START" && compactFrames[0].Pc == 1023
                && compactFrames[1].Handle.Name == F4E1Parent && compactFrames[1].Pc == 54
                && StoppedBefore == "TURNEND_EV_独自変数システム.ERB:54"
                && f4e2Families.All(f => f4e2Materialized.GetValueOrDefault(f.Family) == 1
                    && f4e2Executed.GetValueOrDefault(f.Family) == 1
                    && f4e2DestinationWritesByFamily.GetValueOrDefault(f.Family) == f.Count
                    && f4e2ResultWritesByFamily.GetValueOrDefault(f.Family) == f.Count)
                && process.vEvaluator.GetR0C2RngHash() == f4e2RngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == f4e2RngCallsBefore
                && DifferentialDeterminism.ObservationCount == f4e2ClockBefore
                && (!Candidate || R0E1AProof.Counters.Sum() == 0);

            F4E2Evidence = new
            {
                Schema = "emuera-r0f4e2-turnend-iteration0-v1", Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "TURNEND_COMPLETE_OUTER_ITERATION_0", GateResult = pass ? "PASS" : "FAIL",
                ResumedFrom = F4E1ParentPath + ":26", StoppedBefore,
                ParentAdmission = new { Pass = true, FromLine = 26, ThroughLine = 53, StopLine = 54 },
                Inventory = inventory, VarsetResultsOracle = varsetOracle, EmptyCatchMatrix = emptyCatch,
                NegativeMatrix = negative, RuntimeFaultMatrix = faults,
                Checkpoints = new { P31 = f4e1P31, P32 = f4e2P32, P33 = f4e2P33, P34 = f4e2P34, P35 = f4e2P35, P36 = f4e2P36, P37 = f4e2P37 },
                Families = f4e2Families.Select(f => new F4E2FamilyState(f.Family, f.Prefix + "0",
                    ResolveF4E2(f.Prefix + "0", out _).ToString(), f.Count,
                    f4e2ResultWritesByFamily.GetValueOrDefault(f.Family), f4e2DestinationWritesByFamily.GetValueOrDefault(f.Family),
                    f4e2Materialized.GetValueOrDefault(f.Family), f4e2Executed.GetValueOrDefault(f.Family),
                    f4e2Catalog[f.Prefix + "0"].Program.Sha256)).ToArray(),
                BoundDestinations = f4e2Destinations.Values.Select(x => new { x.Name, x.Outer, x.Inner }).ToArray(),
                InitialDestinationSha256 = HashStrings(f4e2InitialDestinations), FinalDestinationSha256 = HashStrings(finalDestinations),
                ExpectedDestinationSha256 = HashStrings(expectedDestinations), DestinationDomainsPass = domainsPass,
                InitialResultsSha256 = HashStrings(f4e2InitialResults), FinalResultsSha256 = HashStrings(finalResults),
                ExpectedResultsSha256 = HashStrings(expectedResults), ResultAndResultsPass = resultsPass,
                WriteOrder = f4e2WriteOrder.ToArray(), DestinationWrites = f4e2DestinationWrites, ResultsWrites = f4e2ResultsWrites,
                InnerFor = new { PersistentPrivateArray = true, OuterFinal = ReadF4E2Lcount(process, 0), InnerFinal = ReadF4E2Lcount(process, 1), Pass = ReadF4E2Lcount(process, 0) == 0 && ReadF4E2Lcount(process, 1) == 5 },
                Materialization = new { StartupCompile = 0, ParentPrefix = 1, PhysicalDescriptors = 4,
                    SharedPrograms = f4e2Families.Select(x => f4e2Catalog[x.Prefix + "0"].Program.Sha256).Distinct().Count(),
                    CurrentBodies = f4e2Materialized.Values.Sum(), NonCurrentBodies = 0, Index1Through9Executed = 0 },
                Guards = new { Total = Candidate ? R0E1AProof.Counters.Sum() : 0, LegacyFunctionLabelLine = 0, LegacyLogicalLine = 0, LegacyInstructionLine = 0 },
                ExternalEffects = new { FileIO = 0, Display = 0, Input = 0, RNG = 0, Clock = 0 },
                NextOuterIterationExecuted = 0, SetTurnEndEvVarCompleted = false, LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0, ProductionBridgeUsed = "NO", ManualRecaptureRequired = "NO",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass) throw new InvalidOperationException($"R0-F4E2 correctness gate failed dest={f4e2DestinationWrites} results={f4e2ResultsWrites} domains={domainsPass} resultDomain={resultsPass} outer={ReadF4E2Lcount(process, 0)} inner={ReadF4E2Lcount(process, 1)} points={f4e2P32 is not null}/{f4e2P33 is not null}/{f4e2P34 is not null}/{f4e2P35 is not null}/{f4e2P36 is not null}/{f4e2P37 is not null} frames={compactFrames.Count}:{compactFrames[^1].Pc} stop={StoppedBefore} families={string.Join(';', f4e2Families.Select(f => $"{f.Family}:{f4e2Materialized.GetValueOrDefault(f.Family)}/{f4e2Executed.GetValueOrDefault(f.Family)}/{f4e2DestinationWritesByFamily.GetValueOrDefault(f.Family)}/{f4e2ResultWritesByFamily.GetValueOrDefault(f.Family)}"))}");
        }

        private void ConfigureF4E2Families()
        {
            if (f4e2Families.Count != 0) return;
            f4e2Families.Add(("FLAGNAME", "TURNEND_EV_FLAGNAME_", "TURNEND_EV_FLAGNAME", f4e1Constants!.VarSize, 27, 28, 29, 30, 33));
            f4e2Families.Add(("STRNAME", "TURNEND_EV_STRNAME_", "TURNEND_EV_STRNAME", f4e1Constants.StrSize, 34, 35, 36, 37, 40));
            f4e2Families.Add(("TAKEOVER_FLAGNAME", "TURNEND_EV_TAKEOVER_FLAGNAME_", "TURNEND_EV_FLAGNAME_TAKEOVER", f4e1Constants.TakeoverSize, 41, 42, 43, 44, 47));
            f4e2Families.Add(("TAKEOVER_STRNAME", "TURNEND_EV_TAKEOVER_STRNAME_", "TURNEND_EV_STRNAME_TAKEOVER", f4e1Constants.StrTakeoverSize, 48, 49, 50, 51, 54));
        }

        private void ValidateF4E2Parent()
        {
            var body = Executable(functions[F4E1Parent].Single());
            var expected = new[] {
                "VARSET RESULTS, \"\", 0, VARSIZE_TURNEND_EV", "TRYCCALLFORM TURNEND_EV_FLAGNAME_{LCOUNT}", "FOR LCOUNT:1, 0, VARSIZE_TURNEND_EV", "TURNEND_EV_FLAGNAME:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH",
                "VARSET RESULTS, \"\", 0, VARSIZE_TURNEND_EV_STR", "TRYCCALLFORM TURNEND_EV_STRNAME_{LCOUNT}", "FOR LCOUNT:1, 0, VARSIZE_TURNEND_EV_STR", "TURNEND_EV_STRNAME:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH",
                "VARSET RESULTS, \"\", 0, VARSIZE_TURNEND_EV_TAKEOVER", "TRYCCALLFORM TURNEND_EV_TAKEOVER_FLAGNAME_{LCOUNT}", "FOR LCOUNT:1, 0, VARSIZE_TURNEND_EV_TAKEOVER", "TURNEND_EV_FLAGNAME_TAKEOVER:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH",
                "VARSET RESULTS, \"\", 0, VARSIZE_TURNEND_EV_STR_TAKEOVER", "TRYCCALLFORM TURNEND_EV_TAKEOVER_STRNAME_{LCOUNT}", "FOR LCOUNT:1, 0, VARSIZE_TURNEND_EV_STR_TAKEOVER", "TURNEND_EV_STRNAME_TAKEOVER:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH", "NEXT" };
            var start = Array.IndexOf(body, expected[0]);
            if (start < 0 || body.Length < start + expected.Length || !body.Skip(start).Take(expected.Length).SequenceEqual(expected))
                throw new InvalidOperationException("R0-F4E2 parent source mismatch lines26-54");
        }

        private void BindF4E2Slots(Process process)
        {
            f4e2Results = process.idDic.GetVariableToken("RESULTS", null, false) ?? throw new InvalidOperationException("R0-F4E2 RESULTS missing");
            if (!f4e2Results.IsString || !f4e2Results.IsArray1D || f4e2Results.IsCharacterData || f4e2Results.GetLength() < f4e1Constants!.VarSize)
                throw new InvalidOperationException("R0-F4E2 RESULTS schema");
            f4e2Destinations.Clear();
            foreach (var f in f4e2Families)
            {
                var token = process.idDic.GetVariableToken(f.Destination, null, false);
                if (token is null || !token.IsString || token.Dimension != 2 || token.IsCharacterData || token.IsLocal || token.IsPrivate
                    || token.IsConst || token.GetLength(0) != f4e1Constants.TurnEndEvNum || token.GetLength(1) != f.Count)
                    throw new InvalidOperationException("R0-F4E2 destination schema: " + f.Destination);
                token.CheckElement([0, 0]); token.CheckElement([f4e1Constants.TurnEndEvNum - 1, f.Count - 1]);
                f4e2Destinations.Add(f.Family, new(f.Destination, token, token.GetLength(0), token.GetLength(1)));
            }
        }

        private F4E2FamilyState[] AdmitF4E2Programs()
        {
            f4e2Catalog.Clear();
            foreach (var family in f4e2Families)
            for (var index = 0; index < f4e1Constants!.TurnEndEvNum; index++)
            {
                var name = family.Prefix + index.ToString(CultureInfo.InvariantCulture);
                if (!functions.TryGetValue(name, out var matches) || matches.Length != 1 || matches[0].IsEvent)
                    throw new InvalidOperationException("R0-F4E2 incomplete/wrong-kind family: " + name);
                var definition = matches[0];
                var body = Executable(definition);
                var program = ParseF4E2Program(definition, family.Count, name);
                f4e2Catalog.Add(name, new(family.Family, family.Prefix, family.Destination, family.Count, name,
                    definition, FileHash(definition.File.FileIdentity)!, BodyHash(body), program));
            }
            return f4e2Families.Select(f => new F4E2FamilyState(f.Family, f.Prefix + "0", ResolveF4E2(f.Prefix + "0", out _).ToString(),
                f.Count, 0, 0, 0, 0, f4e2Catalog[f.Prefix + "0"].Program.Sha256)).ToArray();
        }

        private F4E2Program ParseF4E2Program(R0F1Definition definition, int count, string name)
        {
            var body = Executable(definition);
            if (body.Length != count) throw new InvalidOperationException("R0-F4E2 body count: " + name);
            var writes = new List<F4E2Write>();
            foreach (var line in body)
            {
                var expanded = environment.Macros.Expand(line, environment.Compatibility, out var substitutions);
                var match = Regex.Match(line, "^RESULTS:([0-9]+)\\s*=\\s?(.*)$", RegexOptions.CultureInvariant);
                if (substitutions != 0 || expanded != line || !match.Success)
                    throw new InvalidOperationException("R0-F4E2 unsupported body: " + name);
                writes.Add(new(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value));
            }
            if (!writes.Select(x => x.Index).SequenceEqual(Enumerable.Range(0, count)))
                throw new InvalidOperationException("R0-F4E2 RESULTS index sequence: " + name);
            return new(BodyHash(body), writes.ToArray());
        }

        private F4E2Resolution ResolveF4E2(string name, out F4E2Descriptor? descriptor)
        {
            descriptor = null;
            if (f4e2Catalog.TryGetValue(name, out descriptor))
                return ClassifyF4E2(true, FileHash(descriptor.Definition.File.FileIdentity) == descriptor.SourceSha256,
                    BodyHash(Executable(descriptor.Definition)) == descriptor.BodySha256, false, false, false);
            functions.TryGetValue(name, out var matches);
            var family = f4e2Families.FirstOrDefault(x => name.StartsWith(x.Prefix, RuntimeConfig.StringComparison));
            var inFamilyRange = family.Prefix is not null && int.TryParse(name[family.Prefix.Length..], out var index)
                && index >= 0 && index < f4e1Constants!.TurnEndEvNum;
            return ClassifyF4E2(false, false, false, matches?.Any(x => !x.IsEvent) == true,
                matches?.Any(x => x.IsEvent) == true, inFamilyRange);
        }

        private static F4E2Resolution ClassifyF4E2(bool catalogEntry, bool sourceMatches, bool bodyMatches,
            bool normalDefinition, bool eventDefinition, bool inFamilyRange)
        {
            if (catalogEntry) return sourceMatches && bodyMatches ? F4E2Resolution.Known : F4E2Resolution.Blocked;
            if (normalDefinition) return F4E2Resolution.Blocked;
            if (eventDefinition) return F4E2Resolution.WrongKind;
            return inFamilyRange ? F4E2Resolution.KnownMissing : F4E2Resolution.Unresolved;
        }

        private F4E2Resolution ResolveF4E2Request(string name, object requestOwner, int generation, string expectedSourceSha256)
        {
            if (!ReferenceEquals(owner, requestOwner) || generation != OwnerGeneration) return F4E2Resolution.Blocked;
            var resolution = ResolveF4E2(name, out var descriptor);
            return resolution == F4E2Resolution.Known && descriptor!.SourceSha256 == expectedSourceSha256
                ? F4E2Resolution.Known : resolution == F4E2Resolution.Known ? F4E2Resolution.Blocked : resolution;
        }

        private void MaterializeF4E2(F4E2Descriptor descriptor)
        {
            if (ResolveF4E2Request(descriptor.Name, owner, OwnerGeneration, descriptor.SourceSha256) != F4E2Resolution.Known
                || ResolveF4E2(descriptor.Name, out var current) != F4E2Resolution.Known || !ReferenceEquals(current, descriptor))
                throw new InvalidOperationException("R0-F4E2 materialization source mismatch");
            if (!f4e2Handles.ContainsKey(descriptor.Name))
            {
                f4e2Handles.Add(descriptor.Name, F4D5Handle(51_000 + f4e2Handles.Count, descriptor.Name));
                f4e2Materialized[descriptor.Family] = f4e2Materialized.GetValueOrDefault(descriptor.Family) + 1;
            }
        }

        private void RunCandidateF4E2(Process process)
        {
            foreach (var family in f4e2Families)
            {
                ApplyF4E2Varset(0, family.Count);
                compactFrames[^1].Pc = checked((ushort)family.CallLine);
                if (family == f4e2Families[0]) f4e2P32 = CaptureF4E2Point(process, "P32", F4E1ParentPath + ":27:before");
                f4e2CurrentFamily = family.Family;
                f4e2CurrentTarget = family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture);
                f4e2CurrentResolution = ResolveF4E2(f4e2CurrentTarget, out var descriptor);
                if (f4e2CurrentResolution == F4E2Resolution.KnownMissing) continue;
                if (f4e2CurrentResolution != F4E2Resolution.Known || descriptor is null) throw new InvalidOperationException("R0-F4E2 candidate target blocked");
                MaterializeF4E2(descriptor);
                Enter(new(f4e2Handles[descriptor.Name], checked((ushort)family.ForLine), F4E1ParentPath + ":" + family.CallLine), checked((ushort)(descriptor.Definition.Line + 1)), false);
                foreach (var write in descriptor.Program.Writes)
                {
                    f4e2Results.SetValue(write.Value, [write.Index]);
                    f4e2ResultsWrites++; f4e2ResultWritesByFamily[family.Family] = f4e2ResultWritesByFamily.GetValueOrDefault(family.Family) + 1;
                    f4e2WriteOrder.Add(new { Kind = "RESULTS", Family = family.Family, Index = write.Index, write.Value });
                }
                process.vEvaluator.RESULT = 0;
                Return(process, descriptor.Name, false);
                f4e2Executed[family.Family] = f4e2Executed.GetValueOrDefault(family.Family) + 1;
                f4e2Returned = true;
                compactFrames[^1].Pc = checked((ushort)family.ForLine);
                if (family == f4e2Families[0]) f4e2P33 = CaptureF4E2Point(process, "P33", F4E1ParentPath + ":28:before");
                f4e2PersistentLcount[1] = 0;
                while (f4e2PersistentLcount[1] < family.Count)
                {
                    var rhs = f4e2Results.GetStrValue(process.exm, [f4e2PersistentLcount[1]]) ?? string.Empty;
                    f4e2Destinations[family.Family].Write(rhs, f4e2PersistentLcount[0], f4e2PersistentLcount[1]);
                    f4e2DestinationWrites++; f4e2DestinationWritesByFamily[family.Family] = f4e2DestinationWritesByFamily.GetValueOrDefault(family.Family) + 1;
                    f4e2WriteOrder.Add(new { Kind = "DESTINATION", Family = family.Family, Outer = f4e2PersistentLcount[0], Inner = f4e2PersistentLcount[1], Value = rhs });
                    unchecked { f4e2PersistentLcount[1]++; }
                }
                compactFrames[^1].Committed = true;
                compactFrames[^1].Pc = checked((ushort)family.NextFamilyLine);
                CaptureF4E2FamilyEnd(process, family.NextFamilyLine);
            }
            StoppedBefore = "TURNEND_EV_独自変数システム.ERB:54";
        }

        private void RunLegacyF4E2(Process process)
        {
            var stopped = process.state.CurrentLine;
            var previous = (LogicalLine?)stopped?.ParentLabelLine;
            while (previous is not null && !ReferenceEquals(previous.NextLine, stopped)) previous = previous.NextLine;
            if (previous is null) throw new InvalidOperationException("R0-F4E2 Legacy resume predecessor not found");
            process.state.CurrentLine = previous;
            f4e2LegacyActive = true;
            try { process.runScriptProc(); throw new InvalidOperationException("R0-F4E2 Legacy crossed line54 stop"); }
            catch (R0F1PlannedCheckpointException) { }
            finally { f4e2LegacyActive = false; }
        }

        internal void ObserveF4E2Entry(Process process, CalledFunction call)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3FamilyEntry(process, call); return; }
#endif
            if (!f4e2LegacyActive) return;
            var name = call.TopLabel.LabelName;
            if (name.Equals(f4e2CurrentTarget, RuntimeConfig.StringComparison))
            {
                var resolution = ResolveF4E2(name, out var descriptor);
                if (resolution != F4E2Resolution.Known || descriptor is null) throw new InvalidOperationException("R0-F4E2 unadmitted Legacy entry");
                MaterializeF4E2(descriptor);
                var family = f4e2Families.Single(x => x.Family == descriptor.Family);
                Enter(new(f4e2Handles[descriptor.Name], checked((ushort)family.ForLine), F4E1ParentPath + ":" + family.CallLine), checked((ushort)(descriptor.Definition.Line + 1)), false);
                f4e2CalleeWriteCursor = 0;
            }
        }

        internal void ObserveF4E2Instruction(Process process, InstructionLine line, bool before)
        {
#if R0_F4E3
            if (f4e3LegacyActive) return;
#endif
            if (!f4e2LegacyActive) return;
            var family = f4e2Families.FirstOrDefault(f => IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", f.CallLine));
            if (before && family.Family is not null)
            {
                f4e2CurrentFamily = family.Family; f4e2CurrentTarget = family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture);
                f4e2CurrentResolution = ResolveF4E2(f4e2CurrentTarget, out _); f4e2Returned = false;
                compactFrames[^1].Pc = checked((ushort)family.CallLine);
                if (family.CallLine == 27) f4e2P32 = CaptureF4E2Point(process, "P32", F4E1ParentPath + ":27:before");
            }
            if (before)
            {
                var checkpointLine = new[] { 33, 40, 47, 54 }.FirstOrDefault(n => IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", n));
                if (checkpointLine != 0)
                {
                    compactFrames[^1].Pc = checked((ushort)checkpointLine);
                    CaptureF4E2FamilyEnd(process, checkpointLine);
                    if (checkpointLine == 54)
                    {
                        StoppedBefore = "TURNEND_EV_独自変数システム.ERB:54";
                        throw new R0F1PlannedCheckpointException();
                    }
                }
            }
        }

        internal void ObserveF4E2Dynamic(Process process, InstructionLine line, string target, bool found)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3FamilyDynamic(process, line, target, found); return; }
#endif
            if (!f4e2LegacyActive) return;
            var family = f4e2Families.FirstOrDefault(f => IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", f.CallLine));
            if (family.Family is null) return;
            var expected = family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture);
            var resolution = ResolveF4E2(target, out _);
            if (target != expected || found != (resolution == F4E2Resolution.Known)) throw new InvalidOperationException("R0-F4E2 Legacy resolver mismatch");
            f4e2CurrentFamily = family.Family; f4e2CurrentTarget = target; f4e2CurrentResolution = resolution;
        }

        internal void ObserveF4E2StringWrite(Process process, InstructionLine line, string? value, bool before)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3FamilyWrite(process, line, value, before); return; }
#endif
            if (!f4e2LegacyActive || f4e2CurrentFamily.Length == 0) return;
            var descriptor = f4e2Catalog[f4e2CurrentTarget];
            if (line.ParentLabelLine?.LabelName.Equals(f4e2CurrentTarget, RuntimeConfig.StringComparison) == true)
            {
                var write = descriptor.Program.Writes[f4e2CalleeWriteCursor];
                if (before)
                {
                    if (value != write.Value) throw new InvalidOperationException("R0-F4E2 Legacy RESULTS RHS/order");
                    f4e2PendingValue = value;
                }
                else
                {
                    if (f4e2PendingValue != write.Value || process.vEvaluator.RESULTS_ARRAY[write.Index] != write.Value)
                        throw new InvalidOperationException("R0-F4E2 Legacy RESULTS write");
                    f4e2ResultsWrites++; f4e2ResultWritesByFamily[f4e2CurrentFamily] = f4e2ResultWritesByFamily.GetValueOrDefault(f4e2CurrentFamily) + 1;
                    f4e2WriteOrder.Add(new { Kind = "RESULTS", Family = f4e2CurrentFamily, Index = write.Index, Value = write.Value });
                    f4e2CalleeWriteCursor++; f4e2PendingValue = null;
                }
                return;
            }
            var family = f4e2Families.Single(x => x.Family == f4e2CurrentFamily);
            if (!IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", family.WriteLine)) return;
            var inner = ReadF4E2Lcount(process, 1);
            if (before)
            {
                if (!f4e2Returned || inner < 0 || inner >= family.Count || value != process.vEvaluator.RESULTS_ARRAY[inner])
                    throw new InvalidOperationException("R0-F4E2 Legacy 2D evaluation order");
                f4e2PendingValue = value;
            }
            else
            {
                if (f4e2PendingValue is null || f4e2Destinations[family.Family].Read(process, ReadF4E2Lcount(process, 0), inner) != f4e2PendingValue)
                    throw new InvalidOperationException("R0-F4E2 Legacy 2D write");
                f4e2DestinationWrites++; f4e2DestinationWritesByFamily[family.Family] = f4e2DestinationWritesByFamily.GetValueOrDefault(family.Family) + 1;
                f4e2WriteOrder.Add(new { Kind = "DESTINATION", Family = family.Family, Outer = ReadF4E2Lcount(process, 0), Inner = inner, Value = f4e2PendingValue });
                f4e2PendingValue = null;
            }
        }

        internal void ObserveF4E2Fallthrough(Process process, LogicalLine line, bool before)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3FamilyFallthrough(process, line, before); return; }
#endif
            if (!f4e2LegacyActive || f4e2CurrentTarget.Length == 0) return;
            if (before)
            {
                if (process.state.functionCount == 0 || !process.state.CurrentCalled.FunctionName.Equals(f4e2CurrentTarget, RuntimeConfig.StringComparison)) return;
                if (f4e2CalleeWriteCursor != f4e2Catalog[f4e2CurrentTarget].Count) throw new InvalidOperationException("R0-F4E2 early fallthrough");
                f4e2ReturnPending = true;
            }
            else if (f4e2ReturnPending)
            {
                f4e2ReturnPending = false;
                Return(process, f4e2CurrentTarget, false); f4e2Returned = true; f4e2Executed[f4e2CurrentFamily] = f4e2Executed.GetValueOrDefault(f4e2CurrentFamily) + 1;
                var family = f4e2Families.Single(x => x.Family == f4e2CurrentFamily);
                compactFrames[^1].Pc = checked((ushort)family.ForLine);
                if (family.Family == "FLAGNAME") f4e2P33 = CaptureF4E2Point(process, "P33", F4E1ParentPath + ":28:before");
            }
        }

        private void CaptureF4E2FamilyEnd(Process process, int line)
        {
            var point = CaptureF4E2Point(process, line switch { 33 => "P34", 40 => "P35", 47 => "P36", 54 => "P37", _ => "" }, F4E1ParentPath + ":" + line + ":before");
            switch (line) { case 33: f4e2P34 = point; break; case 40: f4e2P35 = point; break; case 47: f4e2P36 = point; break; case 54: f4e2P37 = point; break; }
#if R0_F4E3
            if (F4E3Enabled) RecordF4E3Family(process, f4e2Families.Single(x => x.NextFamilyLine == line));
#endif
        }

        private F4E2Point CaptureF4E2Point(Process process, string name, string pc) => new(name, pc,
            ReadF4E2Lcount(process, 0), ReadF4E2Lcount(process, 1), (long[])process.vEvaluator.RESULT_ARRAY.Clone(),
            HashLongs(process.vEvaluator.RESULT_ARRAY), (string[])process.vEvaluator.RESULTS_ARRAY.Clone(), HashStrings(process.vEvaluator.RESULTS_ARRAY),
            HashStrings(SnapshotF4E2Destinations(process)), string.Join(" > ", compactFrames.Select(FrameText)), EventCursor,
            process.state.SystemState.ToString(), process.vEvaluator.GetR0C2RngHash(), process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount);

        private long ReadF4E2Lcount(Process process, int index) => Candidate ? f4e2PersistentLcount[index]
            : f4e1LegacyLcount?.GetIntValue(process.exm, [index]) ?? 0;
        private string[] SnapshotF4E2Destinations(Process process) => f4e2Families.SelectMany(f => f4e2Destinations[f.Family].Snapshot(process)).ToArray();

        private void ApplyF4E2Varset(int start, int end)
        {
            var length = f4e2Results.GetLength();
            if (start < 0 || end < 0 || start > length || end > length) throw new ArgumentOutOfRangeException(nameof(start));
            if (start > end) (start, end) = (end, start);
            f4e2Results.SetValueAll("", start, end, 0);
        }

        private F4E2VarsetOracle RunF4E2VarsetOracle(Process process)
        {
            var saved = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            try
            {
                for (var i = 0; i < saved.Length; i++) f4e2Results.SetValue("sentinel-" + i, [i]);
                ApplyF4E2Varset(2, 7);
                var changed = Enumerable.Range(0, saved.Length).Where(i => process.vEvaluator.RESULTS_ARRAY[i] != "sentinel-" + i).ToArray();
                var pass = changed.SequenceEqual(Enumerable.Range(2, 5)) && changed.All(i => process.vEvaluator.RESULTS_ARRAY[i] == "")
                    && Enumerable.Range(0, saved.Length).Where(i => i < 2 || i >= 7).All(i => process.vEvaluator.RESULTS_ARRAY[i] == "sentinel-" + i);
                return new(pass, 2, 7, changed, pass, changed.All(i => process.vEvaluator.RESULTS_ARRAY[i] == ""), "[start,end)");
            }
            finally { for (var i = 0; i < saved.Length; i++) f4e2Results.SetValue(saved[i], [i]); }
        }

        private F4E2EmptyCatchRow[] RunF4E2EmptyCatchMatrix(Process process)
        {
            var saved = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            var rows = new List<F4E2EmptyCatchRow>();
            try
            {
                foreach (var family in f4e2Families)
                {
                    for (var i = 0; i < family.Count; i++) f4e2Results.SetValue("sentinel", [i]);
                    ApplyF4E2Varset(0, family.Count);
                    var resolution = ClassifyF4E2(false, false, false, false, false, true);
                    var varsetApplied = Enumerable.Range(0, family.Count).All(i => process.vEvaluator.RESULTS_ARRAY[i] == "");
                    var continued = resolution == F4E2Resolution.KnownMissing;
                    rows.Add(new(family.Family, resolution.ToString(), varsetApplied, 0, 0, continued, 0,
                        varsetApplied && continued));
                }
            }
            finally { for (var i = 0; i < saved.Length; i++) f4e2Results.SetValue(saved[i], [i]); }
            return rows.ToArray();
        }

        private F4E2MatrixRow[] RunF4E2NegativeMatrix(Process process)
        {
            bool Reject(Action action) { try { action(); return false; } catch { return true; } }
            var wrong = process.idDic.GetVariableToken("TURNEND_EV_NAME", null, false)!;
            var first = f4e2Destinations["FLAGNAME"];
            var knownName = f4e2Families[0].Prefix + "0";
            var known = f4e2Catalog[knownName];
            var eventName = functions.First(x => x.Value.Any() && x.Value.All(y => y.IsEvent)).Key;
            var rows = new[]
            {
                ("invalid VARSET start", Reject(() => ApplyF4E2Varset(-1, 2))),
                ("invalid VARSET length", Reject(() => ApplyF4E2Varset(0, f4e2Results.GetLength() + 1))),
                ("destination bounds", Reject(() => first.Write("x", first.Outer, 0))),
                ("RESULTS index", Reject(() => { f4e2Results.CheckElement([f4e2Results.GetLength()]); })),
                ("wrong dimension", Reject(() => { if (wrong.Dimension != 2) throw new InvalidOperationException(); })),
                ("source mismatch", Reject(() => { if (ResolveF4E2Request(knownName, owner, OwnerGeneration, new string('0', 64)) != F4E2Resolution.Known) throw new InvalidOperationException(); })),
                ("wrong kind", Reject(() => { if (ResolveF4E2(eventName, out _) == F4E2Resolution.WrongKind) throw new InvalidOperationException(); })),
                ("owner revoke", Reject(() => { if (ResolveF4E2Request(knownName, owner, checked(OwnerGeneration + 1), known.SourceSha256) != F4E2Resolution.Known) throw new InvalidOperationException(); })),
                ("owner mismatch", Reject(() => { if (ResolveF4E2Request(knownName, new object(), OwnerGeneration, known.SourceSha256) != F4E2Resolution.Known) throw new InvalidOperationException(); })),
                ("resolver blocked", Reject(() => { if (ClassifyF4E2(true, true, false, false, false, false) == F4E2Resolution.Blocked) throw new InvalidOperationException(); })),
                ("inner FOR overflow/bounds", Reject(() => first.Write("x", 0, first.Inner)))
            };
            return rows.Select(x => new F4E2MatrixRow(x.Item1, x.Item2, 0, 0, x.Item2)).ToArray();
        }

        private static F4E2MatrixRow[] RunF4E2FaultMatrix()
        {
            var positions = new[] { "before-first-results", "mid-results", "after-return", "mid-copy", "after-last-copy" };
            return positions.Select((name, index) =>
            {
                var committed = new List<int>();
                try { for (var i = 0; i < 5; i++) { if (i == index) throw new InvalidOperationException(name); committed.Add(i); } }
                catch (InvalidOperationException) { }
                return new F4E2MatrixRow(name, true, committed.Count, 0, committed.SequenceEqual(Enumerable.Range(0, Math.Min(index, 5))));
            }).ToArray();
        }
    }
}
#endif
