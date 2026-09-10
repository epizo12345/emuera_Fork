#if R0_D1
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    // Only the diagnostic FlatCandidate opts in. The headless benchmark selects once per batch.
    private bool r0D1Prelinked = true;

    internal void InvalidateR0D1Host()
    {
        if (r0CRegistry is not null)
            InvalidateR0CRegistry("R0D1 host lifetime changed");
    }

    internal sealed partial class R0CRegistry
    {
        internal sealed partial class Binding
        {
            private readonly R0CRegistry registry;
            private readonly QueryProgram program;
            internal readonly int ProgramId;
            internal readonly int Generation;
            internal Binding(R0CRegistry registry, int id)
            {
                this.registry = registry;
                program = registry.programs[id];
                ProgramId = id;
                Generation = registry.generation;
#if R0_D2
                ValidateD2Arguments();
#endif
            }

            private void RequireLive(Process caller)
            {
                if (!ReferenceEquals(caller, registry.owner) || !registry.Enabled)
                    throw new InvalidOperationException("Flat explicit FAIL: prelinked generation/owner invalid");
            }

            internal SingleTerm Execute(Process caller, UserDefinedMethodTerm term)
            {
#if R0_D2
                if (caller.r0D2Direct) return ExecuteDirectArguments(caller, term);
#endif
                RequireLive(caller);
                var transport = term.Argument;
                transport.SetTransporter(caller.exm); // Unchanged native order/defaults/conversions.
                // Argument expressions may reenter the host. Never start Flat after revocation.
                RequireLive(caller);
                for (var i = 0; i < program.Parameters.Length; i++)
                    registry.arguments[i] = program.Parameters[i].String
                        ? QueryValue.S(transport.TransporterStr[i]) : QueryValue.I(transport.TransporterInt[i]);
                registry.Attempts++;
                PerformanceMetrics.RecordR0CFlatAttempt();
                var oldGuard = FlatQueryProof.ForbidLegacy;
                FlatQueryProof.ForbidLegacy = true;
                try
                {
                    var value = registry.context.Execute(ProgramId, registry.arguments.AsSpan(0, program.Parameters.Length), registry.StepLimit);
                    var result = program.ReturnsString ? (SingleTerm)SingleStrTerm.FromValue(value.Text ?? "") : SingleLongTerm.FromValue(value.Integer);
                    registry.Completed++;
                    PerformanceMetrics.RecordR0CFlatComplete();
                    return result;
                }
                catch
                {
                    registry.Faults++;
                    PerformanceMetrics.RecordR0CFlatFault();
                    throw; // No return-to-Legacy path after a Flat attempt.
                }
                finally { FlatQueryProof.ForbidLegacy = oldGuard; }
            }
        }

        internal void InstallBindings()
        {
            // Constructor has already checked exact manifest, admission, REF and closed static cohort.
            if (!Enabled || owner.r0CGeneration != generation
                || !ReferenceEquals(GlobalStatic.Process, owner)
                || !ReferenceEquals(GlobalStatic.EMediator, owner.exm)
                || !ReferenceEquals(GlobalStatic.LabelDictionary, owner.labelDic))
                throw new InvalidOperationException("R0D1 startup host/generation mismatch");
            // Allocate/validate everything before publication; never publish a partial cohort.
            var bindings = ids.Select(pair => (Label: pair.Key, Handle: new Binding(this, pair.Value))).ToArray();
            foreach (var pair in bindings)
                pair.Label.R0D1Binding = pair.Handle;
        }
    }

    internal object RunR0D1SelfTest(string root, string phase)
    {
        r0D1Prelinked = true;
        var common = RunR0CIntegrationSelfTest(root, phase);
        InitializeR0CRegistry(TextWriter.Null); // The common stale-generation test intentionally revoked its registry.
        if (r0CRegistry?.Enabled != true) throw new InvalidOperationException("D1 test re-admission failed");
        var cases = new List<object>();
        static AExpression I(long value) => SingleLongTerm.FromValue(value);
        UserDefinedMethodTerm Term(string name, params AExpression[] args)
            => (UserDefinedMethodTerm)idDic.GetFunctionMethod(labelDic, name, args.ToList(), true);
        var term = Term("HAVE_SKILL", I(-1), I(0));
        _ = term.GetIntValue(exm); // Prove the owner-mismatch test starts from a live, executable binding.
        var oldRegistry = r0CRegistry!;
        var attempts = oldRegistry.Attempts;
        var legacy = r0CLegacyEntries;
        var rng = vEvaluator.GetR0C2RngHash();
        var stateHash = GetBenchmarkStateHash();
        void Reject(string name, Action action)
        {
            bool rejected = false;
            try { action(); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || oldRegistry.Attempts != attempts || r0CLegacyEntries != legacy)
                throw new InvalidOperationException("R0D1 safety test failed: " + name);
            cases.Add(new { Name = name, Pass = true, FlatStarted = false, LegacyRetried = false });
        }
        Reject("binding cannot execute with a different Process owner", () => term.Call.TopLabel.R0D1Binding.Execute(new Process(console), term));
        var manifestForTests = JsonSerializer.Deserialize<R0CManifest>(File.ReadAllText(Program.R0CRegistryManifestPath!))!;
        foreach (var kind in new[] { "SourcePath", "StartLine", "FunctionName", "Fingerprint" })
        {
            var changed = manifestForTests with { Entries = manifestForTests.Entries.Select((e, i) => i != 0 ? e : kind switch
            {
                "SourcePath" => e with { SourcePath = "not-admitted.ERB" },
                "StartLine" => e with { StartLine = e.StartLine + 1 },
                "FunctionName" => e with { FunctionName = "NOT_ADMITTED" },
                _ => e with { Fingerprint = new string('0', 64) }
            }).ToArray() };
            Reject("registry constructor rejects " + kind, () => _ = new R0CRegistry(this, r0CGeneration, oldRegistry.Map, changed));
        }

        // A generation is never resurrected by restoring the host object.
        GlobalStatic.EMediator = null!;
        GlobalStatic.EMediator = exm;
        Reject("mediator change revokes old binding permanently", () => term.GetIntValue(exm));
        void Rebind()
        {
            var manifest = JsonSerializer.Deserialize<R0CManifest>(File.ReadAllText(Program.R0CRegistryManifestPath!))!;
            r0CRegistry = new R0CRegistry(this, r0CGeneration, oldRegistry.Map, manifest);
            r0CRegistry.InstallBindings();
        }
        Rebind();
        var stale = term.Call.TopLabel.R0D1Binding;
        GlobalStatic.LabelDictionary = null!;
        GlobalStatic.LabelDictionary = labelDic;
        Reject("label host change revokes binding", () => term.GetIntValue(exm));
        Rebind();
        Reject("old handle remains rejected after rebinding", () => stale.Execute(this, term));
        GlobalStatic.Process = null!;
        GlobalStatic.Process = this;
        Reject("Process host change revokes binding", () => term.GetIntValue(exm));
        Rebind();
        var before = r0CRegistry!.Attempts;
        var reentrant = Term("HAVE_SKILL", new R0D1InvalidateArgument(this), I(0));
        Reject("argument-side invalidation prevents Flat start", () => reentrant.GetIntValue(exm));
        if (r0CRegistry.Attempts != before || rng != vEvaluator.GetR0C2RngHash() || stateHash != GetBenchmarkStateHash())
            throw new InvalidOperationException("R0D1 reentrant/state invariant failed");
        B1Proof.WriteJson(Path.Combine(root, "raw", phase + ".safety.json"), cases);
        return new { CommonIntegration = common, SafetyCases = cases, Pass = true, RngUnchanged = true,
            FaultInjectionCount = 1, UnexpectedFlatFaults = 0, LegacyRetryAfterFlat = 0, ProductionBridgeUsed = "NO" };
    }

    private sealed class R0D1InvalidateArgument(Process owner) : AExpression(typeof(long))
    {
        public override long GetIntValue(MinorShift.Emuera.Runtime.Script.Statements.Expression.ExpressionMediator mediator)
        { owner.InvalidateR0CRegistry("argument reentry test"); return -1; }
    }

    internal object RunR0D1ReloadTest(string phase)
    {
        // The B1 host never calls the GUI's Initialize (which normally assigns this field).
        // Supply the real owner so the unchanged reload can reach its native ReadAnyKey boundary.
        typeof(MinorShift.Emuera.GameView.EmueraConsole)
            .GetField("process", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(console, this);
        r0D1Prelinked = true;
        var term = (UserDefinedMethodTerm)idDic.GetFunctionMethod(labelDic, "HAVE_SKILL",
            [SingleLongTerm.FromValue(-1), SingleLongTerm.FromValue(0)], true);
        var registry = r0CRegistry ?? throw new InvalidOperationException("Registry missing");
        var handle = term.Call.TopLabel.R0D1Binding;
        var generation = r0CGeneration;
        var path = Path.Combine(Program.ErbDir, "関数", "組み込み関数", "キャラクタデータ参照／ABL", "HAVE_SKILL.ERB");
        if (phase.StartsWith("r0d1reloadall-", StringComparison.Ordinal)) ReloadErbAll().GetAwaiter().GetResult();
        else if (phase.StartsWith("r0d1reloadpartial-", StringComparison.Ordinal)) ReloadPartialErb([path]).GetAwaiter().GetResult();
        else ReloadErbFolder(Path.GetDirectoryName(path)!).GetAwaiter().GetResult();
        var attempts = registry.Attempts; var legacy = r0CLegacyEntries;
        bool rejected = false;
        try { handle.Execute(this, term); }
        catch (InvalidOperationException) { rejected = true; }
        var current = labelDic.GetNonEventLabel("HAVE_SKILL")!;
        if (!rejected || registry.Attempts != attempts || r0CLegacyEntries != legacy
            || registry.Enabled || generation == r0CGeneration
            || ReferenceEquals(current, term.Call.TopLabel) || current.R0D1Binding is not null)
            throw new InvalidOperationException("R0D1 reload left a live/stale binding");
        return new { Phase = phase, Pass = true, PreviousGeneration = generation, CurrentGeneration = r0CGeneration,
            SaveLoaded = false, Scope = "actual source reload and binding lifetime; no game script execution",
            OldLabelReplaced = true, OldHandleRejected = true, NewLabelBinding = "null",
            FlatFaults = registry.Faults, LegacyRetryAfterFlat = 0, ProductionBridgeUsed = "NO" };
    }
}
#endif
