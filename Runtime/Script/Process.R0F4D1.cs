#if R0_F4D1
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal object RunR0F4D1(bool candidate, string root)
    {
        r0f1 = new R0F1Context(candidate, Program.ErbDir,
            Path.GetDirectoryName(Program.CsvDir.TrimEnd(Path.DirectorySeparatorChar))!, this, "actual");
        return r0f1.RunF4D1(this, root);
    }

    internal void R0F4D1AfterLegacyArgumentsBound(CalledFunction call)
        => r0f1?.ObserveF4D1LegacyArgumentsBound(this, call);

    internal void R0F4D1AfterLegacyReturnF(CalledFunction call, SingleTerm value)
    {
        r0f1?.ObserveF4D1LegacyReturnF(this, call, value);
#if R0_F4G4
        r0f1?.ObserveF4G4ReturnF(this, call, value);
#endif
    }

    private sealed partial class R0F1Context
    {
        private const string GetEquipNumName = "GET_EQUIPNUM";
        private const string GetEquipNumPath = "関数/組み込み関数/係数とナンバリング/GET_EQUIPNUM.ERB";
        private const int GetEquipNumLine = 1;
        private const string GetEquipNumFileSha256 = "8A5E6E19E2A2B58133DC9DAAA4FAE4DE34ED61AE185AE0CBB80A17931FA85294";
        private static readonly string[] F4D1Targets =
        [
            "装備箇所_1500", "装備箇所_2500", "装備箇所_3000", "装備箇所_3500",
            "装備箇所_4000", "装備箇所_4500", "装備箇所_5000", "装備箇所_5500"
        ];

        private sealed record ClassADescriptor(string Name, string RelativePath, int Line,
            string SourceSha256, string BodySha256, string Argument, R0F1Definition Definition);
        private sealed record F4D1Point(string Name, string MethodArgs, long? TypedResult,
            long[] Result, string[] Results, int FrameDepth, string CallerBankSha256,
            int ArgumentProducerCalls, int FormalArgsWrites, int NormalResultWrites);
        private sealed record F4D1Case(string Name, string Argument, long Value,
            F4D1Point Q0, F4D1Point Q1, F4D1Point Q2, F4D1Point Q3,
            bool CallerBankUnchanged, int NormalReturns, bool Pass);
        private sealed record F4D1Binding(F4D1Runtime? Owner, int Generation, ClassADescriptor? Descriptor);
        private sealed record BindRequest(string Name, int Arity, string Kind, string SourceSha256,
            string BodySha256, string Schema, bool Static, bool Typed, bool Ref, bool UserOverride,
            bool Alias, bool Effectful, bool Dynamic, bool KnownBuiltin);
        private sealed record MatrixRow(string Case, bool Executed, string Observed,
            string ExpectedFromOracle, int ArgumentProducerCalls, int FormalArgsWrites,
            int ResultWrites, bool Pass);
        private sealed record FaultRow(string Case, bool Executed, string Observed,
            int ArgumentProducerCalls, int FormalArgsWrites, int ResultWrites,
            int LegacyRetry, bool TemporaryCleared, bool Pass);

        private Dictionary<string, ClassADescriptor> f4d1ClassA = null!;
        private CompactNormalHandle f4d1HostHandle;
        private readonly Dictionary<string, CompactNormalHandle> f4d1WrapperHandles = new(StringComparer.Ordinal);
        private bool f4d1HostActive;
        private bool f4d1LegacyProbe;
        private F4D1Point? f4d1LegacyQ1;
        private F4D1Point? f4d1LegacyQ2;
        private string f4d1LegacyCaller = "";
        private string f4d1LegacyCallerBefore = "";
        private int f4d1LegacyProducerCalls;
        private int f4d1LegacyFormalWrites;

        internal object RunF4D1(Process process, string root)
        {
            var inventory = Path.Combine(root, "evidence", "family-target-inventory.json");
            f4d1ClassA = AdmitClassA(inventory);
            if (f4d1ClassA.Count != 1163)
                throw new InvalidOperationException("R0-F4D1 Class A count mismatch");
            InitializeF4D1Frames();
            var startupCompiled = 0;
            var runtime = Candidate ? new F4D1Runtime(this, process) : null;
            if (Candidate && runtime!.CompiledProgramCount != 0)
                throw new InvalidOperationException("R0-F4D1 startup compiled program invariant");

            var cases = new List<F4D1Case>();
            foreach (var name in F4D1Targets)
            {
                var descriptor = f4d1ClassA[name];
                cases.Add(Candidate
                    ? RunCandidateWrapper(process, runtime!, descriptor)
                    : RunLegacyWrapper(process, descriptor));
            }

            var sequence = new[] { "剣", "足", "剣" };
            var repeated = sequence.Select(value =>
            {
                var result = Candidate
                    ? runtime!.InvokeMethodOnly(GetEquipNumName, value, 1_000_000)
                    : InvokeLegacyMethod(process, GetEquipNumName, value);
                return new { Input = value, Value = result.Value, PersistedArgs = result.Args,
                    ResultUnchanged = result.ResultUnchanged, ResultsUnchanged = result.ResultsUnchanged };
            }).ToArray();
            var invalid = new[] { "", "存在しない装備種別", "sword", "剱", "銃 ", "ＧＵＮ" }.Select(value =>
            {
                var result = Candidate
                    ? runtime!.InvokeMethodOnly(GetEquipNumName, value, 1_000_000)
                    : InvokeLegacyMethod(process, GetEquipNumName, value);
                return new { Input = value, Value = result.Value, PersistedArgs = result.Args,
                    ResultUnchanged = result.ResultUnchanged, ResultsUnchanged = result.ResultsUnchanged };
            }).ToArray();

            var synthetic = RunSyntheticConstantDataMatrix(process, runtime);
            var negatives = Candidate ? RunF4D1NegativeMatrix(process, runtime!) : [];
            var faults = Candidate ? RunF4D1FaultMatrix(process) : [];
            var temporaryReleased = Candidate && runtime!.VerifyTemporaryRootsReleased();
            var guards = R0E1AProof.GuardSnapshot();
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var allCasesPass = cases.All(row => row.Pass);
            var repeatedPass = repeated.Select(row => row.PersistedArgs).SequenceEqual(sequence)
                && repeated.All(row => row.ResultUnchanged && row.ResultsUnchanged);
            var invalidPass = invalid.All(row => row.PersistedArgs == row.Input
                && row.ResultUnchanged && row.ResultsUnchanged);
            var negativePass = !Candidate || negatives.All(row => row.Pass);
            var faultPass = !Candidate || faults.All(row => row.Pass);
            var runtimePass = !Candidate || runtime!.Attempts == runtime.Completed + runtime.Faults;
            var classAEntries = f4d1ClassA.Values.Select(row => new
            {
                row.Name, row.RelativePath, row.Line, row.SourceSha256, row.BodySha256,
                row.Argument, Semantic = "RETURN_STATIC_TYPED_GET_EQUIPNUM", Ready = true
            }).ToArray();

            return new
            {
                Schema = "emuera-r0f4d1-return-static-query-oracle-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "CLASS_A_NORMAL_RETURN_STATIC_QUERY_ONLY",
                Deterministic = new { Seed = Program.NextRuntimeDifferentialSeed,
                    ClockBase = Program.NextRuntimeDifferentialClockBase,
                    ClockStepMs = Program.NextRuntimeDifferentialClockStepMs },
                StartupCompiledPrograms = startupCompiled,
                ClassAAdmitted = f4d1ClassA.Count,
                ClassBAdmitted = 0,
                ClassCAdmitted = 0,
                Whole4500FamilyReady = false,
                ActualSetEquipVarExecuted = false,
                F4DScriptWrites = 0,
                Cases = cases,
                RepeatedArgumentSequence = new { Rows = repeated, Pass = repeatedPass },
                InvalidStringOracle = new { Rows = invalid, Pass = invalidPass },
                SyntheticConstantDataMatrix = synthetic.Value,
                NegativeMatrix = negatives,
                RuntimeFaultMatrix = faults,
                SharedProgram = Candidate ? runtime!.Census(f4d1ClassA.Count) : new
                {
                    PhysicalMaterializedDefinitions = 8, SharedNormalReturnTemplates = 1,
                    GetEquipNumPrograms = 1, Kind = "Legacy oracle graph; not compact census"
                },
                TemporaryCompilerRootsReleased = !Candidate || temporaryReleased,
                GraphGuards = guards,
                GraphGuardTotal = guardTotal,
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = false,
                NextRuntimeEnabled = Program.NextRuntimeMode,
                DifferentialCheckpointCapture = Program.NextRuntimeDifferentialCapturePath is not null,
                Result = allCasesPass && repeatedPass && invalidPass && synthetic.Pass
                    && negativePass && faultPass && runtimePass && guardTotal == 0
                    && (!Candidate || temporaryReleased) ? "PASS" : "FAIL",
                Inventory = new { Generated = 4500, Known = 1226, Missing = 3274,
                    ClassA = f4d1ClassA.Count, ClassB = 60, ClassC = 3,
                    ClassAEntries = classAEntries }
            };
        }

        private void InitializeF4D1Frames()
        {
            if (f4d1HostActive) return;
            f4d1HostHandle = Handle(10_000, "R0F4D1_ORACLE_HOST");
            foreach (var name in F4D1Targets)
                f4d1WrapperHandles.Add(name, Handle(10_001 + f4d1WrapperHandles.Count, name));
            if (!persistentBanks.ContainsKey(GetEquipNumName))
                persistentBanks.Add(GetEquipNumName, new PersistentBank());
            Enter(new(f4d1HostHandle, 0, "R0F4D1:host"), 0, dynamic: false);
            f4d1HostActive = true;
        }

        private F4D1Case RunCandidateWrapper(Process process, F4D1Runtime runtime, ClassADescriptor descriptor)
        {
            SetSentinels(process);
            var callerBefore = CandidateCallerBank(descriptor.Name);
            var q0 = Point(process, "Q0", "", null, compactFrames.Count, callerBefore,
                runtime.ArgumentProducerCalls, runtime.FormalArgsWrites, runtime.NormalResultWrites);
            var handle = f4d1WrapperHandles[descriptor.Name];
            Enter(new(handle, 1, descriptor.RelativePath + ":" + descriptor.Line), 1, dynamic: false);
            var binding = runtime.Bind(runtime.PositiveRequest(descriptor));
            var invocation = runtime.InvokeBound(binding, () => descriptor.Argument, 1_000_000);
            var q1 = invocation.Q1;
            var q2 = invocation.Q2;
            compactFrames[^1].EvalTemporary = invocation.Value;
            process.vEvaluator.SetResultX([invocation.Value]);
            runtime.NormalResultWrites++;
            Return(process, descriptor.Name, explicitReturn: true);
            var callerAfter = CandidateCallerBank(descriptor.Name);
            var q3 = Point(process, "Q3", runtime.PersistentArgs(GetEquipNumName), invocation.Value,
                compactFrames.Count, callerAfter, runtime.ArgumentProducerCalls,
                runtime.FormalArgsWrites, runtime.NormalResultWrites);
            var pass = q2.Result.SequenceEqual(q0.Result) && q2.Results.SequenceEqual(q0.Results)
                && q3.Result[0] == invocation.Value && q3.Result.Skip(1).SequenceEqual(q0.Result.Skip(1))
                && q3.Results.SequenceEqual(q0.Results) && callerBefore == callerAfter
                && q1.MethodArgs == descriptor.Argument && q2.MethodArgs == descriptor.Argument
                && compactFrames.Count == 1;
            return new(descriptor.Name, descriptor.Argument, invocation.Value,
                q0, q1, q2, q3, callerBefore == callerAfter, 1, pass);
        }

        private F4D1Case RunLegacyWrapper(Process process, ClassADescriptor descriptor)
        {
            SetSentinels(process);
            var label = process.labelDic.GetNonEventLabel(descriptor.Name)
                ?? throw new InvalidOperationException("Legacy wrapper missing: " + descriptor.Name);
            RequireLegacyIdentity(label, descriptor);
            f4d1LegacyCaller = descriptor.Name;
            f4d1LegacyCallerBefore = LegacyCallerBank(process, label);
            f4d1LegacyProducerCalls = 1;
            f4d1LegacyFormalWrites = 0;
            f4d1LegacyQ1 = null;
            f4d1LegacyQ2 = null;
            var q0 = Point(process, "Q0", "", null, process.state.functionCount,
                f4d1LegacyCallerBefore, 0, 0, 0);
            f4d1LegacyProbe = true;
            try
            {
                var call = CalledFunction.CallFunction(process, descriptor.Name, null);
                process.state.IntoFunction(call, null!, null!);
                process.runScriptProc();
            }
            finally { f4d1LegacyProbe = false; }
            if (f4d1LegacyQ1 is null || f4d1LegacyQ2 is null)
                throw new InvalidOperationException("Legacy Q1/Q2 not observed: " + descriptor.Name);
            var after = LegacyCallerBank(process, label);
            var q3 = Point(process, "Q3", f4d1LegacyQ2.MethodArgs, f4d1LegacyQ2.TypedResult,
                process.state.functionCount, after, f4d1LegacyProducerCalls,
                f4d1LegacyFormalWrites, 1);
            var pass = f4d1LegacyQ2.Result.SequenceEqual(q0.Result)
                && f4d1LegacyQ2.Results.SequenceEqual(q0.Results)
                && q3.Result[0] == f4d1LegacyQ2.TypedResult
                && q3.Result.Skip(1).SequenceEqual(q0.Result.Skip(1))
                && q3.Results.SequenceEqual(q0.Results)
                && f4d1LegacyCallerBefore == after && process.state.functionCount == 0;
            return new(descriptor.Name, descriptor.Argument, q3.Result[0],
                q0, f4d1LegacyQ1, f4d1LegacyQ2, q3,
                f4d1LegacyCallerBefore == after, 1, pass);
        }

        internal void ObserveF4D1LegacyArgumentsBound(Process process, CalledFunction call)
        {
#if R0_F4D4
            ObserveF4D4LegacyArgumentsBound(process, call);
#endif
            if (!f4d1LegacyProbe || !call.TopLabel.LabelName.Equals(GetEquipNumName, StringComparison.OrdinalIgnoreCase))
                return;
            f4d1LegacyFormalWrites++;
            var args = call.TopLabel.Arg[0].GetStrValue(process.exm);
            f4d1LegacyQ1 = Point(process, "Q1", args, null, process.state.functionCount,
                f4d1LegacyCallerBefore, f4d1LegacyProducerCalls, f4d1LegacyFormalWrites, 0);
        }

        internal void ObserveF4D1LegacyReturnF(Process process, CalledFunction call, SingleTerm value)
        {
#if R0_F4D4
            ObserveF4D4LegacyReturnF(process, call, value);
#endif
            if (!f4d1LegacyProbe || !call.TopLabel.LabelName.Equals(GetEquipNumName, StringComparison.OrdinalIgnoreCase))
                return;
            var args = call.TopLabel.Arg[0].GetStrValue(process.exm);
            f4d1LegacyQ2 = Point(process, "Q2", args, value.GetIntValue(process.exm),
                process.state.functionCount, f4d1LegacyCallerBefore,
                f4d1LegacyProducerCalls, f4d1LegacyFormalWrites, 0);
        }

        private (long Value, string Args, bool ResultUnchanged, bool ResultsUnchanged) InvokeLegacyMethod(
            Process process, string name, string argument)
        {
            var result = (long[])process.vEvaluator.RESULT_ARRAY.Clone();
            var results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            var term = process.idDic.GetFunctionMethod(process.labelDic, name,
                [SingleStrTerm.FromValue(argument)], userDefinedOnly: true);
            if (term is not SuperUserDefinedMethodTerm method)
                throw new InvalidOperationException("Legacy method binding failed: " + name);
            var value = method.GetIntValue(process.exm);
            var label = process.labelDic.GetNonEventLabel(name)!;
            return (value, label.Arg[0].GetStrValue(process.exm),
                result.SequenceEqual(process.vEvaluator.RESULT_ARRAY),
                results.SequenceEqual(process.vEvaluator.RESULTS_ARRAY));
        }

        private (object Value, bool Pass) RunSyntheticConstantDataMatrix(Process process, F4D1Runtime? runtime)
        {
            var constant = process.vEvaluator.Constant;
            var field = constant.GetType().GetField("nameToIntDics", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ConstantData dictionary field missing");
            var stores = ReferenceEquals(constant, GlobalStatic.ConstantData)
                ? new[] { (Owner: constant, Dictionaries: (Dictionary<string, int>[])field.GetValue(constant)!) }
                : new[]
                {
                    (Owner: constant, Dictionaries: (Dictionary<string, int>[])field.GetValue(constant)!),
                    (Owner: GlobalStatic.ConstantData,
                        Dictionaries: (Dictionary<string, int>[])field.GetValue(GlobalStatic.ConstantData)!)
                };
            var equipIndex = (int)(VariableCode.EQUIPNAME & VariableCode.__LOWERCASE__);
            var originals = stores.Select(value => value.Dictionaries[equipIndex]).ToArray();
            var rows = new List<object>();
            Dictionary<string, int> First(params (int Index, string Name)[] values)
            {
                var result = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var (index, name) in values)
                    if (!string.IsNullOrEmpty(name) && !result.ContainsKey(name)) result.Add(name, index);
                return result;
            }
            void Run(string name, Dictionary<string, int> fixture, string input)
            {
                foreach (var store in stores) store.Dictionaries[equipIndex] = fixture;
                var expected = constant.TryKeywordToInteger(out var left, VariableCode.EQUIP, input, -1) ? left : -1;
                var baseline = constant.TryKeywordToInteger(out var right, VariableCode.EQUIP, "剣", -1) ? right : -1;
                var observed = Candidate
                    ? runtime!.InvokeMethodOnly(GetEquipNumName, input, 1_000_000).Value
                    : (long)expected - baseline;
                rows.Add(new { Name = name, Input = input, Expected = (long)expected - baseline,
                    Observed = observed, Pass = observed == (long)expected - baseline });
            }
            try
            {
                Run("baseline key missing", First((11, "銃")), "銃");
                Run("duplicate keeps first ascending entry", First((10, "剣"), (11, "銃"), (99, "銃")), "銃");
                Run("case-sensitive variant", First((10, "剣"), (11, "銃")), "銃");
            }
            finally
            {
                for (var i = 0; i < stores.Length; i++)
                    stores[i].Dictionaries[equipIndex] = originals[i];
            }

            var directUnsupported = constant.TryKeywordToInteger(out var found,
                VariableCode.RESULT, "x", -1) ? found : -1;
            var defaultIndex = constant.TryKeywordToInteger(out var defaultEquip,
                VariableCode.EQUIP, "銃", -1) ? defaultEquip : -1;
            var firstIndex = constant.TryKeywordToInteger(out var equip,
                VariableCode.EQUIP, "銃", 1) ? equip : -1;
            rows.Add(new { Name = "unsupported dictionary", Input = "x",
                Expected = (long)-1, Observed = directUnsupported,
                Pass = directUnsupported == -1 });
            rows.Add(new { Name = "GETNUM admitted first-reference index", Input = "銃",
                Expected = (long)defaultIndex, Observed = (long)firstIndex,
                Pass = firstIndex == defaultIndex });
            var rejectedIndex = constant.TryKeywordToInteger(out var rejected,
                VariableCode.EQUIP, "銃", 0) ? rejected : -1;
            rows.Add(new { Name = "GETNUM rejects non-admitted first-reference index", Input = "銃",
                Expected = -1L, Observed = (long)rejectedIndex,
                Pass = rejectedIndex == -1 });
            var pass = rows.All(row => (bool)row.GetType().GetProperty("Pass")!.GetValue(row)!);
            return (new { Isolation = "in-memory dictionary swap restored in finally; Data files unchanged",
                Rows = rows, Pass = pass }, pass);
        }

        private MatrixRow[] RunF4D1NegativeMatrix(Process process, F4D1Runtime runtime)
        {
            var descriptor = f4d1ClassA[F4D1Targets[0]];
            var positive = runtime.PositiveRequest(descriptor);
            var cases = new (string Name, BindRequest Request)[]
            {
                ("wrong arity", positive with { Arity = 1 }),
                ("wrong kind", positive with { Kind = "Method" }),
                ("signature changed", positive with { Typed = false }),
                ("source fingerprint mismatch", positive with { SourceSha256 = new string('0', 64) }),
                ("body mismatch", positive with { BodySha256 = new string('0', 64) }),
                ("schema mismatch", positive with { Schema = "wrong" }),
                ("unresolved effective name", positive with { Name = "装備箇所_NOT_FOUND" }),
                ("alias unsupported", positive with { Alias = true }),
                ("REF unsupported", positive with { Ref = true }),
                ("user override unsupported", positive with { UserOverride = true }),
                ("dynamic expression target", positive with { Dynamic = true }),
                ("effectful expression function", positive with { Effectful = true }),
                ("unknown builtin", positive with { KnownBuiltin = false }),
                ("owner mismatch", positive)
            };
            var rows = new List<MatrixRow>();
            foreach (var item in cases)
            {
                var producerBefore = runtime.ArgumentProducerCalls;
                var formalBefore = runtime.FormalArgsWrites;
                var resultBefore = runtime.NormalResultWrites;
                var observed = "ACCEPTED";
                try
                {
                    _ = item.Name == "owner mismatch"
                        ? runtime.Bind(item.Request, new object())
                        : runtime.Bind(item.Request);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    observed = ex.GetType().Name + ": " + ex.Message;
                }
                var rejected = observed != "ACCEPTED";
                rows.Add(new(item.Name, true, observed, "PRE_EFFECT_REJECT",
                    runtime.ArgumentProducerCalls - producerBefore,
                    runtime.FormalArgsWrites - formalBefore,
                    runtime.NormalResultWrites - resultBefore,
                    rejected && runtime.ArgumentProducerCalls == producerBefore
                    && runtime.FormalArgsWrites == formalBefore
                    && runtime.NormalResultWrites == resultBefore));
            }
            return rows.ToArray();
        }

        private FaultRow[] RunF4D1FaultMatrix(Process process)
        {
            var descriptor = f4d1ClassA[F4D1Targets[0]];
            var rows = new List<FaultRow>();
            void Run(string name, Action<F4D1Runtime, F4D1Binding, Func<string>> action)
            {
                var runtime = new F4D1Runtime(this, process);
                var binding = runtime.Bind(runtime.PositiveRequest(descriptor));
                var producerCalls = 0;
                string Producer() { producerCalls++; return descriptor.Argument; }
                var formalBefore = runtime.FormalArgsWrites;
                var resultBefore = runtime.NormalResultWrites;
                var observed = "COMPLETED";
                try { action(runtime, binding, Producer); }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                { observed = ex.GetType().Name + ": " + ex.Message; }
                var formal = runtime.FormalArgsWrites - formalBefore;
                var writes = runtime.NormalResultWrites - resultBefore;
                var pass = observed != "COMPLETED" && runtime.LegacyRetry == 0 && writes == 0
                    && runtime.ArgumentTemporaryCleared;
                rows.Add(new(name, true, observed, producerCalls, formal, writes,
                    runtime.LegacyRetry, runtime.ArgumentTemporaryCleared, pass));
            }
            Run("owner revoked after argument evaluation",
                (runtime, binding, producer) => runtime.InvokeBound(binding, producer, 1_000_000, revokeAfterArgument: true));
            Run("fuel exhaustion",
                (runtime, binding, producer) => runtime.InvokeBound(binding, producer, 0));
            Run("depth exhaustion",
                (runtime, binding, producer) => runtime.InvokeBound(binding, producer, 1_000_000, depthLimit: 0));
            Run("argument evaluation throw",
                (runtime, binding, producer) => runtime.InvokeBound(binding,
                    () => throw new InvalidOperationException("injected argument fault"), 1_000_000));
            Run("method execution fault",
                (runtime, binding, producer) => runtime.InvokeBound(binding, producer,
                    1_000_000, wrongMethodArgumentType: true));
            return rows.ToArray();
        }

        private Dictionary<string, ClassADescriptor> AdmitClassA(string inventoryPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
            var result = new Dictionary<string, ClassADescriptor>(StringComparer.Ordinal);
            foreach (var row in document.RootElement.GetProperty("Entries").EnumerateArray())
            {
                if (!row.TryGetProperty("BodyKind", out var bodyKind)
                    || bodyKind.GetString() != "UnsupportedReturnGetEquipNum") continue;
                var name = row.GetProperty("Name").GetString()!;
                var path = row.GetProperty("RelativePath").GetString()!;
                var line = row.GetProperty("Line").GetInt32();
                var sourceSha = row.GetProperty("SourceSha256").GetString()!;
                var bodySha = row.GetProperty("BodySha256").GetString()!;
                if (!functions.TryGetValue(name, out var matches) || matches.Length != 1)
                    throw new InvalidOperationException("Class A source missing/ambiguous: " + name);
                var definition = matches[0];
                var executable = Executable(definition);
                if (!definition.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)
                    || definition.Line != line || FileHash(definition.File.FileIdentity) != sourceSha
                    || BodyHash(executable) != bodySha || executable.Length != 1)
                    throw new InvalidOperationException("Class A authority mismatch: " + name);
                var match = Regex.Match(executable[0],
                    "^RETURN GET_EQUIPNUM\\(\"([^\"]+)\"\\)$", RegexOptions.CultureInvariant);
                if (!match.Success || definition.IsEvent
                    || (definition.Function.Flags & (SourceIndexFlags.Preprocessor
                        | SourceIndexFlags.LineContinuation | SourceIndexFlags.OtherSemanticFallback)) != 0)
                    throw new InvalidOperationException("Class A semantic mismatch: " + name);
                var expanded = environment.Macros.Expand(executable[0], environment.Compatibility, out var substitutions);
                if (substitutions != 0 || expanded != executable[0])
                    throw new InvalidOperationException("Class A macro substitution: " + name);
                result.Add(name, new(name, path, line, sourceSha, bodySha, match.Groups[1].Value, definition));
            }
            return result;
        }

        private sealed class F4D1Runtime
        {
            private readonly R0F1Context owner;
            private readonly Process process;
            private readonly object identity = new();
            private readonly int generation = 1;
            private bool active = true;
            private readonly Dictionary<string, (QueryProgram[] Programs, int Id, FlatQueryContext Context)> methods =
                new(StringComparer.OrdinalIgnoreCase);
            private readonly List<WeakReference> temporaryRoots = [];
            private string? argumentTemporary;
            internal int ArgumentProducerCalls;
            internal int FormalArgsWrites;
            internal int NormalResultWrites;
            internal int LegacyRetry;
            internal long Attempts;
            internal long Completed;
            internal long Faults;
            internal int CompiledProgramCount => methods.Values.Sum(value => value.Programs.Length);
            internal bool ArgumentTemporaryCleared => argumentTemporary is null;

            internal F4D1Runtime(R0F1Context owner, Process process)
                => (this.owner, this.process) = (owner, process);

            internal BindRequest PositiveRequest(ClassADescriptor descriptor) =>
                new(descriptor.Name, 0, "Normal", descriptor.SourceSha256,
                    descriptor.BodySha256, "F4D1_CLASS_A_V1", true, true,
                    false, false, false, false, false, true);

            internal F4D1Binding Bind(BindRequest request, object? expectedOwner = null)
            {
                if (expectedOwner is not null && !ReferenceEquals(expectedOwner, identity))
                    throw new InvalidOperationException("F4D1 bind FAIL: owner mismatch");
                RequireLive();
                if (!request.Static || request.Dynamic) throw new NotSupportedException("F4D1 bind FAIL: dynamic target");
                if (!request.Typed) throw new NotSupportedException("F4D1 bind FAIL: typed signature");
                if (request.Ref) throw new NotSupportedException("F4D1 bind FAIL: REF");
                if (request.UserOverride) throw new NotSupportedException("F4D1 bind FAIL: user override");
                if (request.Alias) throw new NotSupportedException("F4D1 bind FAIL: external alias");
                if (request.Effectful) throw new NotSupportedException("F4D1 bind FAIL: effectful expression");
                if (!request.KnownBuiltin) throw new NotSupportedException("F4D1 bind FAIL: unknown builtin");
                if (request.Arity != 0) throw new InvalidOperationException("F4D1 bind FAIL: wrapper arity");
                if (request.Kind != "Normal") throw new InvalidOperationException("F4D1 bind FAIL: wrong kind");
                if (request.Schema != "F4D1_CLASS_A_V1") throw new InvalidOperationException("F4D1 bind FAIL: schema");
                if (!owner.f4d1ClassA.TryGetValue(request.Name, out var descriptor))
                    throw new InvalidOperationException("F4D1 bind FAIL: unresolved effective name");
                if (request.SourceSha256 != descriptor.SourceSha256)
                    throw new InvalidOperationException("F4D1 bind FAIL: source fingerprint");
                if (request.BodySha256 != descriptor.BodySha256)
                    throw new InvalidOperationException("F4D1 bind FAIL: body fingerprint");
                return new(this, generation, descriptor);
            }

            internal (long Value, F4D1Point Q1, F4D1Point Q2) InvokeBound(F4D1Binding binding,
                Func<string> producer, int stepLimit, bool revokeAfterArgument = false,
                int? depthLimit = null, bool wrongMethodArgumentType = false)
            {
                Require(binding);
                Attempts++;
                try
                {
                    ArgumentProducerCalls++;
                    argumentTemporary = producer();
                    if (revokeAfterArgument) active = false;
                    Require(binding);
                    var bank = owner.EnsureF4D1Bank(GetEquipNumName);
                    bank.Args[0] = argumentTemporary;
                    FormalArgsWrites++;
                    var caller = owner.CandidateCallerBank(binding.Descriptor!.Name);
                    var q1 = owner.Point(process, "Q1", bank.Args[0], null,
                        owner.compactFrames.Count, caller, ArgumentProducerCalls,
                        FormalArgsWrites, NormalResultWrites);
                    var method = Materialize(GetEquipNumName);
                    var context = depthLimit.HasValue
                        ? new FlatQueryContext(method.Programs, depthLimit.Value)
                        : method.Context;
                    var argument = wrongMethodArgumentType
                        ? QueryValue.I(1) : QueryValue.S(bank.Args[0]);
                    var value = context.Execute(method.Id, [argument], stepLimit);
                    var q2 = owner.Point(process, "Q2", bank.Args[0], value.Integer,
                        owner.compactFrames.Count, caller, ArgumentProducerCalls,
                        FormalArgsWrites, NormalResultWrites);
                    Completed++;
                    return (value.Integer, q1, q2);
                }
                catch { Faults++; throw; }
                finally { argumentTemporary = null; }
            }

            internal (long Value, string Args, bool ResultUnchanged, bool ResultsUnchanged)
                InvokeMethodOnly(string name, string argument, int stepLimit)
            {
                RequireLive();
                var result = (long[])process.vEvaluator.RESULT_ARRAY.Clone();
                var results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
                ArgumentProducerCalls++;
                argumentTemporary = argument;
                try
                {
                    var bank = owner.EnsureF4D1Bank(name);
                    bank.Args[0] = argumentTemporary;
                    FormalArgsWrites++;
                    var method = Materialize(name);
                    Attempts++;
                    try
                    {
                        var value = method.Context.Execute(method.Id, [QueryValue.S(bank.Args[0])], stepLimit);
                        Completed++;
                        return (value.Integer, bank.Args[0],
                            result.SequenceEqual(process.vEvaluator.RESULT_ARRAY),
                            results.SequenceEqual(process.vEvaluator.RESULTS_ARRAY));
                    }
                    catch { Faults++; throw; }
                }
                finally { argumentTemporary = null; }
            }

            private (QueryProgram[] Programs, int Id, FlatQueryContext Context) Materialize(string name)
            {
                if (methods.TryGetValue(name, out var existing)) return existing;
                if (!owner.functions.TryGetValue(name, out var matches) || matches.Length != 1)
                    throw new InvalidOperationException("F4D1 method missing/ambiguous: " + name);
                var definition = matches[0];
                if (name == GetEquipNumName)
                {
                    if (definition.RelativePath != GetEquipNumPath || definition.Line != GetEquipNumLine
                        || FileHash(definition.File.FileIdentity) != GetEquipNumFileSha256
                        || owner.Executable(definition) is not ["#FUNCTION",
                            "RETURNF GETNUM(EQUIP,ARGS) - GETNUM(EQUIP,\"剣\")"])
                        throw new InvalidOperationException("GET_EQUIPNUM source authority mismatch");
                    if (process.idDic.GetRefMethod(name) is not null
                        || FunctionMethodCreator.GetMethodList().ContainsKey(name))
                        throw new InvalidOperationException("GET_EQUIPNUM effective identity collision");
                }
                var key = B1HotMap.Key(definition.RelativePath, definition.Line, definition.Name);
                var entry = new B1Function(definition.File, definition.Function, key, []);
                var compiler = new FunctionCompiler(owner.environment);
                void Analyze(B1Function value)
                {
                    var read = FunctionSourceReader.Read(value.File, value.Function);
                    if (read.Status != SourceReadStatus.Read)
                        throw new InvalidOperationException("F4D1 source changed: " + value.Function.Name);
                    value.Source = read.Source!.Value;
                    value.Compile = compiler.TryCompileRuntime(value.Source.Value);
                    if (value.Compile.Status != CompileStatus.Compiled)
                        throw new NotSupportedException("F4D1 compiler: " + value.Compile.Detail);
                    try
                    {
                        value.Flat = FlatMethodProgram.Compile(value.Source.Value,
                            value.Compile.Function!, owner.environment);
                    }
                    catch (Exception ex) when (ex is NotSupportedException or FormatException or CodeEE)
                    {
                        value.FlatBlocker = ex.Message;
                    }
                    temporaryRoots.Add(new(value.Source.Value.Bytes));
                }
                var catalog = new FlatQueryCatalog(
                    new Dictionary<string, B1Function>(StringComparer.Ordinal) { [key] = entry },
                    owner.environment, Analyze, new(owner.environment.Compatibility.NameComparer));
                var id = catalog.Link(key);
                var programs = catalog.Snapshot();
                var program = programs[id];
                if (!program.ReturnsString && program.Parameters.Length == 1
                    && program.Parameters[0].String && program.Builtins.All(b => b.Id == PureQueryId.GetNum))
                {
                    var created = (programs, id, new FlatQueryContext(programs));
                    methods.Add(name, created);
                    temporaryRoots.Add(new(compiler));
                    temporaryRoots.Add(new(entry));
                    temporaryRoots.Add(new(catalog));
                    return created;
                }
                throw new InvalidOperationException("F4D1 typed method shape mismatch: " + name);
            }

            internal string PersistentArgs(string name) => owner.EnsureF4D1Bank(name).Args[0];
            internal object Census(int admitted) => new
            {
                PhysicalAdmittedDefinitions = admitted,
                PhysicalMaterializedDefinitions = F4D1Targets.Length,
                SharedNormalReturnTemplates = 1,
                SharedTemplateReferences = F4D1Targets.Length,
                GetEquipNumPrograms = methods.ContainsKey(GetEquipNumName) ? 1 : 0,
                SyntheticFixturePrograms = methods.Count - (methods.ContainsKey(GetEquipNumName) ? 1 : 0),
                CompiledProgramCount,
                DescriptorEstimateBytes = admitted * 128L,
                SharedProgramEstimateBytes = methods.Values.Sum(value =>
                    value.Programs.Sum(program => program.Code.Length * 24L
                        + program.Text.Sum(text => text.Length * 2L))),
                ProcessMemoryBenchmark = false
            };

            internal bool VerifyTemporaryRootsReleased()
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                return temporaryRoots.All(reference => !reference.IsAlive);
            }

            private void Require(F4D1Binding binding)
            {
                RequireLive();
                if (!ReferenceEquals(binding.Owner, this) || binding.Generation != generation
                    || binding.Descriptor is null)
                    throw new InvalidOperationException("F4D1 explicit FAIL: owner/generation");
            }
            private void RequireLive()
            {
                if (!active) throw new InvalidOperationException("F4D1 explicit FAIL: owner revoked");
            }
        }

        private PersistentBank EnsureF4D1Bank(string name)
        {
            if (!persistentBanks.TryGetValue(name, out var bank))
            {
                bank = new();
                persistentBanks.Add(name, bank);
            }
            return bank;
        }

        private F4D1Point Point(Process process, string name, string args, long? typed,
            int depth, string caller, int producers, int formals, int resultWrites) =>
            new(name, args, typed, (long[])process.vEvaluator.RESULT_ARRAY.Clone(),
                (string[])process.vEvaluator.RESULTS_ARRAY.Clone(), depth, caller,
                producers, formals, resultWrites);

        private static void SetSentinels(Process process)
        {
            for (var i = 0; i < process.vEvaluator.RESULT_ARRAY.Length; i++)
                process.vEvaluator.RESULT_ARRAY[i] = 91_000 + i;
            for (var i = 0; i < process.vEvaluator.RESULTS_ARRAY.Length; i++)
                process.vEvaluator.RESULTS_ARRAY[i] = "R0F4D1_RESULTS_" + i;
        }

        private string CandidateCallerBank(string name)
        {
            var bank = EnsureF4D1Bank(name);
            return HashText(string.Join(',', bank.Arg) + "\n" + string.Join('\u001f', bank.Args)
                + "\n" + string.Join(',', bank.Local));
        }

        private string LegacyCallerBank(Process process, FunctionLabelLine label)
        {
            var rows = new List<string>();
            foreach (var family in new[] { "ARG", "ARGS", "LOCAL", "LOCALS" })
            {
                var token = process.idDic.GetNextRuntimeLocalVariableToken(family, label);
                if (token?.GetArray() is Array array)
                    rows.Add(family + ":" + string.Join("\u001f", array.Cast<object?>()
                        .Select(value => value?.ToString() ?? "")));
            }
            return HashText(string.Join("\n", rows));
        }

        private static void RequireLegacyIdentity(FunctionLabelLine label, ClassADescriptor descriptor)
        {
            if (label.IsMethod || label.Position is not { } position
                || !position.Filename.Replace('\\', '/').EndsWith(descriptor.RelativePath,
                    StringComparison.OrdinalIgnoreCase)
                || position.LineNo != descriptor.Line)
                throw new InvalidOperationException("Legacy wrapper identity mismatch: " + descriptor.Name);
        }

        private string[] Executable(R0F1Definition definition) => Lines(Read(definition.Function, definition.File))
            .Skip(1).Select(line => line.Trim())
            .Where(line => line.Length != 0 && !line.StartsWith(';')).ToArray();

        private static string BodyHash(IEnumerable<string> lines) => HashText(string.Join("\n", lines));
        private static string HashText(string text) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
#endif
