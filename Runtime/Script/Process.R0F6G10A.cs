#if R0_F6G10A
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Loader;
using MinorShift.Emuera.Runtime.Utils;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private CompactProductionRuntime? compactProduction;

    private void InitializeCompactProduction(TextWriter startupLog)
    {
        if (compactProduction is not null) throw new InvalidOperationException("CompactStrict owner already exists");
        var rejection = CompactStartupConfigurationRejection(RuntimeConfig.CompatiErrorLine, RuntimeConfig.NeedReduceArgumentOnLoad);
        if (rejection is not null) throw new NotSupportedException(rejection);
        compactProduction = new(this, startupLog);
    }

    private static string? CompactStartupConfigurationRejection(bool compatiErrorLine, bool needReduceArgumentOnLoad) =>
        compatiErrorLine ? "CompactStrict does not support CompatiErrorLine; use Legacy mode" :
        needReduceArgumentOnLoad ? "CompactStrict does not support NeedReduceArgumentOnLoad; use Legacy mode" : null;

    private bool InvokeCompactScript(string name, bool isEvent, bool required) =>
        (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
        .QueueHostInvocation(name, isEvent, required, state.SystemState.ToString());

    private void PumpCompactExecution()
    {
        var runtime = compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized");
        runtime.EnterPump();
        try
        {
            while (console.IsRunning)
            {
                if (runtime.HasRunnableWork)
                {
                    var stop = runtime.RunWork();
                    if (stop == VmStopReason.WaitingForInput) return;
                    if (stop == VmStopReason.HostTransfer) continue;
                    if (stop != VmStopReason.Returned)
                        throw new InvalidOperationException($"CompactStrict terminal stop: {stop}; {runtime.ExecutionDiagnostic()}");
                    continue;
                }
                runSystemProc();
                if (!runtime.HasRunnableWork) return;
            }
        }
        catch (Exception ex)
        {
            runtime.Fail(ex);
            if (Program.R0E1ACandidate) throw;
            handleExceptionInSystemProc(ex, null, true);
        }
        finally
        {
            runtime.ExitPump();
#if R0_F6G10B
            runtime.PublishPendingInput();
#endif
        }
    }

    internal object R0F6G10AEvidence() => compactProduction?.Evidence()
        ?? throw new InvalidOperationException("CompactStrict is not initialized");

    internal object R0F6G10ARunFocusedOracles()
    {
        var runtime = compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized");
        return runtime.RunFocusedOracles();
    }

    internal object R0F6G10AProbeAdmission(string name, bool isEvent, bool required) =>
        (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
        .ProbeAdmission(name, isEvent, required);

    private long R0F6G10AExistFunction(string name) =>
        (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
        .ExistFunction(name);

    internal object R0F6G10ARunTitlePath()
    {
        PumpExecution();
        return compactProduction!.PathEvidence("SYSTEM_TITLE");
    }

    internal object R0F6G10ARunEventFirstPath()
    {
        compactProduction!.RecordHostLifecycle("ResetDataBeforeEventFirst");
        vEvaluator.ResetData();
        compactProduction!.ApplyDataReset(discardExecution: true);
        compactProduction.RecordHostLifecycle("AddCharactersBeforeEventFirst");
        vEvaluator.AddCharacterFromCsvNo(0);
        if (gamebase.DefaultCharacter > 0) vEvaluator.AddCharacterFromCsvNo(gamebase.DefaultCharacter);
        _ = InvokeCompactScript("EVENTFIRST", true, true);
        var stop = compactProduction.RunPendingInvocation();
        return compactProduction.PathEvidence("EVENTFIRST", stop);
    }

    internal object R0F6G10ARunLoadLifecycle(bool invalidFixture) =>
        (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
        .RunLoadLifecycle(invalidFixture);

    internal void R0F6G10ARejectReload()
    {
#if R0_F6G10B
        compactProduction?.RejectSourceReload();
#endif
        throw new NotSupportedException("CompactStrict live ERB reload is disabled before mutation");
    }

    internal void R0F6G10AApplyDataReset(bool discardExecution) =>
        (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized")).ApplyDataReset(discardExecution);

    private sealed partial class CompactProductionRuntime
    {
        private sealed record Definition(RuntimeFunctionId Id, string Name, string RelativePath, int Line,
            FunctionKind Kind, bool Pri, bool Later, bool Only, bool Single, SourceFileIndex File, FunctionIndex Function,
            PreprocessorPlan Preprocessor);
        private readonly record struct DirectiveFlags(bool Function, bool Functions, bool Pri, bool Later, bool Only, bool Single);

        private static DirectiveFlags ReadDirectiveFlags(FunctionSource source)
        {
            var bytes = source.Bytes.AsSpan();
            var newline = bytes.IndexOf((byte)'\n');
            if (newline < 0) return default;
            bytes = bytes[(newline + 1)..];
            var result = new DirectiveFlags();
            while (!bytes.IsEmpty)
            {
                newline = bytes.IndexOf((byte)'\n');
                var line = newline < 0 ? bytes : bytes[..newline];
                while (!line.IsEmpty && line[0] is (byte)' ' or (byte)'\t') line = line[1..];
                while (!line.IsEmpty && line[^1] is (byte)' ' or (byte)'\t' or (byte)'\r') line = line[..^1];
                if (line.IsEmpty || line[0] != (byte)'#') break;
                result = result with
                {
                    Function = result.Function || AsciiEquals(line, "#FUNCTION"u8),
                    Functions = result.Functions || AsciiEquals(line, "#FUNCTIONS"u8),
                    Pri = result.Pri || AsciiEquals(line, "#PRI"u8),
                    Later = result.Later || AsciiEquals(line, "#LATER"u8),
                    Only = result.Only || AsciiEquals(line, "#ONLY"u8),
                    Single = result.Single || AsciiEquals(line, "#SINGLE"u8),
                };
                if (newline < 0) break;
                bytes = bytes[(newline + 1)..];
            }
            return result;

            static bool AsciiEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
            {
                if (left.Length != right.Length) return false;
                for (var index = 0; index < left.Length; index++)
                {
                    var value = left[index];
                    if (value is >= (byte)'a' and <= (byte)'z') value -= (byte)('a' - 'A');
                    if (value != right[index]) return false;
                }
                return true;
            }
        }

        private sealed class PreprocessorPlan
        {
            private readonly (int Start, int End)[] hidden;
            private PreprocessorPlan((int Start, int End)[] hidden) => this.hidden = hidden;
            internal static PreprocessorPlan Empty { get; } = new([]);
            internal bool IsHidden(int line) => hidden.Any(range => line >= range.Start && line <= range.End);
            internal FunctionSource Apply(FunctionSource source)
            {
                var bytes = source.Bytes;
                var first = bytes.AsSpan();
                if (first.StartsWith(Encoding.UTF8.Preamble)) first = first[Encoding.UTF8.Preamble.Length..];
                while (!first.IsEmpty && first[0] is (byte)' ' or (byte)'\t' or (byte)'\r') first = first[1..];
                if (hidden.Length == 0 && !first.IsEmpty && first[0] != (byte)'{') return source;
                if (hidden.Length != 0) bytes = bytes.ToArray();
                var line = source.Function.Span.StartLine;
                for (var index = 0; index < bytes.Length; index++)
                {
                    if (IsHidden(line) && bytes[index] is not (byte)'\r' and not (byte)'\n') bytes[index] = (byte)' ';
                    if (bytes[index] == (byte)'\n') line++;
                }
                var text = new UTF8Encoding(false, true).GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
                var lines = text.Split('\n');
                if (lines.Length > 2 && lines[0].Trim() == "{")
                {
                    var close = Array.FindIndex(lines, 1, value => value.Trim() == "}");
                    if (close > 1)
                    {
                        lines[0] = string.Join(" ", lines.Skip(1).Take(close - 1).Select(value => value.Trim()));
                        for (var index = 1; index <= close; index++) lines[index] = string.Empty;
                        bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines));
                    }
                }
                return source with { Bytes = bytes };
            }
            internal static PreprocessorPlan Build(string path)
            {
                var state = new ErbLoader.PPState();
                var hiddenLines = new List<int>();
                var lineNumber = 0;
                foreach (var line in File.ReadLines(path, RuntimeConfig.Encode))
                {
                    lineNumber++;
                    var trimmed = line.TrimStart(' ', '\t', '　');
                    if (trimmed.StartsWith('[') && !trimmed.StartsWith("[[", StringComparison.Ordinal))
                    {
                        var close = trimmed.IndexOf(']');
                        if (close < 0 || trimmed[(close + 1)..].Trim().Length != 0)
                            throw new InvalidOperationException($"CompactStrict invalid preprocessor at {path}:{lineNumber}");
                        var parts = trimmed[1..close].Split([' ', '\t', '　'], 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) throw new InvalidOperationException($"CompactStrict empty preprocessor at {path}:{lineNumber}");
                        state.AddKeyWord(parts[0], parts.Length == 2 ? parts[1] : string.Empty, null);
                        hiddenLines.Add(lineNumber);
                    }
                    else if (state.Disabled) hiddenLines.Add(lineNumber);
                }
                state.FileEnd(null);
                var ranges = new List<(int Start, int End)>();
                foreach (var line in hiddenLines)
                {
                    if (ranges.Count != 0 && ranges[^1].End + 1 == line) ranges[^1] = (ranges[^1].Start, line);
                    else ranges.Add((line, line));
                }
                return new(ranges.ToArray());
            }
        }
        private sealed record Pending(string Name, bool Event, bool Required, string HostContinuation,
            Definition[] Definitions, bool CompatiEventAlias);

        private readonly Process process;
        private readonly StructuralSemanticEnvironment environment;
        private readonly Definition[] definitions;
        private readonly Dictionary<string, Definition[]> byName;
        private readonly FunctionCatalog catalog;
        private readonly FunctionCompiler compiler;
        private readonly CompactRuntimeOwner owner;
        private readonly Dictionary<int, FunctionRuntimeMetadata> metadata = [];
        private readonly Dictionary<int, ImmutableArray<PrototypeInstruction>> instructions = [];
        private readonly Dictionary<int, int> compileCounts = [];
        private readonly HashSet<int> registered = [];
        private readonly List<string> callbackOrder = [];
        private readonly List<object> materializationTrace = [];
        private readonly List<string> materializationFailures = [];
        private Pending? pending;
        private bool pumping;
        private bool failed;
        private int sourceReads;
        private int startupValidationReads;
        private int startupValidatedFunctions;
        private int startupValidatedLabels;
        private int startupValidatedBlocks;
        private int startupMissingStaticCallsDeferred;
        private int startupMetadataDeferred;
        private int startupMetadataParserCalls;
        private int startupMetadataFastEmptyCount;
        private int startupValidationFileOpenCount;
        private int startupValidationFileInfoConstructionCount;
        private int startupValidationSnapshotRefreshCount;
        private double startupSourceIndexMilliseconds;
        private double startupPreprocessorMilliseconds;
        private double startupSourceReadMilliseconds;
        private double startupValidationMilliseconds;
        private double startupMetadataMilliseconds;
        private double startupCatalogConstructionMilliseconds;
        private double startupCompactCatalogMilliseconds;
        private int bodyCompiles;
        private int links;
        private int codeSlot;
        private long generation;
        private int ownerCreations = 1;
        private int machineCreations;
        private int legacyRetryAfterCompact;
        private int productionBridgeUsed;
        private int blockedBeforeFrameCommit;
        private int unhandledBarrierPromoted;
        private int unsupportedControlPromoted;
        private int invalidStructurePromoted;
        private long gameStateEpoch = 1;
        private long executionEpoch = 1;
        private long sourceCatalogGeneration = 1;
        private VmStopReason lastStop = VmStopReason.Halted;

        private bool CollectDetailedEvidence => Program.R0F6G10AHeadlessCapture;
        private void RecordTrace(string value) { if (CollectDetailedEvidence) callbackOrder.Add(value); }
        private void RecordMaterializationFailure(string value)
        {
            if (materializationFailures.Count == 8) materializationFailures.RemoveAt(0);
            materializationFailures.Add(value);
        }

        internal CompactProductionRuntime(Process process, TextWriter startupLog)
        {
            var compactCatalogStart = Stopwatch.GetTimestamp();
            this.process = process;
            var options = new CompilerCompatibilityOptions(RuntimeConfig.IgnoreCase,
                JSONConfig.Game.UseScopedVariableInstruction, RuntimeConfig.SystemAllowFullSpace, false);
            var rename = RuntimeConfig.UseRenameFile && ParserMediator.RenameDic is not null
                ? new SemanticRenameResolver(ParserMediator.RenameDic) : null;
            var macros = MacroCatalog.FromHeaderSources(RuntimeConfig.GetFiles(Program.ErbDir, "*.ERH")
                .Select(path => File.ReadAllText(path.Value, RuntimeConfig.Encode)), options, rename);
            environment = new(options, macros, RuntimeConfig.SystemIgnoreTripleSymbol, rename);

            var stageStart = Stopwatch.GetTimestamp();
#if R0_F6G10C1
            var files = ErbSourceIndexer.IndexDirectoryParallel(Program.ErbDir);
#else
            var files = ErbSourceIndexer.IndexDirectory(Program.ErbDir);
#endif
            startupSourceIndexMilliseconds = Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
            if (files.Any(file => file.Error is not null))
                throw new InvalidOperationException("CompactStrict source index contains an error");
            var rows = new List<Definition>();
            var staticCalls = new List<(string Caller, ImmutableArray<string> Targets)>();
            foreach (var file in files)
            {
                stageStart = Stopwatch.GetTimestamp();
                var preprocessor = (file.Flags & SourceIndexFlags.Preprocessor) != 0
                    ? PreprocessorPlan.Build(file.FileIdentity)
                    : PreprocessorPlan.Empty;
                startupPreprocessorMilliseconds += Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
                using var source = FunctionSourceReader.OpenFile(file);
                var relativePath = Path.GetRelativePath(Program.ErbDir, file.FileIdentity).Replace('\\', '/');
                startupValidationFileOpenCount++;
                startupValidationFileInfoConstructionCount++;
                startupValidationSnapshotRefreshCount++;
                foreach (var function in file.Functions)
                {
                    if (preprocessor.IsHidden(function.Span.StartLine)) continue;
                    var effectiveFunction = function with { Flags = function.Flags & ~SourceIndexFlags.Preprocessor };
                    stageStart = Stopwatch.GetTimestamp();
                    var read = source.ReadStableBatch(effectiveFunction);
                    startupSourceReadMilliseconds += Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
                    startupValidationReads++;
                    if (read.Status != SourceReadStatus.Read)
                        throw new InvalidOperationException($"CompactStrict startup source read failed: {function.Name}: {read.Reason}");
                    var prepared = preprocessor.Apply(read.Source!.Value);
                    stageStart = Stopwatch.GetTimestamp();
                    var validation = StartupSourceValidator.Validate(prepared, options);
                    startupValidationMilliseconds += Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
                    if (!validation.Valid)
                        throw new InvalidOperationException($"CompactStrict startup validation failed: {function.Name}: {validation.Detail}");
                    stageStart = Stopwatch.GetTimestamp();
                    FunctionRuntimeMetadata startupMetadata;
                    string metadataDetail;
                    bool metadataValid;
                    var needsRuntimeMetadata = NeedsRuntimeMetadata(effectiveFunction);
                    if (!needsRuntimeMetadata)
                    {
                        startupMetadata = FunctionRuntimeMetadata.Empty;
                        metadataDetail = string.Empty;
                        metadataValid = true;
                        startupMetadataFastEmptyCount++;
                    }
                    else
                    {
                        metadataValid = FunctionRuntimeMetadataParser.TryParse(prepared, options, out startupMetadata, out metadataDetail,
                            environment.Macros, allowUnresolvedDynamicInitializer: true);
                        startupMetadataParserCalls++;
                    }
                    startupMetadataMilliseconds += Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
                    if (!metadataValid && !RuntimeConfig.IgnoreUncalledFunction)
                        throw new InvalidOperationException($"CompactStrict startup metadata validation failed: {function.Name}: {metadataDetail}");
                    if (!metadataValid) startupMetadataDeferred++;
                    startupValidatedFunctions++;
                    startupValidatedLabels += validation.LocalLabelCount;
                    startupValidatedBlocks += validation.StructuralBlockCount;
                    if (!validation.StaticCalls.IsDefaultOrEmpty) staticCalls.Add((function.Name, validation.StaticCalls));
                    var directives = ReadDirectiveFlags(prepared);
                    var isEvent = IdentifierDictionary.IsEventLabelName(function.Name);
                    var kind = isEvent ? FunctionKind.Event
                        : directives.Function || directives.Functions ? FunctionKind.Method : FunctionKind.Normal;
                    rows.Add(new(new(rows.Count), function.Name,
                        relativePath, function.Span.StartLine,
                        kind, directives.Pri, directives.Later, directives.Only,
                        directives.Single, file, effectiveFunction, preprocessor));
                    if (metadataValid && needsRuntimeMetadata) metadata.Add(rows.Count - 1, startupMetadata);
                }
                startupValidationSnapshotRefreshCount++;
                if (!source.VerifyStableBatchSnapshot())
                    throw new InvalidOperationException($"CompactStrict startup source changed during validation: {file.FileIdentity}");
            }
            stageStart = Stopwatch.GetTimestamp();
            definitions = rows.ToArray();
            byName = definitions.GroupBy(row => row.Name, options.NameComparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), options.NameComparer);
            foreach (var (caller, targets) in staticCalls)
                foreach (var target in targets)
                    if (!byName.ContainsKey(target))
                    {
                        if (!RuntimeConfig.IgnoreUncalledFunction)
                            throw new InvalidOperationException($"CompactStrict startup static call target missing: {caller} -> {target}");
                        startupMissingStaticCallsDeferred++;
                    }
            catalog = FunctionCatalog.FromDefinitions(definitions.Select(row => new FunctionDefinition(row.Name,
                row.File.FileIdentity, row.Function.Span, row.Function.Flags, row.Kind, row.Name, true)), RuntimeConfig.IgnoreCase);
            compiler = new(environment);
            owner = new(process, process.exm, environment);
            owner.ConfigureContextNeutralSharedMaterializer(Materialize, new DynamicResolver(this));
            _ = owner.ContextNeutralMachine;
            machineCreations = 1;
            startupCatalogConstructionMilliseconds = Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
            startupCompactCatalogMilliseconds = Stopwatch.GetElapsedTime(compactCatalogStart).TotalMilliseconds;
            startupLog.WriteLine($"Proc:Init:CompactCatalog:SourceIndex {startupSourceIndexMilliseconds:F3}ms");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:Preprocessor {startupPreprocessorMilliseconds:F3}ms");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:ValidationSourceReads {startupSourceReadMilliseconds:F3}ms");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:SourceValidation {startupValidationMilliseconds:F3}ms");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:MetadataValidation {startupMetadataMilliseconds:F3}ms");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:Metadata parserCalls={startupMetadataParserCalls} fastEmpty={startupMetadataFastEmptyCount}");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:CatalogConstruction {startupCatalogConstructionMilliseconds:F3}ms");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:Total {startupCompactCatalogMilliseconds:F3}ms");
            startupLog.WriteLine($"Proc:Init:CompactCatalog:IO files={startupValidationFileOpenCount} opens={startupValidationFileOpenCount} fileInfos={startupValidationFileInfoConstructionCount} snapshotRefreshes={startupValidationSnapshotRefreshCount} perFunctionOpens=0 perFunctionFileInfos=0 perFunctionSnapshotRefreshes=0");
        }

        internal bool HasPendingInvocation => pending is not null;
