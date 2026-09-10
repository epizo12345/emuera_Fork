#if R0_F4E1
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed record R0F4E1BreakRow(string Case, int[] IterationSequence, long FinalLcount,
        int[] WriteIndices, int WritesAfterBreak, bool CallerContinuation, long Result0,
        string Results0, long InnerFinal, bool Pass);

    internal object RunR0F4E1BreakMatrix(bool candidate)
    {
        var trace = R0F4E1TestToken("F4E1_TEST_TRACE", 64);
        var writes = R0F4E1TestToken("F4E1_TEST_WRITES", 64);
        var after = R0F4E1TestToken("F4E1_TEST_AFTER", 64);
        var meta = R0F4E1TestToken("F4E1_TEST_META", 16);
        var cases = new[] { ("first", 0), ("middle", 2), ("final", 3), ("none", -1) };
        var rows = new List<R0F4E1BreakRow>();
        foreach (var (name, missing) in cases)
        {
            R0F4E1Reset(trace, 64); R0F4E1Reset(writes, 64); R0F4E1Reset(after, 64); R0F4E1Reset(meta, 16);
            vEvaluator.RESULT_ARRAY[0] = 77;
            vEvaluator.RESULTS_ARRAY[0] = "sentinel";
            if (candidate) R0F4E1RunCompactBreak(missing, trace, writes, after, meta);
            else R0F4E1RunLegacyFixture("F4E1_BREAK_" + name.ToUpperInvariant());
            var iterations = Enumerable.Range(0, 4).Where(i => trace.GetIntValue(exm, [i]) != 0).ToArray();
            var writeIndices = Enumerable.Range(0, 4).Where(i => writes.GetIntValue(exm, [i]) != 0).ToArray();
            var writesAfter = missing < 0 ? 0 : writeIndices.Count(i => i >= missing);
            var expectedFinal = missing < 0 ? 4 : missing + 1;
            var expectedWrites = Enumerable.Range(0, missing < 0 ? 4 : missing).ToArray();
            rows.Add(new(name, iterations, meta.GetIntValue(exm, [0]), writeIndices, writesAfter,
                meta.GetIntValue(exm, [1]) == 1, vEvaluator.RESULT, vEvaluator.RESULTS,
                meta.GetIntValue(exm, [2]), iterations.SequenceEqual(Enumerable.Range(0, missing < 0 ? 4 : missing + 1))
                    && meta.GetIntValue(exm, [0]) == expectedFinal && writeIndices.SequenceEqual(expectedWrites)
                    && writesAfter == 0 && meta.GetIntValue(exm, [1]) == 1
                    && vEvaluator.RESULT == 0 && vEvaluator.RESULTS == "sentinel"));
        }

        R0F4E1Reset(trace, 64); R0F4E1Reset(writes, 64); R0F4E1Reset(after, 64); R0F4E1Reset(meta, 16);
        vEvaluator.RESULT_ARRAY[0] = 77;
        vEvaluator.RESULTS_ARRAY[0] = "sentinel";
        if (candidate) R0F4E1RunCompactNestedBreak(trace, writes, after, meta);
        else R0F4E1RunLegacyFixture("F4E1_BREAK_NESTED");
        var nestedIterations = Enumerable.Range(0, 3).Where(i => trace.GetIntValue(exm, [i]) != 0).ToArray();
        var nestedWrites = Enumerable.Range(0, 3).Where(i => writes.GetIntValue(exm, [i]) != 0).ToArray();
        rows.Add(new("nested FOR inner BREAK", nestedIterations, meta.GetIntValue(exm, [0]), nestedWrites,
            checked((int)Enumerable.Range(0, 64).Sum(i => after.GetIntValue(exm, [i]))),
            meta.GetIntValue(exm, [1]) == 1, vEvaluator.RESULT, vEvaluator.RESULTS,
            meta.GetIntValue(exm, [2]), nestedIterations.SequenceEqual([0, 1, 2])
                && nestedWrites.SequenceEqual([0, 1, 2]) && Enumerable.Range(0, 3).All(i => writes.GetIntValue(exm, [i]) == 1)
                && meta.GetIntValue(exm, [0]) == 3 && meta.GetIntValue(exm, [2]) == 1
                && meta.GetIntValue(exm, [1]) == 1 && vEvaluator.RESULT == 0 && vEvaluator.RESULTS == "sentinel"));
        return new
        {
            Schema = "emuera-r0f4e1-break-matrix-v1",
            Mode = candidate ? "GraphFreeCandidate" : "LegacyControl",
            UsesLegacyScriptExecutor = !candidate,
            UsesCompactBreakExecutor = candidate,
            BreakScope = "current integer FOR only",
            Rows = rows,
            AllPassed = rows.Count == 5 && rows.All(x => x.Pass),
            LegacyRetryAfterCompact = 0
        };
    }

    private VariableToken R0F4E1TestToken(string name, int length)
    {
        var token = idDic.GetVariableToken(name, null, false);
        if (token is null || !token.IsInteger || token.Dimension != 1 || token.GetLength() != length)
            throw new InvalidOperationException("R0-F4E1 fixture token: " + name);
        return token;
    }

    private void R0F4E1RunLegacyFixture(string name)
    {
        var call = CalledFunction.CallFunction(this, name, null)
            ?? throw new InvalidOperationException("R0-F4E1 fixture function missing: " + name);
        state.IntoFunction(call, null!, null!);
        runScriptProc();
        if (state.functionCount != 0) throw new InvalidOperationException("R0-F4E1 fixture did not return: " + name);
    }

    private void R0F4E1RunCompactBreak(int missing, VariableToken trace, VariableToken writes,
        VariableToken after, VariableToken meta)
    {
        long lcount = 0;
        while (lcount < 4)
        {
            trace.SetValue(1, [lcount]);
            if (lcount == missing)
            {
                R0F4E1BreakCurrentFor(ref lcount);
                break;
            }
            writes.SetValue(1, [lcount]);
            after.SetValue(1, [lcount]);
            unchecked { lcount++; }
        }
        meta.SetValue(lcount, [0]);
        meta.SetValue(1, [1]);
        vEvaluator.RESULT = 0;
    }

    private void R0F4E1RunCompactNestedBreak(VariableToken trace, VariableToken writes,
        VariableToken after, VariableToken meta)
    {
        long outer = 0, inner = 0;
        while (outer < 3)
        {
            inner = 0;
            while (inner < 3)
            {
                trace.SetValue(1, [outer]);
                R0F4E1BreakCurrentFor(ref inner);
                break;
            }
            writes.SetValue(inner, [outer]);
            unchecked { outer++; }
        }
        meta.SetValue(outer, [0]);
        meta.SetValue(1, [1]);
        meta.SetValue(inner, [2]);
        vEvaluator.RESULT = 0;
    }

    private static void R0F4E1BreakCurrentFor(ref long counter) { unchecked { counter++; } }
    private void R0F4E1Reset(VariableToken token, int length)
    {
        for (var i = 0; i < length; i++) token.SetValue(0, [i]);
    }

    internal void R0F4E1BeforeLegacyInstruction(InstructionLine line)
    {
        r0f1?.ObserveF4E1LegacyInstruction(this, line, true);
#if R0_F4F
        r0f1?.ObserveF4FInstruction(this, line, true);
#if R0_F4G1
        r0f1?.ObserveF4G1Instruction(this, line, true);
#if R0_F4G2
        r0f1?.ObserveF4G2Instruction(this, line, true);
#if R0_F4G3
        r0f1?.ObserveF4G3Instruction(this, line, true);
#if R0_F4G4
        r0f1?.ObserveF4G4Instruction(this, line, true);
#endif
#endif
#endif
#endif
#endif
    }
    internal void R0F4E1AfterLegacyInstruction(InstructionLine line)
    {
        r0f1?.ObserveF4E1LegacyInstruction(this, line, false);
#if R0_F4F
        r0f1?.ObserveF4FInstruction(this, line, false);
#if R0_F4G1
        r0f1?.ObserveF4G1Instruction(this, line, false);
#if R0_F4G2
        r0f1?.ObserveF4G2Instruction(this, line, false);
#if R0_F4G3
        r0f1?.ObserveF4G3Instruction(this, line, false);
#if R0_F4G4
        r0f1?.ObserveF4G4Instruction(this, line, false);
#endif
#endif
#endif
#endif
#endif
    }
    internal void R0F4E1BeforeLegacyFallthrough(LogicalLine line)
    {
        r0f1?.ObserveF4E1LegacyFallthrough(this, line, true);
#if R0_F4F
        r0f1?.ObserveF4FFallthrough(this, line, true);
#if R0_F4G1
        r0f1?.ObserveF4G1Fallthrough(this, line, true);
#if R0_F4G2
        r0f1?.ObserveF4G2Fallthrough(this, line, true);
#if R0_F4G3
        r0f1?.ObserveF4G3Fallthrough(this, line, true);
#if R0_F4G4
        r0f1?.ObserveF4G4Fallthrough(this, line, true);
#endif
#endif
#endif
#endif
#endif
    }
    internal void R0F4E1AfterLegacyFallthrough(LogicalLine line)
    {
        r0f1?.ObserveF4E1LegacyFallthrough(this, line, false);
#if R0_F4F
        r0f1?.ObserveF4FFallthrough(this, line, false);
#if R0_F4G1
        r0f1?.ObserveF4G1Fallthrough(this, line, false);
#if R0_F4G2
        r0f1?.ObserveF4G2Fallthrough(this, line, false);
#if R0_F4G3
        r0f1?.ObserveF4G3Fallthrough(this, line, false);
#if R0_F4G4
        r0f1?.ObserveF4G4Fallthrough(this, line, false);
#endif
#endif
#endif
#endif
#endif
    }
    internal void R0F4E1ObserveLegacyDynamicCall(InstructionLine line, string target, bool found)
    {
        r0f1?.ObserveF4E1LegacyDynamic(this, line, target, found);
#if R0_F4F
        r0f1?.ObserveF4FDynamic(this, line, target, found);
#if R0_F4G4
        r0f1?.ObserveF4G4Dynamic(this, line, target, found);
#endif
#endif
    }
    internal void R0F4E1BeforeLegacyStringWrite(InstructionLine line, string value)
    {
        r0f1?.ObserveF4E1LegacyStringWrite(this, line, value, true);
#if R0_F4F
        r0f1?.ObserveF4FStringWrite(this, line, value, true);
#if R0_F4G2
        r0f1?.ObserveF4G2StringWrite(this, line, value, true);
#endif
#endif
    }
    internal void R0F4E1AfterLegacyStringWrite(InstructionLine line)
    {
        r0f1?.ObserveF4E1LegacyStringWrite(this, line, null, false);
#if R0_F4F
        r0f1?.ObserveF4FStringWrite(this, line, null, false);
#if R0_F4G2
        r0f1?.ObserveF4G2StringWrite(this, line, null, false);
#endif
#endif
    }
    internal void R0F4E1AfterLegacyArgumentsBound(CalledFunction call)
    {
        r0f1?.ObserveF4E1LegacyEntry(this, call);
#if R0_F4F
        r0f1?.ObserveF4FEntry(this, call);
#if R0_F4G1
        r0f1?.ObserveF4G1Entry(this, call);
#if R0_F4G2
        r0f1?.ObserveF4G2Entry(this, call);
#if R0_F4G3
        r0f1?.ObserveF4G3Entry(this, call);
#if R0_F4G4
        r0f1?.ObserveF4G4Entry(this, call);
#endif
#endif
#endif
#endif
#endif
    }

    private sealed partial class R0F1Context
    {
        private const string F4E1Parent = "SET_TURNEND_EV_VAR";
        private const string F4E1ParentPath = "RPG/ターンエンドイベント/TURNEND_EV_独自変数システム.ERB";
        private const string F4E1NamePrefix = "TURNEND_EV_NAME_";
        private enum F4E1Resolution { Known, KnownMissing, WrongKind, Unresolved, Blocked }

        private sealed record F4E1Constants(int TurnEndEvNum, int VarSize, int StrSize, int TakeoverSize,
            int StrTakeoverSize, string SourcePath, string SourceSha256, object[] Declarations);
        private sealed record F4E1Descriptor(int Index, string Name, string Value, R0F1Definition Definition,
            string SourceSha256, string BodySha256);
        private sealed record F4E1FamilyRow(string Family, int Index, string Name, string Resolution,
            string BodyClass, int[] ResultsIndices, string[] Unsupported, string? SourcePath, int? Line,
            string? SourceSha256, string? BodySha256);
        private sealed record F4E1ResolverRow(string Case, string Resolution, string Expected,
            bool Continued, int EffectsBeforeReject, int LegacyRetry, bool Pass);
        private sealed record F4E1Point(string Name, string ProgramCounter, long Lcount, string DynamicTarget,
            string Resolution, string[] TurnEndNames, string TurnEndNameSha256, long[] Result,
            string ResultSha256, string[] Results, string ResultsSha256, string Frames, string EventCursor,
            string SystemState, bool PendingBegin, string RngSha256, long RngCalls, long ClockCalls);

        internal object? F4E1Evidence { get; private set; }
        private F4E1Constants? f4e1Constants;
        private readonly Dictionary<string, F4E1Descriptor> f4e1Names = new(RuntimeConfig.StrComper);
        private BoundIndexedStringSlot f4e1NameSlot = null!;
        private VariableToken? f4e1LegacyLcount;
        private CompactNormalHandle f4e1ParentHandle;
        private CompactNormalHandle f4e1TargetHandle;
        private bool f4e1LegacyActive;
        private bool f4e1TargetReturnPending;
        private bool f4e1TargetReturned;
        private bool f4e1ResultsWritePending;
        private bool f4e1DestinationWritePending;
        private string? f4e1PendingValue;
        private string f4e1Target = "";
        private F4E1Resolution f4e1Resolution;
        private long f4e1Lcount;
        private int f4e1ParentEntries;
        private int f4e1TargetEntries;
        private int f4e1TargetMaterialized;
        private int f4e1RemainingFamilyExecutions;
        private int f4e1Writes;
        private object? f4e1P29;
        private object? f4e1P30;
        private object? f4e1P31;
        private string[] f4e1InitialNames = [];
        private string f4e1RngBefore = "";
        private long f4e1RngCallsBefore;
        private long f4e1ClockBefore;

        internal void ExecuteF4E1(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1023" || compactFrames.Count != 1
                || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START" || compactFrames[0].Pc != 1023)
                throw new InvalidOperationException("R0-F4E1 requires the fresh F4D5 SYSTEM1023 boundary");

            var stateBefore = process.GetBenchmarkStateHash();
            f4e1Constants = BindF4E1Constants();
            ValidateF4E1Parent();
            var nameInventory = AdmitF4E1Names();
            var remainingInventory = InventoryF4E1Families();
            BindF4E1Destination(process);
            var resolverMatrix = RunF4E1ResolverMatrix(process);
#if R0_F4E3
            if (F4E3Enabled) PrepareF4E3Preflight(process, nameInventory);
#endif
            var stateAfter = process.GetBenchmarkStateHash();
            if (!nameInventory.All(x => x.Resolution is "Known" or "KnownMissing")
                || nameInventory.Any(x => x.Resolution == "Known" && x.BodyClass != "RESULTS literal + fallthrough")
                || stateBefore != stateAfter || resolverMatrix.Any(x => !x.Pass))
                throw new InvalidOperationException("R0-F4E1 preflight blocked before effect");

            f4e1InitialNames = ReadF4E1Names(process);
            f4e1RngBefore = process.vEvaluator.GetR0C2RngHash();
            f4e1RngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            f4e1ClockBefore = DifferentialDeterminism.ObservationCount;

            if (Candidate) RunCandidateF4E1(process);
            else RunLegacyF4E1(process);
#if R0_F4E3
            if (F4E3Enabled) RecordF4E3Name(process);
#endif

            var finalNames = ReadF4E1Names(process);
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var expected = f4e1Resolution == F4E1Resolution.Known ? f4e1Names[f4e1Target].Value : f4e1InitialNames[0];
            var domainPass = finalNames[0] == expected && finalNames.Skip(1).SequenceEqual(f4e1InitialNames.Skip(1));
            var pass = f4e1ParentEntries == 1 && f4e1Target == F4E1NamePrefix + "0"
                && f4e1Resolution == F4E1Resolution.Known && f4e1TargetEntries == 1
                && f4e1TargetMaterialized == 1 && f4e1TargetReturned && f4e1Writes == 1
                && domainPass && f4e1RemainingFamilyExecutions == 0
                && f4e1P29 is not null && f4e1P30 is not null && f4e1P31 is not null
                && compactFrames.Count == 2 && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START"
                && compactFrames[0].Pc == 1023 && compactFrames[1].Handle.Name == F4E1Parent
                && compactFrames[1].Pc == 26
                && StoppedBefore == "TURNEND_EV_独自変数システム.ERB:26"
                && process.vEvaluator.GetR0C2RngHash() == f4e1RngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == f4e1RngCallsBefore
                && DifferentialDeterminism.ObservationCount == f4e1ClockBefore
                && (!Candidate || guardTotal == 0);

            F4E1Evidence = new
            {
                Schema = "emuera-r0f4e1-turnend-name-prefix-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "TURNEND_FIRST_ITERATION_NAME_PREFIX",
                GateResult = pass ? "PASS" : "FAIL",
                Constants = f4e1Constants,
                ParentAdmission = new { Pass = true, Entry = F4E1ParentPath + ":18", Through = F4E1ParentPath + ":25", StoppedBefore },
                TurnEndFamilyInventory = nameInventory,
                RemainingFamilyInventory = remainingInventory,
                ResolverMatrix = resolverMatrix,
                P28 = f4d5P27,
                P29 = f4e1P29,
                P30 = f4e1P30,
                P31 = f4e1P31,
                DynamicTarget = f4e1Target,
                Resolution = f4e1Resolution.ToString(),
                TargetValue = f4e1Names[f4e1Target].Value,
                TargetValueSha256 = Hash(f4e1Names[f4e1Target].Value),
                InitialTurnEndName = f4e1InitialNames,
                InitialTurnEndNameSha256 = HashStrings(f4e1InitialNames),
                FinalTurnEndName = finalNames,
                FinalTurnEndNameSha256 = HashStrings(finalNames),
                DomainOracle = new { Pass = domainPass, Index0Expected = expected, Index0Actual = finalNames[0], OtherCellsUnchanged = finalNames.Skip(1).SequenceEqual(f4e1InitialNames.Skip(1)) },
                SetTurnEndEvVarEntered = f4e1ParentEntries == 1,
                OuterIteration = ReadF4E1Lcount(process),
                TurnEndNameWrite = f4e1Writes == 1 ? "PASS" : "FAIL",
                RemainingFamiliesExecuted = f4e1RemainingFamilyExecutions,
                SetTurnEndEvVarCompleted = false,
                SetGameplayStartPC = "SYSTEM.ERB:1023",
                EventLoadCompleted = false,
                GraphFreeGameResumed = "NOT_YET_PROVEN",
                Materialization = Candidate
                    ? new { StartupCompile = 0, SetTurnEndEvVarPrefix = 1, TurnEndName0 = f4e1TargetMaterialized, RemainingFamilyBodies = 0 }
                    : new { StartupCompile = 0, SetTurnEndEvVarPrefix = 0, TurnEndName0 = 0, RemainingFamilyBodies = 0 },
                ExternalEffects = new { FileIO = 0, Display = 0, Input = 0, RNG = 0, Clock = 0, ScriptStringWrites = f4e1Writes },
                GuardTotal = guardTotal,
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                ManualRecaptureRequired = "NO",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass) throw new InvalidOperationException("R0-F4E1 correctness gate failed");
        }

        private F4E1Constants BindF4E1Constants()
        {
            var path = Path.Combine(DataRoot, "ERB", "RPG", "ターンエンドイベント", "TURNEND_EV.ERH");
            var lines = File.ReadAllLines(path, RuntimeConfig.Encode);
            var declarations = new List<object>();
            int Read(string name)
            {
                var regex = new Regex($"^#DIM\\s+CONST\\s+{Regex.Escape(name)}\\s*=\\s*([0-9]+)(?:\\s*;.*)?$", RegexOptions.CultureInvariant);
                var matches = lines.Select((text, index) => (match: regex.Match(text.Trim()), line: index + 1)).Where(x => x.match.Success).ToArray();
                if (matches.Length != 1 || !int.TryParse(matches[0].match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                    throw new InvalidOperationException("R0-F4E1 constant bind: " + name);
                declarations.Add(new { Name = name, Value = value, Line = matches[0].line });
                return value;
            }
            return new(Read("TURNEND_EV_NUM"), Read("VARSIZE_TURNEND_EV"), Read("VARSIZE_TURNEND_EV_STR"),
                Read("VARSIZE_TURNEND_EV_TAKEOVER"), Read("VARSIZE_TURNEND_EV_STR_TAKEOVER"),
                Path.GetRelativePath(DataRoot, path).Replace('\\', '/'), FileHash(path)!, declarations.ToArray());
        }

        private void ValidateF4E1Parent()
        {
            if (!functions.TryGetValue(F4E1Parent, out var matches) || matches.Length != 1)
                throw new InvalidOperationException("R0-F4E1 parent identity");
            var parent = matches[0];
            var prefix = new[]
            {
                "#DIM LCOUNT, 2", "FOR LCOUNT, 0, TURNEND_EV_NUM", "TRYCCALLFORM TURNEND_EV_NAME_{LCOUNT}",
                "TURNEND_EV_NAME:LCOUNT = %RESULTS%", "CATCH", "BREAK", "ENDCATCH", "VARSET RESULTS, \"\", 0, VARSIZE_TURNEND_EV"
            };
            var body = Executable(parent);
            if (parent.IsEvent || parent.RelativePath != F4E1ParentPath || parent.Line != 18
                || body.Length < prefix.Length || !body.Take(prefix.Length).SequenceEqual(prefix))
                throw new InvalidOperationException("R0-F4E1 parent source mismatch");
        }

        private F4E1FamilyRow[] AdmitF4E1Names()
        {
            f4e1Names.Clear();
            var rows = new List<F4E1FamilyRow>();
            for (var index = 0; index < f4e1Constants!.TurnEndEvNum; index++)
            {
                var name = F4E1NamePrefix + index.ToString(CultureInfo.InvariantCulture);
                if (!functions.TryGetValue(name, out var matches))
                {
                    rows.Add(new("NAME", index, name, sourceComplete ? "KnownMissing" : "Unresolved", "NONE", [], [], null, null, null, null));
                    continue;
                }
                if (matches.Length != 1)
                {
                    rows.Add(new("NAME", index, name, "Blocked", "AMBIGUOUS", [], ["multiple definitions"], null, null, null, null));
                    continue;
                }
                var entry = matches[0];
                if (entry.IsEvent)
                {
                    rows.Add(new("NAME", index, name, "WrongKind", "EVENT", [], ["event definition"], entry.RelativePath, entry.Line, FileHash(entry.File.FileIdentity), null));
                    continue;
                }
                try
                {
                    var value = ParseF4E1NameBody(entry, out var bodySha);
                    var descriptor = new F4E1Descriptor(index, name, value, entry, FileHash(entry.File.FileIdentity)!, bodySha);
                    f4e1Names.Add(name, descriptor);
                    rows.Add(new("NAME", index, name, "Known", "RESULTS literal + fallthrough", [0], [], entry.RelativePath, entry.Line, descriptor.SourceSha256, descriptor.BodySha256));
                }
                catch (Exception ex)
                {
                    rows.Add(new("NAME", index, name, "Blocked", "UNSUPPORTED", [], [ex.Message], entry.RelativePath, entry.Line, FileHash(entry.File.FileIdentity), null));
                }
            }
            return rows.ToArray();
        }

        private F4E1FamilyRow[] InventoryF4E1Families()
        {
            var families = new[] { "FLAGNAME", "STRNAME", "TAKEOVER_FLAGNAME", "TAKEOVER_STRNAME" };
            var rows = new List<F4E1FamilyRow>();
            foreach (var family in families)
            for (var index = 0; index < f4e1Constants!.TurnEndEvNum; index++)
            {
                var name = "TURNEND_EV_" + family + "_" + index.ToString(CultureInfo.InvariantCulture);
                if (!functions.TryGetValue(name, out var matches))
                {
                    rows.Add(new(family, index, name, sourceComplete ? "KnownMissing" : "Unresolved", "NONE", [], [], null, null, null, null));
                    continue;
                }
                if (matches.Length != 1)
                {
                    rows.Add(new(family, index, name, "Blocked", "AMBIGUOUS", [], ["multiple definitions"], null, null, null, null));
                    continue;
                }
                var entry = matches[0];
                var body = Executable(entry);
                var indices = new List<int>();
                var unsupported = new List<string>();
                foreach (var line in body)
                {
                    var match = Regex.Match(line, "^RESULTS:([0-9]+)\\s*=", RegexOptions.CultureInvariant);
                    if (match.Success) indices.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
                    else unsupported.Add(line);
                }
                var resolution = entry.IsEvent ? "WrongKind" : unsupported.Count == 0 ? "Known" : "Blocked";
                rows.Add(new(family, index, name, resolution,
                    unsupported.Count == 0 ? "indexed RESULTS literals + fallthrough" : "UNSUPPORTED",
                    indices.ToArray(), unsupported.ToArray(), entry.RelativePath, entry.Line,
                    FileHash(entry.File.FileIdentity), BodyHash(body)));
            }
            return rows.ToArray();
        }

        private string ParseF4E1NameBody(R0F1Definition entry, out string bodySha)
        {
            var forbidden = SourceIndexFlags.Preprocessor | SourceIndexFlags.Rename | SourceIndexFlags.LineContinuation
                | SourceIndexFlags.OtherSemanticFallback | SourceIndexFlags.FunctionMetadata | SourceIndexFlags.DeclarationDirective;
            if ((entry.Function.Flags & forbidden) != 0) throw new InvalidOperationException("source flags " + entry.Function.Flags);
            var body = Executable(entry);
            if (body.Length != 1 || !body[0].StartsWith("RESULTS = ", StringComparison.Ordinal))
                throw new InvalidOperationException("requires one RESULTS literal write");
            var expanded = environment.Macros.Expand(body[0], environment.Compatibility, out var substitutions);
            if (substitutions != 0 || expanded != body[0]) throw new InvalidOperationException("macro-influenced body");
            var value = body[0]["RESULTS = ".Length..];
            if (value.Length == 0 || value.IndexOfAny(['{', '}', '%', '\\']) >= 0)
                throw new InvalidOperationException("non-literal RESULTS value");
            bodySha = BodyHash(body);
            return value;
        }

        private void BindF4E1Destination(Process process)
        {
            var token = process.idDic.GetVariableToken("TURNEND_EV_NAME", null, false);
            if (token is null || !token.IsString || !token.IsArray1D || token.IsCharacterData || token.IsLocal
                || token.IsPrivate || token.IsConst || token.GetLength() != f4e1Constants!.TurnEndEvNum)
                throw new InvalidOperationException("R0-F4E1 TURNEND_EV_NAME schema mismatch");
            token.CheckElement([0]); token.CheckElement([f4e1Constants.TurnEndEvNum - 1]);
            f4e1NameSlot = new("TURNEND_EV_NAME", token, token.GetLength());
        }

        private F4E1ResolverRow[] RunF4E1ResolverMatrix(Process process)
        {
            var before = process.GetBenchmarkStateHash();
            var cases = new[]
            {
                ProbeF4E1("Known", true, true, true, true, owner, OwnerGeneration, true, F4E1Resolution.Known),
                ProbeF4E1("Missing", false, false, true, true, owner, OwnerGeneration, true, F4E1Resolution.KnownMissing),
                ProbeF4E1("wrong kind", true, true, false, true, owner, OwnerGeneration, true, F4E1Resolution.WrongKind),
                ProbeF4E1("unresolved", false, false, true, false, owner, OwnerGeneration, true, F4E1Resolution.Unresolved),
                ProbeF4E1("blocked", true, false, true, true, owner, OwnerGeneration, true, F4E1Resolution.Blocked),
                ProbeF4E1("source mismatch", true, true, true, true, owner, OwnerGeneration, false, F4E1Resolution.Blocked),
                ProbeF4E1("owner revoke", true, true, true, true, owner, OwnerGeneration + 1, true, F4E1Resolution.Blocked)
            };
            if (process.GetBenchmarkStateHash() != before) throw new InvalidOperationException("R0-F4E1 resolver matrix changed state");
            return cases;
        }

        private F4E1ResolverRow ProbeF4E1(string name, bool exists, bool semantic, bool kind, bool complete,
            object requestOwner, int generation, bool sourceMatch, F4E1Resolution expected)
        {
            var actual = ResolveF4E1Facts(exists, semantic, kind, complete, requestOwner, generation, sourceMatch);
            var continued = actual is F4E1Resolution.Known or F4E1Resolution.KnownMissing;
            return new(name, actual.ToString(), expected.ToString(), continued, 0, 0, actual == expected);
        }

        private F4E1Resolution ResolveF4E1Facts(bool exists, bool semantic, bool kind, bool complete,
            object requestOwner, int generation, bool sourceMatch)
        {
            if (!ReferenceEquals(requestOwner, owner) || generation != OwnerGeneration || !sourceMatch) return F4E1Resolution.Blocked;
            if (!exists) return complete ? F4E1Resolution.KnownMissing : F4E1Resolution.Unresolved;
            if (!kind) return F4E1Resolution.WrongKind;
            return semantic ? F4E1Resolution.Known : F4E1Resolution.Blocked;
        }

        private F4E1Resolution ResolveF4E1(string name, out F4E1Descriptor? descriptor, string prefix = F4E1NamePrefix)
        {
            descriptor = null;
            if (f4e1Names.TryGetValue(name, out descriptor))
                return ResolveF4E1Facts(true, true, !descriptor.Definition.IsEvent, sourceComplete, owner, OwnerGeneration,
                    FileHash(descriptor.Definition.File.FileIdentity) == descriptor.SourceSha256);
            if (functions.TryGetValue(name, out var matches))
                return ResolveF4E1Facts(true, false, matches.Any(x => !x.IsEvent), sourceComplete, owner, OwnerGeneration, true);
            if (!name.StartsWith(prefix, RuntimeConfig.StringComparison)
                || !int.TryParse(name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index < 0 || index >= f4e1Constants!.TurnEndEvNum)
                return F4E1Resolution.Unresolved;
            return ResolveF4E1Facts(false, false, true, sourceComplete, owner, OwnerGeneration, true);
        }

        private void MaterializeF4E1Target(F4E1Descriptor descriptor)
        {
            if (FileHash(descriptor.Definition.File.FileIdentity) != descriptor.SourceSha256
                || ParseF4E1NameBody(descriptor.Definition, out var bodySha) != descriptor.Value
                || bodySha != descriptor.BodySha256)
                throw new InvalidOperationException("R0-F4E1 target source mismatch");
            if (f4e1TargetMaterialized == 0)
            {
                f4e1TargetHandle = F4D5Handle(50_001, descriptor.Name);
                f4e1TargetMaterialized = 1;
            }
        }

        private void RunCandidateF4E1(Process process)
        {
            f4e1ParentHandle = F4D5Handle(50_000, F4E1Parent);
            Enter(new(f4e1ParentHandle, 1024, "SYSTEM.ERB:1023"), 20, false);
            f4e1ParentEntries = 1;
            f4e1Lcount = 0;
            f4e1Target = BuildF4E1Target(f4e1Lcount);
            f4e1Resolution = ResolveF4E1(f4e1Target, out var descriptor);
            f4e1P29 = CaptureF4E1Point(process, "P29", F4E1ParentPath + ":21:before");
            if (f4e1Resolution == F4E1Resolution.KnownMissing)
            {
                Process.R0F4E1BreakCurrentFor(ref f4e1Lcount);
                compactFrames[^1].Pc = 55;
                Return(process, F4E1Parent, false);
                StoppedBefore = F4E1ParentPath + ":55";
                return;
            }
            if (f4e1Resolution != F4E1Resolution.Known || descriptor is null)
                throw new InvalidOperationException("R0-F4E1 target blocked before effect: " + f4e1Resolution);
            MaterializeF4E1Target(descriptor);
            Enter(new(f4e1TargetHandle, 22, F4E1ParentPath + ":21"), checked((ushort)(descriptor.Definition.Line + 1)), false);
            f4e1TargetEntries = 1;
            process.vEvaluator.RESULTS = descriptor.Value;
            process.vEvaluator.RESULT = 0;
            Return(process, descriptor.Name, false);
            f4e1TargetReturned = true;
            f4e1P30 = CaptureF4E1Point(process, "P30", F4E1ParentPath + ":22:before");
            var rhs = process.vEvaluator.RESULTS;
            var index = f4e1Lcount;
            f4e1NameSlot.Write(rhs, index);
            compactFrames[^1].EvalTemporary = index;
            compactFrames[^1].Committed = true;
            compactFrames[^1].Pc = 26;
            f4e1Writes = 1;
            StoppedBefore = "TURNEND_EV_独自変数システム.ERB:26";
            f4e1P31 = CaptureF4E1Point(process, "P31", F4E1ParentPath + ":26:before");
        }

        private void RunLegacyF4E1(Process process)
        {
            var stopped = process.state.CurrentLine;
            var previous = (LogicalLine?)stopped?.ParentLabelLine;
            while (previous is not null && !ReferenceEquals(previous.NextLine, stopped)) previous = previous.NextLine;
            if (previous is null) throw new InvalidOperationException("R0-F4E1 Legacy resume predecessor not found");
            process.state.CurrentLine = previous;
            f4e1LegacyActive = true;
            try
            {
                process.runScriptProc();
                throw new InvalidOperationException("R0-F4E1 Legacy crossed stop boundary");
            }
            catch (R0F1PlannedCheckpointException) { }
            finally { f4e1LegacyActive = false; }
        }

        internal void ObserveF4E1LegacyEntry(Process process, CalledFunction call)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3NameEntry(process, call); return; }
#endif
            if (!f4e1LegacyActive) return;
            var name = call.TopLabel.LabelName;
            if (name.Equals(F4E1Parent, RuntimeConfig.StringComparison))
            {
                f4e1ParentEntries++;
                f4e1LegacyLcount = call.TopLabel.GetPrivateVariable("LCOUNT");
                if (f4e1LegacyLcount is null || !f4e1LegacyLcount.IsInteger || !f4e1LegacyLcount.IsPrivate)
                    throw new InvalidOperationException("R0-F4E1 Legacy persistent LCOUNT bind");
                f4e1ParentHandle = F4D5Handle(50_000, F4E1Parent);
                Enter(new(f4e1ParentHandle, 1024, "SYSTEM.ERB:1023"), 20, false);
            }
            else if (name.Equals(f4e1Target, RuntimeConfig.StringComparison))
            {
                if (f4e1Resolution != F4E1Resolution.Known || !f4e1Names.TryGetValue(name, out var descriptor))
                    throw new InvalidOperationException("R0-F4E1 Legacy target entered without admission");
                MaterializeF4E1Target(descriptor);
                Enter(new(f4e1TargetHandle, 22, F4E1ParentPath + ":21"), checked((ushort)(descriptor.Definition.Line + 1)), false);
                f4e1TargetEntries++;
            }
            else if (name.StartsWith("TURNEND_EV_", RuntimeConfig.StringComparison))
                f4e1RemainingFamilyExecutions++;
        }

        internal void ObserveF4E1LegacyInstruction(Process process, InstructionLine line, bool before)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3Instruction(process, line, before); return; }
#endif
            if (!f4e1LegacyActive) return;
            if (before && IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", 21))
            {
                f4e1Lcount = ReadF4E1Lcount(process);
                f4e1Target = BuildF4E1Target(f4e1Lcount);
                f4e1Resolution = ResolveF4E1(f4e1Target, out _);
                f4e1P29 = CaptureF4E1Point(process, "P29", F4E1ParentPath + ":21:before");
            }
            if (before && IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", 26))
            {
                if (!f4e1DestinationWritePending || f4e1Writes != 1 || !f4e1TargetReturned)
                    throw new InvalidOperationException("R0-F4E1 Legacy incomplete before line26");
                f4e1DestinationWritePending = false;
                compactFrames[^1].Pc = 26;
                StoppedBefore = "TURNEND_EV_独自変数システム.ERB:26";
                f4e1P31 = CaptureF4E1Point(process, "P31", F4E1ParentPath + ":26:before");
                throw new R0F1PlannedCheckpointException();
            }
        }

        internal void ObserveF4E1LegacyDynamic(Process process, InstructionLine line, string target, bool found)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3NameDynamic(process, line, target, found); return; }
#endif
            if (!f4e1LegacyActive || !IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", 21)) return;
            var expected = BuildF4E1Target(ReadF4E1Lcount(process));
            var resolution = ResolveF4E1(target, out _);
            if (target != expected || found != (resolution == F4E1Resolution.Known))
                throw new InvalidOperationException("R0-F4E1 Legacy dynamic resolver mismatch");
            f4e1Target = target;
            f4e1Resolution = resolution;
        }

        internal void ObserveF4E1LegacyStringWrite(Process process, InstructionLine line, string? value, bool before)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3NameWrite(process, line, value, before); return; }
