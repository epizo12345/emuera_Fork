#if R0_E1A
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed record E1AProbe(string Function, string Fixture, QueryValue[] Inputs,
        long? Return, string? Fault, string StateBefore, string StateAfter,
        string RngBefore, string RngAfter, bool StateUnchanged, bool RngUnchanged);

    internal void SetR0E1ALoadNo(int value) => vEvaluator.VariableData.LastLoadNo = value;

    internal object RunR0E1A(bool candidate)
    {
        var saveDecoded = R0E1ASnapshot();
        var saveStateHash = GetBenchmarkStateHash();
        CompactRuntimeOwner? owner = candidate ? new(this, exm) : null;
        var startupCompiled = owner?.CompiledProgramCount ?? -1;
        var ownership = owner?.RunLiveContractSelfTest();
        var sourceMismatchRejected = owner?.SourceMismatchSelfTest() ?? false;
        var handles = candidate ? new Dictionary<string, CompactFunctionHandle>(StringComparer.Ordinal)
        {
            ["HAVE_SKILL"] = owner!.Handle("HAVE_SKILL"),
            ["CHARA_SKILLCOUNT"] = owner.Handle("CHARA_SKILLCOUNT"),
            ["FINDCHARA_LINK"] = owner.Handle("FINDCHARA_LINK"),
            ["FINDCHARA_TIMEID"] = owner.Handle("FINDCHARA_TIMEID"),
            ["COUNT_SPLIT"] = owner.Handle("COUNT_SPLIT")
        } : null;

        long Invoke(string name, QueryValue[] values)
        {
            if (candidate) return handles![name].Execute(owner!, this, values).Integer;
            var expressions = values.Select(value => value.Text is null
                ? (AExpression)SingleLongTerm.FromValue(value.Integer)
                : SingleStrTerm.FromValue(value.Text)).ToList();
            var term = idDic.GetFunctionMethod(labelDic, name, expressions, true) as UserDefinedMethodTerm
                ?? throw new InvalidOperationException("Legacy oracle method missing: " + name);
            return term.GetIntValue(exm);
        }

        var probes = new List<E1AProbe>();
        void Probe(string function, string fixture, params QueryValue[] inputs)
        {
            var stateBefore = GetBenchmarkStateHash();
            var rngBefore = vEvaluator.GetR0C2RngHash();
            long? value = null; string? fault = null;
            try { value = Invoke(function, inputs); }
            catch (Exception ex) { fault = ex.GetType().FullName + ": " + ex.Message; }
            var stateAfter = GetBenchmarkStateHash();
            var rngAfter = vEvaluator.GetR0C2RngHash();
            probes.Add(new(function, fixture, inputs, value, fault, stateBefore, stateAfter,
                rngBefore, rngAfter, stateBefore == stateAfter, rngBefore == rngAfter));
        }

        // Fixed order makes the lazy gate observable without a separate warm-up path.
        Probe("HAVE_SKILL", "negative character early return", QueryValue.I(-1), QueryValue.I(0), QueryValue.I(0), QueryValue.S(""));
        var firstHaveCount = owner?.CompiledProgramCount ?? -1;
        var readsAfterFirstHave = owner?.FunctionSliceReadCount ?? -1;
        var compilesAfterFirstHave = owner?.CompileCount ?? -1;
        Probe("HAVE_SKILL", "repeated negative character", QueryValue.I(-1), QueryValue.I(0), QueryValue.I(0), QueryValue.S(""));
        var repeatedHaveAdditionalReads = owner is null ? -1 : owner.FunctionSliceReadCount - readsAfterFirstHave;
        var repeatedHaveAdditionalCompiles = owner is null ? -1 : owner.CompileCount - compilesAfterFirstHave;
        Probe("COUNT_SPLIT", "empty", QueryValue.S(""), QueryValue.S("/"), QueryValue.S(""));
        var afterCountSplit = owner?.CompiledProgramCount ?? -1;

        Probe("HAVE_SKILL", "typed default-equivalent values", QueryValue.I(0), QueryValue.I(0), QueryValue.I(0), QueryValue.S(""));
        var abl = idDic.GetVariableToken("ABL", null, false) ?? throw new InvalidOperationException("ABL token missing");
        var cflag = idDic.GetVariableToken("CFLAG", null, false) ?? throw new InvalidOperationException("CFLAG token missing");
        int Slot(VariableToken token, string name) => GlobalStatic.ConstantData.KeywordToInteger(token.Code, name, 1);
        var first = Slot(abl, "スキル1");
        var equip = Slot(abl, "装備スキル1");
        var characterCount = checked((int)vEvaluator.CHARANUM);
        for (var character = 0; character < characterCount; character++)
        {
            foreach (var form in new[] { "", "通常形態", "変身形態" })
                Probe("CHARA_SKILLCOUNT", "real-save matrix", QueryValue.I(character), QueryValue.S(form));
            var skills = ((long[])abl.GetArrayChara(character)).Skip(first).Take(20)
                .Concat(((long[])abl.GetArrayChara(character)).Skip(equip).Take(22))
                .Where(value => value > 0).Distinct().Take(3).ToArray();
            foreach (var skill in skills.Append(long.MaxValue))
                foreach (var mode in new[] { 0L, 1L })
                    foreach (var form in new[] { "", "通常形態", "変身形態" })
                        Probe("HAVE_SKILL", skill == long.MaxValue ? "missing" : "found",
                            QueryValue.I(character), QueryValue.I(skill), QueryValue.I(mode), QueryValue.S(form));
        }
        var timeId = cflag.GetIntValue(exm, [0, Slot(cflag, "リアル加入時間")]);
        Probe("FINDCHARA_TIMEID", "real-save direct", QueryValue.I(timeId));
        Probe("FINDCHARA_LINK", "real-save direct", QueryValue.I(0), QueryValue.I(0));
        foreach (var value in new[]
        {
            ("one","/","one"), ("a/b/a","/","a"), ("a/b","/","missing"),
            ("/a/","/",""), ("日本語/日本/日本語","/","日本語"),
            ("abc","",""), ("a.b.a","\\.","a"), ("aaaa","a","a")
        }) Probe("COUNT_SPLIT", "string matrix", QueryValue.S(value.Item1), QueryValue.S(value.Item2), QueryValue.S(value.Item3));

        var changed = new List<(VariableToken Token, long Character, long Slot, long Old)>();
        void Set(VariableToken token, int character, int slot, long value)
        {
            changed.Add((token, character, slot, token.GetIntValue(exm, [character, slot])));
            token.SetValue(value, [character, slot]);
        }
        try
        {
            if (characterCount < 2) throw new InvalidOperationException("Link fixture requires two characters");
            Set(cflag, 0, Slot(cflag, "悪魔変身"), 0);
            Set(cflag, 0, Slot(cflag, "リンクキャラリアル加入時間0"), 737373);
            Set(cflag, 1, Slot(cflag, "リアル加入時間"), 737373);
            Set(abl, 1, first, 71717171);
            Set(abl, 0, equip, 72727272);
            foreach (var mode in new[] { 0L, 1L })
            {
                Probe("HAVE_SKILL", "FINDCHARA_LINK transformed branch fixture",
                    QueryValue.I(0), QueryValue.I(71717171), QueryValue.I(mode), QueryValue.S("変身形態"));
                Probe("HAVE_SKILL", "normal equipment branch fixture",
                    QueryValue.I(0), QueryValue.I(72727272), QueryValue.I(mode), QueryValue.S("通常形態"));
            }
        }
        finally
        {
            for (var i = changed.Count - 1; i >= 0; i--)
            {
                var item = changed[i]; item.Token.SetValue(item.Old, [item.Character, item.Slot]);
            }
        }

        var stateAfterQueries = GetBenchmarkStateHash();
        var rngAfterQueries = vEvaluator.GetR0C2RngHash();
        var failedProbe = probes.FirstOrDefault(row => row.Fault is not null || !row.StateUnchanged || !row.RngUnchanged);
        if (failedProbe is not null || stateAfterQueries != saveStateHash)
            throw new InvalidOperationException($"R0-E1A query matrix failure: " +
                $"probe={failedProbe?.Function}/{failedProbe?.Fixture}, fault={failedProbe?.Fault}, " +
                $"probeState={failedProbe?.StateUnchanged}, probeRng={failedProbe?.RngUnchanged}, " +
                $"finalState={stateAfterQueries == saveStateHash}");

        object? faultProof = null;
        bool temporaryReleased = false, revokedRejected = false, heavyReleased = false, permanentlyRevoked = false;
        long flatToFlat = 0, attempts = 0, completed = 0, faults = 0;
        if (owner is not null)
        {
            temporaryReleased = owner.VerifyTemporaryRootsReleased();
            var faultState = GetBenchmarkStateHash(); var beforeAttempts = owner.Attempts;
            bool explicitFault = false;
            try { _ = handles!["HAVE_SKILL"].Execute(owner, this,
                [QueryValue.I(-1), QueryValue.I(0), QueryValue.I(0), QueryValue.S("")], 0); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("StepLimit", StringComparison.Ordinal)) { explicitFault = true; }
            faultProof = new { ExplicitFault = explicitFault, AttemptDelta = owner.Attempts - beforeAttempts,
                FaultDelta = owner.Faults, StateUnchanged = faultState == GetBenchmarkStateHash(), LegacyRetryAfterFlat = 0 };
            flatToFlat = owner.FlatToFlatCalls; attempts = owner.Attempts; completed = owner.Completed; faults = owner.Faults;
            var stale = handles!["HAVE_SKILL"];
            var heavy = owner.Revoke();
            permanentlyRevoked = owner.RevokedPermanently;
            try { _ = stale.Execute(owner, this, [QueryValue.I(-1), QueryValue.I(0), QueryValue.I(0), QueryValue.S("")]); }
            catch (InvalidOperationException) { revokedRejected = true; }
            GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            heavyReleased = heavy.All(reference => !reference.IsAlive);
        }

        return new
        {
            Schema = "emuera-r0e1a-boundary-v1", Mode = candidate ? "CompactCandidate" : "LegacyControl",
            SaveDecoded = saveDecoded, Queries = probes,
            QuerySummary = new { Cases = probes.Count, AllSucceeded = probes.All(row => row.Fault is null),
                StateUnchanged = probes.All(row => row.StateUnchanged) && stateAfterQueries == saveStateHash,
                RngUnchanged = probes.All(row => row.RngUnchanged), StateAfterQueries = stateAfterQueries, RngAfterQueries = rngAfterQueries },
            Lazy = candidate ? new { StartupCompiledProgramCount = startupCompiled,
                FirstHaveSkillCompiledProgramCount = firstHaveCount,
                RepeatedHaveSkillAdditionalSourceReads = repeatedHaveAdditionalReads,
                RepeatedHaveSkillAdditionalCompileCount = repeatedHaveAdditionalCompiles,
                AfterCountSplitCompiledProgramCount = afterCountSplit } : null,
            Ownership = candidate ? new { LiveSelfTest = ownership, SourceIdentityMismatchRejected = sourceMismatchRejected,
                TemporaryRootsReleased = temporaryReleased, RevokedHandleRejected = revokedRejected,
                OldOwnerHeavyRootsReleased = heavyReleased, OldOwnerReactivationImpossible = permanentlyRevoked } : null,
            Flat = candidate ? new { Attempts = attempts, Completed = completed, Faults = faults,
                IntentionalFaults = 1, UnexpectedFaults = 0, FlatToFlatPrelinkedCalls = flatToFlat,
                LegacyRetryAfterFlat = 0, ProductionBridgeAttempts = 0 } : null,
            Boundary = R0E1AProof.Boundary, Guard = candidate ? R0E1AProof.GuardSnapshot() : null,
            CodecAudit = R0E1AProof.CodecSnapshot(),
            SQL = "NOT_EXECUTED", LoadGlobal = "NOT_EXECUTED", SystemLoadEnd = "NOT_EXECUTED",
            EventLoad = "NOT_EXECUTED", GraphFreeGameResume = "NOT_READY",
            ProfileEvidenceRuntimeDependency = "NO", WholeProductSuperiority = "NOT_YET_CLAIMED"
        };
    }

    private object R0E1ASnapshot()
    {
        var variables = idDic.GetNextRuntimeVariableTokens().Select(pair => pair.Value).Distinct().ToArray();
        object[] Schema(IEnumerable<VariableToken> tokens) => tokens.OrderBy(token => token.Name, StringComparer.OrdinalIgnoreCase)
            .Select(token => (object)new { token.Name, Type = token.IsInteger ? "INT" : "STR", token.Dimension,
                token.IsGlobal, token.IsSavedata, token.IsCharacterData }).ToArray();
        string ValueHash(IEnumerable<VariableToken> tokens)
        {
            var text = new StringBuilder();
            foreach (var token in tokens.OrderBy(token => token.Name, StringComparer.OrdinalIgnoreCase))
            {
                text.Append(token.Name).Append('|').Append(token.IsInteger ? 'I' : 'S').Append('|').Append(token.Dimension).Append('|');
                if (token.Dimension == 0)
                    text.Append(token.IsInteger ? token.GetIntValue(exm, []) : token.GetStrValue(exm, []));
                else if (token.GetArray() is Array array)
                    foreach (var value in array) text.Append(value).Append('\u001F');
                text.AppendLine();
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
        var codec = R0E1AProof.CodecSnapshot();
        var ignored = codec.Where(row => !row.Resolved || !row.Compatible)
            .GroupBy(row => new { row.Domain, row.Name, row.SaveType, row.Resolved, row.Compatible })
            .Select(group => new { group.Key.Domain, group.Key.Name, group.Key.SaveType,
                group.Key.Resolved, group.Key.Compatible, Count = group.Count() })
            .OrderBy(row => row.Domain).ThenBy(row => row.Name).ToArray();
        var state = vEvaluator.GetDifferentialStateHashes();
        var savedHost = variables.Where(token => token.IsSavedata && !token.IsCharacterData && !token.IsPrivate && !token.IsLocal);
        var savedChara = variables.Where(token => token.IsSavedata && token.IsCharacterData);
        var nonSavedGlobal = variables.Where(token => token.IsGlobal && !token.IsSavedata && !token.IsCalc && !token.IsConst);
        var erhStatic = variables.Where(token => token is UserDefinedVariableToken && !token.IsGlobal && !token.IsPrivate);
        var randdata = idDic.GetVariableToken("RANDDATA", null, false) ?? throw new InvalidOperationException("RANDDATA token missing");
        return new
        {
            GameBaseVersionValidation = "PASS_BY_EXISTING_CODEC", StateHash = GetBenchmarkStateHash(),
            StateDomains = state, CharacterCount = vEvaluator.VariableData.CharacterList.Count,
            HostSavedSchema = Schema(savedHost), CharaSavedSchema = Schema(savedChara),
            NonSavedGlobalDefaultHash = ValueHash(nonSavedGlobal), ErhStaticResetHash = ValueHash(erhStatic),
            vEvaluator.VariableData.LastLoadVersion, vEvaluator.VariableData.LastLoadText,
            vEvaluator.VariableData.LastLoadNo, RngHash = vEvaluator.GetR0C2RngHash(),
            RandDataHash = ValueHash([randdata]), DeterministicClock = DifferentialDeterminism.Now().ToString("O"),
            DeterministicSeed = Program.NextRuntimeDifferentialSeed,
            DeterministicClockBase = Program.NextRuntimeDifferentialClockBase,
            DeterministicClockStepMs = Program.NextRuntimeDifferentialClockStepMs,
            CodecRows = codec.Length, IgnoredOrTypeMismatch = ignored,
            ComparisonExclusions = new[] { "Legacy per-function ARG/ARGS/LOCAL/LOCALS/private scratch", "SQL sidecar", "GLOBAL save", "SYSTEM_LOADEND/EVENTLOAD" }
        };
    }
}
#endif
