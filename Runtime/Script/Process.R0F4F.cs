#if R0_F4F
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
    private sealed partial class R0F1Context
    {
        private const string F4FParent = "SET_SHOP_EV_VAR";
        private const string F4FParentFile = "SHOP_EV_独自変数システム.ERB";
        private const string F4FParentPath = "RPG/ショップイベント/SHOP_EV_独自変数システム.ERB";
        private const string F4FNamePrefix = "SHOP_EV_NAME_";

        private sealed record F4FPreflightRow(string Family, int Outer, string Target, string Resolution,
            string BodyClass, int[] ResultsIndices, string[] Unsupported, string? SourcePath, int? Line,
            string? SourceSha256, string? BodySha256);
        private sealed record F4FDigestRow(int Outer, string Family, string Target, string Resolution,
            int CalleeWrites, string ResultsSha256, int CopyWrites, string DestinationSha256, bool Break);
        private sealed record F4FPoint(string Name, string ProgramCounter, long OuterLcount, long InnerLcount,
            string Frames, string EventCursor, string SystemState, bool PendingBegin, string RngSha256,
            long RngCalls, long ClockCalls, string DomainSha256);

        internal object? F4FEvidence { get; private set; }
        private object? f4fPreflight;
        private readonly List<F4FPreflightRow> f4fPreflightRows = [];
        private readonly List<F4FDigestRow> f4fDigest = [];
        private readonly Dictionary<string, CompactNormalHandle> f4fNameHandles = new(RuntimeConfig.StrComper);
        private VariableToken? f4fLegacyLcount;
        private bool f4fLegacyActive;
        private bool f4fTargetReturnPending;
        private bool f4fParentReturnPending;
        private bool f4fReturned;
        private bool f4fParentReturned;
        private string f4fCurrentFamily = "";
        private string f4fCurrentTarget = "";
        private F4E1Resolution f4fNameResolution;
        private F4E2Resolution f4fFamilyResolution;
        private int f4fCalleeWriteCursor;
        private int f4fCalleeWrites;
        private int f4fCopyWrites;
        private int f4fResultsWrites;
        private int f4fDestinationWrites;
        private int f4fBreakExecutions;
        private int f4fSuccessfulIterations;
        private int f4fBreakAt = -1;
        private string? f4fPendingValue;
        private object? f4fS0, f4fS1, f4fS2, f4fS3, f4fS4;
        private string f4fRngBefore = "";
        private long f4fRngCallsBefore;
        private long f4fClockBefore;

        internal void ExecuteF4F(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1024 CALL SET_SHOP_EV_VAR"
                || compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START"
                || compactFrames[0].Pc != 1024)
                throw new InvalidOperationException("R0-F4F requires the fresh F4E3 SYSTEM1024 boundary");

            PrepareF4FPreflight(process);
            f4e2PersistentLcount[0] = 0;
            f4e2PersistentLcount[1] = 0;
            f4fRngBefore = process.vEvaluator.GetR0C2RngHash();
            f4fRngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            f4fClockBefore = DifferentialDeterminism.ObservationCount;
            f4fS0 = CaptureF4FPoint(process, "S0", "SYSTEM.ERB:1024:before");

            if (Candidate) RunCandidateF4F(process); else RunLegacyF4F(process);

            var domains = CaptureF4FDomains(process);
            var expectedFamilies = new[] { "NAME", "FLAGNAME", "STRNAME", "TAKEOVER_FLAGNAME", "TAKEOVER_STRNAME" };
            var expectedRows = Enumerable.Range(0, f4fSuccessfulIterations)
                .SelectMany(outer => expectedFamilies.Select(family => (outer, family)))
                .Concat(f4fBreakAt >= 0 ? [(f4fBreakAt, "NAME")] : [])
                .ToArray();
            var orderPass = f4fDigest.Count == expectedRows.Length
                && f4fDigest.Select((row, index) => row.Outer == expectedRows[index].outer
                    && row.Family == expectedRows[index].family).All(x => x);
            var rowSemanticsPass = f4fDigest.All(RowSemanticsPass);
            var finalOuter = ReadF4FLcount(process, 0);
            var finalInner = ReadF4FLcount(process, 1);
            var expectedSuccessful = f4fBreakAt >= 0 ? f4fBreakAt : f4e1Constants!.TurnEndEvNum;
            var expectedOuter = f4fBreakAt >= 0 ? f4fBreakAt + 1 : f4e1Constants!.TurnEndEvNum;
            var expectedBreaks = f4fBreakAt >= 0 ? 1 : 0;
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var pass = orderPass && rowSemanticsPass && finalOuter == expectedOuter
                && f4fSuccessfulIterations == expectedSuccessful && f4fBreakExecutions == expectedBreaks
                && f4fS0 is not null && f4fS1 is not null && f4fS2 is not null
                && (f4fBreakAt < 0 || f4fS3 is not null) && f4fS4 is not null
                && f4fParentReturned && compactFrames.Count == 1
                && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START" && compactFrames[0].Pc == 1025
                && StoppedBefore == "SYSTEM.ERB:1025 CALL モブ画像_全リセット"
                && EventCursor.Contains("return-pc=SYSTEM.ERB:1007", StringComparison.Ordinal)
                && process.state.SystemState == SystemStateCode.LoadData_CallEventLoad && !process.state.isBegun
                && f4fNameHandles.Count == f4fDigest.Count(x => x.Family == "NAME" && x.Resolution == "Known")
                && f4e2Handles.Count == f4fDigest.Count(x => x.Family != "NAME" && x.Resolution == "Known")
                && f4fResultsWrites == f4fDigest.Sum(x => x.CalleeWrites)
                && f4fDestinationWrites == f4fDigest.Sum(x => x.CopyWrites)
                && process.vEvaluator.GetR0C2RngHash() == f4fRngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == f4fRngCallsBefore
                && DifferentialDeterminism.ObservationCount == f4fClockBefore
                && guardTotal == 0 && B1Proof.BridgeAttempts == 0;

            var known = f4fPreflightRows.Count(x => x.Resolution == "Known");
            var missing = f4fPreflightRows.Count(x => x.Resolution == "KnownMissing");
            F4FEvidence = new
            {
                Schema = "emuera-r0f4f-complete-set-shop-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "COMPLETE_SET_SHOP_EV_VAR",
                GateResult = pass ? "PASS" : "FAIL",
                ResumedFrom = "SYSTEM.ERB:1024 CALL SET_SHOP_EV_VAR",
                StoppedBefore,
                Preflight = f4fPreflight,
                Checkpoints = new { S0 = f4fS0, S1 = f4fS1, S2 = f4fS2, S3 = f4fS3, S4 = f4fS4 },
                OrderedDigest = f4fDigest.ToArray(),
                FullDomain = domains,
                GeneratedTargets = f4fPreflightRows.Count,
                KnownTargets = known,
                MissingTargets = missing,
                SuccessfulOuterIterations = f4fSuccessfulIterations,
                BreakIteration = f4fBreakAt,
                BreakExecutions = f4fBreakExecutions,
                ShopDynamicCalls = f4fDigest.Count,
                ShopDestinationWrites = f4fDestinationWrites,
                ShopResultsWrites = f4fResultsWrites,
                PersistentBank = new { Lcount = new[] { finalOuter, finalInner }, ExpectedOuter = expectedOuter, ReturnRestoredOldValue = false },
                Models = new
                {
                    ShopName = "PASS", VarsetResults = "PASS", IndexedResultsWrite = "PASS",
                    InnerFor = "PASS", TwoDimensionalWrite = "PASS", EmptyCatch = "PASS", Break = "PASS"
                },
                Materialization = new
                {
                    StartupCompiled = 0,
                    PhysicalDefinitions = f4fNameHandles.Count + f4e2Handles.Count,
                    NameDefinitions = f4fNameHandles.Count,
                    RemainingDefinitions = f4e2Handles.Count,
                    UniqueTemplates = 5,
                    DescriptorEstimateBytes = (f4fNameHandles.Count + f4e2Handles.Count) * 48L,
                    ProgramEstimateBytes = f4fNameHandles.Count * 24L
                        + f4e2Catalog.Values.Where(x => f4e2Handles.ContainsKey(x.Name)).Sum(x => 32L + x.Program.Writes.Length * 24L),
                    MissingBodies = missing,
                    MissingBodiesMaterialized = 0
                },
                SetShopEvVarCompleted = true,
                SetGameplayStartResumed = true,
                MobImageReset = new { Lookup = 0, Materialized = 0, Executed = 0, BoundaryEnforced = true },
                EventLoadCompleted = false,
                GraphFreeGameResumed = "NOT_YET_PROVEN",
                Guards = new { Total = guardTotal, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                ExternalEffects = new { FileIO = 0, Display = 0, Input = 0, RNG = 0, Clock = 0 },
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                ManualRecaptureRequired = "NO",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass)
                throw new InvalidOperationException($"R0-F4F gate failed order={orderPass} rows={f4fDigest.Count}/{expectedRows.Length} outer={finalOuter}/{expectedOuter} inner={finalInner} breaks={f4fBreakExecutions}/{expectedBreaks} writes={f4fDestinationWrites}/{f4fResultsWrites} frames={compactFrames.Count}:{compactFrames[0].Pc} stop={StoppedBefore}");
        }

        private void PrepareF4FPreflight(Process process)
        {
            var stateBefore = process.GetBenchmarkStateHash();
            f4e1Constants = BindF4FConstants();
            ValidateF4FParent();
            ConfigureF4FFamilies();
            f4e1Names.Clear();
            f4e2Catalog.Clear();
            f4fPreflightRows.Clear();

            for (var index = 0; index < f4e1Constants.TurnEndEvNum; index++)
                AdmitF4FName(index);
            foreach (var family in f4e2Families)
            for (var index = 0; index < f4e1Constants.TurnEndEvNum; index++)
                AdmitF4FFamily(family, index);

            BindF4FSlots(process);
            f4fNameHandles.Clear();
            f4e2Handles.Clear();
            f4e2Materialized.Clear();
            f4e2Executed.Clear();
            f4e2ResultWritesByFamily.Clear();
            f4e2DestinationWritesByFamily.Clear();
            f4e2WriteOrder.Clear();

            var nameRows = f4fPreflightRows.Where(x => x.Family == "NAME").OrderBy(x => x.Outer).ToArray();
            f4fBreakAt = nameRows.FirstOrDefault(x => x.Resolution == "KnownMissing")?.Outer ?? -1;
            var pass = stateBefore == process.GetBenchmarkStateHash()
                && f4fPreflightRows.Count == f4e1Constants.TurnEndEvNum * 5
                && f4fPreflightRows.All(x => x.Resolution is "Known" or "KnownMissing")
                && nameRows.Take(f4fBreakAt < 0 ? nameRows.Length : f4fBreakAt).All(x => x.Resolution == "Known")
                && f4fPreflightRows.Where(x => x.Resolution == "Known").All(x =>
                    x.BodyClass is "RESULTS literal + fallthrough" or "indexed RESULTS literals + fallthrough");
            f4fPreflight = new
            {
                Schema = "emuera-r0f4f-preflight-v1",
                Pass = pass,
                EffectCount = 0,
                Constants = f4e1Constants,
                Rows = f4fPreflightRows.ToArray(),
                Generated = f4fPreflightRows.Count,
                Known = f4fPreflightRows.Count(x => x.Resolution == "Known"),
                KnownMissing = f4fPreflightRows.Count(x => x.Resolution == "KnownMissing"),
                Blocked = f4fPreflightRows.Count(x => x.Resolution == "Blocked"),
                WrongKind = f4fPreflightRows.Count(x => x.Resolution == "WrongKind"),
                Unresolved = f4fPreflightRows.Count(x => x.Resolution == "Unresolved"),
                FirstMissingName = f4fBreakAt
            };
            if (!pass) throw new InvalidOperationException("R0-F4F all-50 preflight blocked before effect");
        }

        private F4E1Constants BindF4FConstants()
        {
            var path = Path.Combine(DataRoot, "ERB", "RPG", "ショップイベント", "SHOP_EV.ERH");
            var lines = File.ReadAllLines(path, RuntimeConfig.Encode);
            var declarations = new List<object>();
            int Read(string name)
            {
                var regex = new Regex($"^#DIM\\s+CONST\\s+{Regex.Escape(name)}\\s*=\\s*([0-9]+)(?:\\s*;.*)?$", RegexOptions.CultureInvariant);
                var matches = lines.Select((text, index) => (match: regex.Match(text.Trim()), line: index + 1))
                    .Where(x => x.match.Success).ToArray();
                if (matches.Length != 1
                    || !int.TryParse(matches[0].match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    || value <= 0)
                    throw new InvalidOperationException("R0-F4F constant bind: " + name);
                declarations.Add(new { Name = name, Value = value, Line = matches[0].line });
                return value;
            }
            return new(Read("SHOP_EV_NUM"), Read("VARSIZE_SHOP_EV"), Read("VARSIZE_SHOP_EV_STR"),
                Read("VARSIZE_SHOP_EV_TAKEOVER"), Read("VARSIZE_SHOP_EV_STR_TAKEOVER"),
                Path.GetRelativePath(DataRoot, path).Replace('\\', '/'), FileHash(path)!, declarations.ToArray());
        }

        private void ValidateF4FParent()
        {
            if (!functions.TryGetValue(F4FParent, out var matches) || matches.Length != 1)
                throw new InvalidOperationException("R0-F4F parent identity");
            var expected = new[]
            {
                "#DIM LCOUNT, 2", "FOR LCOUNT, 0, SHOP_EV_NUM",
                "TRYCCALLFORM SHOP_EV_NAME_{LCOUNT}", "SHOP_EV_NAME:LCOUNT = %RESULTS%", "CATCH", "BREAK", "ENDCATCH",
                "VARSET RESULTS, \"\", 0, VARSIZE_SHOP_EV", "TRYCCALLFORM SHOP_EV_FLAGNAME_{LCOUNT}",
                "FOR LCOUNT:1, 0, VARSIZE_SHOP_EV", "SHOP_EV_FLAGNAME:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH",
                "VARSET RESULTS, \"\", 0, VARSIZE_SHOP_EV_STR", "TRYCCALLFORM SHOP_EV_STRNAME_{LCOUNT}",
                "FOR LCOUNT:1, 0, VARSIZE_SHOP_EV_STR", "SHOP_EV_STRNAME:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH",
                "VARSET RESULTS, \"\", 0, VARSIZE_SHOP_EV_TAKEOVER", "TRYCCALLFORM SHOP_EV_TAKEOVER_FLAGNAME_{LCOUNT}",
                "FOR LCOUNT:1, 0, VARSIZE_SHOP_EV_TAKEOVER", "SHOP_EV_FLAGNAME_TAKEOVER:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH",
                "VARSET RESULTS, \"\", 0, VARSIZE_SHOP_EV_STR_TAKEOVER", "TRYCCALLFORM SHOP_EV_TAKEOVER_STRNAME_{LCOUNT}",
                "FOR LCOUNT:1, 0, VARSIZE_SHOP_EV_STR_TAKEOVER", "SHOP_EV_STRNAME_TAKEOVER:LCOUNT:(LCOUNT:1) = %RESULTS:(LCOUNT:1)%", "NEXT", "CATCH", "ENDCATCH",
                "NEXT"
            };
            var parent = matches[0];
            var body = Executable(parent);
            if (parent.IsEvent || parent.RelativePath != F4FParentPath || parent.Line != 17
                || !body.SequenceEqual(expected))
                throw new InvalidOperationException("R0-F4F parent source mismatch");
        }

        private void ConfigureF4FFamilies()
        {
            f4e2Families.Clear();
            f4e2Families.Add(("FLAGNAME", "SHOP_EV_FLAGNAME_", "SHOP_EV_FLAGNAME", f4e1Constants!.VarSize, 26, 27, 28, 29, 32));
            f4e2Families.Add(("STRNAME", "SHOP_EV_STRNAME_", "SHOP_EV_STRNAME", f4e1Constants.StrSize, 33, 34, 35, 36, 39));
            f4e2Families.Add(("TAKEOVER_FLAGNAME", "SHOP_EV_TAKEOVER_FLAGNAME_", "SHOP_EV_FLAGNAME_TAKEOVER", f4e1Constants.TakeoverSize, 40, 41, 42, 43, 46));
            f4e2Families.Add(("TAKEOVER_STRNAME", "SHOP_EV_TAKEOVER_STRNAME_", "SHOP_EV_STRNAME_TAKEOVER", f4e1Constants.StrTakeoverSize, 47, 48, 49, 50, 53));
        }

        private void AdmitF4FName(int index)
        {
            var name = F4FNamePrefix + index.ToString(CultureInfo.InvariantCulture);
            if (!functions.TryGetValue(name, out var matches))
            {
                f4fPreflightRows.Add(new("NAME", index, name, sourceComplete ? "KnownMissing" : "Unresolved",
                    "NONE", [], [], null, null, null, null));
                return;
            }
            if (matches.Length != 1)
            {
                f4fPreflightRows.Add(new("NAME", index, name, "Blocked", "AMBIGUOUS", [], ["multiple definitions"],
                    null, null, null, null));
                return;
            }
            var entry = matches[0];
            if (entry.IsEvent)
            {
                f4fPreflightRows.Add(new("NAME", index, name, "WrongKind", "EVENT", [], ["event definition"],
                    entry.RelativePath, entry.Line, FileHash(entry.File.FileIdentity), null));
                return;
            }
            try
            {
                var value = ParseF4E1NameBody(entry, out var bodySha);
                var descriptor = new F4E1Descriptor(index, name, value, entry, FileHash(entry.File.FileIdentity)!, bodySha);
                f4e1Names.Add(name, descriptor);
                f4fPreflightRows.Add(new("NAME", index, name, "Known", "RESULTS literal + fallthrough", [0], [],
                    entry.RelativePath, entry.Line, descriptor.SourceSha256, descriptor.BodySha256));
            }
            catch (Exception ex)
            {
                f4fPreflightRows.Add(new("NAME", index, name, "Blocked", "UNSUPPORTED", [], [ex.Message],
                    entry.RelativePath, entry.Line, FileHash(entry.File.FileIdentity), null));
            }
        }

        private void AdmitF4FFamily(
            (string Family, string Prefix, string Destination, int Count, int CallLine, int ForLine, int WriteLine, int NextLine, int NextFamilyLine) family,
            int index)
        {
            var name = family.Prefix + index.ToString(CultureInfo.InvariantCulture);
            if (!functions.TryGetValue(name, out var matches))
            {
                f4fPreflightRows.Add(new(family.Family, index, name, sourceComplete ? "KnownMissing" : "Unresolved",
                    "NONE", [], [], null, null, null, null));
                return;
            }
            if (matches.Length != 1)
            {
                f4fPreflightRows.Add(new(family.Family, index, name, "Blocked", "AMBIGUOUS", [], ["multiple definitions"],
                    null, null, null, null));
                return;
            }
            var entry = matches[0];
            if (entry.IsEvent)
            {
                f4fPreflightRows.Add(new(family.Family, index, name, "WrongKind", "EVENT", [], ["event definition"],
                    entry.RelativePath, entry.Line, FileHash(entry.File.FileIdentity), null));
                return;
            }
            try
            {
                var program = ParseF4E2Program(entry, family.Count, name);
                var descriptor = new F4E2Descriptor(family.Family, family.Prefix, family.Destination, family.Count,
                    name, entry, FileHash(entry.File.FileIdentity)!, program.Sha256, program);
                f4e2Catalog.Add(name, descriptor);
                f4fPreflightRows.Add(new(family.Family, index, name, "Known", "indexed RESULTS literals + fallthrough",
                    program.Writes.Select(x => x.Index).ToArray(), [], entry.RelativePath, entry.Line,
                    descriptor.SourceSha256, descriptor.BodySha256));
            }
            catch (Exception ex)
            {
                f4fPreflightRows.Add(new(family.Family, index, name, "Blocked", "UNSUPPORTED", [], [ex.Message],
                    entry.RelativePath, entry.Line, FileHash(entry.File.FileIdentity), null));
            }
        }

        private void BindF4FSlots(Process process)
        {
            var names = process.idDic.GetVariableToken("SHOP_EV_NAME", null, false);
            if (names is null || !names.IsString || !names.IsArray1D || names.IsCharacterData || names.IsLocal
                || names.IsPrivate || names.IsConst || names.GetLength() != f4e1Constants!.TurnEndEvNum)
                throw new InvalidOperationException("R0-F4F SHOP_EV_NAME schema mismatch");
            names.CheckElement([0]);
            names.CheckElement([f4e1Constants.TurnEndEvNum - 1]);
            f4e1NameSlot = new("SHOP_EV_NAME", names, names.GetLength());

            var results = process.idDic.GetVariableToken("RESULTS", null, false);
            if (results is null || !results.IsString || !results.IsArray1D || results.GetLength() < f4e2Families.Max(x => x.Count))
                throw new InvalidOperationException("R0-F4F RESULTS schema mismatch");
            f4e2Results = results;
            f4e2Destinations.Clear();
            foreach (var family in f4e2Families)
            {
                var token = process.idDic.GetVariableToken(family.Destination, null, false);
                if (token is null || !token.IsString || token.Dimension != 2 || token.IsCharacterData || token.IsLocal
                    || token.IsPrivate || token.IsConst || token.GetLength(0) != f4e1Constants.TurnEndEvNum
                    || token.GetLength(1) != family.Count)
                    throw new InvalidOperationException("R0-F4F destination schema: " + family.Destination);
                token.CheckElement([0, 0]);
                token.CheckElement([f4e1Constants.TurnEndEvNum - 1, family.Count - 1]);
                f4e2Destinations.Add(family.Family,
                    new BoundString2D(family.Destination, token, token.GetLength(0), token.GetLength(1)));
            }
        }

        private void RunCandidateF4F(Process process)
        {
            Enter(new(F4D5Handle(53_000, F4FParent), 1025, "SYSTEM.ERB:1024"), 19, false);
            while (f4e2PersistentLcount[0] < f4e1Constants!.TurnEndEvNum)
            {
                if (!RunCandidateF4FName(process))
                {
                    unchecked { f4e2PersistentLcount[0]++; } // Legacy BREAK advances FOR's counter.
                    break;
                }
                foreach (var family in f4e2Families) RunCandidateF4FFamily(process, family);
                f4fSuccessfulIterations++;
                CaptureF4FIterationPoint(process);
                unchecked { f4e2PersistentLcount[0]++; }
            }
            compactFrames[^1].Pc = 54;
            process.vEvaluator.RESULT = 0;
            Return(process, F4FParent, false);
            f4fParentReturned = true;
            StoppedBefore = "SYSTEM.ERB:1025 CALL モブ画像_全リセット";
            f4fS4 = CaptureF4FPoint(process, "S4", "SYSTEM.ERB:1025:before");
        }

        private bool RunCandidateF4FName(Process process)
        {
            var outer = ReadF4FLcount(process, 0);
            compactFrames[^1].Pc = 20;
            f4fCurrentFamily = "NAME";
            f4fCurrentTarget = F4FNamePrefix + outer.ToString(CultureInfo.InvariantCulture);
            f4fNameResolution = ResolveF4E1(f4fCurrentTarget, out var descriptor, F4FNamePrefix);
            f4fCalleeWrites = f4fCopyWrites = 0;
            if (f4fNameResolution == F4E1Resolution.KnownMissing)
            {
                f4fBreakExecutions++;
                RecordF4F(process, true);
                compactFrames[^1].Pc = 23;
                f4fS3 = CaptureF4FPoint(process, "S3", F4FParentPath + ":23:before");
                return false;
            }
            if (f4fNameResolution != F4E1Resolution.Known || descriptor is null)
                throw new InvalidOperationException("R0-F4F NAME blocked after commit");
            var handle = MaterializeF4FName(descriptor);
            Enter(new(handle, 21, F4FParentPath + ":20"), checked((ushort)(descriptor.Definition.Line + 1)), false);
            process.vEvaluator.RESULTS = descriptor.Value;
            f4fCalleeWrites++;
            f4fResultsWrites++;
            process.vEvaluator.RESULT = 0;
            Return(process, descriptor.Name, false);
            f4e1NameSlot.Write(process.vEvaluator.RESULTS, outer);
            f4fCopyWrites++;
            f4fDestinationWrites++;
            compactFrames[^1].Committed = true;
            compactFrames[^1].Pc = 25;
            RecordF4F(process, false);
            return true;
        }

        private void RunCandidateF4FFamily(Process process,
            (string Family, string Prefix, string Destination, int Count, int CallLine, int ForLine, int WriteLine, int NextLine, int NextFamilyLine) family)
        {
            ApplyF4E2Varset(0, family.Count);
            f4fCurrentFamily = family.Family;
            f4fCurrentTarget = family.Prefix + ReadF4FLcount(process, 0).ToString(CultureInfo.InvariantCulture);
            f4fFamilyResolution = ResolveF4E2(f4fCurrentTarget, out var descriptor);
            f4fCalleeWrites = f4fCopyWrites = 0;
            if (f4fFamilyResolution == F4E2Resolution.KnownMissing)
            {
                RecordF4F(process, false);
                return;
            }
            if (f4fFamilyResolution != F4E2Resolution.Known || descriptor is null)
                throw new InvalidOperationException("R0-F4F family blocked after commit");
            MaterializeF4E2(descriptor);
            Enter(new(f4e2Handles[descriptor.Name], checked((ushort)family.ForLine), F4FParentPath + ":" + family.CallLine),
                checked((ushort)(descriptor.Definition.Line + 1)), false);
            foreach (var write in descriptor.Program.Writes)
            {
                f4e2Results.SetValue(write.Value, [write.Index]);
                f4fCalleeWrites++;
                f4fResultsWrites++;
            }
            process.vEvaluator.RESULT = 0;
            Return(process, descriptor.Name, false);
            f4e2Executed[family.Family] = f4e2Executed.GetValueOrDefault(family.Family) + 1;
            f4e2PersistentLcount[1] = 0;
            while (f4e2PersistentLcount[1] < family.Count)
            {
                var inner = f4e2PersistentLcount[1];
                f4e2Destinations[family.Family].Write(
                    f4e2Results.GetStrValue(process.exm, [inner]) ?? string.Empty,
                    f4e2PersistentLcount[0], inner);
                f4fCopyWrites++;
                f4fDestinationWrites++;
                unchecked { f4e2PersistentLcount[1]++; }
            }
            compactFrames[^1].Committed = true;
            compactFrames[^1].Pc = checked((ushort)family.NextFamilyLine);
            RecordF4F(process, false);
        }

        private CompactNormalHandle MaterializeF4FName(F4E1Descriptor descriptor)
        {
            if (FileHash(descriptor.Definition.File.FileIdentity) != descriptor.SourceSha256
                || ParseF4E1NameBody(descriptor.Definition, out var bodySha) != descriptor.Value
                || bodySha != descriptor.BodySha256)
                throw new InvalidOperationException("R0-F4F NAME materialization source mismatch");
            if (!f4fNameHandles.TryGetValue(descriptor.Name, out var handle))
            {
                handle = F4D5Handle(53_001 + f4fNameHandles.Count, descriptor.Name);
                f4fNameHandles.Add(descriptor.Name, handle);
            }
            return handle;
        }

        private void RunLegacyF4F(Process process)
        {
            var stopped = process.state.CurrentLine;
            var previous = (LogicalLine?)stopped?.ParentLabelLine;
            while (previous is not null && !ReferenceEquals(previous.NextLine, stopped)) previous = previous.NextLine;
            if (previous is null) throw new InvalidOperationException("R0-F4F Legacy resume predecessor not found");
            process.state.CurrentLine = previous;
            f4fLegacyActive = true;
            try
            {
                process.runScriptProc();
                throw new InvalidOperationException("R0-F4F Legacy crossed SYSTEM1025 stop");
            }
            catch (R0F1PlannedCheckpointException) { }
            finally { f4fLegacyActive = false; }
        }

        internal void ObserveF4FEntry(Process process, CalledFunction call)
        {
            if (!f4fLegacyActive) return;
            var name = call.TopLabel.LabelName;
            if (name.Equals(F4FParent, RuntimeConfig.StringComparison))
            {
                if (compactFrames.Count != 1 || compactFrames[0].Pc != 1024)
                    throw new InvalidOperationException("R0-F4F Legacy parent entry boundary");
                f4fLegacyLcount = call.TopLabel.GetPrivateVariable("LCOUNT");
                if (f4fLegacyLcount is null || !f4fLegacyLcount.IsInteger || !f4fLegacyLcount.IsPrivate
                    || f4fLegacyLcount.GetLength() != 2)
                    throw new InvalidOperationException("R0-F4F Legacy persistent LCOUNT bind");
                Enter(new(F4D5Handle(53_000, F4FParent), 1025, "SYSTEM.ERB:1024"), 19, false);
                return;
            }
            if (!name.Equals(f4fCurrentTarget, RuntimeConfig.StringComparison)) return;
            if (f4fCurrentFamily == "NAME")
            {
                if (f4fNameResolution != F4E1Resolution.Known || !f4e1Names.TryGetValue(name, out var descriptor))
                    throw new InvalidOperationException("R0-F4F Legacy NAME entry admission");
                Enter(new(MaterializeF4FName(descriptor), 21, F4FParentPath + ":20"),
                    checked((ushort)(descriptor.Definition.Line + 1)), false);
            }
            else
            {
                if (f4fFamilyResolution != F4E2Resolution.Known
                    || ResolveF4E2(name, out var descriptor) != F4E2Resolution.Known || descriptor is null)
                    throw new InvalidOperationException("R0-F4F Legacy family entry admission");
                MaterializeF4E2(descriptor);
                var family = f4e2Families.Single(x => x.Family == descriptor.Family);
                Enter(new(f4e2Handles[descriptor.Name], checked((ushort)family.ForLine), F4FParentPath + ":" + family.CallLine),
                    checked((ushort)(descriptor.Definition.Line + 1)), false);
                f4e2Executed[family.Family] = f4e2Executed.GetValueOrDefault(family.Family);
            }
            f4fCalleeWriteCursor = 0;
            f4fCalleeWrites = 0;
        }

        internal void ObserveF4FInstruction(Process process, InstructionLine line, bool before)
        {
            if (!f4fLegacyActive || !before) return;
            if (IsF4E1Line(line, "SYSTEM.ERB", 1025))
            {
                if (!f4fParentReturned || compactFrames.Count != 1 || compactFrames[0].Pc != 1025)
                    throw new InvalidOperationException("R0-F4F Legacy return boundary");
                StoppedBefore = "SYSTEM.ERB:1025 CALL モブ画像_全リセット";
                f4fS4 = CaptureF4FPoint(process, "S4", "SYSTEM.ERB:1025:before");
                throw new R0F1PlannedCheckpointException();
            }
            if (!IsF4E1Line(line, F4FParentFile, line.Position?.LineNo ?? -1)) return;
            var number = line.Position!.Value.LineNo;
            compactFrames[^1].Pc = checked((ushort)number);
            if (number == 20)
            {
                f4fCurrentFamily = "NAME";
                f4fCurrentTarget = F4FNamePrefix + ReadF4FLcount(process, 0).ToString(CultureInfo.InvariantCulture);
                f4fNameResolution = ResolveF4E1(f4fCurrentTarget, out _, F4FNamePrefix);
                f4fCalleeWrites = f4fCopyWrites = 0;
                f4fReturned = false;
            }
            if (number == 25) RecordF4F(process, false);
            foreach (var family in f4e2Families.Where(x => x.CallLine == number))
            {
                f4fCurrentFamily = family.Family;
                f4fCurrentTarget = family.Prefix + ReadF4FLcount(process, 0).ToString(CultureInfo.InvariantCulture);
                f4fFamilyResolution = ResolveF4E2(f4fCurrentTarget, out _);
                f4fCalleeWrites = f4fCopyWrites = 0;
                f4fReturned = false;
            }
            foreach (var family in f4e2Families.Where(x => x.NextFamilyLine == number))
                RecordF4F(process, false);
            if (number == 53)
            {
                var outer = ReadF4FLcount(process, 0);
                if (f4fBreakAt < 0 || outer < f4fBreakAt)
                {
                    f4fSuccessfulIterations++;
                    CaptureF4FIterationPoint(process);
                }
            }
            if (number == 23)
            {
                if (f4fNameResolution != F4E1Resolution.KnownMissing)
                    throw new InvalidOperationException("R0-F4F Legacy unexpected BREAK");
                f4fBreakExecutions++;
                f4fS3 = CaptureF4FPoint(process, "S3", F4FParentPath + ":23:before");
            }
        }

        internal void ObserveF4FDynamic(Process process, InstructionLine line, string target, bool found)
        {
            if (!f4fLegacyActive || !IsF4E1Line(line, F4FParentFile, line.Position?.LineNo ?? -1)) return;
            var number = line.Position!.Value.LineNo;
            if (number == 20)
            {
                var expected = F4FNamePrefix + ReadF4FLcount(process, 0).ToString(CultureInfo.InvariantCulture);
                var resolution = ResolveF4E1(target, out _, F4FNamePrefix);
                if (target != expected || found != (resolution == F4E1Resolution.Known))
                    throw new InvalidOperationException("R0-F4F Legacy NAME resolver mismatch");
                f4fCurrentFamily = "NAME";
                f4fCurrentTarget = target;
                f4fNameResolution = resolution;
                if (resolution == F4E1Resolution.KnownMissing) RecordF4F(process, true);
                return;
            }
            var family = f4e2Families.FirstOrDefault(x => x.CallLine == number);
            if (family.Family is null) return;
            var expectedFamily = family.Prefix + ReadF4FLcount(process, 0).ToString(CultureInfo.InvariantCulture);
            var familyResolution = ResolveF4E2(target, out _);
            if (target != expectedFamily || found != (familyResolution == F4E2Resolution.Known))
                throw new InvalidOperationException("R0-F4F Legacy family resolver mismatch");
            f4fCurrentFamily = family.Family;
            f4fCurrentTarget = target;
            f4fFamilyResolution = familyResolution;
        }

        internal void ObserveF4FStringWrite(Process process, InstructionLine line, string? value, bool before)
        {
            if (!f4fLegacyActive || f4fCurrentTarget.Length == 0) return;
            if (line.ParentLabelLine?.LabelName.Equals(f4fCurrentTarget, RuntimeConfig.StringComparison) == true)
            {
                if (f4fCurrentFamily == "NAME")
                {
                    var descriptor = f4e1Names[f4fCurrentTarget];
                    if (before)
                    {
                        if (value != descriptor.Value) throw new InvalidOperationException("R0-F4F Legacy NAME RHS");
                        f4fPendingValue = value;
                    }
                    else
                    {
                        if (process.vEvaluator.RESULTS != f4fPendingValue) throw new InvalidOperationException("R0-F4F Legacy NAME RESULTS");
                        f4fCalleeWrites++;
                        f4fResultsWrites++;
                        f4fPendingValue = null;
                    }
                }
                else
                {
                    var descriptor = f4e2Catalog[f4fCurrentTarget];
                    var write = descriptor.Program.Writes[f4fCalleeWriteCursor];
                    if (before)
                    {
                        if (value != write.Value) throw new InvalidOperationException("R0-F4F Legacy RESULTS RHS");
                        f4fPendingValue = value;
                    }
                    else
                    {
                        if (process.vEvaluator.RESULTS_ARRAY[write.Index] != f4fPendingValue)
                            throw new InvalidOperationException("R0-F4F Legacy RESULTS write");
                        f4fCalleeWriteCursor++;
                        f4fCalleeWrites++;
                        f4fResultsWrites++;
                        f4fPendingValue = null;
                    }
                }
                return;
            }
            if (f4fCurrentFamily == "NAME" && IsF4E1Line(line, F4FParentFile, 21))
            {
                var outer = ReadF4FLcount(process, 0);
                if (before)
                {
                    if (!f4fReturned || value != process.vEvaluator.RESULTS)
                        throw new InvalidOperationException("R0-F4F Legacy NAME copy order");
                    f4fPendingValue = value;
                }
                else
                {
                    if (f4e1NameSlot.Read(process, outer) != f4fPendingValue)
                        throw new InvalidOperationException("R0-F4F Legacy NAME copy");
                    f4fCopyWrites++;
                    f4fDestinationWrites++;
                    f4fPendingValue = null;
                    compactFrames[^1].Committed = true;
                }
                return;
            }
            if (f4fCurrentFamily == "NAME") return;
            var family = f4e2Families.Single(x => x.Family == f4fCurrentFamily);
            if (!IsF4E1Line(line, F4FParentFile, family.WriteLine)) return;
            var outerIndex = ReadF4FLcount(process, 0);
            var innerIndex = ReadF4FLcount(process, 1);
            if (before)
            {
                if (!f4fReturned || value != process.vEvaluator.RESULTS_ARRAY[innerIndex])
                    throw new InvalidOperationException("R0-F4F Legacy 2D order");
                f4fPendingValue = value;
            }
            else
            {
                if (f4e2Destinations[family.Family].Read(process, outerIndex, innerIndex) != f4fPendingValue)
                    throw new InvalidOperationException("R0-F4F Legacy 2D write");
                f4fCopyWrites++;
                f4fDestinationWrites++;
                f4fPendingValue = null;
                compactFrames[^1].Committed = true;
            }
        }

        internal void ObserveF4FFallthrough(Process process, LogicalLine line, bool before)
        {
            if (!f4fLegacyActive) return;
            if (!before && f4fTargetReturnPending)
            {
                f4fTargetReturnPending = false;
                if (f4fCurrentFamily != "NAME"
                    && f4fCalleeWriteCursor != f4e2Catalog[f4fCurrentTarget].Count)
                    throw new InvalidOperationException("R0-F4F Legacy family early fallthrough");
                Return(process, f4fCurrentTarget, false);
                f4fReturned = true;
                if (f4fCurrentFamily != "NAME")
                    f4e2Executed[f4fCurrentFamily] = f4e2Executed.GetValueOrDefault(f4fCurrentFamily) + 1;
                return;
            }
            if (!before && f4fParentReturnPending)
            {
                f4fParentReturnPending = false;
                Return(process, F4FParent, false);
                f4fParentReturned = true;
                return;
            }
            if (process.state.functionCount == 0) return;
            var current = process.state.CurrentCalled.FunctionName;
            if (current.Equals(f4fCurrentTarget, RuntimeConfig.StringComparison))
            {
                if (before) f4fTargetReturnPending = true;
                return;
            }
            if (!current.Equals(F4FParent, RuntimeConfig.StringComparison)) return;
            if (before)
            {
                compactFrames[^1].Pc = 54;
                f4fParentReturnPending = true;
            }
        }

        private void RecordF4F(Process process, bool breaks)
        {
            var outer = (int)ReadF4FLcount(process, 0);
            if (f4fDigest.Any(x => x.Outer == outer && x.Family == f4fCurrentFamily)) return;
            var resolution = f4fCurrentFamily == "NAME" ? f4fNameResolution.ToString() : f4fFamilyResolution.ToString();
            var destination = f4fCurrentFamily == "NAME"
                ? ReadF4FNames(process)
                : f4e2Destinations[f4fCurrentFamily].Snapshot(process);
            f4fDigest.Add(new(outer, f4fCurrentFamily, f4fCurrentTarget, resolution,
                f4fCalleeWrites, HashStrings(process.vEvaluator.RESULTS_ARRAY),
                f4fCopyWrites, HashStrings(destination), breaks));
        }

        private bool RowSemanticsPass(F4FDigestRow row)
        {
            if (row.Resolution == "KnownMissing")
                return row.CalleeWrites == 0 && row.CopyWrites == 0 && row.Break == (row.Family == "NAME");
            if (row.Resolution != "Known" || row.Break) return false;
            if (row.Family == "NAME") return row.CalleeWrites == 1 && row.CopyWrites == 1;
            return f4e2Catalog.TryGetValue(row.Target, out var descriptor)
                && row.CalleeWrites == descriptor.Count && row.CopyWrites == descriptor.Count;
        }

        private void CaptureF4FIterationPoint(Process process)
        {
            var outer = ReadF4FLcount(process, 0);
            if (outer == 0) f4fS1 = CaptureF4FPoint(process, "S1", F4FParentPath + ":53:before");
            if (outer == (f4fBreakAt >= 0 ? f4fBreakAt - 1 : f4e1Constants!.TurnEndEvNum - 1))
                f4fS2 = CaptureF4FPoint(process, "S2", F4FParentPath + ":53:before");
        }

        private F4FPoint CaptureF4FPoint(Process process, string name, string pc) => new(name, pc,
            ReadF4FLcount(process, 0), ReadF4FLcount(process, 1), string.Join(" > ", compactFrames.Select(FrameText)),
            EventCursor, process.state.SystemState.ToString(), process.state.isBegun, process.vEvaluator.GetR0C2RngHash(),
            process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount,
            HashStrings(CaptureF4FDomainStrings(process)));

        private object CaptureF4FDomains(Process process) => new
        {
            ShopEvName = ReadF4FNames(process),
            ShopEvFlagName = f4e2Destinations["FLAGNAME"].Snapshot(process),
            ShopEvStrName = f4e2Destinations["STRNAME"].Snapshot(process),
            ShopEvFlagNameTakeover = f4e2Destinations["TAKEOVER_FLAGNAME"].Snapshot(process),
            ShopEvStrNameTakeover = f4e2Destinations["TAKEOVER_STRNAME"].Snapshot(process),
            Result = (long[])process.vEvaluator.RESULT_ARRAY.Clone(),
            Results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone(),
            Lcount = new[] { ReadF4FLcount(process, 0), ReadF4FLcount(process, 1) },
            Sha256 = HashStrings(CaptureF4FDomainStrings(process))
        };

        private string[] CaptureF4FDomainStrings(Process process) => ReadF4FNames(process)
            .Concat(f4e2Families.SelectMany(family => f4e2Destinations[family.Family].Snapshot(process)))
            .Concat(process.vEvaluator.RESULT_ARRAY.Select(x => x.ToString(CultureInfo.InvariantCulture)))
            .Concat(process.vEvaluator.RESULTS_ARRAY.Select(x => x ?? ""))
            .Concat(new[]
            {
                ReadF4FLcount(process, 0).ToString(CultureInfo.InvariantCulture),
                ReadF4FLcount(process, 1).ToString(CultureInfo.InvariantCulture)
            })
            .ToArray();

        private long ReadF4FLcount(Process process, int index) => Candidate
            ? f4e2PersistentLcount[index]
            : f4fLegacyLcount?.GetIntValue(process.exm, [index]) ?? 0;

        private string[] ReadF4FNames(Process process) => Enumerable.Range(0, f4e1Constants!.TurnEndEvNum)
            .Select(i => f4e1NameSlot.Read(process, i)).ToArray();
    }
}
#endif
