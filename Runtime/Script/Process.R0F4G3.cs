#if R0_F4G3
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string G3Initialize = "INITIALIZE_MESSAGE";
        private const string G3InitializeFile = "DIV_MESSAGE.ERB";
        private const string G3SetFlag = "SET_FLAG_USEABLE_TITLE";

        private sealed class G3RangeWrite
        {
            internal required string Name;
            internal required string Rhs;
            internal required int SourceLine;
            internal required bool IsString;
            internal required VariableToken Target;
            internal VariableToken? Source;
            internal string StringLiteral = "";
        }

        private sealed record G3VarsetRow(int Sequence, int SourceLine, string Name, string Type, string Rhs,
            long Start, long End, int CellWrites, int[] ChangedIndices, int[] UnchangedIndices,
            string InitialSha256, string FinalSha256);
        private sealed record G3Point(string Name, string ProgramCounter, string Frames, string EventCursor,
            string SystemState, bool PendingBegin, string StateSha256, string DomainsSha256,
            long Result0, string ResultsSha256, string RngSha256, long RngCalls, long ClockCalls);

        internal object? F4G3Evidence { get; private set; }
        private readonly List<G3RangeWrite> g3Ranges = [];
        private readonly List<G3VarsetRow> g3Varsets = [];
        private readonly List<int> g3LegacyVarsetOrder = [];
        private readonly HashSet<string> g3Materialized = new(RuntimeConfig.StrComper);
        private Dictionary<string, object> g3InitialDomains = [];
        private R0F1Definition g3InitializeDefinition = null!;
        private bool g3LegacyActive, g3InitializeReturnPending, g3InitializeReturned;
        private long g3Arg, g3LMin, g3LMax, g3Maximum;
        private object? g3T0, g3T1, g3InitializePreflight, g3SetFlagInventory, g3BranchMatrix;
        private string g3RngBefore = "";
        private long g3RngCallsBefore, g3ClockBefore;

        internal void ExecuteF4G3(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1031 CALL INITIALIZE_MESSAGE"
                || compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START"
                || compactFrames[0].Pc != 1031)
                throw new InvalidOperationException("R0-F4G3 requires the fresh F4G2 SYSTEM1031 boundary");

            PrepareG3Initialize(process);
            g3RngBefore = process.vEvaluator.GetR0C2RngHash();
            g3RngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            g3ClockBefore = DifferentialDeterminism.ObservationCount;
            g3T0 = CaptureG3Point(process, "T0", "SYSTEM.ERB:1031:before");

            if (Candidate) RunCandidateG3A(process); else RunLegacyG3A(process);

            var finalDomains = CaptureG3Domains(process);
            var rangePass = g3InitializeReturned && g3Arg == -1 && g3LMin == 0 && g3LMax == g3Maximum
                && g3Varsets.Count == 16 && g3Varsets.Sum(x => x.CellWrites) == 16 * g3Maximum
                && g3LegacyVarsetOrder.SequenceEqual(g3Ranges.Select(x => x.SourceLine))
                && G3DomainsMatchExpected(process, finalDomains);
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var pass = rangePass && g3T0 is not null && g3T1 is not null
                && StoppedBefore == "SYSTEM.ERB:1033 CALL SET_FLAG_USEABLE_TITLE"
                && compactFrames.Count == 1 && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START" && compactFrames[0].Pc == 1033
                && process.vEvaluator.GetR0C2RngHash() == g3RngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == g3RngCallsBefore
                && DifferentialDeterminism.ObservationCount == g3ClockBefore
                && guardTotal == 0 && B1Proof.BridgeAttempts == 0;

            F4G3Evidence = new
            {
                Schema = "emuera-r0f4g3-complete-set-gameplay-start-tail-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "COMPLETE_SET_GAMEPLAY_START_TAIL",
                GateResult = pass ? "PARTIAL_PASS" : "FAIL",
                LastCompletedSubregion = pass ? "G3-A" : "NONE",
                ResumedFrom = "SYSTEM.ERB:1031 CALL INITIALIZE_MESSAGE",
                StoppedBefore,
                Checkpoints = new { T0 = g3T0, T1 = g3T1, T2 = (object?)null, T3 = (object?)null, T4 = (object?)null, T5 = (object?)null, T6 = (object?)null, T7 = (object?)null },
                InitializeMessagePreflight = g3InitializePreflight,
                InitializeMessageOracle = new
                {
                    Pass = rangePass,
                    Argument = g3Arg,
                    LMin = g3LMin,
                    LMax = g3LMax,
                    Maximum = g3Maximum,
                    VarsetOrder = g3LegacyVarsetOrder.ToArray(),
                    Varsets = g3Varsets.ToArray(),
                    TotalCellWrites = g3Varsets.Sum(x => x.CellWrites),
                    InitialDomains = g3InitialDomains,
                    FinalDomains = finalDomains,
                    InitialSha256 = HashG3Domains(g3InitialDomains),
                    FinalSha256 = HashG3Domains(finalDomains),
                    BranchMatrix = g3BranchMatrix,
                    Result = (long[])process.vEvaluator.RESULT_ARRAY.Clone(),
                    Results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone()
                },
                SetFlagUseableTitleInventory = g3SetFlagInventory,
                SetFlagUseableTitleClosureReady = false,
                SetFlagUseableTitleCompleted = false,
                DisposeGraphicsCharaCompleted = false,
                SaveGlobalCompleted = false,
                SaveGlobalPayloadOracle = "NOT_REACHED",
                IsDebugAssignmentCompleted = false,
                DisposeGraphicsTempCompleted = false,
                SetGameplayStartCompleted = false,
                F4StartupDynamicPackageCompleted = false,
                Materialization = new { StartupCompiled = 0, Names = g3Materialized.ToArray(), Count = g3Materialized.Count, UnrelatedBodies = 0 },
                Guards = new { Total = guardTotal, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                VerupBase = new { Lookup = 0, Materialized = 0, Executed = 0 },
                EventLoadCompleted = false,
                GraphFreeGameResumed = "NOT_YET_PROVEN",
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                ArchitectureEscalationRequired = "NO",
                ManualRecaptureRequired = "NO",
                NextRecommendation = "REVIEW_NEXT_BLOCKER",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass) throw new InvalidOperationException("R0-F4G3 G3-A gate failed");
        }

        private void PrepareG3Initialize(Process process)
        {
            var stateBefore = process.GetBenchmarkStateHash();
            g3InitializeDefinition = G2Definition(G3Initialize, 697, "DIV_メッセージウィンドウ/DIV_MESSAGE.ERB");
            var source = Lines(Read(g3InitializeDefinition.Function, g3InitializeDefinition.File));
            var header = Regex.Match(source[0].Trim(), "^@INITIALIZE_MESSAGE\\s*,\\s*ARG\\s*=\\s*(-?[0-9]+)$");
            if (!header.Success) throw new InvalidOperationException("R0-F4G3 default argument source mismatch");
            g3Arg = long.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);

            var maxToken = RequireG3Token(process, "最大メッセージウィンドウ数", integer: true, writable: false);
            g3Maximum = ReadG3IntScalar(process, maxToken);
            if (g3Maximum <= 0 || g3Maximum > 1024) throw new InvalidOperationException("R0-F4G3 maximum range");

            var sourceLines = File.ReadAllLines(g3InitializeDefinition.File.FileIdentity, RuntimeConfig.Encode);
            var varsetRegex = new Regex("^VARSET\\s+([^,]+)\\s*,\\s*(.+?)\\s*,\\s*L_MIN\\s*,\\s*L_MAX$", RegexOptions.CultureInvariant);
            for (var line = 705; line <= 720; line++)
            {
                var match = varsetRegex.Match(sourceLines[line - 1].Trim());
                if (!match.Success) throw new InvalidOperationException("R0-F4G3 VARSET source mismatch line " + line);
                var name = match.Groups[1].Value.Trim();
                var rhs = match.Groups[2].Value.Trim();
                var target = process.idDic.GetVariableToken(name, null, false)
                    ?? throw new InvalidOperationException("R0-F4G3 destination missing: " + name);
                if (!target.IsArray1D || target.IsCharacterData || target.IsLocal || target.IsPrivate || target.IsConst || target.GetLength() != g3Maximum)
                    throw new InvalidOperationException("R0-F4G3 destination schema: " + name);
                var descriptor = new G3RangeWrite { Name = name, Rhs = rhs, SourceLine = line, IsString = target.IsString, Target = target };
                if (rhs == "\"\"") descriptor.StringLiteral = "";
                else
                {
                    descriptor.Source = process.idDic.GetVariableToken(rhs, null, false)
                        ?? throw new InvalidOperationException("R0-F4G3 RHS missing: " + rhs);
                    if (descriptor.Source.IsString != descriptor.IsString || !descriptor.Source.IsConst
                        || (descriptor.Source.Dimension != 0 && (!descriptor.Source.IsArray1D || descriptor.Source.GetLength() < 1)))
                        throw new InvalidOperationException("R0-F4G3 RHS schema: " + rhs);
                }
                g3Ranges.Add(descriptor);
            }
            if (g3Ranges.Count != 16 || g3Ranges.Count(x => x.IsString) != 7 || g3Ranges.Count(x => !x.IsString) != 9)
                throw new InvalidOperationException("R0-F4G3 VARSET inventory count");

            g3InitialDomains = CaptureG3Domains(process);
            var compile = new FunctionCompiler(environment).TryCompileRuntime(g3InitializeDefinition.File, g3InitializeDefinition.Function);
            g3BranchMatrix = new { Rows = new[] { -1L, 0, g3Maximum - 1, g3Maximum, -2 }.Select(arg =>
            {
                var legacy = LegacyG3Range(arg, g3Maximum); var candidate = CandidateG3Range(arg, g3Maximum);
                return new { Arg = arg, Legacy = legacy, Candidate = candidate, Pass = legacy == candidate };
            }).ToArray() };
            g3InitializePreflight = new
            {
                Pass = stateBefore == process.GetBenchmarkStateHash(),
                EffectCount = 0,
                Definition = G2DefinitionRow(g3InitializeDefinition),
                RuntimeCompile = new { Status = compile.Status.ToString(), Instructions = compile.Function?.Instructions.Length ?? 0 },
                DefaultArgument = new { Name = "ARG", Source = header.Groups[1].Value, Value = g3Arg, Storage = "PersistentBank" },
                DynamicDefaults = new[] { new { Name = "L_MIN", Source = "0" }, new { Name = "L_MAX", Source = "最大メッセージウィンドウ数" } },
                RangeSemantics = "Legacy VARSET evaluates end, then start, swaps if start>end, and writes [start,end)",
                Variables = g3Ranges.Select(x => new { x.SourceLine, x.Name, Type = x.IsString ? "String" : "Integer", x.Rhs, Length = x.Target.GetLength() }).ToArray(),
                BranchMatrix = g3BranchMatrix
            };
        }

        private void RunCandidateG3A(Process process)
        {
            G3Materialize(G3Initialize, 56_000);
            Enter(new(F4D5Handle(56_000, G3Initialize), 1033, "SYSTEM.ERB:1031"), 698, false);
            compactFrames[^1].ArgBank[0] = g3Arg;
            (g3LMin, g3LMax) = CandidateG3Range(g3Arg, g3Maximum);
            foreach (var descriptor in g3Ranges)
            {
                var initial = SnapshotG3Range(process, descriptor);
                if (descriptor.IsString)
                {
                    var value = descriptor.Source is null ? descriptor.StringLiteral : ReadG3StringScalar(process, descriptor.Source);
                    for (var i = g3LMin; i < g3LMax; i++) descriptor.Target.SetValue(value, [i]);
                }
                else
                {
                    var value = ReadG3IntScalar(process, descriptor.Source!);
                    for (var i = g3LMin; i < g3LMax; i++) descriptor.Target.SetValue(value, [i]);
                }
                g3LegacyVarsetOrder.Add(descriptor.SourceLine);
                AddG3Varset(process, descriptor, initial);
            }
            process.vEvaluator.RESULT = 0;
            Return(process, G3Initialize, false);
            g3InitializeReturned = true;
            StoppedBefore = "SYSTEM.ERB:1033 CALL SET_FLAG_USEABLE_TITLE";
            compactFrames[^1].Pc = 1033;
            g3T1 = CaptureG3Point(process, "T1", "SYSTEM.ERB:1033:before");
            PrepareG3SetFlag(process);
        }

        private void RunLegacyG3A(Process process)
        {
            var stopped = process.state.CurrentLine;
            var previous = (LogicalLine?)stopped?.ParentLabelLine;
            while (previous is not null && !ReferenceEquals(previous.NextLine, stopped)) previous = previous.NextLine;
            if (previous is null) throw new InvalidOperationException("R0-F4G3 Legacy resume predecessor");
            process.state.CurrentLine = previous;
            g3LegacyActive = true;
            try { process.runScriptProc(); throw new InvalidOperationException("R0-F4G3 Legacy crossed SYSTEM1033"); }
            catch (R0F1PlannedCheckpointException) { }
            finally { g3LegacyActive = false; }
            foreach (var descriptor in g3Ranges) AddG3Varset(process, descriptor, (object[])g3InitialDomains[descriptor.Name]);
        }

        internal void ObserveF4G3Entry(Process process, CalledFunction call)
        {
            if (!g3LegacyActive || !call.TopLabel.LabelName.Equals(G3Initialize, RuntimeConfig.StringComparison)) return;
            G3Materialize(G3Initialize, 56_000);
            Enter(new(F4D5Handle(56_000, G3Initialize), 1033, "SYSTEM.ERB:1031"), 698, false);
            if (call.TopLabel.Arg.Length != 1 || call.TopLabel.Arg[0].GetOperandType() != typeof(long))
                throw new InvalidOperationException("R0-F4G3 Legacy ARG schema");
            g3Arg = call.TopLabel.Arg[0].GetIntValue(process.exm);
        }

        internal void ObserveF4G3Instruction(Process process, InstructionLine line, bool before)
        {
            if (!g3LegacyActive || line.Position is not { } position) return;
            if (!before && Path.GetFileName(position.Filename).Equals(G3InitializeFile, StringComparison.OrdinalIgnoreCase)
                && position.LineNo == 720)
            {
                g3LMin = G3Private(process.state.CurrentCalled, "L_MIN", process);
                g3LMax = G3Private(process.state.CurrentCalled, "L_MAX", process);
                return;
            }
            if (!before) return;
            if (Path.GetFileName(position.Filename).Equals(G3InitializeFile, StringComparison.OrdinalIgnoreCase)
                && position.LineNo is >= 705 and <= 720)
                g3LegacyVarsetOrder.Add(position.LineNo);
            if (!IsF4D5Line(line, "SYSTEM.ERB", 1033)) return;
            if (!g3InitializeReturned && compactFrames.Count == 2 && compactFrames[^1].Handle.Name == G3Initialize)
            {
                process.vEvaluator.RESULT = 0;
                Return(process, G3Initialize, false);
                g3InitializeReturned = true;
            }
            if (!g3InitializeReturned || compactFrames.Count != 1 || compactFrames[0].Pc != 1033)
                throw new InvalidOperationException("R0-F4G3 Legacy INITIALIZE_MESSAGE return boundary");
            StoppedBefore = "SYSTEM.ERB:1033 CALL SET_FLAG_USEABLE_TITLE";
            g3T1 = CaptureG3Point(process, "T1", "SYSTEM.ERB:1033:before");
            PrepareG3SetFlag(process);
            throw new R0F1PlannedCheckpointException();
        }

        internal void ObserveF4G3Fallthrough(Process process, LogicalLine line, bool before)
        {
            if (!g3LegacyActive || process.state.functionCount == 0
                || !process.state.CurrentCalled.FunctionName.Equals(G3Initialize, RuntimeConfig.StringComparison)) return;
            if (before)
            {
                g3InitializeReturnPending = true;
                g3LMin = G3Private(process.state.CurrentCalled, "L_MIN", process);
                g3LMax = G3Private(process.state.CurrentCalled, "L_MAX", process);
                return;
            }
            if (!g3InitializeReturnPending) return;
            g3InitializeReturnPending = false;
            process.vEvaluator.RESULT = 0;
            Return(process, G3Initialize, false);
            g3InitializeReturned = true;
        }

        private void PrepareG3SetFlag(Process process)
        {
            var stateBefore = process.GetBenchmarkStateHash();
            var parent = G2Definition(G3SetFlag, 1232, "SYSTEM.ERB");
            var getUseable = G2Definition("GET_USEABLE_EXTRA_TITLE", 15, "作品管理/GET_USEABLE_EXTRA_TITLE.ERB");
            var targets = Enumerable.Range(0, 100).Select(index =>
            {
                var name = $"SET_FLAG_USEABLE_TITLE_{index}";
                functions.TryGetValue(name, out var values);
                var known = values is { Length: 1 } && !values[0].IsEvent;
                var definition = known ? values![0] : null;
                var body = definition is null ? [] : Executable(definition);
                var calls = body.SelectMany(FindG3Calls).Distinct(RuntimeConfig.StrComper).ToArray();
                var compile = definition is null ? null : new FunctionCompiler(environment).TryCompileRuntime(definition.File, definition.Function);
                return new
                {
                    Index = index,
                    Name = name,
                    Resolution = known ? "Known" : "KnownMissing",
                    Definition = definition is null ? null : G2DefinitionRow(definition),
                    RuntimeCompile = compile is null ? null : new { Status = compile.Status.ToString(), Instructions = compile.Function?.Instructions.Length ?? 0 },
                    Calls = calls,
                    ExistingAdmission = false
                };
            }).ToArray();
            var parentCompile = new FunctionCompiler(environment).TryCompileRuntime(parent.File, parent.Function);
            var getCompile = new FunctionCompiler(environment).TryCompileRuntime(getUseable.File, getUseable.Function);
            var knownTargets = targets.Where(x => x.Resolution == "Known").ToArray();
            g3SetFlagInventory = new
            {
                Pass = stateBefore == process.GetBenchmarkStateHash(),
                EffectCount = 0,
                ClosureReady = false,
                Reason = "CURRENT_COMPACT_ADMISSION_INCOMPLETE_BEFORE_EFFECT",
                Parent = G2DefinitionRow(parent),
                ParentCompile = new { Status = parentCompile.Status.ToString(), Instructions = parentCompile.Function?.Instructions.Length ?? 0 },
                ExpressionCallee = new { Definition = G2DefinitionRow(getUseable), RuntimeCompile = new { Status = getCompile.Status.ToString(), Instructions = getCompile.Function?.Instructions.Length ?? 0 }, ExistingAdmission = false },
                DynamicRange = new { Start = 0, EndExclusive = 100, Known = knownTargets.Length, Missing = targets.Length - knownTargets.Length },
                Targets = targets,
                Blockers = new[]
                {
                    "GET_USEABLE_EXTRA_TITLE function-expression call is not admitted by the current staged executor",
                    "Seven known TRY dynamic targets require a shared execution path for POS/CSTR/NO/GROUPMATCH/STRFIND and one nested user function",
                    "A dedicated SET_FLAG_USEABLE_TITLE C# handler is prohibited; the reusable closure must be admitted in a later slice"
                },
                Classification = "Incremental semantic/admission work; no new ownership, lifetime, frame, or fallback architecture required",
                PlannedStop = "SYSTEM.ERB:1033 before first effect",
                Materialized = 0,
                Executed = 0
            };
            if (stateBefore != process.GetBenchmarkStateHash()) throw new InvalidOperationException("R0-F4G3 G3-B cold preflight mutated state");
        }

        private static IEnumerable<string> FindG3Calls(string statement)
        {
            foreach (Match match in Regex.Matches(statement, "(?<![@A-Z0-9_])([A-Z_][A-Z0-9_ぁ-んァ-ヶ一-龠]*)\\s*\\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                yield return match.Groups[1].Value;
        }

        private static (long Min, long Max) LegacyG3Range(long arg, long max) => arg >= 0 && arg <= max - 1 ? (arg, arg + 1) : (0, max);
        private static (long Min, long Max) CandidateG3Range(long arg, long max) => arg >= 0 && arg < max ? (arg, arg + 1) : (0, max);

        private VariableToken RequireG3Token(Process process, string name, bool integer, bool writable)
        {
            var token = process.idDic.GetVariableToken(name, null, false)
                ?? throw new InvalidOperationException("R0-F4G3 token missing: " + name);
            if (integer != token.IsInteger || (writable && token.IsConst)) throw new InvalidOperationException("R0-F4G3 token schema: " + name);
            return token;
        }

        private Dictionary<string, object> CaptureG3Domains(Process process)
        {
            var result = new Dictionary<string, object>(RuntimeConfig.StrComper);
            foreach (var descriptor in g3Ranges) result.Add(descriptor.Name, SnapshotG3Range(process, descriptor));
            return result;
        }

        private object[] SnapshotG3Range(Process process, G3RangeWrite descriptor) => Enumerable.Range(0, descriptor.Target.GetLength())
            .Select(i => descriptor.IsString ? (object)(descriptor.Target.GetStrValue(process.exm, [i]) ?? "") : descriptor.Target.GetIntValue(process.exm, [i])).ToArray();

        private void AddG3Varset(Process process, G3RangeWrite descriptor, object[] initial)
        {
            var final = SnapshotG3Range(process, descriptor);
            var changed = Enumerable.Range(0, final.Length).Where(i => !Equals(initial[i], final[i])).ToArray();
            var unchanged = Enumerable.Range(0, final.Length).Where(i => Equals(initial[i], final[i])).ToArray();
            g3Varsets.Add(new(g3Varsets.Count + 1, descriptor.SourceLine, descriptor.Name, descriptor.IsString ? "String" : "Integer",
                descriptor.Rhs, g3LMin, g3LMax, checked((int)(g3LMax - g3LMin)), changed, unchanged,
                HashG3Values(initial), HashG3Values(final)));
        }

        private bool G3DomainsMatchExpected(Process process, Dictionary<string, object> final)
        {
            foreach (var descriptor in g3Ranges)
            {
                var initial = (object[])g3InitialDomains[descriptor.Name];
                var values = (object[])final[descriptor.Name];
                object expected = descriptor.IsString
                    ? descriptor.Source is null ? descriptor.StringLiteral : ReadG3StringScalar(process, descriptor.Source)
                    : ReadG3IntScalar(process, descriptor.Source!);
                for (var i = 0; i < values.Length; i++)
                    if (!Equals(values[i], i >= g3LMin && i < g3LMax ? expected : initial[i])) return false;
            }
            return true;
        }

        private static string HashG3Values(IEnumerable<object> values) => Hash(string.Join('\u001f', values.Select(x => x is string s ? "S:" + s : "I:" + Convert.ToString(x, CultureInfo.InvariantCulture))));
        private static string HashG3Domains(Dictionary<string, object> domains) => Hash(string.Join('\n', domains.Select(x => x.Key + ":" + HashG3Values((object[])x.Value))));
        private static long ReadG3IntScalar(Process process, VariableToken token) => token.GetIntValue(process.exm, token.Dimension == 0 ? [] : [0]);
        private static string ReadG3StringScalar(Process process, VariableToken token) => token.GetStrValue(process.exm, token.Dimension == 0 ? [] : [0]) ?? "";
        private G3Point CaptureG3Point(Process process, string name, string pc) => new(name, pc,
            string.Join(" > ", compactFrames.Select(FrameText)), EventCursor, process.state.SystemState.ToString(), process.state.isBegun,
            process.GetBenchmarkStateHash(), HashG3Domains(CaptureG3Domains(process)), process.vEvaluator.RESULT,
            HashStrings(process.vEvaluator.RESULTS_ARRAY), process.vEvaluator.GetR0C2RngHash(),
            process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount);
        private void G3Materialize(string name, int id) { if (g3Materialized.Add(name)) _ = F4D5Handle(id, name); DemandCompiledBodies = Math.Max(DemandCompiledBodies, g3Materialized.Count); }
        private static long G3Private(CalledFunction call, string name, Process process) => call.TopLabel.GetPrivateVariable(name)?.GetIntValue(process.exm, [0])
            ?? throw new InvalidOperationException("R0-F4G3 private bind: " + name);
    }
}
#endif
