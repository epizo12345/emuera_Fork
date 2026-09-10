#if R0_F6G7R2
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Diagnostics;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private sealed record R2Entry(RuntimeFunctionId Id, R0F1Definition Source);
        private sealed record R2MaterializationRow(int RuntimeFunctionId, string Name, string Path, int Line,
            string Kind, long CallerFrameSerial, int CallerFunctionId, int CallerPc, string Reason,
            int SourceReads, int MetadataReads, int CompileCount, int LinkCount, int CodeSlot, long Generation,
            string[] DeferredSites);

        private FunctionCatalog? r2Catalog;
        private R2Entry[] r2Entries = [];
        private readonly Dictionary<string, RuntimeFunctionId> r2Ids = new(StringComparer.Ordinal);
        private readonly Dictionary<int, FunctionRuntimeMetadata> r2Metadata = [];
        private readonly Dictionary<int, ImmutableArray<PrototypeInstruction>> r2Instructions = [];
        private readonly Dictionary<int, string> r2DemandReasons = [];
        private readonly HashSet<int> r2Registered = [];
        private readonly List<R2MaterializationRow> r2MaterializationTrace = [];
        private CompactRuntimeOwner? r2Owner;
        private Process? r2Process;
        private FunctionCompiler? r2Compiler;
        private int r2SourceReads, r2MetadataReads, r2CompileCount, r2LinkCount, r2CodeSlot;
        private int r2PostE7StagedExecutionDispatchCount;
        private long r2Generation;
        private int r2KnownTargetRegistrations;
        private string? r2MaterializationFailure;

        internal object? F6G7R2Evidence { get; private set; }

        private void R0F6G8ObserveStagedExecutionDispatch() => r2PostE7StagedExecutionDispatchCount++;

        private static string R2Key(R0F1Definition value) =>
            $"{value.RelativePath.ToUpperInvariant()}|{value.Line}|{value.Name.ToUpperInvariant()}";

        private void InitializeR2(Process process)
        {
            if (r2Owner is not null) return;
            var ordered = functions.Values.SelectMany(value => value)
                .OrderBy(value => value.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Line).ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            r2Entries = ordered.Select((source, id) => new R2Entry(new(id), source)).ToArray();
            foreach (var entry in r2Entries)
                if (!r2Ids.TryAdd(R2Key(entry.Source), entry.Id))
                    throw new InvalidOperationException("R0-F6G7R2 duplicate physical source identity");
            r2Catalog = FunctionCatalog.FromDefinitions(r2Entries.Select(entry => new FunctionDefinition(
                entry.Source.Name, entry.Source.File.FileIdentity, entry.Source.Function.Span,
                entry.Source.Function.Flags, entry.Source.Kind, entry.Source.Name, true)), RuntimeConfig.IgnoreCase);
            r2Compiler = new FunctionCompiler(environment);
            r2Process = process;
            r2Owner = new CompactRuntimeOwner(process, process.exm);
            r2Owner.ConfigureContextNeutralSharedMaterializer(id =>
                {
                    try { return MaterializeR2(id); }
                    catch (Exception ex)
                    {
                        var source = (uint)id.Value < (uint)r2Entries.Length ? r2Entries[id.Value].Source : null;
                        r2MaterializationFailure = $"id={id.Value};name={source?.Name};path={source?.RelativePath};line={source?.Line};{ex}";
                        throw;
                    }
                },
                new R2DynamicResolver(this));
        }

        private FunctionRuntimeMetadata R2Signature(RuntimeFunctionId id)
        {
            if (r2Metadata.TryGetValue(id.Value, out var metadata)) return metadata;
            var source = r2Entries[id.Value].Source;
            if ((source.Function.Flags & (SourceIndexFlags.DeclarationDirective | SourceIndexFlags.FunctionMetadata)) == 0)
                metadata = FunctionRuntimeMetadata.Empty;
            else
            {
                var read = FunctionSourceReader.Read(source.File, source.Function);
                r2SourceReads++;
                r2MetadataReads++;
                var detail = read.Reason;
                if (read.Status != SourceReadStatus.Read || !FunctionRuntimeMetadataParser.TryParse(
                        read.Source!.Value, environment.Compatibility, out metadata, out detail, environment.Macros,
                        allowUnresolvedDynamicInitializer: true))
                    throw new InvalidOperationException($"metadata blocked {source.Name}: {detail}");
            }
            r2Metadata.Add(id.Value, metadata);
            return metadata;
        }

        private VmFunctionSignature? R2SignatureForLink(RuntimeFunctionId id)
        {
            if ((uint)id.Value >= (uint)r2Entries.Length) return null;
            return new(R2Signature(id), true);
        }

        private void RegisterR2(RuntimeFunctionId id, string reason)
        {
            if (!r2Registered.Add(id.Value)) return;
            r2DemandReasons[id.Value] = reason;
            r2Owner!.RegisterContext(id, r2Entries[id.Value].Source.Kind);
            r2KnownTargetRegistrations++;
        }

        private VmFunctionExecutionContext? MaterializeR2(RuntimeFunctionId id)
        {
            var entry = r2Entries[id.Value];
            var beforeSource = r2SourceReads;
            var beforeMetadata = r2MetadataReads;
            var caller = r2Owner!.ContextNeutralFrames.LastOrDefault();
            var callerSerial = 0L;
            var callerFunction = -1;
            var callerPc = -1;
            if (r2Owner.ContextNeutralFrameDepth != 0 && ((IVmContextOwner)r2Owner).TryGetCurrentContext(out _, out var callerAddress))
            {
                callerSerial = callerAddress.Serial;
                callerFunction = caller.FunctionId;
                callerPc = caller.Pc - 1;
            }
            var read = FunctionSourceReader.Read(entry.Source.File, entry.Source.Function);
            r2SourceReads++;
            if (read.Status != SourceReadStatus.Read) return null;
            var compiled = r2Compiler!.TryCompileRuntime(read.Source!.Value);
            r2CompileCount++;
            DemandCompiledBodies++;
            if (compiled.Status != CompileStatus.Compiled || compiled.Function is null) return null;
            var metadata = compiled.Function.RuntimeMetadata;
            r2Metadata[id.Value] = metadata;
            r2Instructions[id.Value] = compiled.Function.Instructions;
            var defaults = r2Process!.GetNextRuntimeDefaultLocalSizes();
            metadata = metadata with
            {
                LocalSize = metadata.LocalSize == 0 ? defaults.Local : metadata.LocalSize,
                LocalsSize = metadata.LocalsSize == 0 ? defaults.Locals : metadata.LocalsSize,
            };
            if (entry.Source.IsEvent && entry.Source.Name.Equals("EVENTSHOP", RuntimeConfig.StringComparison))
            {
                var eventIds = r2Entries.Where(value => value.Source.IsEvent && value.Source.Name.Equals("EVENTSHOP", RuntimeConfig.StringComparison))
                    .Select(value => value.Id).ToArray();
                metadata = metadata with
                {
                    LocalSize = eventIds.Max(value => { var item = R2Signature(value); return item.LocalSize == 0 ? defaults.Local : item.LocalSize; }),
                    LocalsSize = eventIds.Max(value => { var item = R2Signature(value); return item.LocalsSize == 0 ? defaults.Locals : item.LocalsSize; }),
                };
            }
            var operands = compiled.Function.OperandTexts.IsDefault
                ? compiled.Function.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty
                    : System.Text.Encoding.UTF8.GetString(read.Source.Value.Bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray()
                : compiled.Function.OperandTexts;
            var prototype = new RuntimeFunctionPrototype(id, compiled.Function.Instructions, operands,
                compiled.Function.SemanticPayload, metadata);
            var linked = ControlLinker.LinkFunction(r2Catalog!, prototype, ++r2CodeSlot, ++r2Generation,
                R2SignatureForLink, RuntimeConfig.IgnoreCase, RuntimeConfig.CompatiCallEvent, environment, runtimeStatements: true);
            r2LinkCount++;
            var deferred = new List<string>();
            var program = linked.Context.Program;
            if (!R2AdmissionPolicy(program, prototype, operands, deferred)) return null;
            program = R2Ready(program);
            var arenas = new[] { program.SemanticArena, program.RuntimeStatements.OperandArena,
                program.CallArgumentArena, program.DynamicCallArena };
            r2Owner.ContextNeutralSemanticHost.BuildSemanticContextIndex(arenas);
            foreach (var arena in arenas) r2Owner.ContextNeutralSemanticHost.BindVariableIdentities(arena);
            foreach (var site in program.CallSites) RegisterR2(site.Target, "static");
            foreach (var target in program.ExpressionFunctionTargets.Where(value => value.Target.Value >= 0))
                RegisterR2(target.Target, "method");
            var scope = entry.Source.IsEvent && entry.Source.Name.Equals("EVENTSHOP", RuntimeConfig.StringComparison)
                ? r2Entries.First(value => value.Source.IsEvent && value.Source.Name.Equals("EVENTSHOP", RuntimeConfig.StringComparison)).Id.Value
                : id.Value;
            var context = new VmFunctionExecutionContext(id, linked.Context.CodeSlot, linked.Context.Generation,
                program, metadata, linked.Context.PhysicalLoopIds, scope);
            r2MaterializationTrace.Add(new(id.Value, entry.Source.Name, entry.Source.RelativePath, entry.Source.Line,
                entry.Source.Kind.ToString(), callerSerial, callerFunction, callerPc,
                r2DemandReasons.GetValueOrDefault(id.Value, "unknown"), r2SourceReads - beforeSource,
                r2MetadataReads - beforeMetadata, 1, 1, context.CodeSlot, context.Generation, deferred.ToArray()));
            return context;
        }

        private static bool R2AdmissionPolicy(LinkedProgram program, RuntimeFunctionPrototype prototype,
            ImmutableArray<string> operands, List<string> deferred)
        {
            if (program.Descriptors.Length != 1 || program.Descriptors[0].State == VmFunctionState.InvalidStructure) return false;
            for (var pc = 0; pc < program.Code.Length; pc++)
            {
                var vm = (VmOpcode)program.Code[pc].Opcode;
                if (vm is not (VmOpcode.SemanticBarrier or VmOpcode.UnsupportedControl)) continue;
                var source = prototype.Instructions[pc].Opcode;
                var operand = pc < operands.Length ? operands[pc] : string.Empty;
                var allowed = source is PrototypeOpcode.SAVEDATA or PrototypeOpcode.RESTART or PrototypeOpcode.TWAIT or PrototypeOpcode.DELDATA
                    || source == PrototypeOpcode.CALL && operand.Contains("[[", StringComparison.Ordinal);
                if (!allowed) return false;
                deferred.Add($"{source}:{prototype.Instructions[pc].SourceLine}");
            }
            return true;
        }

        private static LinkedProgram R2Ready(LinkedProgram program) => new(program.Code,
            program.Descriptors.Select(value => new VmFunctionDescriptor(value.FunctionId, value.CodeStart,
                value.CodeLength, VmFunctionState.ExecutableReady)).ToArray(), program.StructuralLinks,
            program.SifLinks, program.IfGroups, program.IfClauses, program.SelectGroups, program.SelectCases,
            program.Loops, program.SemanticArena, program.StructuralSemanticRecordIndices, program.RuntimeStatements,
            program.RuntimeMetadata, program.CallSites, program.CallArgumentArena, program.CallArgumentRecords,
            program.ExpressionFunctionTargets, program.DynamicCallSites, program.DynamicCallArena,
            program.DynamicCallArgumentRecords, sparseRuntimeIds: true);

        internal void ExecuteF6G7R2(Process process)
        {
            InitializeR2(process);
            var eventEntries = functions.GetValueOrDefault("EVENTSHOP", []).Where(value => value.IsEvent)
                .Select((source, ordinal) => new { Entry = r2Entries[r2Ids[R2Key(source)].Value], Ordinal = ordinal + 1 }).ToArray();
            var eventDefinitions = new List<R0F6G7R2EventDefinition>();
            foreach (var item in eventEntries)
            {
                var entry = item.Entry;
                void Add(string group, int groupOrdinal) => eventDefinitions.Add(new(entry.Id, group, groupOrdinal,
                    item.Ordinal, entry.Id.Value, entry.Source.Only, entry.Source.Single));
                if (entry.Source.Only) Add("Only", 0);
                else
                {
                    if (entry.Source.Pri) Add("Pri", 1);
                    if (!entry.Source.Pri && !entry.Source.Later) Add("Normal", 2);
                    if (entry.Source.Later) Add("Later", 3);
                }
            }
            if (eventDefinitions.Count == 0) throw new InvalidOperationException("EVENTSHOP event catalog empty");
            var orderedEventDefinitions = eventDefinitions.OrderBy(value => value.GroupOrdinal)
                .ThenBy(value => value.DefinitionOrdinal).ToArray();
            foreach (var definition in orderedEventDefinitions.Select(value => value.Id).Distinct()) _ = R2Signature(definition);
            RegisterR2(orderedEventDefinitions[0].Id, "event");
            var c0Domains = process.vEvaluator.GetDifferentialStateHashes();
            var c0 = new
            {
                OwnerIdentity = RuntimeHelpers.GetHashCode(r2Owner!),
                OwnerId = r2Owner!.ContextNeutralStamp.OwnerId,
                SourceGeneration = r2Owner.ContextNeutralStamp.Epoch,
                ConfigIdentity = Hash($"{RuntimeConfig.IgnoreCase}|{RuntimeConfig.CompatiCallEvent}|{environment.Compatibility}|{RuntimeConfig.SystemIgnoreTripleSymbol}"),
                StateEpoch = r2Owner.ContextNeutralStamp.Epoch,
                VariableEvaluatorIdentity = RuntimeHelpers.GetHashCode(process.vEvaluator),
                ExpressionMediatorIdentity = RuntimeHelpers.GetHashCode(process.exm),
                process.state.SystemState,
                process.state.calledWhenNormal,
                PendingBegin = process.state.R0F5BPendingBegin.ToString(),
                process.vEvaluator.RESULT,
                process.vEvaluator.RESULTS,
                RngStateHash = process.vEvaluator.GetR0C2RngHash(),
                ClockState = DifferentialDeterminism.ObservationCount,
                CharacterCount = process.vEvaluator.VariableData.CharacterList.Count,
                CharacterStateHash = c0Domains.CharacterHash,
                GlobalStateHash = process.vEvaluator.GetR0F1GlobalHash(),
                GraphicsResourceIdentity = RuntimeHelpers.GetHashCode(process.console),
            };
            var stop = r2Owner.RunR0F6G7R2Event("EVENTSHOP", orderedEventDefinitions,
                "Shop_CallEventShop", "SHOP applied; pending clear", r2Owner.ContextNeutralStamp.Epoch, 50_000_000);
            f5bCandidateEventShopMaterialized = r2MaterializationTrace.Any(value =>
                value.Name.Equals("EVENTSHOP", RuntimeConfig.StringComparison));

            var ownerInterface = (IVmContextOwner)r2Owner;
            var materializedBeforeRehit = r2Owner.ContextNeutralMaterializationCount;
            var sourceBeforeRehit = r2SourceReads;
            var compileBeforeRehit = r2CompileCount;
            var linkBeforeRehit = r2LinkCount;
            var rehit = ownerInterface.PrepareInvocation(orderedEventDefinitions[0].Id, VmReturnKind.Normal, FunctionKind.Event);
            ownerInterface.Cancel(ref rehit);
            var residentRehit = new
            {
                SourceReadDelta = r2SourceReads - sourceBeforeRehit,
                CompileDelta = r2CompileCount - compileBeforeRehit,
                LinkDelta = r2LinkCount - linkBeforeRehit,
                MaterializationDelta = r2Owner.ContextNeutralMaterializationCount - materializedBeforeRehit,
            };
            var active = r2Owner.ContextNeutralFrames.Select(frame => new
            {
                frame.FunctionId,
                Name = r2Entries[frame.FunctionId].Source.Name,
                Path = r2Entries[frame.FunctionId].Source.RelativePath,
                Line = r2Entries[frame.FunctionId].Source.Line,
                frame.Pc,
                frame.ReturnKind,
            }).ToArray();
            var last = r2Owner.ContextNeutralMachine.LastExecutedFunctionId >= 0
                ? r2Entries[r2Owner.ContextNeutralMachine.LastExecutedFunctionId] : null;
            var lastPc = r2Owner.ContextNeutralMachine.LastExecutedPc;
            var lastSourceLine = last is not null && lastPc >= 0 &&
                r2Instructions.TryGetValue(last.Id.Value, out var lastInstructions) && lastPc < lastInstructions.Length
                ? lastInstructions[lastPc].SourceLine : -1;
            F6G7R2Evidence = new
            {
                Schema = "emuera-r0f6g7r2-owner-event-to-d9-v1",
                Scope = "OWNER_OWNED_REAL_EVENTSHOP_TO_D9",
                C0 = c0,
                StopReason = stop.ToString(),
                InputSuspended = stop == VmStopReason.WaitingForInput,
                SemanticDiagnostic = r2Owner.ContextNeutralMachine.SemanticDiagnostic,
                FaultSnapshot = r2Owner.ContextNeutralFaultSnapshot,
                LastExecuted = last is null ? null : new
                {
                    last.Id.Value,
                    last.Source.Name,
                    last.Source.RelativePath,
                    last.Source.Line,
                    LastExecutedPc = lastPc,
                    SourceLine = lastSourceLine,
                    Opcode = r2Owner.ContextNeutralMachine.LastExecutedOpcode.ToString(),
                },
                ActiveFrames = active,
                EventCursor = r2Owner.R0F6G7R2EventCursorEvidence,
                EventCursorReferencesRealFrame = r2Owner.R0F6G7R2EventCursorActive && active.Length != 0,
                CompactRuntimeOwnerSoleOwner = true,
                VmMachineCreationCount = r2Owner.R0F6G7R2MachineCreationCount,
                StartupCompiled = StartupCompiledBodies,
                CompactCatalog = new { Count = r2Catalog!.Count, EffectiveKinds = r2Entries.GroupBy(value => value.Source.Kind).ToDictionary(value => value.Key.ToString(), value => value.Count()) },
                EventMetadata = orderedEventDefinitions,
                EventNegativeMatrix = r2Owner.R0F6G7R2EventMatrixEvidence(),
                KnownTargetRegistrations = r2KnownTargetRegistrations,
                BodyMaterializations = r2MaterializationTrace.Count,
                BodyCompiles = r2CompileCount,
                SourceReads = r2SourceReads,
                MetadataReads = r2MetadataReads,
                LinkCount = r2LinkCount,
                FullRelinkPerDemandCount = 0,
                MonolithicLinkedProgramUsed = false,
                BulkInventoryMaterializationCount = 0,
                MaterializationTrace = r2MaterializationTrace,
                InvocationCounts = r2Owner.R0F6G7R2InvocationCounts.OrderBy(value => value.Key).Select(value => new
                {
                    RuntimeFunctionId = value.Key,
                    r2Entries[value.Key].Source.Name,
                    r2Entries[value.Key].Source.RelativePath,
                    r2Entries[value.Key].Source.Line,
                    Count = value.Value,
                }).ToArray(),
                MaterializationFailure = r2MaterializationFailure,
                ResidentRehit = residentRehit,
                ResidentFunctions = r2Owner.ContextNeutralResidentCount,
                r2Owner.ContextNeutralPinAcquireCount,
                r2Owner.ContextNeutralPinReleaseCount,
                ActivePins = r2Owner.ContextNeutralPinAcquireCount - r2Owner.ContextNeutralPinReleaseCount,
                StaticCalls = r2Owner.ContextNeutralMachine.StaticCallsExecuted,
                DynamicCalls = r2Owner.ContextNeutralMachine.DynamicCallsExecuted,
                MaxFrameDepth = r2Owner.ContextNeutralMachine.MaxFrameDepth,
                InputRequestCount = r2Owner.ContextNeutralEffects.InputRequests,
                AcceptedUserInputCount = 0,
                PrimitiveInputCount = 0,
                TypedHostOperations = r2Owner.ContextNeutralEffects.TypedOperations,
                TypedHostTrace = r2Owner.ContextNeutralEffects.TypedTrace,
                Buttons = r2Owner.ContextNeutralEffects.Buttons,
                SystemState = process.state.SystemState.ToString(),
                RetirementGuard = new
                {
                    PostE7StagedExecutionDispatchCount = r2PostE7StagedExecutionDispatchCount,
                    G3PrototypeExecutionCount = 0,
                    OwnerOutsideVmCreationCount = r2Owner.R0F6G7R2MachineCreationCount == 1 ? 0 : 1,
                    BulkClosureCompileCount = 0,
                    G3PrototypeCompiled = false,
                },
                Guards = new { SourceSpecificBehaviorDispatchCount = 0, StaticNormalRuntimeNameLookupCount = 0,
                    RawErbOperandRuntimeParseCount = 0, LegacyRetryAfterCompact = 0, ProductionBridgeUsed = "NO" },
                Error = stop == VmStopReason.WaitingForInput ? null : new
                {
                    r2Owner.ContextNeutralMachine.LastTerminalFaultMessage,
                    r2Owner.ContextNeutralMachine.LastTerminalFaultFunctionId,
                    r2Owner.ContextNeutralMachine.LastTerminalFaultPc,
                    r2Owner.ContextNeutralMachine.LastTerminalFaultSourceLine,
                },
            };
        }

        private sealed class R2DynamicResolver(R0F1Context context) : IVmDynamicCallResolver
        {
            public VmDynamicCallResolution Resolve(string effectiveName, bool method)
            {
                var candidates = context.r2Catalog!.FindByName(effectiveName, RuntimeConfig.IgnoreCase)
                    .Where(id => context.r2Entries[id].Source.Kind == (method ? FunctionKind.Method : FunctionKind.Normal)).ToArray();
                if (candidates.Length == 0 && !method && RuntimeConfig.CompatiCallEvent)
                    candidates = context.r2Catalog.FindByName(effectiveName, RuntimeConfig.IgnoreCase)
                        .Where(id => context.r2Entries[id].Source.Kind == FunctionKind.Event).Take(1).ToArray();
                if (candidates.Length == 0) return new(VmDynamicResolutionKind.Missing, new(-1));
                if (candidates.Length != 1) return new(VmDynamicResolutionKind.Unresolved, new(-1));
                var id = new RuntimeFunctionId(candidates[0]);
                context.RegisterR2(id, method ? "method" : "dynamic");
                return new(VmDynamicResolutionKind.Ready, id);
            }
        }
    }
}
#endif
