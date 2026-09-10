#if R0_C
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private static readonly string[] R0CKeys =
    [
        B1HotMap.Key("関数/組み込み関数/キャラクタデータ参照／ABL/HAVE_SKILL.ERB", 8, "HAVE_SKILL"),
        B1HotMap.Key("関数/私家版追加関数/CHARA_SKILLCOUNT.ERB", 6, "CHARA_SKILLCOUNT"),
        B1HotMap.Key("関数/組み込み関数/キャラクタ検索/FINDCHARA_LINK.ERB", 5, "FINDCHARA_LINK"),
        B1HotMap.Key("関数/組み込み関数/キャラクタ検索/FINDCHARA_TIMEID.ERB", 5, "FINDCHARA_TIMEID"),
        B1HotMap.Key("関数/汎用組み込み関数/SPLIT/COUNT_SPLIT.ERB", 21, "COUNT_SPLIT")
    ];

    internal sealed record R0CManifestEntry(string Key, string SourcePath, int StartLine, string FunctionName, string Fingerprint);
    internal sealed record R0CManifest(string Schema, R0CManifestEntry[] Entries);

    internal sealed partial class R0CRegistry
    {
        private readonly Process owner;
        private readonly int generation;
        private readonly Dictionary<FunctionLabelLine, int> ids;
        private readonly HashSet<string> cohortNames;
        private readonly QueryProgram[] programs;
        private readonly FlatQueryContext context;
        private readonly QueryValue[] arguments = new QueryValue[32];
        internal bool Enabled { get; private set; } = true;
        internal string Reason { get; private set; } = "Ready";
        internal int StepLimit { get; set; } = 1_000_000;
        internal long Attempts { get; private set; }
        internal long Completed { get; private set; }
        internal long Faults { get; private set; }
        internal long ContextCalls => context.CallsExecuted;
        internal B1HotMap Map { get; }

        internal R0CRegistry(Process owner, int generation, B1HotMap map, R0CManifest manifest)
        {
            this.owner = owner;
            this.generation = generation;
            Map = map;
            ValidateManifest(map, manifest);
            var catalog = new FlatQueryCatalog(map);
            catalog.Link(R0CKeys[0]);
            catalog.Link(R0CKeys[4]);
            programs = catalog.Snapshot();
            if (!programs.Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(R0CKeys))
                throw new InvalidOperationException("C0 closure is not the exact five-method cohort");
            context = new FlatQueryContext(programs);
            ids = new Dictionary<FunctionLabelLine, int>(ReferenceEqualityComparer.Instance);
            cohortNames = programs.Select(p => map.Functions[p.Key].Function.Name).ToHashSet(map.Environment.Compatibility.NameComparer);
            for (var id = 0; id < programs.Length; id++)
            {
                var entry = map.Functions[programs[id].Key];
                var label = owner.labelDic.GetNonEventLabel(entry.Function.Name)
                    ?? throw new InvalidOperationException("Legacy label missing: " + entry.Function.Name);
                var position = label.Position ?? throw new InvalidOperationException("Legacy label position missing");
                if (B1HotMap.Key(position.Filename, position.LineNo, label.LabelName) != entry.Key)
                    throw new InvalidOperationException("Legacy label identity mismatch: " + entry.Function.Name);
                if (label.Arg.Any(a => a.Identifier.IsReference))
                    throw new InvalidOperationException("REF parameter is not eligible: " + entry.Function.Name);
                ids.Add(label, id);
            }
        }

        internal static void ValidateManifest(B1HotMap map, R0CManifest manifest)
        {
            if (manifest.Schema != "emuera-r0c-registry-v1" || manifest.Entries.Length != R0CKeys.Length)
                throw new InvalidOperationException("Registry manifest schema/count mismatch");
            var entries = manifest.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
            if (!entries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(R0CKeys))
                throw new InvalidOperationException("Registry identity set mismatch");
            foreach (var key in R0CKeys)
            {
                var expected = entries[key];
                var actual = map.Functions[key];
                map.Analyze(actual);
                var path = Path.GetRelativePath(Program.ErbDir, actual.File.FileIdentity).Replace('\\', '/');
                var fingerprint = SourceFingerprint.FromBytes(actual.Source!.Value.Bytes).ToHexString();
                if (!string.Equals(expected.SourcePath.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase)
                    || expected.StartLine != actual.Function.Span.StartLine
                    || !string.Equals(expected.FunctionName, actual.Function.Name, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(expected.Fingerprint, fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("Registry source identity/fingerprint mismatch: " + actual.Function.Name);
            }
        }

        internal bool TryExecute(UserDefinedMethodTerm term, out SingleTerm? result)
        {
            result = null;
            if (!cohortNames.Contains(term.Call.TopLabel.LabelName))
                return false;
            if (!Enabled || owner.r0CGeneration != generation)
                throw new InvalidOperationException("Flat explicit FAIL: registry generation invalid");
            if (!ReferenceEquals(GlobalStatic.EMediator, owner.exm)
                || !ReferenceEquals(GlobalStatic.LabelDictionary, owner.labelDic))
                throw new InvalidOperationException("Flat explicit FAIL: host lifetime mismatch");
            if (!ids.TryGetValue(term.Call.TopLabel, out var id))
                throw new InvalidOperationException("Flat explicit FAIL: bound label identity mismatch");
            var program = programs[id];
            var transport = term.Argument;
            transport.SetTransporter(owner.exm); // Native evaluation order/default/type conversion, exactly once.
            for (var i = 0; i < program.Parameters.Length; i++)
                arguments[i] = program.Parameters[i].String
                    ? QueryValue.S(transport.TransporterStr[i])
                    : QueryValue.I(transport.TransporterInt[i]);
            Attempts++;
            PerformanceMetrics.RecordR0CFlatAttempt();
            var oldGuard = FlatQueryProof.ForbidLegacy;
            FlatQueryProof.ForbidLegacy = true;
            try
            {
                var value = context.Execute(id, arguments.AsSpan(0, program.Parameters.Length), StepLimit);
                result = program.ReturnsString ? SingleStrTerm.FromValue(value.Text ?? "") : SingleLongTerm.FromValue(value.Integer);
                Completed++;
                PerformanceMetrics.RecordR0CFlatComplete();
                return true;
            }
            catch
            {
                Faults++;
                PerformanceMetrics.RecordR0CFlatFault();
                throw; // Flat開始後は明示FAIL。Legacy retryは存在しない。
            }
            finally
            {
                FlatQueryProof.ForbidLegacy = oldGuard;
            }
        }

        internal int BindR0C2(UserDefinedMethodTerm term)
            => ids.TryGetValue(term.Call.TopLabel, out var id) ? id
                : throw new InvalidOperationException("R0-C2 prebind failed: " + term.Call.TopLabel.LabelName);

        internal long ExecuteR0C2Prebound(UserDefinedMethodTerm term, int id)
        {
            var program = programs[id];
            var transport = term.Argument;
            transport.SetTransporter(owner.exm);
            for (var i = 0; i < program.Parameters.Length; i++)
                arguments[i] = program.Parameters[i].String
                    ? QueryValue.S(transport.TransporterStr[i]) : QueryValue.I(transport.TransporterInt[i]);
            Attempts++;
            var oldGuard = FlatQueryProof.ForbidLegacy;
            FlatQueryProof.ForbidLegacy = true;
            try
            {
                var value = context.Execute(id, arguments.AsSpan(0, program.Parameters.Length), StepLimit);
                Completed++;
                return value.Integer;
            }
            catch { Faults++; throw; }
            finally { FlatQueryProof.ForbidLegacy = oldGuard; }
        }

        internal long ExecuteR0C2Core(int id, QueryValue[] inputs) => context.Execute(id, inputs, StepLimit).Integer;

        internal void Disable(string reason)
        {
            Enabled = false;
            Reason = reason;
        }
    }

    private R0CRegistry? r0CRegistry;
    private string r0CRegistryReason = "NotInitialized";
#if R0_D1
    private int r0D1Generation;
    private int r0CGeneration
    {
        get => r0D1Generation;
        set
        {
            // Revoke the shared generation before changing its identity, even for test rollback.
            if (value != r0D1Generation) r0CRegistry?.Disable("R0D1 generation changed");
            r0D1Generation = value;
        }
    }
#else
    private int r0CGeneration;
#endif
    private long r0CLegacyEntries;

    private void InitializeR0CRegistry(TextWriter log)
    {
        r0CGeneration++;
        r0CRegistry = null;
        r0CRegistryReason = "LegacyControl";
        if (!Program.R0CFlatCandidate)
        {
            PerformanceMetrics.ConfigureR0C(Program.R0CMode, false, "LegacyControl");
            return;
        }
        try
        {
            var manifest = JsonSerializer.Deserialize<R0CManifest>(File.ReadAllText(Program.R0CRegistryManifestPath!))
                ?? throw new InvalidOperationException("Registry manifest is empty");
            r0CRegistry = new R0CRegistry(this, r0CGeneration, new B1HotMap(Path.GetDirectoryName(Path.GetDirectoryName(Program.R0CRegistryManifestPath!))!), manifest);
#if R0_D1
            r0CRegistry.InstallBindings();
#endif
            r0CRegistryReason = "Ready";
            PerformanceMetrics.ConfigureR0C(Program.R0CMode, true, "Ready");
            log.WriteLine("R0C:Registry enabled cohort=5 generation=" + r0CGeneration);
        }
        catch (Exception ex)
        {
#if R0_D1
            r0CRegistry?.Disable("Startup admission failed");
            r0CRegistry = null;
#endif
            var reason = ex.GetType().Name + ": " + ex.Message;
            r0CRegistryReason = reason;
            PerformanceMetrics.ConfigureR0C(Program.R0CMode, false, reason);
            log.WriteLine("R0C:Registry disabled: " + reason);
        }
    }

    private void InvalidateR0CRegistry(string reason)
    {
        r0CGeneration++;
        r0CRegistry?.Disable(reason);
        PerformanceMetrics.ConfigureR0C(Program.R0CMode, false, reason);
    }

    private bool TryGetR0CValue(UserDefinedMethodTerm term, out SingleTerm? result)
    {
        result = null;
        return r0CRegistry?.TryExecute(term, out result) == true;
    }

    internal object WriteR0CManifest(string root)
    {
        var map = new B1HotMap(root);
        var entries = R0CKeys.Select(key =>
        {
            var entry = map.Functions[key];
            map.Analyze(entry);
            return new R0CManifestEntry(entry.Key,
                Path.GetRelativePath(Program.ErbDir, entry.File.FileIdentity).Replace('\\', '/'),
                entry.Function.Span.StartLine, entry.Function.Name,
                SourceFingerprint.FromBytes(entry.Source!.Value.Bytes).ToHexString());
        }).ToArray();
        var manifest = new R0CManifest("emuera-r0c-registry-v1", entries);
        B1Proof.WriteJson(Path.Combine(root, "evidence", "r0c-registry.json"), manifest);
        return new { Phase = "r0cmanifest", Entries = entries.Length, Cohort = entries.Select(e => e.FunctionName).ToArray() };
    }

    private sealed class CountingExpression(Type type, object value, int ordinal, List<int> order) : AExpression(type)
    {
        public override long GetIntValue(ExpressionMediator mediator) { order.Add(ordinal); return (long)value; }
        public override string GetStrValue(ExpressionMediator mediator) { order.Add(ordinal); return (string)value; }
    }

    internal object RunR0CIntegrationSelfTest(string root, string phase)
    {
#if R0_D1
        r0D1Prelinked = phase.StartsWith("r0d1", StringComparison.Ordinal) || phase.StartsWith("r0d2", StringComparison.Ordinal);
#endif
        if (r0CRegistry?.Enabled != true)
            throw new InvalidOperationException("R0-C registry did not enable at startup: " + r0CRegistryReason);
        var cases = new List<object>();
        long Flat(string name, params AExpression[] args)
        {
            var term = idDic.GetFunctionMethod(labelDic, name, args.ToList(), true) as UserDefinedMethodTerm
                ?? throw new InvalidOperationException("Typed method missing: " + name);
            return term.GetIntValue(exm);
        }
        long Legacy(string name, params AExpression[] args)
        {
            var saved = r0CRegistry;
#if R0_D1
            var linked = r0D1Prelinked;
            r0D1Prelinked = false;
#endif
            r0CRegistry = null;
            try { return (idDic.GetFunctionMethod(labelDic, name, args.ToList(), true) as SuperUserDefinedMethodTerm)!.GetIntValue(exm); }
            finally
            {
                r0CRegistry = saved;
#if R0_D1
                r0D1Prelinked = linked;
#endif
            }
        }
        static AExpression I(long value) => SingleLongTerm.FromValue(value);

        var expected = Legacy("HAVE_SKILL", I(-1), I(0));
        var beforeAttempt = r0CRegistry.Attempts;
        var actual = Flat("HAVE_SKILL", I(-1), I(0));
        if (actual != expected || r0CRegistry.Attempts != beforeAttempt + 1 || r0CRegistry.Completed != r0CRegistry.Attempts)
            throw new InvalidOperationException("Eligible seam did not use Flat");
        cases.Add(new { Name = "eligible exact identity uses Flat", Pass = true });

        var order = new List<int>();
        actual = Flat("HAVE_SKILL",
            new CountingExpression(typeof(long), -1L, 0, order),
            new CountingExpression(typeof(long), 0L, 1, order),
            new CountingExpression(typeof(long), 0L, 2, order),
            new CountingExpression(typeof(string), "", 3, order));
        if (actual != expected || !order.SequenceEqual([0, 1, 2, 3]))
            throw new InvalidOperationException("Native argument evaluation order/count mismatch");
        if (Flat("HAVE_SKILL", I(-1), I(0)) != expected)
            throw new InvalidOperationException("Default argument transport mismatch");
        cases.Add(new { Name = "arguments once/order/default", Pass = true, Order = order });

        var expression = OperatorMethodManager.ReduceBinaryTerm(OperatorCode.Plus,
            idDic.GetFunctionMethod(labelDic, "HAVE_SKILL", [I(-1), I(0)], true), I(7));
        if (expression.GetIntValue(exm) != expected + 7)
            throw new InvalidOperationException("Flat return was not consumed by caller expression");
        cases.Add(new { Name = "Flat return in caller expression", Pass = true });

        var legacyBefore = r0CLegacyEntries;
        _ = Legacy("BUST", I(-1));
        if (r0CLegacyEntries != legacyBefore + 1)
            throw new InvalidOperationException("Noneligible method did not use Legacy");
        cases.Add(new { Name = "noneligible uses Legacy", Pass = true });

        var bust = idDic.GetFunctionMethod(labelDic, "BUST", [I(-1)], true) as UserDefinedMethodTerm
            ?? throw new InvalidOperationException("BUST term missing");
        var reference = UserDefinedRefMethod.CreateR0CTest("R0C_REF_TEST", bust.Call);
        legacyBefore = r0CLegacyEntries;
        _ = new UserDefinedRefMethodTerm(reference, [I(-1)]).GetIntValue(exm);
        if (r0CLegacyEntries != legacyBefore + 1)
            throw new InvalidOperationException("REF method did not stay on Legacy");
        cases.Add(new { Name = "REF method uses Legacy", Pass = true });

        var savedRegistry = r0CRegistry;
        var manifest = JsonSerializer.Deserialize<R0CManifest>(File.ReadAllText(Program.R0CRegistryManifestPath!))!;
        var validationMap = new B1HotMap(root);
        bool Rejected(R0CManifest candidate)
        {
            try { R0CRegistry.ValidateManifest(validationMap, candidate); return false; }
            catch (InvalidOperationException) { return true; }
        }
        var changedIdentity = manifest with { Entries = manifest.Entries.Select((e, i) => i == 0 ? e with { SourcePath = "wrong.ERB" } : e).ToArray() };
        var changedFingerprint = manifest with { Entries = manifest.Entries.Select((e, i) => i == 0 ? e with { Fingerprint = new string('0', 64) } : e).ToArray() };
        if (!Rejected(changedIdentity) || !Rejected(changedFingerprint))
            throw new InvalidOperationException("Identity/fingerprint mismatch was admitted");
        r0CRegistry = null; // exact behavior after startup identity/fingerprint rejection
#if R0_D1
        var wasLinked = r0D1Prelinked;
        r0D1Prelinked = false; // Legacy-only simulation; D1 startup rejection is also tested through actual initialization.
#endif
        legacyBefore = r0CLegacyEntries;
        _ = (idDic.GetFunctionMethod(labelDic, "HAVE_SKILL", [I(-1), I(0)], true) as SuperUserDefinedMethodTerm)!.GetIntValue(exm);
        if (r0CLegacyEntries != legacyBefore + 1)
            throw new InvalidOperationException("Disabled registry did not stay on Legacy");
        r0CRegistry = savedRegistry;
#if R0_D1
        r0D1Prelinked = wasLinked;
#endif
        cases.Add(new { Name = "identity mismatch disables Flat startup", Pass = true });
        cases.Add(new { Name = "fingerprint mismatch disables Flat startup", Pass = true });

        var hash = GetBenchmarkStateHash();
        var oldReturn = state.MethodReturnValue;
        var oldLine = state.CurrentLine;
        var oldFunctionCount = state.functionCount;
        var callsBefore = FlatQueryProof.LegacyEntryAttempts;
        var faultsBefore = r0CRegistry.Faults;
        r0CRegistry.StepLimit = 0;
        var faulted = false;
        try { Flat("HAVE_SKILL", I(-1), I(0)); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("StepLimit", StringComparison.Ordinal)) { faulted = true; }
        finally { r0CRegistry.StepLimit = 1_000_000; }
        if (!faulted || r0CRegistry.Faults != faultsBefore + 1 || FlatQueryProof.LegacyEntryAttempts != callsBefore)
            throw new InvalidOperationException("Flat fault/no-retry invariant failed");
        cases.Add(new { Name = "Flat fault has no Legacy retry", Pass = true });

        var contextCallsBefore = savedRegistry.ContextCalls;
        var nestedExpected = Legacy("HAVE_SKILL", I(0), I(long.MaxValue));
        oldReturn = state.MethodReturnValue;
        oldLine = state.CurrentLine;
        oldFunctionCount = state.functionCount;
        var nestedActual = Flat("HAVE_SKILL", I(0), I(long.MaxValue));
        if (nestedActual != nestedExpected || savedRegistry.ContextCalls <= contextCallsBefore + 1)
            throw new InvalidOperationException("Nested static Flat call failed");
        cases.Add(new { Name = "nested static cohort call", Pass = true });

        var nativeScratch = new List<(MinorShift.Emuera.GameData.Variable.VariableToken Token, Array? Live, Array? Copy)>();
        foreach (var key in R0CKeys)
        {
            var entry = savedRegistry.Map.Functions[key];
            var label = labelDic.GetNonEventLabel(entry.Function.Name)!;
            foreach (var family in new[] { "ARG", "ARGS", "LOCAL", "LOCALS" })
            {
                var token = idDic.GetNextRuntimeLocalVariableToken(family, label);
                if (token != null && token.GetLength() > 0)
                {
                    var live = token.GetArray() as Array;
                    nativeScratch.Add((token, live, live?.Clone() as Array));
                }
            }
            var privateVariables = entry.Compile!.Function!.RuntimeMetadata!.PrivateVariables;
            foreach (var name in privateVariables.IsDefaultOrEmpty ? [] : privateVariables.Select(v => v.Name))
            {
                var token = label.GetPrivateVariable(name) ?? throw new InvalidOperationException("Private binding missing");
                var live = token.GetArray() as Array;
                nativeScratch.Add((token, live, live?.Clone() as Array));
            }
        }
        oldReturn = state.MethodReturnValue;
        oldLine = state.CurrentLine;
        oldFunctionCount = state.functionCount;
        _ = Flat("HAVE_SKILL", I(-1), I(0));
        if (nativeScratch.Any(pair => !ReferenceEquals(pair.Live, pair.Token.GetArray() as Array)
                || pair.Live != null && !pair.Live.Cast<object?>().SequenceEqual(pair.Copy!.Cast<object?>()))
            || !ReferenceEquals(oldReturn, state.MethodReturnValue) || !ReferenceEquals(oldLine, state.CurrentLine)
            || oldFunctionCount != state.functionCount || methodStack != 0)
            throw new InvalidOperationException("Flat changed Legacy function scratch");
        cases.Add(new { Name = "Legacy ARG/LOCAL/private scratch unchanged", Pass = true, Arrays = nativeScratch.Count });

        legacyBefore = r0CLegacyEntries;
        r0CGeneration++;
        var generationFault = false;
        try { Flat("HAVE_SKILL", I(-1), I(0)); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("generation", StringComparison.Ordinal)) { generationFault = true; }
        finally { r0CGeneration--; }
        if (!generationFault || r0CLegacyEntries != legacyBefore)
            throw new InvalidOperationException("Generation mismatch used Legacy fallback");
        cases.Add(new { Name = "runtime generation mismatch is explicit failure", Pass = true });

        if (hash != GetBenchmarkStateHash() || !ReferenceEquals(oldReturn, state.MethodReturnValue)
            || !ReferenceEquals(oldLine, state.CurrentLine) || oldFunctionCount != state.functionCount || methodStack != 0)
            throw new InvalidOperationException("Flat changed Legacy state/scratch anchors");
        if (FlatQueryProof.LegacyEntryAttempts != 0 || B1Proof.BridgeAttempts != 0)
            throw new InvalidOperationException("Flat entered Legacy or production bridge");
        B1Proof.WriteJson(Path.Combine(root, "raw", phase + ".cases.json"), cases);
        return new
        {
            Phase = phase, Pass = true, Cases = cases.Count,
            savedRegistry.Attempts, savedRegistry.Completed, savedRegistry.Faults,
            LegacyRetryAfterFlat = 0, FlatQueryProof.LegacyEntryAttempts,
            B1Proof.BridgeAttempts, StateUnchanged = true, ProductionBridgeUsed = "NO"
        };
    }
}
#endif
