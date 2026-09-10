#if R0_F4D4
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal object RunR0F4D4(bool candidate, string root)
    {
        r0f1 = new R0F1Context(candidate, Program.ErbDir,
            Path.GetDirectoryName(Program.CsvDir.TrimEnd(Path.DirectorySeparatorChar))!, this, "actual");
        return r0f1.RunF4D4(this, root);
    }

    internal void R0F4D4BeforeLegacyInstruction(InstructionLine line)
        => r0f1?.ObserveF4D4LegacyInstruction(line, before: true);

    internal void R0F4D4AfterLegacyInstruction(InstructionLine line)
        => r0f1?.ObserveF4D4LegacyInstruction(line, before: false);

    internal void R0F4D4BeforeLegacyFallthrough(LogicalLine line)
        => r0f1?.ObserveF4D4LegacyFallthrough(line);

    private sealed partial class R0F1Context
    {
        private const string F4D4Schema = "emuera-r0f4d4-class-c-unreachable-return-v1";
        private static readonly F4D4Spec[] F4D4Specs =
        [
            new("装備箇所_3203", "RPG/アイテム関連/装備品/頭/3203_ウィッチレディハット.ERB", 79, 80, 84),
            new("装備箇所_4601", "RPG/アイテム関連/装備品/足/4601_ウィッチレディブーツ.ERB", 51, 52, 56),
            new("装備箇所_4602", "RPG/アイテム関連/装備品/足/4602_ネコマタブーツ.ERB", 49, 50, 54)
        ];

        private sealed record F4D4Spec(string Name, string Path, int HeaderLine, int FirstReturnLine, int TrailingReturnLine);
        private sealed record F4D4BodyValidation(bool Pass, string Reason, string Argument,
            int FirstReturnOffset, int[] TrailingReturnOffsets, int ExecutableCount,
            bool FirstReturnTerminal, bool FirstToTrailingEdge, int TrailingIncomingEdges,
            string[] ReachableInstructions, string[] UnreachableInstructions);
        private sealed record F4D4Descriptor(F4D4Spec Spec, string SourceSha256, string BodySha256,
            string WholeBodySha256, string Argument, int NextPhysicalHeaderLine, int SpanEndLine,
            R0F1Definition Definition, string[] PhysicalLines, F4D4BodyValidation Validation);
        private sealed record F4D4BindRequest(object Owner, int Generation, string Name,
            string SourceSha256, string BodySha256, SourceIndexFlags Flags, string[] PhysicalLines);
        private sealed record F4D4NegativeRow(string Case, string Observed, string Expected,
            int ScriptEffects, bool Pass);
        private sealed record F4D4OracleRow(string Name, string SourcePath, int FirstReturnLine,
            int TrailingReturnLine, string Argument, long ExpectedFromConstantData, long TypedResult,
            string MethodArgsFinalValue, long[] Result, long[] ResultTail, string[] Results,
            int FirstReturnEntered, int GetEquipNumCallCount, int NormalReturnCount,
            int CallerResumeCount, string CallerPc, int TrailingReturnEntered,
            int TrailingReturnCompleted, int FunctionFallthroughCount, bool Pass);

        private readonly object f4d4Owner = new();
        private const int F4D4Generation = 1;
        private Dictionary<string, F4D4Descriptor> f4d4ClassC = null!;
        private bool f4d4LegacyProbe;
        private F4D4Descriptor? f4d4LegacyCurrent;
        private int f4d4FirstEntered;
        private int f4d4FirstCompleted;
        private int f4d4MethodCalls;
        private int f4d4NormalReturns;
        private int f4d4CallerResumes;
        private int f4d4TrailingEntered;
        private int f4d4TrailingCompleted;
        private int f4d4Fallthrough;
        private long? f4d4TypedResult;
        private string f4d4MethodArgs = "";

        internal object RunF4D4(Process process, string root)
        {
            var inventoryPath = Path.Combine(root, "evidence", "family-target-inventory.json");
            f4d1ClassA = AdmitClassA(inventoryPath);
            f4d4ClassC = AdmitF4D4ClassC(inventoryPath);
            var allowed = RunF4D4AllowedSuffixMatrix();
            var negatives = RunF4D4NegativeMatrix();
            F4D1Runtime? runtime = null;
            object fault = new { Executed = false, Pass = !Candidate, Reason = "Legacy positive oracle does not inject source faults" };
            if (Candidate)
            {
                InitializeF4D1Frames();
                foreach (var descriptor in f4d4ClassC.Values.OrderBy(value => value.Spec.Name, StringComparer.Ordinal))
                    f4d1WrapperHandles.Add(descriptor.Spec.Name, Handle(30_000 + f4d1WrapperHandles.Count, descriptor.Spec.Name));
                runtime = new F4D1Runtime(this, process);
                fault = RunF4D4FirstExpressionFault(process, runtime, f4d4ClassC[F4D4Specs[0].Name]);
            }

            var cases = F4D4Specs.Select(spec => Candidate
                ? RunCandidateF4D4(process, runtime!, f4d4ClassC[spec.Name])
                : RunLegacyF4D4(process, f4d4ClassC[spec.Name])).ToArray();
            var finalArgs = Candidate ? runtime!.PersistentArgs(GetEquipNumName) : ReadLegacyPersistentArgs(process);
            var guards = R0E1AProof.GuardSnapshot();
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            using var classB = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "evidence", "class-b-authority.json")));
            using var inventory = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
            var entries = inventory.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
            var known = entries.Count(row => row.GetProperty("Resolution").GetString() != "KnownMissing");
            var missing = entries.Length - known;
            var saved = BindF4D2Saved(process, F4D2SavedName, 60);
            var equip = BindF4D3Array(process, "EQUIP_PART", 4500);
            var manifest = CurrentManifest();
            var sourceCensus = f4d4ClassC.Values.OrderBy(value => value.Spec.Name, StringComparer.Ordinal).Select(value => new
            {
                value.Spec.Name, RelativePath=value.Spec.Path, value.Spec.HeaderLine,
                value.Spec.FirstReturnLine, value.Spec.TrailingReturnLine, value.SourceSha256,
                value.BodySha256, value.WholeBodySha256, value.Argument, value.NextPhysicalHeaderLine,
                value.SpanEndLine, Flags=value.Definition.Function.Flags.ToString(),
                PhysicalBody=value.PhysicalLines.Select((line, index) => new { Line=value.Spec.HeaderLine + index, Text=line }).ToArray(),
                value.Validation
            }).ToArray();
            var pass = f4d1ClassA.Count == 1163 && f4d4ClassC.Count == 3
                && sourceCensus.All(row => row.Validation.Pass) && allowed.Pass
                && negatives.All(row => row.Pass) && cases.All(row => row.Pass)
                && finalArgs == "足" && (!Candidate || (bool)fault.GetType().GetProperty("Pass")!.GetValue(fault)!)
                && guardTotal == 0;
            return new
            {
                Schema = F4D4Schema,
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "CLASS_C_UNREACHABLE_RETURN_ONLY",
                Deterministic = new { Seed=Program.NextRuntimeDifferentialSeed,
                    ClockBase=Program.NextRuntimeDifferentialClockBase, ClockStepMs=Program.NextRuntimeDifferentialClockStepMs },
                StartupCompiledPrograms = 0,
                ClassCSourceCensus = sourceCensus,
                WholeBodyReachabilityModel = new { Pass=sourceCensus.All(row => row.Validation.Pass),
                    FirstReturnTerminal=sourceCensus.All(row => row.Validation.FirstReturnTerminal),
                    FirstToTrailingEdges=sourceCensus.Sum(row => row.Validation.FirstToTrailingEdge ? 1 : 0),
                    TrailingIncomingEdges=sourceCensus.Sum(row => row.Validation.TrailingIncomingEdges) },
                AllowedSuffixMatrix = allowed.Value,
                ReachabilityNegativeMatrix = negatives,
                RealOracle = cases,
                PersistentGetEquipNumArgs = new { Sequence=new[]{"頭","足","足"}, Final=finalArgs, Pass=finalArgs=="足" },
                FirstExpressionFault = fault,
                TrailingReturnExecutionCount = new { Entered=cases.Sum(row => row.TrailingReturnEntered), Completed=cases.Sum(row => row.TrailingReturnCompleted) },
                SnapshotIdentity = new { SourceGenerationSha256=F4D3SourceGeneration(),
                    ConfigSha256=F4D3ConfigIdentity(), ErhSchemaSha256=F4D3SchemaIdentity(saved.Token, equip.Token),
                    Save219Sha256=FileHash(Path.Combine(Config.SavDir, "save219.sav")),
                    manifest.FileCount, manifest.TotalBytes, manifest.Sha256 },
                FamilyReadiness = new { ClassAAdmitted=f4d1ClassA.Count,
                    ClassBWrapperSemanticReady=classB.RootElement.GetProperty("Wrappers").GetArrayLength(),
                    ClassBActualClosureReady="INHERITED_PENDING_SNAPSHOT_CHECK", ClassCAdmitted=pass?3:0,
                    Known4500TargetsReady=pass?f4d1ClassA.Count+60+3:f4d1ClassA.Count+60,
                    Known4500TargetsTotal=known, Missing4500Targets=missing, Whole4500FamilyReady=false,
                    ReadinessKind="PREFLIGHT_ONLY" },
                LazyMemory = Candidate ? new { PhysicalClassCDescriptors=3, SharedReturnExpressionTemplates=1,
                    GetEquipNumPrograms=1, StartupCompiled=0, RetainedEstimateBytes=3*256L+2*96L,
                    Whole4500BodiesCompiled=0 } : new { PhysicalClassCDescriptors=3,
                    SharedReturnExpressionTemplates=0, GetEquipNumPrograms=0, StartupCompiled=0,
                    RetainedEstimateBytes=0L, Whole4500BodiesCompiled=0 },
                GraphGuards = Candidate ? guards : null,
                GraphGuardTotal = guardTotal,
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                ActualSetEquipVarExecuted = false,
                F4DScriptWrites = 0,
                NextRuntimeEnabled = Program.NextRuntimeMode,
                Result = pass ? "PASS" : "FAIL"
            };
        }

        private Dictionary<string, F4D4Descriptor> AdmitF4D4ClassC(string inventoryPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
            var rows = document.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
            var result = new Dictionary<string, F4D4Descriptor>(StringComparer.Ordinal);
            foreach (var spec in F4D4Specs)
            {
                var authority = rows.Where(row => row.GetProperty("Name").GetString() == spec.Name).ToArray();
                if (authority.Length != 1) throw new InvalidOperationException("F4D4 inventory identity: " + spec.Name);
                var row = authority[0];
                if (row.GetProperty("RelativePath").GetString() != spec.Path
                    || row.GetProperty("Line").GetInt32() != spec.HeaderLine
                    || row.GetProperty("BodyKind").GetString() != "UnsupportedMultipleReturn")
                    throw new InvalidOperationException("F4D4 inventory authority mismatch: " + spec.Name);
                if (!functions.TryGetValue(spec.Name, out var matches) || matches.Length != 1)
                    throw new InvalidOperationException("F4D4 physical identity missing/ambiguous: " + spec.Name);
                var definition = matches[0];
                var physical = Lines(Read(definition.Function, definition.File));
                var validation = ValidateF4D4Body(spec.Name, physical, definition.Function.Flags);
                var sourceSha = FileHash(definition.File.FileIdentity)!;
                var bodySha = BodyHash(F4D4Executable(physical));
                var nextHeader = definition.File.Functions.Where(item => item.Span.StartLine > definition.Line)
                    .Select(item => item.Span.StartLine).DefaultIfEmpty(definition.Function.Span.EndLine + 1).Min();
                if (!definition.RelativePath.Equals(spec.Path, StringComparison.OrdinalIgnoreCase)
                    || definition.Line != spec.HeaderLine || sourceSha != row.GetProperty("SourceSha256").GetString()
                    || bodySha != row.GetProperty("BodySha256").GetString() || !validation.Pass
                    || spec.HeaderLine + validation.FirstReturnOffset != spec.FirstReturnLine
                    || validation.TrailingReturnOffsets.Length != 1
                    || spec.HeaderLine + validation.TrailingReturnOffsets[0] != spec.TrailingReturnLine
                    || definition.Function.Span.EndLine >= nextHeader)
                    throw new InvalidOperationException("F4D4 whole-body admission mismatch: " + spec.Name);
                var descriptor = new F4D4Descriptor(spec, sourceSha, bodySha,
                    HashText(string.Join('\n', physical)), validation.Argument, nextHeader,
                    definition.Function.Span.EndLine, definition, physical, validation);
                var bind = BindF4D4(descriptor, new(f4d4Owner, F4D4Generation, spec.Name,
                    sourceSha, bodySha, definition.Function.Flags, physical));
                if (!bind.Pass) throw new InvalidOperationException("F4D4 bind failed: " + bind.Reason);
                result.Add(spec.Name, descriptor);
            }
            return result;
        }

        private (bool Pass, string Reason) BindF4D4(F4D4Descriptor descriptor, F4D4BindRequest request)
        {
            if (!ReferenceEquals(request.Owner, f4d4Owner)) return (false, "owner mismatch");
            if (request.Generation != F4D4Generation) return (false, "owner revoked");
            if (request.Name != descriptor.Spec.Name) return (false, "physical name mismatch");
            var validation = ValidateF4D4Body(request.Name, request.PhysicalLines, request.Flags);
            if (!validation.Pass) return (false, validation.Reason);
            if (request.SourceSha256 != descriptor.SourceSha256) return (false, "source fingerprint mismatch");
            if (request.BodySha256 != descriptor.BodySha256) return (false, "body hash mismatch");
            return (true, "Ready");
        }

        private F4D4BodyValidation ValidateF4D4Body(string name, string[] lines, SourceIndexFlags flags)
        {
            F4D4BodyValidation Fail(string reason) => new(false, reason, "", -1, [], 0,
                false, false, 0, [], []);
            if (flags != SourceIndexFlags.None) return Fail("source flags: " + flags);
            if (lines.Length < 2 || lines[0].Trim() != "@" + name) return Fail("physical header");
            var executable = Enumerable.Range(1, lines.Length - 1)
                .Where(index => lines[index].Trim() is { Length: > 0 } text && !text.StartsWith(';')).ToArray();
            if (executable.Length == 0) return Fail("first terminal RETURN missing");
            var first = lines[executable[0]].Trim();
            var match = Regex.Match(first, "^RETURN GET_EQUIPNUM\\(\"([^\"]+)\"\\)$", RegexOptions.CultureInvariant);
            if (!match.Success) return Fail("first executable is not unconditional terminal RETURN GET_EQUIPNUM");
            var expanded = environment.Macros.Expand(first, environment.Compatibility, out var substitutions);
            if (substitutions != 0 || expanded != first) return Fail("first RETURN macro substitution");
            var trailing = new List<int>();
            foreach (var index in Enumerable.Range(executable[0] + 1, lines.Length - executable[0] - 1))
            {
                var text = lines[index].Trim();
                if (text.Length == 0 || text.StartsWith(';')) continue;
                if (text != "RETURN 1") return Fail("unapproved unreachable suffix: " + text);
                expanded = environment.Macros.Expand(text, environment.Compatibility, out substitutions);
                if (substitutions != 0 || expanded != text) return Fail("suffix macro substitution");
                trailing.Add(index);
            }
            return new(true, "FirstTerminalReturnWithRestrictedUnreachableSuffix", match.Groups[1].Value,
                executable[0], trailing.ToArray(), executable.Length, true, false, 0,
                [$"pc{executable[0]}:RETURN GET_EQUIPNUM -> CALLER"],
                trailing.Select(index => $"pc{index}:RETURN 1:incoming=0").ToArray());
        }

        private (object Value, bool Pass) RunF4D4AllowedSuffixMatrix()
        {
            var rows = new[]
            {
                ("comment only", new[]{"@SYNTHETIC", "RETURN GET_EQUIPNUM(\"頭\")", ";comment"}),
                ("blank only", new[]{"@SYNTHETIC", "RETURN GET_EQUIPNUM(\"頭\")", ""}),
                ("literal RETURN 1", new[]{"@SYNTHETIC", "RETURN GET_EQUIPNUM(\"頭\")", "RETURN 1"}),
                ("multiple unreachable literal RETURNs", new[]{"@SYNTHETIC", "RETURN GET_EQUIPNUM(\"頭\")", "RETURN 1", ";gap", "RETURN 1"})
            }.Select(test =>
            {
                var result = ValidateF4D4Body("SYNTHETIC", test.Item2, SourceIndexFlags.None);
                return new { Case=test.Item1, Admitted=result.Pass, result.Reason,
                    TrailingReturns=result.TrailingReturnOffsets.Length, Scope="SYNTHETIC_POLICY_ONLY", Pass=result.Pass };
            }).ToArray();
            var pass = rows.All(row => row.Pass);
            return (new { Policy="Only comment, blank, and literal RETURN 1 after the first terminal RETURN",
                ProductGeneralClaim=false, AllPassed=pass, Rows=rows }, pass);
        }

        private F4D4NegativeRow[] RunF4D4NegativeMatrix()
        {
            var descriptor = f4d4ClassC[F4D4Specs[0].Name];
            var rows = new List<F4D4NegativeRow>();
            void Run(string name, Func<F4D4BindRequest> request)
            {
                var observed = "ACCEPTED";
                try
                {
                    var result = BindF4D4(descriptor, request());
                    if (!result.Pass) observed = result.Reason;
                }
                catch (Exception ex) { observed = ex.GetType().Name + ": " + ex.Message; }
                rows.Add(new(name, observed, "PRE_EFFECT_REJECT", 0, observed != "ACCEPTED"));
            }
            F4D4BindRequest Base(string[]? lines=null) => new(f4d4Owner, F4D4Generation,
                descriptor.Spec.Name, descriptor.SourceSha256, descriptor.BodySha256,
                SourceIndexFlags.None, lines ?? descriptor.PhysicalLines.ToArray());
            string[] Mutate(int index, string value)
            {
                var lines=descriptor.PhysicalLines.ToArray(); lines[index]=value; return lines;
            }
            Run("first RETURN removed", () => Base(Mutate(descriptor.Validation.FirstReturnOffset, ";removed")));
            Run("first RETURN conditional", () => Base(Mutate(descriptor.Validation.FirstReturnOffset, "SIF 1")));
            Run("reachable label/jump target after first RETURN", () => Base(Mutate(descriptor.Validation.TrailingReturnOffsets[0], "$TAIL")));
            Run("explicit branch to suffix", () => Base(Mutate(descriptor.Validation.FirstReturnOffset, "GOTO TAIL")));
            Run("trailing executable write", () => Base(Mutate(descriptor.Validation.TrailingReturnOffsets[0], "RESULT = 1")));
            Run("suffix physical header", () => Base(Mutate(descriptor.Validation.TrailingReturnOffsets[0], "@OTHER")));
            Run("source fingerprint mismatch", () => Base() with { SourceSha256=new string('0',64) });
            Run("body hash mismatch", () => Base() with { BodySha256=new string('0',64) });
            Run("owner mismatch", () => Base() with { Owner=new object() });
            Run("owner revoke", () => Base() with { Generation=F4D4Generation+1 });
            return rows.ToArray();
        }

        private F4D4OracleRow RunCandidateF4D4(Process process, F4D1Runtime runtime, F4D4Descriptor descriptor)
        {
            SetSentinels(process);
            var returnPc = (ushort)(40_000 + Array.FindIndex(F4D4Specs, value => value.Name == descriptor.Spec.Name));
            Enter(new(f4d1WrapperHandles[descriptor.Spec.Name], returnPc, "R0F4D4:" + descriptor.Spec.Name),
                (ushort)descriptor.Spec.FirstReturnLine, dynamic: false);
            var invocation = runtime.InvokeMethodOnly(GetEquipNumName, descriptor.Argument, 1_000_000);
            compactFrames[^1].EvalTemporary = invocation.Value;
            process.vEvaluator.SetResultX([invocation.Value]);
            Return(process, descriptor.Spec.Name, explicitReturn: true);
            var result = (long[])process.vEvaluator.RESULT_ARRAY.Clone();
            var results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            var expected = ExpectedF4D4Value(process, descriptor.Argument);
            var callerPc = $"R0F4D4_HOST:resume={returnPc}";
            var pass = invocation.Value == expected && invocation.Args == descriptor.Argument
                && result[0] == invocation.Value && F4D4TailStable(result) && F4D4ResultsStable(results)
                && compactFrames.Count == 1 && compactFrames[^1].Pc == returnPc;
            return new(descriptor.Spec.Name, descriptor.Spec.Path, descriptor.Spec.FirstReturnLine,
                descriptor.Spec.TrailingReturnLine, descriptor.Argument, expected, invocation.Value,
                invocation.Args, result, result.Skip(1).ToArray(), results, 1, 1, 1, 1,
                callerPc, 0, 0, 0, pass);
        }

        private F4D4OracleRow RunLegacyF4D4(Process process, F4D4Descriptor descriptor)
        {
            SetSentinels(process);
            ResetF4D4LegacyProbe(descriptor);
            var label = process.labelDic.GetNonEventLabel(descriptor.Spec.Name)
                ?? throw new InvalidOperationException("F4D4 Legacy function missing: " + descriptor.Spec.Name);
            if (label.IsMethod || label.Position is not { } position
                || position.LineNo != descriptor.Spec.HeaderLine
                || !position.Filename.Replace('\\','/').EndsWith(descriptor.Spec.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("F4D4 Legacy identity mismatch: " + descriptor.Spec.Name);
            f4d4LegacyProbe = true;
            try
            {
                var call = CalledFunction.CallFunction(process, descriptor.Spec.Name, null);
                process.state.IntoFunction(call, null!, null!);
                process.runScriptProc();
            }
            finally { f4d4LegacyProbe = false; }
            if (f4d4FirstCompleted == 1 && process.state.functionCount == 0) f4d4CallerResumes = 1;
            var result = (long[])process.vEvaluator.RESULT_ARRAY.Clone();
            var results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            var expected = ExpectedF4D4Value(process, descriptor.Argument);
            var typed = f4d4TypedResult ?? long.MinValue;
            var returnPc = 40_000 + Array.FindIndex(F4D4Specs, value => value.Name == descriptor.Spec.Name);
            var pass = f4d4FirstEntered == 1 && f4d4FirstCompleted == 1 && f4d4MethodCalls == 1
                && f4d4NormalReturns == 1 && f4d4CallerResumes == 1 && f4d4TrailingEntered == 0
                && f4d4TrailingCompleted == 0 && f4d4Fallthrough == 0 && typed == expected
                && f4d4MethodArgs == descriptor.Argument && result[0] == typed
                && F4D4TailStable(result) && F4D4ResultsStable(results);
            return new(descriptor.Spec.Name, descriptor.Spec.Path, descriptor.Spec.FirstReturnLine,
                descriptor.Spec.TrailingReturnLine, descriptor.Argument, expected, typed,
                f4d4MethodArgs, result, result.Skip(1).ToArray(), results, f4d4FirstEntered,
                f4d4MethodCalls, f4d4NormalReturns, f4d4CallerResumes,
                $"R0F4D4_HOST:resume={returnPc}", f4d4TrailingEntered,
                f4d4TrailingCompleted, f4d4Fallthrough, pass);
        }

        private object RunF4D4FirstExpressionFault(Process process, F4D1Runtime runtime, F4D4Descriptor descriptor)
        {
            SetSentinels(process);
            var depth = compactFrames.Count;
            Enter(new(f4d1WrapperHandles[descriptor.Spec.Name], 49_999, "R0F4D4:fault"),
                (ushort)descriptor.Spec.FirstReturnLine, dynamic: false);
            var observed = "COMPLETED";
            try { _ = runtime.InvokeMethodOnly(GetEquipNumName, descriptor.Argument, 0); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            { observed = ex.GetType().Name + ": " + ex.Message; }
            while (compactFrames.Count > depth) compactFrames.RemoveAt(compactFrames.Count - 1);
            var resultStable = F4D4AllSentinels(process.vEvaluator.RESULT_ARRAY)
                && F4D4ResultsStable(process.vEvaluator.RESULTS_ARRAY);
            var pass = observed != "COMPLETED" && resultStable && runtime.LegacyRetry == 0;
            return new { Executed=true, Injection="GET_EQUIPNUM expression step limit 0",
                Observed=observed, TerminalFail=observed!="COMPLETED", FirstReturnEntered=1,
                TrailingReturnExecution=0, NormalReturnCompletion=0, CallerResumeCount=0,
                LegacyRetry=runtime.LegacyRetry, ResultAndResultsUnchanged=resultStable, Pass=pass };
        }

        internal void ObserveF4D4LegacyArgumentsBound(Process process, CalledFunction call)
        {
            if (!f4d4LegacyProbe || !call.TopLabel.LabelName.Equals(GetEquipNumName, StringComparison.OrdinalIgnoreCase)) return;
            f4d4MethodCalls++;
            f4d4MethodArgs = call.TopLabel.Arg[0].GetStrValue(process.exm);
        }

        internal void ObserveF4D4LegacyReturnF(Process process, CalledFunction call, SingleTerm value)
        {
            if (!f4d4LegacyProbe || !call.TopLabel.LabelName.Equals(GetEquipNumName, StringComparison.OrdinalIgnoreCase)) return;
            f4d4TypedResult = value.GetIntValue(process.exm);
        }

        internal void ObserveF4D4LegacyInstruction(InstructionLine line, bool before)
        {
            if (!f4d4LegacyProbe || f4d4LegacyCurrent is null || line.Position is not { } position
                || line.ParentLabelLine?.LabelName != f4d4LegacyCurrent.Spec.Name) return;
            if (position.LineNo == f4d4LegacyCurrent.Spec.FirstReturnLine)
            {
                if (before) f4d4FirstEntered++;
                else { f4d4FirstCompleted++; f4d4NormalReturns++; }
            }
            if (position.LineNo == f4d4LegacyCurrent.Spec.TrailingReturnLine)
            {
                if (before) f4d4TrailingEntered++;
                else f4d4TrailingCompleted++;
            }
        }

        internal void ObserveF4D4LegacyFallthrough(LogicalLine line)
        {
            if (f4d4LegacyProbe && f4d4LegacyCurrent is not null
                && line.ParentLabelLine?.LabelName == f4d4LegacyCurrent.Spec.Name) f4d4Fallthrough++;
        }

        private void ResetF4D4LegacyProbe(F4D4Descriptor descriptor)
        {
            f4d4LegacyCurrent=descriptor; f4d4FirstEntered=0; f4d4FirstCompleted=0;
            f4d4MethodCalls=0; f4d4NormalReturns=0; f4d4CallerResumes=0;
            f4d4TrailingEntered=0; f4d4TrailingCompleted=0; f4d4Fallthrough=0;
            f4d4TypedResult=null; f4d4MethodArgs="";
        }

        private static string[] F4D4Executable(string[] lines) => lines.Skip(1).Select(line => line.Trim())
            .Where(line => line.Length != 0 && !line.StartsWith(';')).ToArray();

        private static long ExpectedF4D4Value(Process process, string argument)
        {
            var constant = process.vEvaluator.Constant;
            var value = constant.TryKeywordToInteger(out var found, VariableCode.EQUIP, argument, -1) ? found : -1;
            var baseline = constant.TryKeywordToInteger(out var sword, VariableCode.EQUIP, "剣", -1) ? sword : -1;
            return (long)value - baseline;
        }

        private static bool F4D4TailStable(long[] result) => result.Skip(1).Select((value,index) => value == 91_001 + index).All(value => value);
        private static bool F4D4AllSentinels(long[] result) => result.Select((value,index) => value == 91_000 + index).All(value => value);
        private static bool F4D4ResultsStable(string[] results) => results.Select((value,index) => value == "R0F4D1_RESULTS_" + index).All(value => value);
    }
}
#endif