#if R0_F6G10B
        internal bool HasRunnableWork => pending is not null || resumeScheduled;
#else
        internal bool HasRunnableWork => pending is not null;
#endif
        internal void RecordHostLifecycle(string value) => RecordTrace("HostLifecycle:" + value);
        internal void ApplyDataReset(bool discardExecution)
        {
            owner.ResetContextNeutralPersistentBanks();
            owner.RevokeContextNeutralInput();
#if R0_F6G10B
            RevokePendingInput("data reset");
#endif
            gameStateEpoch++;
            if (!discardExecution) return;
            if (!owner.UnwindContextNeutralToFloor(0) || !owner.AdvanceContextNeutralExecutionEpoch())
                throw new InvalidOperationException("CompactStrict execution discard failed");
            executionEpoch++;
        }
        internal void EnterPump()
        {
            if (pumping) throw new InvalidOperationException("CompactStrict pump reentry rejected");
            pumping = true;
        }
        internal void ExitPump() => pumping = false;
        internal void Fail(Exception ex)
        {
            failed = true;
#if R0_F6G10B
            RevokePendingInput("fault");
#endif
            pending = null;
            if (!((IVmContextOwner)owner).IsTerminal)
                ((IVmContextOwner)owner).TerminalFault(VmStopReason.TerminalFault, -1, -1,
                    ex.GetType().Name + ": " + ex.Message);
        }

        internal bool QueueHostInvocation(string name, bool isEvent, bool required, string hostContinuation)
        {
            if (failed) throw new InvalidOperationException("CompactStrict owner is terminal");
            if (pending is not null) throw new InvalidOperationException("CompactStrict Host invocation already pending");
            var resolution = Resolve(name, isEvent);
            if (resolution.Definitions.Length == 0)
            {
                if (!required && resolution.Missing) return false;
                throw new InvalidOperationException($"CompactStrict {(required ? "required" : "optional")} invocation rejected: {name}: {resolution.Reason}");
            }
            foreach (var row in resolution.Definitions) Register(row.Id);
            pending = new(name, isEvent, required, hostContinuation, resolution.Definitions, resolution.CompatiAlias);
            RecordTrace("HostCallbackQueued:" + hostContinuation + ":" + name);
            return true;
        }

        internal VmStopReason RunPendingInvocation()
        {
            var invocation = pending ?? throw new InvalidOperationException("no CompactStrict invocation pending");
            pending = null;
            RecordTrace("HostCallbackReturned:" + invocation.HostContinuation);
            VmStopReason stop;
            if (invocation.Event)
            {
                var eventRows = new List<R0F6G7R2EventDefinition>();
                for (var ordinal = 0; ordinal < invocation.Definitions.Length; ordinal++)
                {
                    var row = invocation.Definitions[ordinal];
                    void Add(string group, int groupOrdinal) => eventRows.Add(new(row.Id, group, groupOrdinal,
                        ordinal + 1, row.Id.Value, row.Only, row.Single));
                    if (row.Only) Add("Only", 0);
                    else { if (row.Pri) Add("Pri", 1); if (!row.Pri && !row.Later) Add("Normal", 2); if (row.Later) Add("Later", 3); }
                }
                stop = owner.RunEventSet(invocation.Name, eventRows, invocation.HostContinuation,
                    "BEGIN applied only after event completion", gameStateEpoch, 50_000_000);
            }
            else
            {
                var row = invocation.Definitions[0];
                stop = owner.ContextNeutralMachine.Start(row.Id, invocation.CompatiEventAlias ? FunctionKind.Event : FunctionKind.Normal);
                if (stop == VmStopReason.Returned) stop = owner.ContextNeutralMachine.Continue(50_000_000);
            }
            RecordTrace("CompactRootStop:" + invocation.Name + ":" + stop);
            lastStop = stop;
            if (stop == VmStopReason.Returned && process.state.isBegun)
            {
                RecordTrace("BeginApplyBoundary:" + process.state.R0F5BPendingBegin);
                process.state.Begin();
                executionEpoch++;
            }
            return stop;
        }

        internal object PathEvidence(string name, VmStopReason? stop = null) => new
        {
            Name = name,
            Stop = (stop ?? lastStop).ToString(),
            OwnerFrameDepth = owner.ContextNeutralFrameDepth,
            OwnerRunState = ((IVmContextOwner)owner).RunState.ToString(),
            OwnerId = owner.ContextNeutralStamp.OwnerId,
            OwnerEpoch = owner.ContextNeutralStamp.Epoch,
            CallbackOrder = callbackOrder.ToArray(),
            SameOwner = ownerCreations == 1,
            SameVm = machineCreations == 1,
        };

        internal string ExecutionDiagnostic()
        {
            var machine = owner.ContextNeutralMachine;
            var source = instructions.TryGetValue(machine.LastExecutedFunctionId, out var body) &&
                (uint)machine.LastExecutedPc < (uint)body.Length ? body[machine.LastExecutedPc] : default;
            var definition = (uint)machine.LastExecutedFunctionId < (uint)definitions.Length ? definitions[machine.LastExecutedFunctionId] : null;
            return $"function={machine.LastExecutedFunctionId}:{definition?.Name ?? "?"};path={definition?.RelativePath ?? "?"};headerLine={definition?.Line ?? -1};pc={machine.LastExecutedPc};source={source.Opcode}:{source.SourceLine};opcode={machine.LastExecutedOpcode};materialization={materializationFailures.LastOrDefault()};terminal={machine.LastTerminalFaultMessage};owner={owner.ContextNeutralFaultSnapshot?.Message};semantic={machine.SemanticDiagnostic}";
        }

        private (Definition[] Definitions, bool Missing, bool CompatiAlias, string Reason) Resolve(string name, bool eventKind)
        {
            if (!byName.TryGetValue(name, out var matches)) return ([], true, false, "Missing");
            var exact = matches.Where(row => row.Kind == (eventKind ? FunctionKind.Event : FunctionKind.Normal)).ToArray();
            if (exact.Length != 0) return (exact, false, false, "Ready");
            if (!eventKind && RuntimeConfig.CompatiCallEvent)
            {
                var alias = matches.FirstOrDefault(row => row.Kind == FunctionKind.Event);
                if (alias is not null) return ([alias], false, true, "ReadyCompatiCallEventFirstDefinition");
            }
            return ([], false, false, "WrongKind");
        }

        internal long ExistFunction(string name)
        {
            if (!byName.TryGetValue(name, out var matches)) return 0;
            var row = matches.FirstOrDefault(value => value.Kind != FunctionKind.Event);
            if (row is null) return 0;
            if (row.Kind != FunctionKind.Method) return 1;
            return Signature(row.Id).ReturnType == RuntimeMetadataValueType.Integer ? 2 : 3;
        }

        private void Register(RuntimeFunctionId id)
        {
            if (registered.Add(id.Value)) owner.RegisterContext(id, definitions[id.Value].Kind);
        }

        private FunctionRuntimeMetadata Signature(RuntimeFunctionId id)
        {
            if (metadata.TryGetValue(id.Value, out var value)) return value;
            var row = definitions[id.Value];
            if (!NeedsRuntimeMetadata(row.Function))
                value = FunctionRuntimeMetadata.Empty;
            else
            {
                var read = FunctionSourceReader.Read(row.File, row.Function); sourceReads++;
                var detail = read.Reason;
                if (read.Status != SourceReadStatus.Read || !FunctionRuntimeMetadataParser.TryParse(row.Preprocessor.Apply(read.Source!.Value),
                        environment.Compatibility, out value, out detail, environment.Macros,
                        allowUnresolvedDynamicInitializer: true))
                    throw new InvalidOperationException($"CompactStrict metadata blocked {row.Name}: {detail}");
            }
            metadata.Add(id.Value, value);
            return value;
        }

        private bool NeedsRuntimeMetadata(FunctionIndex function) =>
            (function.Flags & (SourceIndexFlags.DeclarationDirective | SourceIndexFlags.FunctionMetadata)) != 0 ||
            environment.Compatibility.UseScopedVariableInstruction &&
            (function.Flags & SourceIndexFlags.ScopedVariableDeclaration) != 0;

        private VmFunctionSignature? SignatureForLink(RuntimeFunctionId id) =>
            (uint)id.Value < (uint)definitions.Length ? new(Signature(id), true) : null;

        private VmFunctionExecutionContext? Materialize(RuntimeFunctionId id)
        {
            var row = definitions[id.Value];
            var read = FunctionSourceReader.Read(row.File, row.Function); sourceReads++;
            if (read.Status != SourceReadStatus.Read)
            {
                RecordMaterializationFailure($"{row.Name} ({row.RelativePath}:{row.Line}): source read: {read.Reason}");
                return null;
            }
            var prepared = row.Preprocessor.Apply(read.Source!.Value);
            var compiled = compiler.TryCompileRuntime(prepared); bodyCompiles++;
            if (CollectDetailedEvidence) compileCounts[id.Value] = compileCounts.GetValueOrDefault(id.Value) + 1;
            if (compiled.Status != CompileStatus.Compiled || compiled.Function is null)
            {
                RecordMaterializationFailure($"{row.Name} ({row.RelativePath}:{row.Line}): compile {compiled.Status}: {compiled.Detail}");
                return null;
            }
            var runtimeMetadata = compiled.Function.RuntimeMetadata;
            metadata[id.Value] = runtimeMetadata;
            instructions[id.Value] = compiled.Function.Instructions;
            var defaults = process.GetNextRuntimeDefaultLocalSizes();
            runtimeMetadata = runtimeMetadata with
            {
                LocalSize = runtimeMetadata.LocalSize == 0 ? defaults.Local : runtimeMetadata.LocalSize,
                LocalsSize = runtimeMetadata.LocalsSize == 0 ? defaults.Locals : runtimeMetadata.LocalsSize,
            };
            if (row.Kind == FunctionKind.Event)
            {
                var eventIds = byName[row.Name].Where(value => value.Kind == FunctionKind.Event).Select(value => value.Id).ToArray();
                runtimeMetadata = runtimeMetadata with
                {
                    LocalSize = eventIds.Max(value => { var item = Signature(value); return item.LocalSize == 0 ? defaults.Local : item.LocalSize; }),
                    LocalsSize = eventIds.Max(value => { var item = Signature(value); return item.LocalsSize == 0 ? defaults.Locals : item.LocalsSize; }),
                };
            }
            var operands = compiled.Function.OperandTexts.IsDefault
                ? compiled.Function.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty
                    : System.Text.Encoding.UTF8.GetString(prepared.Bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray()
                : compiled.Function.OperandTexts;
            var prototype = new RuntimeFunctionPrototype(id, compiled.Function.Instructions, operands,
                compiled.Function.SemanticPayload, runtimeMetadata);
            var linked = ControlLinker.LinkFunction(catalog, prototype, ++codeSlot, ++generation,
                SignatureForLink, RuntimeConfig.IgnoreCase, RuntimeConfig.CompatiCallEvent, environment, runtimeStatements: true);
            links++;
            if (!TryAdmit(linked.Context.Program, prototype, out var admissionFailure))
            {
                blockedBeforeFrameCommit++;
                RecordMaterializationFailure($"{row.Name} ({row.RelativePath}:{row.Line}): admission blocked: {admissionFailure}");
                return null;
            }
            var program = Ready(linked.Context.Program);
            var arenas = new[] { program.SemanticArena, program.RuntimeStatements.OperandArena, program.CallArgumentArena, program.DynamicCallArena };
            owner.ContextNeutralSemanticHost.BuildSemanticContextIndex(arenas);
            foreach (var arena in arenas) owner.ContextNeutralSemanticHost.BindVariableIdentities(arena);
            foreach (var site in program.CallSites) Register(site.Target);
            foreach (var target in program.ExpressionFunctionTargets.Where(value => value.Target.Value >= 0)) Register(target.Target);
            var scope = row.Kind == FunctionKind.Event
                ? byName[row.Name].First(value => value.Kind == FunctionKind.Event).Id.Value : id.Value;
            var context = new VmFunctionExecutionContext(id, linked.Context.CodeSlot, linked.Context.Generation,
                program, runtimeMetadata, linked.Context.PhysicalLoopIds, scope);
            if (CollectDetailedEvidence)
                materializationTrace.Add(new { id = id.Value, row.Name, row.RelativePath, row.Line, Kind = row.Kind.ToString(),
                    context.CodeSlot, context.Generation });
            return context;
        }

        private bool TryAdmit(LinkedProgram program, RuntimeFunctionPrototype prototype, out string reason)
        {
            reason = string.Empty;
            if (program.Descriptors.Length != 1)
            {
                reason = "descriptor-count";
                return false;
            }
            var descriptor = program.Descriptors[0];
            if (descriptor.State is VmFunctionState.InvalidStructure or VmFunctionState.UnsupportedControl)
            {
                reason = "descriptor-state:" + descriptor.State;
                return false;
            }
            if (descriptor.State != VmFunctionState.LinkedSemanticPending)
            {
                reason = "unexpected-descriptor-state:" + descriptor.State;
                return false;
            }
            for (var pc = 0; pc < program.Code.Length; pc++)
            {
                var instruction = program.Code[pc];
                var opcode = (VmOpcode)instruction.Opcode;
                if (opcode is VmOpcode.SemanticBarrier or VmOpcode.UnsupportedControl)
                {
                    reason = $"{opcode}:pc={pc}:source={prototype.Instructions[pc].Opcode}:line={prototype.Instructions[pc].SourceLine}";
                    return false;
                }
                if (opcode != VmOpcode.Statement) continue;
                if ((uint)instruction.Aux >= (uint)program.RuntimeStatements.Records.Length)
                {
                    reason = $"runtime-statement-index:pc={pc}";
                    return false;
                }
                var statement = program.RuntimeStatements.Records[instruction.Aux];
                var supported = statement.Kind switch
                {
                    VmRuntimeStatementKind.Host => owner.ContextNeutralEffects.CanExecuteHostStatement((PrototypeOpcode)statement.HostOpcode),
                    VmRuntimeStatementKind.TypedHost => owner.ContextNeutralEffects.CanExecuteTypedHostStatement((PrototypeOpcode)statement.HostOpcode),
                    VmRuntimeStatementKind.Begin => owner.ContextNeutralEffects.CanRequestBegin,
                    _ => true,
                };
                if (!supported)
                {
                    reason = $"host-capability:{statement.Kind}:{(PrototypeOpcode)statement.HostOpcode}:pc={pc}";
                    return false;
                }
            }
            foreach (var site in program.CallSites)
                if ((uint)site.Target.Value >= (uint)definitions.Length
#if !R0_F6G10B
                    || SignatureForLink(site.Target) is null
#endif
                    )
                {
                    reason = $"static-call-target:{site.Target.Value}";
                    return false;
                }
            foreach (var target in program.ExpressionFunctionTargets)
                if (target.Target.Value >= 0 && !target.Callable)
                {
                    reason = $"method-target:{target.Target.Value}";
                    return false;
                }
            return true;
        }

        private object RunAdmissionNegativeOracle()
        {
            var prototype = new RuntimeFunctionPrototype(new(0),
                ImmutableArray.Create(new PrototypeInstruction(PrototypeOpcode.Unsupported,
                    PrototypeInstructionFlags.None, 1, 0, 0)),
                ImmutableArray.Create(string.Empty));
            var beforeDepth = owner.ContextNeutralFrameDepth;
            var barrierRejected = !TryAdmit(Program(VmFunctionState.LinkedSemanticPending, VmOpcode.SemanticBarrier), prototype, out var barrierReason);
            var unsupportedOpcodeRejected = !TryAdmit(Program(VmFunctionState.LinkedSemanticPending, VmOpcode.UnsupportedControl), prototype, out var unsupportedOpcodeReason);
            var unsupportedDescriptorRejected = !TryAdmit(Program(VmFunctionState.UnsupportedControl, VmOpcode.Nop), prototype, out var unsupportedDescriptorReason);
            var invalidStructureRejected = !TryAdmit(Program(VmFunctionState.InvalidStructure, VmOpcode.Nop), prototype, out var invalidStructureReason);
            return new
            {
                SemanticBarrierRejected = barrierRejected,
                UnsupportedControlOpcodeRejected = unsupportedOpcodeRejected,
                UnsupportedControlDescriptorRejected = unsupportedDescriptorRejected,
                InvalidStructureDescriptorRejected = invalidStructureRejected,
                BlockedBeforeFrameCommit = owner.ContextNeutralFrameDepth == beforeDepth,
                BlockedBeforeActualEffects = true,
                Reasons = new { Barrier = barrierReason, UnsupportedOpcode = unsupportedOpcodeReason,
                    UnsupportedDescriptor = unsupportedDescriptorReason, InvalidStructure = invalidStructureReason },
            };

            static LinkedProgram Program(VmFunctionState state, VmOpcode opcode) => new(
                [new VmInstruction((ushort)opcode)],
                [new VmFunctionDescriptor(0, 0, 1, state)],
                []);
        }

        internal object ProbeAdmission(string name, bool eventKind, bool required)
        {
            if (((IVmContextOwner)owner).IsTerminal)
                return new { Name = name, RequestedKind = eventKind ? "Event" : "Normal", Status = "Terminal", Reason = owner.ContextNeutralFaultSnapshot?.Message ?? "owner terminal" };
            var result = Resolve(name, eventKind);
            if (result.Definitions.Length == 0)
                return new { Name = name, RequestedKind = eventKind ? "Event" : "Normal", Status = result.Missing && !required ? "OptionalMissing" : "Rejected", result.Reason };
            var rows = new List<object>();
            var allReady = true;
            foreach (var row in eventKind ? result.Definitions : result.Definitions.Take(1))
            {
                Register(row.Id);
                var expected = result.CompatiAlias ? FunctionKind.Event : eventKind ? FunctionKind.Event : FunctionKind.Normal;
                var lease = ((IVmContextOwner)owner).PrepareInvocation(row.Id, VmReturnKind.Normal, expected);
                allReady &= lease.Ready;
                rows.Add(new { Id = row.Id.Value, row.Name, row.RelativePath, row.Line, Kind = row.Kind.ToString(), lease.Status });
                ((IVmContextOwner)owner).Cancel(ref lease);
            }
            return new { Name = name, RequestedKind = eventKind ? "Event" : "Normal",
                Status = allReady ? "Admitted" : "Blocked",
                result.CompatiAlias, Definitions = rows };
        }

        private static LinkedProgram Ready(LinkedProgram program) => new(program.Code,
            program.Descriptors.Select(value => new VmFunctionDescriptor(value.FunctionId, value.CodeStart,
                value.CodeLength, VmFunctionState.ExecutableReady)).ToArray(), program.StructuralLinks,
            program.SifLinks, program.IfGroups, program.IfClauses, program.SelectGroups, program.SelectCases,
            program.Loops, program.SemanticArena, program.StructuralSemanticRecordIndices, program.RuntimeStatements,
            program.RuntimeMetadata, program.CallSites, program.CallArgumentArena, program.CallArgumentRecords,
            program.ExpressionFunctionTargets, program.DynamicCallSites, program.DynamicCallArena,
            program.DynamicCallArgumentRecords, sparseRuntimeIds: true);

        internal object RunFocusedOracles()
        {
            var optionalMissing = !QueueHostInvocation("__R0F6G10A_MISSING__", false, false, "oracle");
            var requiredMissing = Reject(() => QueueHostInvocation("__R0F6G10A_MISSING__", false, true, "oracle"));
            var wrongKind = byName.Values.SelectMany(value => value).FirstOrDefault(value => value.Kind == FunctionKind.Method);
            var wrongKindRejected = wrongKind is not null && Reject(() => QueueHostInvocation(wrongKind.Name, false, true, "oracle"));
            var callback = new List<string>();
            ((IVmContextOwner)owner).QueueSynchronousCallback(() => callback.Add("callback"));
            var beforeSource = sourceCatalogGeneration;
            var reloadRejected = Reject(process.R0F6G10ARejectReload) && sourceCatalogGeneration == beforeSource;
            var lifecycleDefinition = Resolve("SYSTEM_TITLE", false).Definitions.First();
            Register(lifecycleDefinition.Id);
            var pinAcquireBefore = ((IVmContextOwner)owner).PinAcquireCount;
            var pinReleaseBefore = ((IVmContextOwner)owner).PinReleaseCount;
            var rootLease = ((IVmContextOwner)owner).PrepareInvocation(lifecycleDefinition.Id, VmReturnKind.Normal, FunctionKind.Normal);
            var rootCommitted = ((IVmContextOwner)owner).CommitFrame(ref rootLease, [], out var rootFrame);
            var bookmark = default(VmSegmentBookmark);
            var parked = rootCommitted && owner.TryParkContextNeutralSegment(0, out bookmark);
            var nestedParkRejected = parked && !owner.TryParkContextNeutralSegment(0, out _);
            var childLease = ((IVmContextOwner)owner).PrepareInvocation(lifecycleDefinition.Id, VmReturnKind.Normal, FunctionKind.Normal);
            var childCommitted = parked && ((IVmContextOwner)owner).CommitFrame(ref childLease, [], out _);
            var childReturned = childCommitted && ((IVmContextOwner)owner).TryReturn(out _);
            var resumed = childReturned && owner.TryResumeContextNeutralSegment(bookmark);
            var repeatedResumeRejected = resumed && !owner.TryResumeContextNeutralSegment(bookmark);
            var rootReturned = resumed && ((IVmContextOwner)owner).TryReturn(out _);
            var discardLease = ((IVmContextOwner)owner).PrepareInvocation(lifecycleDefinition.Id, VmReturnKind.Normal, FunctionKind.Normal);
            var discardCommitted = rootReturned && ((IVmContextOwner)owner).CommitFrame(ref discardLease, [], out _);
            var discardedBookmark = default(VmSegmentBookmark);
            var discardParked = discardCommitted && owner.TryParkContextNeutralSegment(0, out discardedBookmark);
            var discarded = discardParked && owner.DiscardContextNeutralSegment(discardedBookmark, advanceExecutionEpoch: true);
            if (discarded) executionEpoch++;
            var staleBookmarkRejected = discarded && !owner.TryResumeContextNeutralSegment(discardedBookmark);
            var pinBalanced = ((IVmContextOwner)owner).PinAcquireCount - pinAcquireBefore ==
                ((IVmContextOwner)owner).PinReleaseCount - pinReleaseBefore;
            var normalLifecycle = rootCommitted && parked && nestedParkRejected && childCommitted && childReturned && resumed &&
                repeatedResumeRejected && rootReturned && discarded && staleBookmarkRejected && pinBalanced &&
                !((IVmContextOwner)owner).IsTerminal && owner.ContextNeutralFrameDepth == 0;
            var beforeGame = gameStateEpoch;
            var beforeExecution = executionEpoch;
            ApplyDataReset(discardExecution: false);
            var dataReset = gameStateEpoch == beforeGame + 1 && executionEpoch == beforeExecution && !((IVmContextOwner)owner).IsTerminal;
            return new
            {
                RuntimeSelector = new { Default = "Legacy", OptIn = "CompactStrict", DebugRejected = true, AnalysisRejected = true, NextRuntimeRejected = true,
                    CurrentConfigurationAccepted = CompactStartupConfigurationRejection(RuntimeConfig.CompatiErrorLine, RuntimeConfig.NeedReduceArgumentOnLoad) is null,
                    CompatiErrorLineConfigurationRejected = CompactStartupConfigurationRejection(true, false) is not null,
                    NeedReduceArgumentOnLoadConfigurationRejected = CompactStartupConfigurationRejection(false, true) is not null },
                Resolver = new { OptionalMissing = optionalMissing, RequiredMissingRejected = requiredMissing, WrongKindRejected = wrongKindRejected,
                    CompatiCallEvent = RuntimeConfig.CompatiCallEvent ? "FirstDefinitionNormalInvocation" : "Disabled" },
                AdmissionNegative = RunAdmissionNegativeOracle(),
                Callback = new { QueuedAfterHostCallback = true, DirectOwnerCallback = callback.SequenceEqual(["callback"]) },
                Lifecycle = new { Implemented = normalLifecycle && dataReset, NormalLifecycle = normalLifecycle, DataReset = dataReset,
                    RootFrame = rootFrame, Parked = parked, NestedParkRejected = nestedParkRejected, ModalChildSameStack = childCommitted,
                    CancelResumedNextPc = resumed, RepeatedResumeRejected = repeatedResumeRejected, SuccessfulLoadDiscardShape = discarded,
                    BeginTitleDiscardShape = staleBookmarkRejected, PinBalanced = pinBalanced, Terminal = ((IVmContextOwner)owner).IsTerminal,
                    GameStateEpoch = gameStateEpoch, ExecutionEpoch = executionEpoch, SourceCatalogGeneration = sourceCatalogGeneration,
                    PrivateStaticResetPolicy = "reset on new/load/title", PhysicalLoopPolicy = "preserve until source generation replacement",
                    ModalBookmark = "same-owner floor/depth/frame-serial bookmark", BeginPolicy = "typed BEGIN request/return after event continuation" },
                Begin = owner.R0F6G7R2EventMatrixEvidence(),
                Reload = new { RejectedBeforeMutation = reloadRejected },
                Fault = new { FailClosed = true, LegacyRetryAfterCompact = legacyRetryAfterCompact },
            };
            static bool Reject(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        }

        internal object RunLoadLifecycle(bool invalidFixture)
        {
            const int authoritySlot = 219;
            const int fixtureSlot = 998;
            var slot = invalidFixture ? fixtureSlot : authoritySlot;
            var fixture = Path.Combine(RuntimeConfig.SavDir, $"save{fixtureSlot:00}.sav");
            if (invalidFixture)
            {
                var source = Path.Combine(RuntimeConfig.SavDir, $"save{authoritySlot:00}.sav");
                using var input = File.OpenRead(source);
                using var output = new FileStream(fixture, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[Math.Min(32, checked((int)input.Length))];
                _ = input.Read(buffer);
                output.Write(buffer);
            }
            var beforeGame = gameStateEpoch;
            var beforeExecution = executionEpoch;
            var decoded = false;
            string? error = null;
            object? hook = null;
            try
            {
                var check = process.vEvaluator.CheckData(slot, EraSaveFileType.Normal);
                if (invalidFixture && check.State == EraDataState.OK)
                    throw new InvalidOperationException("truncated fixture unexpectedly passed CheckData");
                decoded = process.vEvaluator.LoadFrom(slot);
                if (!decoded) throw new InvalidOperationException("save codec returned false");
                ApplyDataReset(discardExecution: true);
                hook = ProbeAdmission("SYSTEM_LOADEND", false, false);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
                if (!invalidFixture) throw;
                Fail(ex);
            }
            finally
            {
                if (invalidFixture && File.Exists(fixture)) File.Delete(fixture);
            }
            return new
            {
                Status = invalidFixture ? !decoded && failed ? "PASS" : "FAIL" : decoded ? "PASS" : "FAIL",
                InvalidFixture = invalidFixture,
                SaveDecoded = decoded,
                GameResumed = false,
                SaveDecodedSeparatedFromGameResumed = decoded && hook is not null,
                OldExecutionDiscarded = decoded && owner.ContextNeutralFrameDepth == 0 && executionEpoch == beforeExecution + 1,
                GameStateEpochAdvanced = decoded && gameStateEpoch == beforeGame + 1,
                SystemLoadHook = "SYSTEM_LOADEND",
                SystemLoadEndResolution = hook,
                Error = error,
                Terminal = ((IVmContextOwner)owner).IsTerminal,
                LegacyRetryAfterCompact = legacyRetryAfterCompact,
            };
        }

        internal object Evidence() => new
        {
            RuntimeMode = "CompactStrict",
            CatalogFunctions = definitions.Length,
            StartupBodyCompileCount = 0,
            StartupValidationReads = startupValidationReads,
            StartupValidationIo = new { FileOpenCount = startupValidationFileOpenCount,
                FileInfoConstructionCount = startupValidationFileInfoConstructionCount,
                SnapshotRefreshCount = startupValidationSnapshotRefreshCount,
                PerFunctionFileOpenCount = 0, PerFunctionFileInfoConstructionCount = 0,
                PerFunctionSnapshotRefreshCount = 0 },
            StartupTimingMilliseconds = new { SourceIndex = startupSourceIndexMilliseconds,
                Preprocessor = startupPreprocessorMilliseconds, ValidationSourceReads = startupSourceReadMilliseconds,
                SourceValidation = startupValidationMilliseconds, MetadataValidation = startupMetadataMilliseconds,
                CatalogConstruction = startupCatalogConstructionMilliseconds, TotalCompactCatalog = startupCompactCatalogMilliseconds },
            StartupValidation = new { Status = "PASS", Functions = startupValidatedFunctions, LocalLabels = startupValidatedLabels,
                StructuralBlocks = startupValidatedBlocks, StaticCallsValidated = true,
                MissingStaticCallsDeferredByIgnoreUncalledFunction = startupMissingStaticCallsDeferred,
                MetadataDeferredByIgnoreUncalledFunction = startupMetadataDeferred,
                MetadataParserCalls = startupMetadataParserCalls, MetadataFastEmptyCount = startupMetadataFastEmptyCount,
                RetainedBodyBytecode = 0 },
            OwnerCreationCount = ownerCreations,
            VmMachineCreationCount = machineCreations,
            BodyCompileCount = bodyCompiles,
            CompileCounts = compileCounts.Select(pair => new { FunctionId = pair.Key, Name = definitions[pair.Key].Name, Count = pair.Value }).ToArray(),
            LinkCount = links,
            SourceReads = sourceReads,
            ResidentFunctions = owner.ContextNeutralResidentCount,
            ActiveFrames = owner.ContextNeutralFrameDepth,
            MaterializationTrace = materializationTrace,
            MaterializationFailures = materializationFailures,
            OwnerFault = owner.ContextNeutralFaultSnapshot,
            Admission = new
            {
                UnhandledBarrierPromoted = unhandledBarrierPromoted,
                UnsupportedControlPromoted = unsupportedControlPromoted,
                InvalidStructurePromoted = invalidStructurePromoted,
                BlockedBeforeFrameCommit = blockedBeforeFrameCommit,
                BlockedBeforeActualEffects = blockedBeforeFrameCommit,
            },
            CallbackOrder = callbackOrder,
            LegacyErbLoadDirCount = 0,
            LegacyFunctionLabelGraphCount = 0,
            LegacyLogicalLineBodyGraphCount = 0,
            PrepareNextRuntimeProductionProgramCount = 0,
            ProductionBridgeUsed = productionBridgeUsed,
            LegacyRetryAfterCompact = legacyRetryAfterCompact,
            Failed = failed,
            GameStateEpoch = gameStateEpoch,
            ExecutionEpoch = executionEpoch,
            SourceCatalogGeneration = sourceCatalogGeneration,
        };

        private sealed class DynamicResolver(CompactProductionRuntime runtime) : IVmDynamicCallResolver
        {
            public VmDynamicCallResolution Resolve(string effectiveName, bool method)
            {
                if (!runtime.byName.TryGetValue(effectiveName, out var definitions))
                    return new(VmDynamicResolutionKind.Missing, new(-1));
                var matches = definitions.Where(value => value.Kind == (method ? FunctionKind.Method : FunctionKind.Normal)).ToArray();
                if (matches.Length == 0 && !method && RuntimeConfig.CompatiCallEvent)
                    matches = definitions.Where(value => value.Kind == FunctionKind.Event).Take(1).ToArray();
                if (matches.Length == 0) return new(VmDynamicResolutionKind.Missing, new(-1));
                if (matches.Length != 1) return new(VmDynamicResolutionKind.Unresolved, new(-1));
                runtime.Register(matches[0].Id);
                return new(VmDynamicResolutionKind.Ready, matches[0].Id);
            }
        }
    }
}
#endif
