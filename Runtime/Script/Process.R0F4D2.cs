#if R0_F4D2
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal object RunR0F4D2(bool candidate, string root)
    {
        r0f1 = new R0F1Context(candidate, Program.ErbDir,
            Path.GetDirectoryName(Program.CsvDir.TrimEnd(Path.DirectorySeparatorChar))!, this, "actual");
        return r0f1.RunF4D2(this, root);
    }

    internal void R0F4D2AfterLegacyArgumentsBound(CalledFunction call)
        => r0f1?.ObserveF4D2LegacyEntry(this, call);

    private sealed partial class R0F1Context
    {
        private const string F4D2HelperName = "装備箇所_改造装備";
        private const string F4D2HelperPath = "ＳＨＯＰ関連/117_ドワーフの鍛冶屋.ERB";
        private const string F4D2SavedName = "ベース装備番号";
        private const string F4D2Schema = "F4D2_CLASS_B_V1";
        private static readonly int[] F4D2RepresentativeArguments = [0, 9, 10, 29, 30, 49, 59];
        private static readonly long[] F4D2RepresentativeTargets = [1500, 2500, 3000, 3500, 4000, 5000, 5500];

        private sealed record ClassBDescriptor(string Name, string RelativePath, int Line,
            string SourceSha256, string BodySha256, int Argument, string Callee, R0F1Definition Definition);
        private sealed record HelperDescriptor(string Name, string RelativePath, int Line,
            string SourceSha256, string BodySha256, R0F1Definition Definition);
        private sealed record F4D2BindRequest(string Name, int Arity, string Kind, string SourceSha256,
            string BodySha256, string Schema, bool Static, bool Effectful);
        private sealed record F4D2Binding(F4D2Runtime? Owner, int Generation, ClassBDescriptor? Descriptor);
        private sealed record F4D2Point(string Name, long HelperArg, long SavedValue, string GeneratedTarget,
            string Resolution, string[] Stack, string GetEquipNumArgs, long[] Result, string[] Results,
            int ArgumentEvaluations, int ArgWrites, int SavedReads, int ResultWrites, int Returns);
        private sealed record F4D2Case(string Wrapper, int Argument, long SavedValue, string GeneratedTarget,
            string Resolution, long Value, bool WriteIntent, int InnerExecutions, F4D2Point Before,
            F4D2Point After, string WrapperBankBefore, string WrapperBankAfter, bool Pass);
        private sealed record F4D2MatrixRow(string Case, string Observed, string Expected,
            int ArgumentEvaluations, int ArgWrites, int ResultWrites, int Returns, int LegacyRetry, bool Pass);
        private sealed record F4D2FaultRow(string Case, string Observed, int ArgumentEvaluations,
            int ArgWrites, int ResultWrites, int Returns, long CommittedArg, long CommittedResult0,
            int LegacyRetry, bool TemporaryCleared, bool Pass);
        private sealed record F4D2ResolverRow(string Name, string Target, string Observed, string Expected, bool Pass);
        private readonly record struct ClosureState(string PhysicalDefinition, long BoundFormal);
        private enum ClosureDisposition { KnownReady, KnownMissing, WrongKind, Unresolved, Blocked, Cycle, DepthExceeded }
        private enum F4D2Fault { None, RevokeAfterArgStore, SavedRead, Resolver, InnerExecution }

        private sealed record BoundSavedIntArray(string Name, VariableToken Token, int Length)
        {
            internal long Read(Process process, long index)
            {
                if (index < 0 || index >= Length) throw new IndexOutOfRangeException($"{Name}:{index}");
                return Token.GetIntValue(process.exm, [index]);
            }
            internal void Write(long value, long index)
            {
                if (index < 0 || index >= Length) throw new IndexOutOfRangeException($"{Name}:{index}");
                Token.SetValue(value, [index]);
            }
        }

        private Dictionary<string, ClassBDescriptor> f4d2ClassB = null!;
        private HelperDescriptor f4d2Helper = null!;
        private BoundSavedIntArray f4d2Saved = null!;
        private CompactNormalHandle f4d2HelperHandle;
        private readonly Dictionary<string, CompactNormalHandle> f4d2WrapperHandles = new(StringComparer.Ordinal);
        private bool f4d2LegacyProbe;
        private readonly List<string> f4d2LegacyStack = [];
        private int f4d2LegacyInnerExecutions;

        internal object RunF4D2(Process process, string root)
        {
            f4d1ClassA = AdmitClassA(Path.Combine(root, "evidence", "family-target-inventory.json"));
            if (f4d1ClassA.Count != 1163) throw new InvalidOperationException("R0-F4D2 Class A authority mismatch");
            f4d2ClassB = AdmitClassB(Path.Combine(root, "evidence", "class-b-authority.json"));
            f4d2Helper = AdmitF4D2Helper();
            f4d2Saved = BindF4D2Saved(process, F4D2SavedName, 60);
            InitializeF4D2Frames();

            var runtime = Candidate ? new F4D2Runtime(this, process, f4d2Saved) : null;
            var startupCompiled = Candidate ? runtime!.CompiledPrograms : 0;
            if (startupCompiled != 0) throw new InvalidOperationException("R0-F4D2 startup compiled invariant");

            var cases = new List<F4D2Case>();
            for (var i = 0; i < F4D2RepresentativeArguments.Length; i++)
            {
                var descriptor = f4d2ClassB.Values.Single(value => value.Argument == F4D2RepresentativeArguments[i]);
                f4d2Saved.Write(F4D2RepresentativeTargets[i], descriptor.Argument);
                cases.Add(Candidate
                    ? runtime!.Invoke(descriptor, () => descriptor.Argument)
                    : RunLegacyF4D2(process, descriptor));
            }

            var missingNumber = FindF4D2MissingNumber();
            var missingDescriptor = f4d2ClassB.Values.Single(value => value.Argument == 9);
            f4d2Saved.Write(missingNumber, missingDescriptor.Argument);
            var missingCase = Candidate
                ? runtime!.Invoke(missingDescriptor, () => missingDescriptor.Argument)
                : RunLegacyF4D2(process, missingDescriptor);

            var repeatedRows = new List<F4D2Case>();
            foreach (var (argument, target) in new[] { (0, 1500L), (59, 5500L), (0, 1500L) })
            {
                var descriptor = f4d2ClassB.Values.Single(value => value.Argument == argument);
                f4d2Saved.Write(target, argument);
                repeatedRows.Add(Candidate
                    ? runtime!.Invoke(descriptor, () => descriptor.Argument)
                    : RunLegacyF4D2(process, descriptor));
            }
            var helperArg = ReadPersistentArg(process);
            var repeatedPass = repeatedRows.All(value => value.Pass) && helperArg == 0;

            var outsideKnown = FindF4D2OutsideKnown();
            var outsideMatrix = new[]
            {
                ResolveEvidence("outside-range known", "装備箇所_" + outsideKnown.ToString(CultureInfo.InvariantCulture), "Blocked"),
                ResolveEvidence("outside-range missing", "装備箇所_" + missingNumber.ToString(CultureInfo.InvariantCulture), "KnownMissing")
            };
            var outerVsInner = new
            {
                OuterMissing = new { Resolution="KnownMissing", WriteIntent=false, Value=(long?)null },
                OuterKnownInnerMissing = new { Resolution=missingCase.Resolution, WriteIntent=missingCase.WriteIntent, Value=missingCase.Value },
                Pass = missingCase.Resolution == "KnownMissing" && missingCase.WriteIntent && missingCase.Value == 0
            };

            var closure = RunF4D2ClosureMatrix(outsideKnown, missingNumber);
            var negatives = Candidate ? RunF4D2NegativeMatrix(process, runtime!, outsideKnown) : [];
            var faults = Candidate ? RunF4D2FaultMatrix(process) : [];
            var guardSnapshot = R0E1AProof.GuardSnapshot();
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var casesPass = cases.All(value => value.Pass);
            var negativePass = !Candidate || negatives.All(value => value.Pass);
            var faultPass = !Candidate || faults.All(value => value.Pass);
            var closurePass = closure.AllPassed;
            var inventoryRows = f4d2ClassB.Values.OrderBy(value => value.Argument).Select(value => new
            {
                value.Name, value.RelativePath, value.Line, value.SourceSha256, value.BodySha256,
                LiteralArgument=value.Argument, StaticCallee=value.Callee, BodyShape="CALL_STATIC_INT;RETURN_RESULT",
                SharedTemplate="F4D2_WRAPPER_V1", SourceVerified=true
            }).ToArray();
            var pass = f4d2ClassB.Count == 60 && casesPass && missingCase.Pass && repeatedPass
                && outsideMatrix.All(value => value.Pass) && outerVsInner.Pass && closurePass
                && negativePass && faultPass && guardTotal == 0;

            return new
            {
                Schema="emuera-r0f4d2-class-b-semantic-oracle-v1",
                Mode=Candidate?"GraphFreeCandidate":"LegacyControl",
                Scope="CLASS_B_HELPER_SEMANTIC_ONLY",
                StartupCompiledPrograms=startupCompiled,
                ClassAAdmitted=f4d1ClassA.Count,
                ClassBWrapperSourceCensus=f4d2ClassB.Count,
                ClassBWrapperSemanticReady=pass?f4d2ClassB.Count:0,
                ClassBActualClosureReady=false,
                ClassCAdmitted=0,
                Whole4500FamilyReady=false,
                ActualSetEquipVarExecuted=false,
                F4DScriptWrites=0,
                WrapperInventory=inventoryRows,
                Helper=new { f4d2Helper.Name, f4d2Helper.RelativePath, f4d2Helper.Line,
                    f4d2Helper.SourceSha256, f4d2Helper.BodySha256, SourceVerified=true },
                SavedBinding=new { f4d2Saved.Name, f4d2Saved.Length, IsSavedata=f4d2Saved.Token.IsSavedata,
                    IsInteger=f4d2Saved.Token.IsInteger, f4d2Saved.Token.Dimension, Authority="VariableEvaluator token" },
                Cases=cases,
                InnerMissing=missingCase,
                RepeatedPersistentArg=new { Sequence=new[]{0,59,0}, Rows=repeatedRows, FinalHelperArg=helperArg, Pass=repeatedPass },
                OuterRangeResolution=outsideMatrix,
                OuterMissingVsInnerMissing=outerVsInner,
                ClosureCertificateMatrix=closure,
                NegativeMatrix=negatives,
                RuntimeFaultMatrix=faults,
                SharedProgram=Candidate?runtime!.Census():new { Kind="Legacy oracle graph; not compact census" },
                GraphGuards=guardSnapshot,
                GraphGuardTotal=guardTotal,
                LegacyErbGraphAvoided=Candidate,
                LegacyRetryAfterCompact=0,
                ProductionBridgeUsed=false,
                NextRuntimeEnabled=Program.NextRuntimeMode,
                DifferentialCheckpointCapture=Program.NextRuntimeDifferentialCapturePath is not null,
                Result=pass?"PASS":"FAIL"
            };
        }

        private Dictionary<string, ClassBDescriptor> AdmitClassB(string authorityPath)
        {
            using var authority = JsonDocument.Parse(File.ReadAllBytes(authorityPath));
            var expected = authority.RootElement.GetProperty("Wrappers").EnumerateArray()
                .ToDictionary(row => row.GetProperty("Name").GetString()!, row => row, StringComparer.Ordinal);
            var result = new Dictionary<string, ClassBDescriptor>(StringComparer.Ordinal);
            foreach (var row in expected.Values)
            {
                var name = row.GetProperty("Name").GetString()!;
                var path = row.GetProperty("Path").GetString()!;
                var line = row.GetProperty("Line").GetInt32();
                var callee = row.GetProperty("Callee").GetString()!;
                var argument = row.GetProperty("Argument").GetInt32();
                if (!functions.TryGetValue(name, out var matches) || matches.Length != 1)
                    throw new InvalidOperationException("F4D2 wrapper missing/ambiguous: " + name);
                var definition = matches[0];
                var body = Executable(definition);
                var match = body.Length == 2 ? Regex.Match(body[0],
                    "^CALL\\s+([^,]+),\\s*(-?[0-9]+)$", RegexOptions.CultureInvariant) : Match.Empty;
                if (!match.Success || !body[1].Equals("RETURN RESULT", StringComparison.Ordinal)
                    || !match.Groups[1].Value.Equals(callee, StringComparison.Ordinal)
                    || !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    || parsed != argument || definition.IsEvent || definition.RelativePath != path || definition.Line != line)
                    throw new InvalidOperationException("F4D2 wrapper authority/body mismatch: " + name);
                result.Add(name, new(name, definition.RelativePath, definition.Line,
                    FileHash(definition.File.FileIdentity)!, BodyHash(body), argument, callee, definition));
            }
            if (result.Count != 60 || !result.Values.Select(value => value.Argument).Order().SequenceEqual(Enumerable.Range(0, 60))
                || result.Values.Any(value => value.Callee != F4D2HelperName))
                throw new InvalidOperationException("F4D2 wrapper census mismatch");
            return result;
        }

        private HelperDescriptor AdmitF4D2Helper()
        {
            if (!functions.TryGetValue(F4D2HelperName, out var matches) || matches.Length != 1)
                throw new InvalidOperationException("F4D2 helper missing/ambiguous");
            var definition = matches[0];
            var body = Executable(definition);
            var expected = new[] { "RESULT = 0", "TRYCALLFORM 装備箇所_{ベース装備番号:ARG}", "RETURN RESULT" };
            if (definition.IsEvent || definition.RelativePath != F4D2HelperPath || definition.Line != 2871
                || !body.SequenceEqual(expected))
                throw new InvalidOperationException("F4D2 helper authority/body mismatch");
            return new(F4D2HelperName, definition.RelativePath, definition.Line,
                FileHash(definition.File.FileIdentity)!, BodyHash(body), definition);
        }

        private static BoundSavedIntArray BindF4D2Saved(Process process, string name, int expectedLength)
        {
            var token = process.idDic.GetVariableToken(name, null, false);
            if (token is null || !token.IsInteger || !token.IsSavedata || token.IsPrivate
                || token.Dimension != 1 || token.GetLength() != expectedLength)
                throw new InvalidOperationException("F4D2 SAVEDATA schema mismatch: " + name);
            return new(name, token, expectedLength);
        }

        private void InitializeF4D2Frames()
        {
            InitializeF4D1Frames();
            f4d2HelperHandle = Handle(20_000, F4D2HelperName);
            foreach (var descriptor in f4d2ClassB.Values.OrderBy(value => value.Argument))
                f4d2WrapperHandles.Add(descriptor.Name, Handle(20_001 + descriptor.Argument, descriptor.Name));
        }

        private F4D2Case RunLegacyF4D2(Process process, ClassBDescriptor descriptor)
        {
            SetSentinels(process);
            f4d2LegacyStack.Clear();
            f4d2LegacyInnerExecutions = 0;
            var wrapperLabel = process.labelDic.GetNonEventLabel(descriptor.Name)
                ?? throw new InvalidOperationException("F4D2 Legacy wrapper missing: " + descriptor.Name);
            var bankBefore = LegacyCallerBank(process, wrapperLabel);
            var saved = f4d2Saved.Read(process, descriptor.Argument);
            var generated = F4D2TargetName(saved);
            var resolution = F4D2Resolution(generated);
            var before = CaptureF4D2Point(process, "Before", descriptor.Argument, saved, generated, resolution,
                [], "", 0, 0, 0, 0, 0);
            f4d2LegacyProbe = true;
            try
            {
                var call = CalledFunction.CallFunction(process, descriptor.Name, null);
                process.state.IntoFunction(call, null!, null!);
                process.runScriptProc();
            }
            finally { f4d2LegacyProbe = false; }
            var helperArg = ReadPersistentArg(process);
            var after = CaptureF4D2Point(process, "After", helperArg, saved, generated, resolution,
                f4d2LegacyStack.ToArray(), ReadLegacyPersistentArgs(process), 1, 1, 1,
                resolution == "KnownReady" ? 3 : 2, 2 + (resolution == "KnownReady" ? 1 : 0));
            var bankAfter = LegacyCallerBank(process, wrapperLabel);
            var value = process.vEvaluator.RESULT;
            var expected = ExpectedF4D2Value(process, resolution, generated);
            var pass = value == expected && helperArg == descriptor.Argument && bankBefore == bankAfter
                && process.vEvaluator.RESULT_ARRAY.Skip(1).SequenceEqual(before.Result.Skip(1))
                && process.vEvaluator.RESULTS_ARRAY.SequenceEqual(before.Results)
                && (resolution == "KnownReady" ? f4d2LegacyInnerExecutions == 1 : f4d2LegacyInnerExecutions == 0);
            return new(descriptor.Name, descriptor.Argument, saved, generated, resolution, value, true,
                f4d2LegacyInnerExecutions, before, after, bankBefore, bankAfter, pass);
        }

        internal void ObserveF4D2LegacyEntry(Process process, CalledFunction call)
        {
            if (!f4d2LegacyProbe) return;
            var name = call.TopLabel.LabelName;
            if (name == F4D2HelperName || f4d2ClassB.ContainsKey(name) || f4d1ClassA.ContainsKey(name)
                || name.Equals(GetEquipNumName, StringComparison.OrdinalIgnoreCase))
                f4d2LegacyStack.Add(name);
            if (f4d1ClassA.ContainsKey(name)) f4d2LegacyInnerExecutions++;
        }

        private long ReadPersistentArg(Process process)
        {
            if (Candidate) return EnsureF4D1Bank(F4D2HelperName).Arg[0];
            var helper = process.labelDic.GetNonEventLabel(F4D2HelperName)
                ?? throw new InvalidOperationException("F4D2 Legacy helper missing");
            var token = process.idDic.GetNextRuntimeLocalVariableToken("ARG", helper)
                ?? throw new InvalidOperationException("F4D2 Legacy helper ARG missing");
            return token.GetIntValue(process.exm, [0]);
        }

        private static string ReadLegacyPersistentArgs(Process process)
        {
            var method = process.labelDic.GetNonEventLabel(GetEquipNumName)
                ?? throw new InvalidOperationException("F4D2 Legacy GET_EQUIPNUM missing");
            var token = process.idDic.GetNextRuntimeLocalVariableToken("ARGS", method)
                ?? throw new InvalidOperationException("F4D2 Legacy GET_EQUIPNUM ARGS missing");
            return token.GetStrValue(process.exm, [0]);
        }

        private string F4D2Resolution(string name)
        {
            var resolution = Resolve(name, eventKind:false);
            if (resolution == R0F1Resolution.WrongKind) return "WrongKind";
            if (!name.StartsWith("装備箇所_", StringComparison.Ordinal)) return "Unresolved";
            if (resolution == R0F1Resolution.Ready)
                return f4d1ClassA.ContainsKey(name) ? "KnownReady" : "Blocked";
            return resolution switch
            {
                R0F1Resolution.KnownMissing => "KnownMissing",
                R0F1Resolution.WrongKind => "WrongKind",
                R0F1Resolution.Unknown => "Unresolved",
                _ => "Blocked"
            };
        }

        private long ExpectedF4D2Value(Process process, string resolution, string generated)
        {
            if (resolution == "KnownMissing") return 0;
            if (resolution != "KnownReady" || !f4d1ClassA.TryGetValue(generated, out var descriptor))
                throw new InvalidOperationException("F4D2 expected value unavailable: " + generated);
            var constant = process.vEvaluator.Constant;
            var value = constant.TryKeywordToInteger(out var found, VariableCode.EQUIP, descriptor.Argument, -1) ? found : -1;
            var baseline = constant.TryKeywordToInteger(out var sword, VariableCode.EQUIP, "剣", -1) ? sword : -1;
            return (long)value - baseline;
        }

        private F4D2ResolverRow ResolveEvidence(string name, string target, string expected)
        {
            var observed = F4D2Resolution(target);
            return new(name, target, observed, expected, observed == expected);
        }

        private long FindF4D2MissingNumber()
        {
            for (long value = 900_000; value < 901_000; value++)
                if (F4D2Resolution(F4D2TargetName(value)) == "KnownMissing") return value;
            throw new InvalidOperationException("F4D2 KnownMissing fixture unavailable");
        }

        private long FindF4D2OutsideKnown()
        {
            foreach (var name in functions.Keys.Order(StringComparer.Ordinal))
            {
                if (!name.StartsWith("装備箇所_", StringComparison.Ordinal)
                    || !long.TryParse(name[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    || value is >= 1500 and <= 5999) continue;
                if (Resolve(name, false) == R0F1Resolution.Ready) return value;
            }
            throw new InvalidOperationException("F4D2 outside-range Known fixture unavailable");
        }

        private static string F4D2TargetName(long value) => "装備箇所_" + value.ToString(CultureInfo.InvariantCulture);

        private sealed class F4D2Runtime
        {
            private readonly R0F1Context owner;
            private readonly Process process;
            private readonly BoundSavedIntArray saved;
            private readonly object identity = new();
            private readonly int generation = 1;
            private readonly F4D1Runtime methods;
            private readonly HashSet<string> materializedWrappers = new(StringComparer.Ordinal);
            private readonly HashSet<string> materializedNested = new(StringComparer.Ordinal);
            private bool active = true;
            private long? argumentTemporary;
            internal int ArgumentEvaluations;
            internal int ArgWrites;
            internal int SavedReads;
            internal int ResultWrites;
            internal int Returns;
            internal int LegacyRetry;
            internal int InnerExecutions;
            internal int CompiledPrograms => materializedWrappers.Count + (materializedWrappers.Count == 0 ? 0 : 1)
                + materializedNested.Count + methods.CompiledProgramCount;
            internal bool TemporaryCleared => argumentTemporary is null;

            internal F4D2Runtime(R0F1Context owner, Process process, BoundSavedIntArray saved)
                => (this.owner, this.process, this.saved, methods) = (owner, process, saved, new(owner, process));

            internal F4D2BindRequest Positive(ClassBDescriptor descriptor) => new(descriptor.Name, 0,
                "Normal", descriptor.SourceSha256, descriptor.BodySha256, F4D2Schema, true, false);

            internal F4D2Binding Bind(F4D2BindRequest request, object? expectedOwner = null)
            {
                RequireLive();
                if (expectedOwner is not null && !ReferenceEquals(identity, expectedOwner))
                    throw new InvalidOperationException("F4D2 bind owner mismatch");
                if (!request.Static || request.Effectful) throw new NotSupportedException("F4D2 wrapper call kind");
                if (request.Kind != "Normal") throw new InvalidOperationException("F4D2 wrapper wrong kind");
                if (request.Arity != 0) throw new InvalidOperationException("F4D2 wrapper arity");
                if (request.Schema != F4D2Schema) throw new InvalidOperationException("F4D2 schema mismatch");
                if (!owner.f4d2ClassB.TryGetValue(request.Name, out var descriptor))
                    throw new InvalidOperationException("F4D2 wrapper unresolved");
                if (request.SourceSha256 != descriptor.SourceSha256 || request.BodySha256 != descriptor.BodySha256)
                    throw new InvalidOperationException("F4D2 wrapper source mismatch");
                return new(this, generation, descriptor);
            }

            internal F4D2Case Invoke(ClassBDescriptor descriptor, Func<int> producer,
                F4D2Fault fault = F4D2Fault.None)
            {
                var binding = Bind(Positive(descriptor));
                var depth = owner.compactFrames.Count;
                F4D2Point before;
                string bankBefore;
                try
                {
                    Require(binding);
                    SetSentinels(process);
                    bankBefore = owner.CandidateCallerBank(descriptor.Name);
                    var savedBefore = saved.Read(process, descriptor.Argument);
                    before = owner.CaptureF4D2Point(process, "Before", owner.EnsureF4D1Bank(F4D2HelperName).Arg[0],
                        savedBefore, F4D2TargetName(savedBefore), owner.F4D2Resolution(F4D2TargetName(savedBefore)),
                        [], "", ArgumentEvaluations, ArgWrites, SavedReads, ResultWrites, Returns);
                    owner.Enter(new(owner.f4d2WrapperHandles[descriptor.Name], 0,
                        descriptor.RelativePath + ":" + descriptor.Line), 0, false);
                    materializedWrappers.Add(descriptor.Name);
                    ArgumentEvaluations++;
                    argumentTemporary = producer();
                    Require(binding);
                    var helperBank = owner.EnsureF4D1Bank(F4D2HelperName);
                    helperBank.Arg[0] = argumentTemporary.Value;
                    ArgWrites++;
                    if (fault == F4D2Fault.RevokeAfterArgStore) { active = false; Require(binding); }
                    owner.Enter(new(owner.f4d2HelperHandle, 1, F4D2HelperPath + ":2871"), 0, false);
                    process.vEvaluator.SetResultX([0]);
                    ResultWrites++;
                    if (fault == F4D2Fault.SavedRead) throw new InvalidOperationException("injected SAVEDATA read fault");
                    var value = saved.Read(process, helperBank.Arg[0]);
                    SavedReads++;
                    var generated = F4D2TargetName(value);
                    if (fault == F4D2Fault.Resolver) throw new InvalidOperationException("injected dynamic resolver fault");
                    var resolution = owner.F4D2Resolution(generated);
                    var stack = new List<string> { descriptor.Name, F4D2HelperName };
                    if (resolution == "KnownReady")
                    {
                        if (fault == F4D2Fault.InnerExecution) throw new InvalidOperationException("injected inner execution fault");
                        var inner = owner.f4d1ClassA[generated];
                        stack.Add(generated);
                        stack.Add(GetEquipNumName);
                        ExecuteInner(inner);
                    }
                    else if (resolution != "KnownMissing")
                        throw new NotSupportedException("F4D2 dynamic target " + resolution);
                    var helperResult = process.vEvaluator.RESULT;
                    process.vEvaluator.SetResultX([helperResult]);
                    ResultWrites++;
                    owner.Return(process, F4D2HelperName, true);
                    Returns++;
                    var wrapperResult = process.vEvaluator.RESULT;
                    process.vEvaluator.SetResultX([wrapperResult]);
                    ResultWrites++;
                    owner.Return(process, descriptor.Name, true);
                    Returns++;
                    argumentTemporary = null;
                    var after = owner.CaptureF4D2Point(process, "After", helperBank.Arg[0], value, generated, resolution,
                        stack.ToArray(), methods.PersistentArgs(GetEquipNumName), ArgumentEvaluations,
                        ArgWrites, SavedReads, ResultWrites, Returns);
                    var bankAfter = owner.CandidateCallerBank(descriptor.Name);
                    var expected = owner.ExpectedF4D2Value(process, resolution, generated);
                    var pass = process.vEvaluator.RESULT == expected && helperBank.Arg[0] == descriptor.Argument
                        && bankBefore == bankAfter && after.Result.Skip(1).SequenceEqual(before.Result.Skip(1))
                        && after.Results.SequenceEqual(before.Results)
                        && (resolution == "KnownReady" ? InnerExecutions > 0 : true);
                    return new(descriptor.Name, descriptor.Argument, value, generated, resolution,
                        process.vEvaluator.RESULT, true, resolution == "KnownReady" ? 1 : 0,
                        before, after, bankBefore, bankAfter, pass);
                }
                catch
                {
                    argumentTemporary = null;
                    throw;
                }
                finally
                {
                    // Diagnostic harness discards terminal frame scratch between independent fault cases.
                    while (owner.compactFrames.Count > depth) owner.compactFrames.RemoveAt(owner.compactFrames.Count - 1);
                }
            }

            private void ExecuteInner(ClassADescriptor descriptor)
            {
                materializedNested.Add(descriptor.Name);
                owner.Enter(new(owner.f4d1WrapperHandles[descriptor.Name], 1,
                    descriptor.RelativePath + ":" + descriptor.Line), 1, false);
                var binding = methods.Bind(methods.PositiveRequest(descriptor));
                var invocation = methods.InvokeBound(binding, () => descriptor.Argument, 1_000_000);
                owner.compactFrames[^1].EvalTemporary = invocation.Value;
                process.vEvaluator.SetResultX([invocation.Value]);
                methods.NormalResultWrites++;
                ResultWrites++;
                owner.Return(process, descriptor.Name, true);
                Returns++;
                InnerExecutions++;
            }

            internal string ResolveRequired(string target, string? expectedSource = null)
            {
                RequireLive();
                var resolution = owner.F4D2Resolution(target);
                if (resolution == "KnownReady" && expectedSource is not null
                    && owner.f4d1ClassA[target].SourceSha256 != expectedSource)
                    throw new InvalidOperationException("F4D2 dynamic target source mismatch");
                if (resolution is not ("KnownReady" or "KnownMissing"))
                    throw new NotSupportedException("F4D2 dynamic target " + resolution);
                return resolution;
            }

            internal void ValidateHelper(string kind)
            {
                RequireLive();
                if (kind != "Normal") throw new InvalidOperationException("F4D2 helper wrong kind");
            }

            internal void Revoke() => active = false;
            internal long PersistentArg => owner.EnsureF4D1Bank(F4D2HelperName).Arg[0];
            internal object Census() => new
            {
                PhysicalAdmittedWrappers=owner.f4d2ClassB.Count,
                PhysicalMaterializedWrappers=materializedWrappers.Count,
                SharedWrapperTemplates=materializedWrappers.Count==0?0:1,
                HelperPrograms=materializedWrappers.Count==0?0:1,
                NestedClassATargetPrograms=materializedNested.Count,
                TypedMethodPrograms=methods.CompiledProgramCount,
                DescriptorEstimateBytes=owner.f4d2ClassB.Count*128L+192L,
                LegacyObjectGraphBuilt=false,
                ProcessMemoryBenchmark=false
            };

            private void Require(F4D2Binding binding)
            {
                RequireLive();
                if (!ReferenceEquals(binding.Owner, this) || binding.Generation != generation || binding.Descriptor is null)
                    throw new InvalidOperationException("F4D2 owner/generation mismatch");
            }
            private void RequireLive()
            {
                if (!active) throw new InvalidOperationException("F4D2 owner revoked");
            }
        }

        private F4D2Point CaptureF4D2Point(Process process, string name, long helperArg, long savedValue,
            string generated, string resolution, string[] stack, string methodArgs,
            int arguments, int argWrites, int savedReads, int resultWrites, int returns) =>
            new(name, helperArg, savedValue, generated, resolution, stack, methodArgs,
                (long[])process.vEvaluator.RESULT_ARRAY.Clone(), (string[])process.vEvaluator.RESULTS_ARRAY.Clone(),
                arguments, argWrites, savedReads, resultWrites, returns);

        private dynamic RunF4D2ClosureMatrix(long outsideKnown, long missing)
        {
            ClosureDisposition Classify(string target) => F4D2Resolution(target) switch
            {
                "KnownReady" => ClosureDisposition.KnownReady,
                "KnownMissing" => ClosureDisposition.KnownMissing,
                "WrongKind" => ClosureDisposition.WrongKind,
                "Unresolved" => ClosureDisposition.Unresolved,
                _ => ClosureDisposition.Blocked
            };
            ClosureDisposition Walk(ClosureState start, int maxDepth,
                Func<ClosureState, (ClosureDisposition Terminal, ClosureState? Next)> next)
            {
                var active = new HashSet<ClosureState>();
                ClosureDisposition Visit(ClosureState state, int depth)
                {
                    if (depth > maxDepth) return ClosureDisposition.DepthExceeded;
                    if (!active.Add(state)) return ClosureDisposition.Cycle;
                    var edge = next(state);
                    var result = edge.Next is { } child ? Visit(child, depth + 1) : edge.Terminal;
                    active.Remove(state);
                    return result;
                }
                return Visit(start, 0);
            }
            var sameNameDifferentFormal = Walk(new(F4D2HelperName, 0), 4, state => state.BoundFormal switch
            {
                0 => (ClosureDisposition.KnownReady, new ClosureState(F4D2HelperName, 1)),
                _ => (ClosureDisposition.KnownReady, null)
            });
            var rows = new[]
            {
                ("known ready", Classify("装備箇所_1500"), ClosureDisposition.KnownReady),
                ("known missing", Classify(F4D2TargetName(missing)), ClosureDisposition.KnownMissing),
                ("wrong kind", Classify("EVENTLOAD"), ClosureDisposition.WrongKind),
                ("unresolved", ClosureDisposition.Unresolved, ClosureDisposition.Unresolved),
                ("known unsupported", Classify(F4D2TargetName(outsideKnown)), ClosureDisposition.Blocked),
                ("same helper different formal", sameNameDifferentFormal, ClosureDisposition.KnownReady),
                ("cycle", Walk(new(F4D2HelperName,0),4,state=>(ClosureDisposition.KnownReady,state)), ClosureDisposition.Cycle),
                ("depth exceeded", Walk(new(F4D2HelperName,0),1,state=>(ClosureDisposition.KnownReady,new ClosureState(F4D2HelperName,state.BoundFormal+1))), ClosureDisposition.DepthExceeded)
            }.Select(value => new { Case=value.Item1, Observed=value.Item2.ToString(), Expected=value.Item3.ToString(),
                StateKey="physical definition + bound formal values", Pass=value.Item2==value.Item3 }).ToArray();
            return new { Count=rows.Length, AllPassed=rows.All(value=>value.Pass), Rows=rows };
        }

        private F4D2MatrixRow[] RunF4D2NegativeMatrix(Process process, F4D2Runtime runtime, long outsideKnown)
        {
            var descriptor = f4d2ClassB.Values.OrderBy(value => value.Argument).First();
            var positive = runtime.Positive(descriptor);
            var rows = new List<F4D2MatrixRow>();
            void Run(string name, Action action, string expected="PRE_EFFECT_REJECT")
            {
                var a=runtime.ArgumentEvaluations; var w=runtime.ArgWrites; var r=runtime.ResultWrites; var ret=runtime.Returns;
                var observed="ACCEPTED";
                try { action(); }
                catch(Exception ex) when(ex is InvalidOperationException or NotSupportedException or IndexOutOfRangeException)
                { observed=ex.GetType().Name+": "+ex.Message; }
                rows.Add(new(name,observed,expected,runtime.ArgumentEvaluations-a,runtime.ArgWrites-w,
                    runtime.ResultWrites-r,runtime.Returns-ret,runtime.LegacyRetry,
                    observed!="ACCEPTED"&&runtime.ArgumentEvaluations==a&&runtime.ArgWrites==w&&runtime.ResultWrites==r&&runtime.Returns==ret&&runtime.LegacyRetry==0));
            }
            Run("wrapper wrong kind",()=>runtime.Bind(positive with { Kind="Method" }));
            Run("helper wrong kind",()=>runtime.ValidateHelper("Method"));
            Run("wrong arity",()=>runtime.Bind(positive with { Arity=1 }));
            Run("source mismatch",()=>runtime.Bind(positive with { SourceSha256=new string('0',64) }));
            Run("SAVEDATA schema mismatch",()=>BindF4D2Saved(process,F4D2SavedName,61));
            Run("index out of range",()=>f4d2Saved.Read(process,60));
            Run("unresolved target",()=>runtime.ResolveRequired("NOT_AN_EQUIPMENT_TARGET"));
            Run("known unsupported target",()=>runtime.ResolveRequired(F4D2TargetName(outsideKnown)));
            Run("owner mismatch",()=>runtime.Bind(positive,new object()));
            var revoked=new F4D2Runtime(this,process,f4d2Saved); revoked.Revoke();
            Run("owner revoke",()=>revoked.Bind(revoked.Positive(descriptor)));
            Run("dynamic target source mismatch",()=>runtime.ResolveRequired("装備箇所_1500",new string('0',64)));
            var closure=RunF4D2ClosureMatrix(outsideKnown,FindF4D2MissingNumber());
            Run("cycle",()=>RequireClosureDisposition(closure,"cycle","KnownReady"));
            Run("depth exceeded",()=>RequireClosureDisposition(closure,"depth exceeded","KnownReady"));
            return rows.ToArray();
        }

        private static void RequireClosureDisposition(dynamic matrix, string name, string expected)
        {
            foreach(var row in matrix.Rows)
                if(row.Case==name)
                {
                    if(row.Observed!=expected) throw new NotSupportedException("F4D2 certificate "+row.Observed);
                    return;
                }
            throw new InvalidOperationException("F4D2 certificate row missing");
        }

        private F4D2FaultRow[] RunF4D2FaultMatrix(Process process)
        {
            var descriptor=f4d2ClassB.Values.OrderBy(value=>value.Argument).First();
            f4d2Saved.Write(1500,descriptor.Argument);
            var rows=new List<F4D2FaultRow>();
            void Run(string name,F4D2Fault fault,Func<int> producer,int expectedArgWrites,int expectedResultWrites)
            {
                var runtime=new F4D2Runtime(this,process,f4d2Saved);
                var argBefore=ReadPersistentArg(process);
                SetSentinels(process);
                var observed="COMPLETED";
                try { runtime.Invoke(descriptor,producer,fault); }
                catch(Exception ex) when(ex is InvalidOperationException or NotSupportedException)
                { observed=ex.GetType().Name+": "+ex.Message; }
                var result0=process.vEvaluator.RESULT;
                var pass=observed!="COMPLETED"&&runtime.ArgWrites==expectedArgWrites
                    && runtime.ResultWrites==expectedResultWrites&&runtime.Returns==0&&runtime.LegacyRetry==0
                    && runtime.TemporaryCleared&&runtime.PersistentArg==(expectedArgWrites==0?argBefore:descriptor.Argument)
                    && result0==(expectedResultWrites==0?91_000:0);
                rows.Add(new(name,observed,runtime.ArgumentEvaluations,runtime.ArgWrites,runtime.ResultWrites,
                    runtime.Returns,runtime.PersistentArg,result0,runtime.LegacyRetry,runtime.TemporaryCleared,pass));
            }
            Run("argument evaluation fault",F4D2Fault.None,()=>throw new InvalidOperationException("injected argument evaluation fault"),0,0);
            Run("owner revoke after ARG store",F4D2Fault.RevokeAfterArgStore,()=>descriptor.Argument,1,0);
            Run("SAVEDATA read fault",F4D2Fault.SavedRead,()=>descriptor.Argument,1,1);
            Run("dynamic resolver fault",F4D2Fault.Resolver,()=>descriptor.Argument,1,1);
            Run("inner execution fault",F4D2Fault.InnerExecution,()=>descriptor.Argument,1,1);
            return rows.ToArray();
        }
    }
}
#endif