#endif
            if (!f4e1LegacyActive) return;
            if (line.ParentLabelLine?.LabelName.Equals(f4e1Target, RuntimeConfig.StringComparison) == true)
            {
                if (before)
                {
                    if (!f4e1Names.TryGetValue(f4e1Target, out var descriptor) || value != descriptor.Value)
                        throw new InvalidOperationException("R0-F4E1 Legacy RESULTS RHS mismatch");
                    f4e1ResultsWritePending = true;
                    f4e1PendingValue = value;
                }
                else
                {
                    if (!f4e1ResultsWritePending || process.vEvaluator.RESULTS != f4e1PendingValue)
                        throw new InvalidOperationException("R0-F4E1 Legacy RESULTS write mismatch");
                    f4e1ResultsWritePending = false;
                }
                return;
            }
            if (!IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", 22)) return;
            if (before)
            {
                if (!f4e1TargetReturned || value != process.vEvaluator.RESULTS || ReadF4E1Lcount(process) != 0)
                    throw new InvalidOperationException($"R0-F4E1 Legacy indexed write order mismatch returned={f4e1TargetReturned} valueMatch={value == process.vEvaluator.RESULTS} lcount={ReadF4E1Lcount(process)} target={f4e1Target}");
                f4e1P30 = CaptureF4E1Point(process, "P30", F4E1ParentPath + ":22:before");
                f4e1PendingValue = value;
            }
            else
            {
                if (f4e1PendingValue is null || f4e1NameSlot.Read(process, 0) != f4e1PendingValue)
                    throw new InvalidOperationException("R0-F4E1 Legacy destination write mismatch");
                f4e1Writes++;
                f4e1DestinationWritePending = true;
                compactFrames[^1].EvalTemporary = 0;
                compactFrames[^1].Committed = true;
                f4e1PendingValue = null;
            }
        }

        internal void ObserveF4E1LegacyFallthrough(Process process, LogicalLine line, bool before)
        {
#if R0_F4E3
            if (f4e3LegacyActive) { ObserveF4E3NameOrParentFallthrough(process, line, before); return; }
#endif
            if (!f4e1LegacyActive) return;
            if (before)
            {
                if (process.state.functionCount == 0
                    || !process.state.CurrentCalled.FunctionName.Equals(f4e1Target, RuntimeConfig.StringComparison)) return;
                if (f4e1ResultsWritePending) throw new InvalidOperationException("R0-F4E1 Legacy target returned during write");
                f4e1TargetReturnPending = true;
            }
            else if (f4e1TargetReturnPending)
            {
                f4e1TargetReturnPending = false;
                if (process.vEvaluator.RESULT != 0 || !f4e1Names.TryGetValue(f4e1Target, out var descriptor)
                    || process.vEvaluator.RESULTS != descriptor.Value)
                    throw new InvalidOperationException("R0-F4E1 Legacy target fallthrough mismatch");
                Return(process, f4e1Target, false);
                f4e1TargetReturned = true;
            }
        }

        private F4E1Point CaptureF4E1Point(Process process, string name, string pc)
        {
            var names = ReadF4E1Names(process);
            var result = (long[])process.vEvaluator.RESULT_ARRAY.Clone();
            var results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            return new(name, pc, ReadF4E1Lcount(process), f4e1Target, f4e1Resolution.ToString(), names,
                HashStrings(names), result, HashLongs(result), results, HashStrings(results),
                string.Join(" > ", compactFrames.Select(FrameText)), EventCursor, process.state.SystemState.ToString(),
                process.state.isBegun, process.vEvaluator.GetR0C2RngHash(),
                process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount);
        }

        private long ReadF4E1Lcount(Process process) => Candidate ? f4e1Lcount
            : f4e1LegacyLcount?.GetIntValue(process.exm, [0]) ?? 0;
        private string[] ReadF4E1Names(Process process) => Enumerable.Range(0, f4e1Constants!.TurnEndEvNum)
            .Select(i => f4e1NameSlot.Read(process, i)).ToArray();
        private static string BuildF4E1Target(long lcount) => F4E1NamePrefix + lcount.ToString(CultureInfo.InvariantCulture);
        private static bool IsF4E1Line(LogicalLine line, string file, int number) => line.Position is { } position
            && position.LineNo == number && Path.GetFileName(position.Filename).Equals(file, StringComparison.OrdinalIgnoreCase);
    }
}
#endif
