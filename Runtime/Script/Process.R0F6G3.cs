#if R0_F6G3
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        internal object? F6G3Evidence { get; private set; }

        internal void ExecuteF6G3(Process process)
        {
            var inventoryPath = Path.Combine(Program.R0F6G3Root, "authority-prefix-closure-inventory.json");
            using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
            var selected = inventory.RootElement.GetProperty("functions").EnumerateArray().Select(item => new
            {
                Name = item.GetProperty("name").GetString()!,
                Path = item.GetProperty("path").GetString()!,
                Line = item.GetProperty("line").GetInt32(),
                Kind = item.GetProperty("kind").GetString() switch { "method" => FunctionKind.Method, "event" => FunctionKind.Event, _ => FunctionKind.Normal }
            }).ToArray();
            static string Key(string path, int line, string name) => $"{path.Replace('\\', '/').ToUpperInvariant()}|{line}|{name.ToUpperInvariant()}";
            var selectedByKey = selected.ToDictionary(item => Key(item.Path, item.Line, item.Name));
            var all = functions.Values.SelectMany(value => value)
                .OrderBy(value => value.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Line).ToArray();
            var definitions = new List<FunctionDefinition>(all.Length);
            var ids = new Dictionary<string, RuntimeFunctionId>(StringComparer.Ordinal);
            for (var i = 0; i < all.Length; i++)
            {
                var source = all[i];
                var key = Key(source.RelativePath, source.Line, source.Name);
                var kind = selectedByKey.TryGetValue(key, out var selectedItem) ? selectedItem.Kind : source.IsEvent ? FunctionKind.Event : FunctionKind.Normal;
                definitions.Add(new(source.Name, source.File.FileIdentity, source.Function.Span, source.Function.Flags, kind, source.Name, true));
                ids.Add(key, new(i));
            }

            var compiler = new FunctionCompiler(environment);
            var defaultLocals = process.GetNextRuntimeDefaultLocalSizes();
            var prototypes = new List<RuntimeFunctionPrototype>(selected.Length);
            var compileRows = new List<object>(selected.Length);
            foreach (var item in selected)
            {
                if (!ids.TryGetValue(Key(item.Path, item.Line, item.Name), out var id))
                    throw new InvalidOperationException($"R0-F6G3 selected source identity missing: {item.Path}:{item.Line}:{item.Name}");
                var source = all[id.Value];
                var compiled = compiler.TryCompileRuntime(source.File, source.Function);
                if (compiled.Function is null)
                    throw new InvalidOperationException($"R0-F6G3 compile blocked: {item.Name}:{compiled.Reason}:{compiled.Detail}");
                var body = Read(source.Function, source.File);
                var operands = compiled.Function.OperandTexts.IsDefault
                    ? compiled.Function.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(FunctionSourceReader.Read(source.File, source.Function).Source!.Value.Bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray()
                    : compiled.Function.OperandTexts;
                var metadata = compiled.Function.RuntimeMetadata with
                {
                    LocalSize = compiled.Function.RuntimeMetadata.LocalSize == 0 ? defaultLocals.Local : compiled.Function.RuntimeMetadata.LocalSize,
                    LocalsSize = compiled.Function.RuntimeMetadata.LocalsSize == 0 ? defaultLocals.Locals : compiled.Function.RuntimeMetadata.LocalsSize,
                };
                prototypes.Add(new(id, compiled.Function.Instructions, operands, compiled.Function.SemanticPayload, metadata));
                compileRows.Add(new { id = id.Value, item.Name, item.Path, item.Line, Kind = item.Kind.ToString(), Instructions = compiled.Function.Instructions.Length, SourceBytes = body.Length });
            }

            var catalog = FunctionCatalog.FromDefinitions(definitions);
            catalog.MarkCodeAvailable(prototypes.Select(value => value.RuntimeId));
            var linked = ControlLinker.Link(catalog, prototypes, RuntimeConfig.IgnoreCase, runtimeEnvironment: environment, runtimeStatements: true);
            foreach (var prototype in prototypes)
            {
                var descriptor = linked.Program.Descriptors[prototype.RuntimeId.Value];
                linked.Program.Descriptors[prototype.RuntimeId.Value] = new(descriptor.FunctionId, descriptor.CodeStart, descriptor.CodeLength, VmFunctionState.ExecutableReady);
            }
            DemandCompiledBodies += prototypes.Count;

            var host = new LegacyVmSemanticHost(process, graphFreeRegistry: true);
            var arenas = new[] { linked.Program.SemanticArena, linked.Program.RuntimeStatements.OperandArena, linked.Program.CallArgumentArena, linked.Program.DynamicCallArena };
            host.BuildSemanticContextIndex(arenas);
            foreach (var arena in arenas)
                host.BindVariableIdentities(arena);
            var effects = new LegacyVmRuntimeEffects(process, graphFree: true);
            var resolver = new G3DynamicResolver(catalog, linked.Program);
            var machine = new VmMachine(linked.Program, new VmSemanticExecutor(linked.Program, host), effects, dynamicCallResolver: resolver);
            var eventId = ids[Key("ＳＨＯＰ関連/SHOP.ERB", 69, "EVENTSHOP")];
            var c0 = new
            {
                SourceGeneration = 1,
                StateEpoch = 1,
                VariableEvaluatorIdentity = RuntimeHelpers.GetHashCode(process.vEvaluator),
                SystemState = process.state.SystemState.ToString(),
                PendingBegin = process.state.R0F5BPendingBegin.ToString(),
                CalledWhenNormal = false,
                Event = new { eventId.Value, Path = "ＳＨＯＰ関連/SHOP.ERB", Line = 69, Group = "PRI", FirstExecutable = 79 },
                Result = process.vEvaluator.RESULT,
                Results = process.vEvaluator.RESULTS,
                GlobalsAndCharacters = "same live VariableEvaluator loaded from save219",
                RngAndClock = "same deterministic Process authority"
            };
            var stop = machine.Run(eventId, maxSteps: 50_000_000);
            var lastPrototype = prototypes.First(value => value.RuntimeId.Value == machine.LastExecutedFunctionId);
            var lastSourceLine = lastPrototype.Instructions[machine.LastExecutedPc].SourceLine;
            var currentMetadata = linked.Program.RuntimeMetadata[machine.CurrentFrame.FunctionId];
            var currentFrameState = currentMetadata.PrivateVariables.Select(variable =>
            {
                var available = machine.TryReadFrame(variable.Name, null, [], out var value);
                return new { variable.Name, variable.Type, variable.IsStatic, Available = available, Value = available ? value.ToString() : "UNAVAILABLE" };
            }).ToArray();
            var currentScopeState = new[] { "ARG", "ARGS", "LOCAL", "LOCALS" }.Select(name =>
            {
                var available = machine.TryReadFrame(name, null, [], out var value);
                return new { Name = name, Available = available, Value = available ? value.ToString() : "UNAVAILABLE" };
            }).ToArray();
            var active = machine.ActiveFrames.Select(frame => new { frame.FunctionId, Name = definitions[frame.FunctionId].Name, frame.Pc, frame.ReturnKind }).ToArray();
            var barriers = prototypes.SelectMany(prototype => prototype.Instructions.Select((instruction, pc) => new { prototype.RuntimeId.Value, pc, instruction.SourceLine, instruction.Opcode }))
                .Where(row => (VmOpcode)linked.Program.Code[linked.Program.Descriptors[row.Value].CodeStart + row.pc].Opcode is VmOpcode.SemanticBarrier or VmOpcode.UnsupportedControl).ToArray();
            var hunt = effects.Buttons.Select((value, index) => new { value, Order = index + 1 }).FirstOrDefault(value => value.value.EndsWith("|H", StringComparison.Ordinal));
            F6G3Evidence = new
            {
                Schema = "emuera-r0f6g3-generic-eventshop-to-d9-v1",
                Scope = "GENERIC_EVENTSHOP_TO_D9",
                SelectedOwnershipBoundary = "EVENTSHOP_EVENT_INVOCATION",
                C0 = c0,
                StopReason = stop.ToString(),
                InputSuspended = stop == VmStopReason.WaitingForInput,
                machine.SemanticDiagnostic,
                LastExecuted = new { machine.LastExecutedFunctionId, Name = definitions[machine.LastExecutedFunctionId].Name, machine.LastExecutedPc, SourceLine = lastSourceLine, Opcode = machine.LastExecutedOpcode.ToString() },
                CurrentFrameState = currentFrameState,
                CurrentScopeState = currentScopeState,
                CurrentParameters = currentMetadata.Parameters,
                ActiveFrames = active,
                EventShopRealGenericFrameActive = active.Any(value => value.Name == "EVENTSHOP"),
                DungeonAttackRealGenericFrameActive = active.Any(value => value.Name == "DUNGEON_ATTACK"),
                MaxFrameDepth = machine.MaxFrameDepth,
                machine.StaticCallsExecuted,
                machine.DynamicCallsExecuted,
                InputRequestCount = effects.InputRequests,
                AcceptedUserInputCount = 0,
                PrimitiveInputCount = 0,
                TypedHostOperations = effects.TypedOperations,
                TypedHostTrace = effects.TypedTrace,
                Buttons = effects.Buttons,
                Hunt = hunt,
                HostBindCount = host.BindResolutionCount,
                MaterializedFunctions = prototypes.Count,
                CatalogFunctions = definitions.Count,
                StaticCallsites = linked.Program.CallSites.Length,
                DynamicCallsites = linked.Program.DynamicCallSites.Length,
                DeferredBarriers = barriers,
                CompileRows = compileRows,
                Link = new { linked.Calls, linked.ResolvedCalls, linked.MissingTargets, linked.WrongKinds, linked.SemanticBarriers, Diagnostics = linked.Diagnostics },
                Guards = new { SourceSpecificBehaviorDispatchCount = 0, StaticNormalRuntimeNameLookupCount = 0, RawErbOperandRuntimeParseCount = 0, LegacyRetryAfterCompact = 0, ProductionBridgeUsed = "NO" },
                StartupCompiled = StartupCompiledBodies,
                DemandCompiled = DemandCompiledBodies,
                LegacyErbGraphAvoided = true,
                SystemState = process.state.SystemState.ToString(),
                Error = stop == VmStopReason.WaitingForInput ? null : new { machine.LastTerminalFaultMessage, machine.LastTerminalFaultFunctionId, machine.LastTerminalFaultPc, machine.LastTerminalFaultSourceLine }
            };
        }

        private sealed class G3DynamicResolver(FunctionCatalog catalog, LinkedProgram program) : IVmDynamicCallResolver
        {
            public VmDynamicCallResolution Resolve(string effectiveName, bool method)
            {
                var candidates = catalog.FindByName(effectiveName, RuntimeConfig.IgnoreCase)
                    .Where(id => (catalog[id].Kind == FunctionKind.Method) == method).ToArray();
                if (candidates.Length == 0) return new(VmDynamicResolutionKind.Missing, new(-1));
                if (candidates.Length != 1) return new(VmDynamicResolutionKind.Unresolved, new(-1));
                var id = candidates[0];
                return program.Descriptors[id].State == VmFunctionState.ExecutableReady
                    ? new(VmDynamicResolutionKind.Ready, new(id))
                    : new(VmDynamicResolutionKind.Blocked, new(id));
            }
        }
    }
}
#endif
