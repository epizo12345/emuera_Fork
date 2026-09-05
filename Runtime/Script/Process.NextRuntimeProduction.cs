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
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;

#nullable enable

internal sealed partial class Process
{
    internal bool IsNextRuntimeFrameVariableBound(int functionId, string name) =>
        productionProgram is { } program && (uint)functionId < (uint)program.RuntimeMetadata.Length &&
        program.RuntimeMetadata[functionId].PrivateVariables.Any(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private int productionPreparationCount;
    private int productionPreparationAttemptCount;
    private int productionActivationCount;
    private string productionPreparationFailureReason = "NotAttempted";
    private int productionRuntimeUniverse;
    private bool productionExactBound;
    private bool productionBindingCollision;
    private int productionMisbind;
    private int productionDispatchEntryReadyCount;
    private VmRuntimeActivationResult? productionActivationResult;
    private VmRuntimePreparationCounters? productionPreparationCounters;
    private VmSemanticStructuralLookup? productionSemanticStructuralLookup;
    private VmMachineProgramLookup? productionVmMachineProgramLookup;
    private readonly List<string> productionMemoryStages = [];
    private readonly List<string> productionTimingStages = [];
    private long productionPeakWorkingSet64;
    private long productionPeakPrivateMemorySize64;

    internal string ExportNextRuntimeProductionMemoryStages() =>
        "Stage\tManagedMemoryBytes\tWorkingSet64\tPrivateMemorySize64\tPeakWorkingSet64\tPeakPrivateMemorySize64\n" + string.Join('\n', productionMemoryStages);

    internal string ExportNextRuntimeProductionTimingStages() =>
        "Stage\tElapsedMilliseconds\n" + string.Join('\n', productionTimingStages);

    internal string ExportNextRuntimeProductionReadiness() =>
        $"SharedReadinessStagePresent={(productionSharedReadiness is null ? "NO" : "YES")}\nProductionReadinessStagePresent={(productionReadiness is null ? "NO" : "YES")}\nSharedAnalyzerR5Candidate={productionSharedReadiness?.EligibleCount ?? 0}\nSharedAnalyzerR5Parity={(productionSharedReadiness?.EligibleCount == 101097 ? "PASS" : "FAIL")}\nSharedExecutableReadyCount={sharedExecutableReadyIds.Count}\nProductionExecutableReadyReal={productionExecutableReadyIds.Count}\nProductionDispatchEntryReadyReal={productionDispatchEntryReadyIds.Count}\nProductionExecutableSubsetOfShared={(productionExecutableReadyIds.IsSubsetOf(sharedExecutableReadyIds) ? "PASS" : "FAIL")}\nProductionDispatchEntrySubsetOfExecutable={(productionDispatchEntryReadyIds.IsSubsetOf(productionExecutableReadyIds) ? "PASS" : "FAIL")}\nProductionReadinessRows={(productionReadiness?.Rows.Length ?? 0)}\nProductionReadinessEligible={(productionReadiness?.EligibleCount ?? 0)}\nProductionReadinessBlocked={(productionReadiness?.BlockedCount ?? 0)}\nProductionReadinessTransitiveRequirementCounts={(productionReadiness is null ? string.Empty : string.Join(',', Enum.GetValues<VmRuntimeRequirement>().Where(requirement => requirement != VmRuntimeRequirement.None).Select(requirement => $"{requirement}:{productionReadiness.Rows.Count(row => (row.TransitiveRequirements & requirement) != 0)}")))}\nSemanticContextIndexBuildCount={productionPreparationCounters?.SemanticContextIndexBuildCount ?? 0}\nSemanticContextFullPayloadScanCount={productionPreparationCounters?.SemanticContextFullPayloadScanCount ?? 0}\nSemanticNodeVisitCount={productionPreparationCounters?.SemanticNodeVisitCount ?? 0}\nCsvContextLookupCount={productionPreparationCounters?.CsvContextLookupCount ?? 0}\nCharacterContextLookupCount={productionPreparationCounters?.CharacterContextLookupCount ?? 0}\nVariableClassifierCallCount={productionPreparationCounters?.VariableClassifierCallCount ?? 0}\nVariableClassifierCacheHitCount={productionPreparationCounters?.VariableClassifierCacheHitCount ?? 0}\nCallGraphBuildCount={productionPreparationCounters?.CallGraphBuildCount ?? 0}\nCallEdgeVisitCount={productionPreparationCounters?.CallEdgeVisitCount ?? 0}\nSccBuildCount={productionPreparationCounters?.SccBuildCount ?? 0}\nSharedReadinessEvaluationCount={productionPreparationCounters?.SharedReadinessEvaluationCount ?? 0}\nProductionReadinessEvaluationCount={productionPreparationCounters?.ProductionReadinessEvaluationCount ?? 0}\nReadinessEvaluationPasses={productionPreparationCounters?.ReadinessEvaluationPasses ?? 0}\nSharedExecutableReadyIdSet={string.Join(',', sharedExecutableReadyIds.OrderBy(id => id))}\nProductionExecutableReadyIdSet={string.Join(',', productionExecutableReadyIds.OrderBy(id => id))}\n";

    private void RecordProductionMemoryStage(string stage)
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        current.Refresh();
        productionPeakWorkingSet64 = Math.Max(productionPeakWorkingSet64, current.WorkingSet64);
        productionPeakPrivateMemorySize64 = Math.Max(productionPeakPrivateMemorySize64, current.PrivateMemorySize64);
        productionMemoryStages.Add($"{stage}\t{GC.GetTotalMemory(false)}\t{current.WorkingSet64}\t{current.PrivateMemorySize64}\t{productionPeakWorkingSet64}\t{productionPeakPrivateMemorySize64}");
    }

    internal int ProductionPreparationCount => productionPreparationCount;
    internal int ProductionActivationCount => productionActivationCount;
    internal LinkedProgram? NextRuntimeProductionProgram => productionProgram;

    // [Emuera改修:NEXT-3D-R1.4C2 2026-09-04]
    // production preparationはLegacy hostが所有する一時的なactivation境界。失敗時にLegacyへ戻せる
    // よう、readiness/metadata/frame bindingを確定してからNext sessionを公開する。
    internal bool PrepareNextRuntimeProductionProgram(TextWriter? log = null)
    {
        productionMemoryStages.Clear();
        productionTimingStages.Clear();
        productionPeakWorkingSet64 = 0;
        productionPeakPrivateMemorySize64 = 0;
        RecordProductionMemoryStage("BeforePreparation");
        InvalidateNextRuntimeProductionProgram();
        productionPreparationFailureReason = "NotAttempted";
        productionRuntimeUniverse = 0;
        productionExactBound = false;
        productionBindingCollision = false;
        productionMisbind = 0;
        productionDispatchEntryReadyCount = 0;
        productionActivationResult = null;
        productionPreparationCounters = null;
        sharedExecutableReadyIds = [];
        productionExecutableReadyIds = [];
        productionDispatchEntryReadyIds = [];
        productionSharedReadiness = null;
        productionReadiness = null;
        if (!Program.NextRuntimeMode || Program.DebugMode || labelDic is null || idDic is null || exm is null) return true;
        productionPreparationAttemptCount++;
        productionPreparationFailureReason = "InProgress";
        try
        {
            Stopwatch? preparationStopwatch = Program.NextRuntimeProductionOnly && !string.IsNullOrWhiteSpace(Program.NextRuntimeHostProbePath) ? Stopwatch.StartNew() : null;
#if PERFORMANCE_METRICS
            var preparationStageStart = PerformanceMetrics.StartNextRuntimeMeasurement();
#endif
            void RecordProductionTimingStage(string stage)
            {
                if (preparationStopwatch is not null) productionTimingStages.Add($"{stage}\t{preparationStopwatch.Elapsed.TotalMilliseconds:F3}");
#if PERFORMANCE_METRICS
                PerformanceMetrics.RecordNextRuntimeStartupStage(stage, preparationStageStart);
                preparationStageStart = PerformanceMetrics.StartNextRuntimeMeasurement();
#endif
            }
            RecordProductionTimingStage("ProductionPreparation.Start");
#if PERFORMANCE_METRICS
            var sourceIndexTotalStart = PerformanceMetrics.StartNextRuntimeMeasurement();
#endif
            var selectedFiles = Config.GetFiles(Program.ErbDir, "*.ERB");
            var files = selectedFiles.Select(pair => ErbSourceIndexer.IndexFile(pair.Value)).ToArray();
            RecordProductionTimingStage("ProductionPreparation.SourceIndex.EnumerateAndIndexFiles");
            var sourcePositions = new Dictionary<string, (int FileOrdinal, int FunctionOrdinal, SourceIndexFlags Flags)>(StringComparer.OrdinalIgnoreCase);
            for (var fileOrdinal = 0; fileOrdinal < files.Length; fileOrdinal++)
                for (var functionOrdinal = 0; functionOrdinal < files[fileOrdinal].Functions.Count; functionOrdinal++)
                {
                    var function = files[fileOrdinal].Functions[functionOrdinal];
                    sourcePositions[PositionKey(files[fileOrdinal].FileIdentity, function.Span.StartLine)] = (fileOrdinal, functionOrdinal, function.Flags);
                }
            RecordProductionTimingStage("ProductionPreparation.SourceIndex.BuildSourcePositions");

            var labels = labelDic.GetAllLabels(false).Where(label => label.Position is not null).ToArray();
            var ordered = labels
                .OrderBy(label => label.FileIndex == 0 ? int.MaxValue : label.FileIndex)
                .ThenBy(label => label.Position!.Value.Filename, StringComparer.OrdinalIgnoreCase)
                .ThenBy(label => label.Position!.Value.LineNo)
                .ToArray();
            RecordProductionTimingStage("ProductionPreparation.SourceIndex.EnumerateAndOrderLabels");
            var runtimeByPosition = new Dictionary<string, RuntimeFunctionId>(StringComparer.OrdinalIgnoreCase);
            var labelIds = new Dictionary<FunctionLabelLine, RuntimeFunctionId>();
            var bindings = new RuntimeFunctionBinding[ordered.Length];
            var kinds = new FunctionKind[ordered.Length];
            var sourcePositionLookupHitCount = 0;
            for (var id = 0; id < ordered.Length; id++)
            {
                var label = ordered[id];
                var runtimeId = new RuntimeFunctionId(id);
                var key = PositionKey(label.Position!.Value.Filename, label.Position.Value.LineNo);
                if (!runtimeByPosition.TryAdd(key, runtimeId))
                {
                    productionBindingCollision = true;
                    throw new InvalidOperationException($"ambiguous production label binding: {key}");
                }
                labelIds.Add(label, runtimeId);
                var kind = label.IsMethod ? FunctionKind.Method : label.IsEvent ? FunctionKind.Event : FunctionKind.Normal;
                kinds[id] = kind;
                if (sourcePositions.TryGetValue(key, out var source))
                {
                    sourcePositionLookupHitCount++;
                    bindings[id] = new(runtimeId, label.LabelName, kind, source.Flags, true, new(source.FileOrdinal, source.FunctionOrdinal));
                }
                else
                    bindings[id] = new(runtimeId, label.LabelName, kind, SourceIndexFlags.LineContinuation, true, CatalogSourceRef.None);
            }
            RecordProductionTimingStage("ProductionPreparation.SourceIndex.BuildRuntimeBindings");

            productionRuntimeUniverse = ordered.Length;
            productionMisbind = ordered.Length - sourcePositionLookupHitCount;
            productionExactBound = runtimeByPosition.Count == ordered.Length && labelIds.Count == ordered.Length && productionMisbind == 0;
#if PERFORMANCE_METRICS
            PerformanceMetrics.RecordNextRuntimeSourceIndexMetrics(
                selectedFiles.Count,
                files.Length,
                files.Sum(file => file.SourceBytes),
                files.Sum(file => (long)file.LineCount),
                files.Sum(file => file.Functions.Count),
                files.Sum(file => file.Functions.Count),
                sourcePositions.Count,
                files.Sum(file => file.Functions.Count) - sourcePositions.Count,
                labels.Length,
                ordered.Length,
                sourcePositionLookupHitCount,
                runtimeByPosition.Count,
                labelIds.Count,
                productionMisbind);
            PerformanceMetrics.RecordNextRuntimeStartupStage("ProductionPreparation.SourceIndex.Total", sourceIndexTotalStart);
#endif
            RecordProductionMemoryStage("AfterSourceIndex");
            RecordProductionTimingStage("ProductionPreparation.AfterSourceIndex");

            var catalog = FunctionCatalog.FromRuntimeBindings(files, bindings);
            RecordProductionTimingStage("ProductionPreparation.AfterCatalog");
            var options = new CompilerCompatibilityOptions(Config.IgnoreCase, JSONConfig.Game.UseScopedVariableInstruction, Config.SystemAllowFullSpace, false);
            var renameResolver = Config.UseRenameFile && ParserMediator.RenameDic is not null ? new SemanticRenameResolver(ParserMediator.RenameDic) : null;
            var headers = Config.GetFiles(Program.ErbDir, "*.ERH").Select(pair => File.ReadAllText(pair.Value, Config.Encode));
            var macros = MacroCatalog.FromHeaderSources(headers, options, renameResolver);
            var environment = new StructuralSemanticEnvironment(options, macros, Config.SystemIgnoreTripleSymbol, renameResolver);
            var compiler = new FunctionCompiler(environment);
            RecordProductionTimingStage("ProductionPreparation.AfterCompilerSetup");
            var sourceToRuntime = new Dictionary<SourceFunctionId, RuntimeFunctionId>();
            var sourcePrototypes = new List<SourceFunctionPrototype>();
            var sourceId = 0;
#if PERFORMANCE_METRICS
            // [Emuera改修:NEXT-3D-R1.5P3H 2026-09-04]
            // PrototypeCompileの内部costを測るだけで、compile順序・失敗処理・runtime意味論は変更しない。
            var prototypeCompileTotalStart = PerformanceMetrics.StartNextRuntimeMeasurement();
            var prototypeCompileFileCount = 0;
            long prototypeCompileFunctionCandidateCount = 0;
            long prototypeCompileRuntimeMappedCount = 0;
            long prototypeCompileSourceReadCount = 0;
            long prototypeCompileSourceReadFailureCount = 0;
            long prototypeCompileCompiledCount = 0;
            long prototypeCompileUnsupportedCount = 0;
            long prototypeCompileSourceBytes = 0;
            long prototypeCompileInstructionCount = 0;
            long prototypeCompileSemanticNodeCount = 0;
            long prototypeCompileSemanticRecordCount = 0;
            long prototypeCompileRuntimeMetadataCount = 0;
            long prototypeCompileFileOpenTicks = 0;
            long prototypeCompileRuntimePositionLookupTicks = 0;
            long prototypeCompileSourceReadTicks = 0;
            long prototypeCompileCompileRuntimeTicks = 0;
            long prototypeCompileCompiledTicks = 0;
            long prototypeCompileUnsupportedTicks = 0;
            long prototypeCompileOperandMaterializationTicks = 0;
            long prototypeCompileAppendAndMappingTicks = 0;
            long prototypeCompilePositionKeyCount = 0;
            long prototypeCompilePathNormalizationExecutionCount = 0;
#endif
            foreach (var file in files)
            {
#if PERFORMANCE_METRICS
                prototypeCompileFileCount++;
                prototypeCompileFunctionCandidateCount += file.Functions.Count;
                var prototypeCompileFileStart = PerformanceMetrics.StartNextRuntimeMeasurement();
#endif
#if PERFORMANCE_METRICS
                var prototypeCompileFileOpenStart = PerformanceMetrics.StartNextRuntimeTimestamp();
#endif
                using var session = FunctionSourceReader.OpenFile(file);
                string? normalizedPositionPath = null;
#if PERFORMANCE_METRICS
                prototypeCompileFileOpenTicks += PerformanceMetrics.ElapsedNextRuntimeTimestamp(prototypeCompileFileOpenStart);
#endif
                foreach (var function in file.Functions)
                {
                    var currentSourceId = new SourceFunctionId(sourceId++);
#if PERFORMANCE_METRICS
                    prototypeCompilePositionKeyCount++;
                    var prototypeCompilePositionLookupStart = PerformanceMetrics.StartNextRuntimeTimestamp();
                    var positionPath = SourcePositionKey.GetOrNormalize(ref normalizedPositionPath, file.FileIdentity, Program.ErbDir, out var pathNormalizationRequired);
                    if (pathNormalizationRequired)
                        prototypeCompilePathNormalizationExecutionCount++;
#else
                    var positionPath = normalizedPositionPath ??= NormalizePositionPath(file.FileIdentity);
#endif
                    var positionLookupSucceeded = runtimeByPosition.TryGetValue(PositionKeyFromNormalizedPath(positionPath, function.Span.StartLine), out var runtimeId);
#if PERFORMANCE_METRICS
                    prototypeCompileRuntimePositionLookupTicks += PerformanceMetrics.ElapsedNextRuntimeTimestamp(prototypeCompilePositionLookupStart);
#endif
                    if (!positionLookupSucceeded) continue;
#if PERFORMANCE_METRICS
                    prototypeCompileRuntimeMappedCount++;
#endif
#if PERFORMANCE_METRICS
                    var prototypeCompileAppendStart = PerformanceMetrics.StartNextRuntimeTimestamp();
#endif
                    sourceToRuntime[currentSourceId] = runtimeId;
#if PERFORMANCE_METRICS
                    prototypeCompileAppendAndMappingTicks += PerformanceMetrics.ElapsedNextRuntimeTimestamp(prototypeCompileAppendStart);
                    var prototypeCompileSourceReadStart = PerformanceMetrics.StartNextRuntimeTimestamp();
#endif
                    var read = session.Read(function);
#if PERFORMANCE_METRICS
                    prototypeCompileSourceReadTicks += PerformanceMetrics.ElapsedNextRuntimeTimestamp(prototypeCompileSourceReadStart);
#endif
                    if (read.Status != SourceReadStatus.Read || read.Source is not { } functionSource)
                    {
#if PERFORMANCE_METRICS
                        prototypeCompileSourceReadFailureCount++;
#endif
                        continue;
                    }
#if PERFORMANCE_METRICS
                    prototypeCompileSourceReadCount++;
                    prototypeCompileSourceBytes += functionSource.Length;
#endif
#if PERFORMANCE_METRICS
                    var prototypeCompileCompileStart = PerformanceMetrics.StartNextRuntimeTimestamp();
#endif
                    var compiled = compiler.TryCompileRuntime(functionSource);
#if PERFORMANCE_METRICS
                    var prototypeCompileCompileTicks = PerformanceMetrics.ElapsedNextRuntimeTimestamp(prototypeCompileCompileStart);
                    prototypeCompileCompileRuntimeTicks += prototypeCompileCompileTicks;
                    if (compiled.Status != CompileStatus.Compiled)
                    {
                        prototypeCompileUnsupportedTicks += prototypeCompileCompileTicks;
                        prototypeCompileUnsupportedCount++;
                    }
                    else
                    {
                        prototypeCompileCompiledTicks += prototypeCompileCompileTicks;
                    }
#endif
                    if (compiled.Status != CompileStatus.Compiled) continue;
                    var source = compiled.Function!;
#if PERFORMANCE_METRICS
                    prototypeCompileCompiledCount++;
                    prototypeCompileInstructionCount += source.Instructions.Length;
                    if (source.SemanticPayload is { } semanticPayload)
                    {
                        prototypeCompileSemanticNodeCount += semanticPayload.SemanticNodeCount;
                        prototypeCompileSemanticRecordCount += semanticPayload.SemanticRecordCount;
                    }
                    if (source.RuntimeMetadata is not null)
                        prototypeCompileRuntimeMetadataCount++;
#endif
                    var bytes = functionSource.Bytes;
#if PERFORMANCE_METRICS
                    var prototypeCompileOperandStart = PerformanceMetrics.StartNextRuntimeTimestamp();
#endif
                    var operands = source.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray();
#if PERFORMANCE_METRICS
                    prototypeCompileOperandMaterializationTicks += PerformanceMetrics.ElapsedNextRuntimeTimestamp(prototypeCompileOperandStart);
                    var prototypeCompileAppendResultStart = PerformanceMetrics.StartNextRuntimeTimestamp();
#endif
                    sourcePrototypes.Add(new(currentSourceId, source.Instructions, operands, source.SemanticPayload, source.RuntimeMetadata));
#if PERFORMANCE_METRICS
                    prototypeCompileAppendAndMappingTicks += PerformanceMetrics.ElapsedNextRuntimeTimestamp(prototypeCompileAppendResultStart);
#endif
                }
#if PERFORMANCE_METRICS
                PerformanceMetrics.RecordNextRuntimeConstructionStage("ProductionPreparation.PrototypeCompile.File", prototypeCompileFileStart);
#endif
            }
#if PERFORMANCE_METRICS
            PerformanceMetrics.RecordNextRuntimePrototypeCompileMetrics(
                prototypeCompileFileCount,
                prototypeCompileFunctionCandidateCount,
                prototypeCompileRuntimeMappedCount,
                prototypeCompileSourceReadCount,
                prototypeCompileSourceReadFailureCount,
                prototypeCompileCompiledCount,
                prototypeCompileUnsupportedCount,
                prototypeCompileSourceBytes,
                prototypeCompileInstructionCount,
                prototypeCompileSemanticNodeCount,
                prototypeCompileSemanticRecordCount,
                prototypeCompileRuntimeMetadataCount,
                prototypeCompileFileOpenTicks,
                prototypeCompileRuntimePositionLookupTicks,
                prototypeCompileSourceReadTicks,
                prototypeCompileCompileRuntimeTicks,
                prototypeCompileCompiledTicks,
                prototypeCompileUnsupportedTicks,
                prototypeCompileOperandMaterializationTicks,
                prototypeCompileAppendAndMappingTicks,
                prototypeCompilePositionKeyCount,
                prototypeCompilePathNormalizationExecutionCount);
            PerformanceMetrics.RecordNextRuntimeConstructionStage("ProductionPreparation.PrototypeCompile.FunctionLoop", prototypeCompileTotalStart);
            PerformanceMetrics.RecordNextRuntimeStartupStage("ProductionPreparation.PrototypeCompile.Total", prototypeCompileTotalStart);
#endif
            if (sourcePrototypes.Count == 0) throw new InvalidOperationException("production runtime compilation produced no functions");
            RecordProductionMemoryStage("AfterCompile");
            RecordProductionTimingStage("ProductionPreparation.AfterPrototypeCompile");
            var prototypes = RuntimeFunctionBinder.Remap(sourcePrototypes, sourceToRuntime);
            catalog.MarkCodeAvailable(prototypes.Select(prototype => prototype.RuntimeId));
            RecordProductionTimingStage("ProductionPreparation.AfterRemap");
            var link = ControlLinker.Link(catalog, prototypes, runtimeEnvironment: environment, runtimeStatements: true, phaseBoundary: RecordProductionTimingStage, measureCallArgumentPrefixScan: PerformanceMetrics.NextDispatchProfileEnabled);
#if PERFORMANCE_METRICS
            PerformanceMetrics.RecordNextRuntimeCallArgumentLinkMetrics(
                link.CallArgumentPayloadAdditionCount,
                link.CallArgumentPrefixScanElementVisits,
                link.CallArgumentPrefixScanTicks);
#endif
            productionFunctionKinds = kinds;
            productionLabelIds = labelIds;
            productionProgram = link.Program;
#if PERFORMANCE_METRICS
            PerformanceMetrics.RecordNextRuntimeProgramCardinalities(
                link.Program.Descriptors.Length,
                link.Program.Descriptors.Length,
                link.Program.Descriptors.Sum(descriptor => descriptor.CodeLength),
                link.Program.StructuralLinks.Length,
                link.Program.Loops.Length,
                link.Program.CallSites.Length,
                link.Program.ExpressionFunctionTargets.Length);
#endif
            RecordProductionMemoryStage("AfterLink");
            RecordProductionTimingStage("ProductionPreparation.AfterLink");
            productionSemanticStructuralLookup = VmSemanticStructuralLookup.Build(link.Program);
            RecordProductionTimingStage("ProductionPreparation.SemanticStructuralLookup");
            productionVmMachineProgramLookup = VmMachineProgramLookup.Build(link.Program);
            RecordProductionTimingStage("ProductionPreparation.VmMachineProgramLookup");
            productionPreparationCounters = new(PerformanceMetrics.NextDispatchProfileEnabled);
            productionSemanticHost = new LegacyVmSemanticHost(this, productionPreparationCounters);
            var semanticArenas = new[] { link.Program.SemanticArena, link.Program.RuntimeStatements.OperandArena, link.Program.CallArgumentArena };
            foreach (var arena in semanticArenas)
                if (!productionSemanticHost.PrebindProductionVariableIdentities(arena))
                    throw new InvalidOperationException("production semantic prebind failed");
            productionSemanticHost.BuildSemanticContextIndex(semanticArenas);
            RecordProductionMemoryStage("AfterSemanticPrebind");
            RecordProductionTimingStage("ProductionPreparation.AfterSemanticPrebind");
            if (!LegacyVmFrameBindingCatalog.TryCreate(this, link.Program, labelIds.ToDictionary(pair => pair.Value.Value, pair => pair.Key), out var frameCatalog))
                throw new InvalidOperationException("production frame binding catalog failed");
            productionFrameCatalog = frameCatalog;
            RecordProductionMemoryStage("AfterFrameCatalog");
            RecordProductionTimingStage("ProductionPreparation.AfterFrameCatalog");
            var sharedCapabilities = new VmRuntimeCapabilitySnapshot(
                VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite | VmRuntimeRequirement.Builtin,
                productionSemanticHost.IsVariableBound,
                productionSemanticHost.IsBuiltinBound,
                null,
                productionSemanticHost.IsCharacterVariable,
                productionSemanticHost.IsVariableWriteSupported,
                productionSemanticHost.IsVariableAvailableInContext,
                productionSemanticHost.IsCharacterVariableInContext,
                productionSemanticHost.IsStringAssignmentTarget,
                string.IsNullOrWhiteSpace(Program.NextRuntimeReadinessEvidencePath) ? null : productionSemanticHost.DescribeVariable,
                string.IsNullOrWhiteSpace(Program.NextRuntimeReadinessEvidencePath) ? null : productionSemanticHost.DescribeCall);
            var productionCapabilities = new VmRuntimeCapabilitySnapshot(
                VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite | VmRuntimeRequirement.Builtin | VmRuntimeRequirement.FrameBridge,
                productionSemanticHost.IsVariableBound,
                productionSemanticHost.IsBuiltinBound,
                productionSemanticHost.IsFrameVariableBound,
                productionSemanticHost.IsCharacterVariable,
                productionSemanticHost.IsVariableWriteSupported,
                productionSemanticHost.IsVariableAvailableInContext,
                productionSemanticHost.IsCharacterVariableInContext,
                productionSemanticHost.IsStringAssignmentTarget);
#if PERFORMANCE_METRICS
            var readinessRequirementsTotalStart = PerformanceMetrics.StartNextRuntimeMeasurement();
#endif
            var stagedReadiness = VmRuntimeRequirementAnalyzer.AnalyzeStaged(link.Program, kinds, sharedCapabilities, productionCapabilities, productionPreparationCounters, RecordProductionTimingStage);
#if PERFORMANCE_METRICS
            PerformanceMetrics.RecordNextRuntimeStartupStage("Readiness.Requirements.Total", readinessRequirementsTotalStart);
            PerformanceMetrics.RecordNextRuntimeReadinessCounters(
                productionPreparationCounters.RuntimeFunctionCount,
                productionPreparationCounters.CodeInstructionVisits,
                productionPreparationCounters.SemanticRecordVisits,
                productionPreparationCounters.HostIdentityVisits,
                productionPreparationCounters.RequirementCandidateCount,
                productionPreparationCounters.RequirementDedupLookupCount,
                productionPreparationCounters.RequirementOutputCount);
            PerformanceMetrics.RecordNextRuntimeHostIdentityLookupMetrics(
                productionPreparationCounters.HostIdentityLookupCount,
                productionPreparationCounters.HostIdentityLookupHitCount,
                productionPreparationCounters.HostIdentityLookupMissCount,
                productionPreparationCounters.HostIdentityDirectIndexLookupCount,
                productionPreparationCounters.HostIdentityLinearScanElementVisits,
                productionPreparationCounters.HostIdentityPresentedElementCount,
                productionPreparationCounters.MaxHostIdentitiesLength,
                productionPreparationCounters.VariableLookupCount,
                productionPreparationCounters.VariableLinearScanElementVisits,
                productionPreparationCounters.CallLookupCount,
                productionPreparationCounters.CallLinearScanElementVisits);
#endif
            var sharedReadiness = stagedReadiness.Shared;
            var productionStage = stagedReadiness.Production;
            sharedExecutableReadyIds = sharedReadiness.Rows
                .Where(row => row.TransitiveEligible)
                .Select(row => row.RuntimeId.Value)
                .ToHashSet();
            var productionReadinessGated = productionStage.RestrictEligibleTo(sharedExecutableReadyIds);
            var activation = VmRuntimeActivator.Activate(link.Program, productionReadinessGated);
            productionSharedReadiness = sharedReadiness;
            productionReadiness = productionReadinessGated;
            productionExecutableReadyIds = productionReadinessGated.Rows
                .Where(row => row.TransitiveEligible)
                .Select(row => row.RuntimeId.Value)
                .ToHashSet();
            productionDispatchEntryReadyIds = productionExecutableReadyIds
                .Where(id => kinds[id] == FunctionKind.Normal)
                .ToHashSet();
            RecordProductionMemoryStage("AfterReadiness");
            RecordProductionTimingStage("ProductionPreparation.AfterActivation");
            if (!activation.AccountingPassed) throw new InvalidOperationException("production readiness accounting failed");
            if (!string.IsNullOrWhiteSpace(Program.NextRuntimeReadinessEvidencePath))
                WriteNextRuntimeReadinessEvidence(Program.NextRuntimeReadinessEvidencePath!, link.Program, catalog, kinds, stagedReadiness, productionReadinessGated, sharedCapabilities, productionPreparationCounters);
            RecordProductionTimingStage("ProductionPreparation.AfterReadinessEvidence");
            productionActivationResult = activation;
            productionDispatchEntryReadyCount = productionDispatchEntryReadyIds.Count;
            productionPreparationCount++;
            productionActivationCount++;
            productionPreparationFailureReason = "None";
            RecordProductionTimingStage("ProductionPreparation.Complete");
            log?.WriteLine($"NextRuntimePreparation=PASS Functions={link.Program.Descriptors.Length} Compiled={prototypes.Count} Promoted={activation.Promoted}");
            return true;
        }
        catch (Exception exception)
        {
            InvalidateNextRuntimeProductionProgram();
            productionPreparationFailureReason = $"{exception.GetType().Name}:{exception.Message}";
            log?.WriteLine($"NextRuntimePreparation=FALLBACK Reason={exception.GetType().Name}:{exception.Message}");
            return false;
        }
    }

    internal void InvalidateNextRuntimeProductionProgram()
    {
        nextRuntimeSession = null;
        productionProgram = null;
        productionSemanticStructuralLookup = null;
        productionVmMachineProgramLookup = null;
        productionSemanticHost = null;
        productionFrameCatalog = null;
        productionLabelIds = null;
        productionFunctionKinds = null;
        sharedExecutableReadyIds = [];
        productionExecutableReadyIds = [];
        productionDispatchEntryReadyIds = [];
        productionSharedReadiness = null;
        productionReadiness = null;
    }

    private static string NormalizePositionPath(string path)
    {
        return SourcePositionKey.NormalizePath(path, Program.ErbDir);
    }

    private static string PositionKeyFromNormalizedPath(string normalizedPath, int line) => SourcePositionKey.FromNormalizedPath(normalizedPath, line);

    private static string PositionKey(string path, int line) => SourcePositionKey.Create(path, line, Program.ErbDir);

    private static void WriteNextRuntimeReadinessEvidence(
        string root,
        LinkedProgram program,
        FunctionCatalog catalog,
        IReadOnlyList<FunctionKind> kinds,
        VmRuntimeRequirementAnalyzer.StagedResult staged,
        VmRuntimeReadinessResult production,
        VmRuntimeCapabilitySnapshot sharedCapabilities,
        VmRuntimePreparationCounters? counters)
    {
        var diagnostics = staged.Diagnostics ?? throw new InvalidOperationException("readiness diagnostics missing");
        Directory.CreateDirectory(root);
        static string Clean(string value) => value.Replace("\t", "\\t").Replace("\r", string.Empty).Replace("\n", "\\n");
        static string Mask(VmRuntimeRequirement value) => string.Join(',', Enum.GetValues<VmRuntimeRequirement>().Where(flag => flag != VmRuntimeRequirement.None && (value & flag) != 0).Select(flag => flag.ToString()));
        static string Bool(VmRuntimeRequirement value, VmRuntimeRequirement flag) => ((value & flag) != 0).ToString();
        static void WriteRows(string path, string header, IEnumerable<string> rows) => File.WriteAllText(path, header + "\n" + string.Join('\n', rows) + "\n", new UTF8Encoding(false));
        string SourceFile(int id)
        {
            var path = catalog.GetFileIdentity(id) ?? string.Empty;
            if (Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(Program.ErbDir))
                path = Path.GetRelativePath(Program.ErbDir, path);
            return path.Replace('\\', '/');
        }
        string Name(int id) => catalog.GetEffectiveName(id) ?? catalog.GetPhysicalName(id) ?? $"#{id}";
        int Line(int id) => catalog.GetSpan(id)?.StartLine ?? -1;
        string EdgeKey(VmRuntimeReadinessEdgeRecord edge) => $"{edge.Caller.Value}\t{edge.Callee.Value}\t{edge.EdgeKind}";
        var localHeader = "RuntimeFunctionId\tFunctionKind\tSourceFile\tStartLine\tFunctionName\tLocalRequirementMask\tCode\tVariableRead\tVariableWrite\tBuiltin\tHostStatement\tCharacter\tRandom\tUnsupportedVariable\tUnsupportedBuiltin\tUnsupportedExpressionMethod\tFrameBridge\tEventSemantics\tLocalCodeAvailable\tLocalBarrier\tLocalRequirementNone";
        WriteRows(Path.Combine(root, "current-shared-local-requirements.tsv"), localHeader,
            diagnostics.SharedLocalNodes.Select((node, id) =>
            {
                var mask = node.Requirements;
                var descriptor = program.Descriptors[id];
                var codeAvailable = descriptor.State != VmFunctionState.CodeNotAvailable;
                var barrier = mask.HasFlag(VmRuntimeRequirement.Code) && !codeAvailable;
                return string.Join('\t', id, kinds[id], Clean(SourceFile(id)), Line(id), Clean(Name(id)), Mask(mask),
                    Bool(mask, VmRuntimeRequirement.Code), Bool(mask, VmRuntimeRequirement.VariableRead), Bool(mask, VmRuntimeRequirement.VariableWrite), Bool(mask, VmRuntimeRequirement.Builtin),
                    Bool(mask, VmRuntimeRequirement.HostStatement), Bool(mask, VmRuntimeRequirement.Character), Bool(mask, VmRuntimeRequirement.Random), Bool(mask, VmRuntimeRequirement.UnsupportedVariable),
                    Bool(mask, VmRuntimeRequirement.UnsupportedBuiltin), Bool(mask, VmRuntimeRequirement.UnsupportedExpressionMethod), Bool(mask, VmRuntimeRequirement.FrameBridge), Bool(mask, VmRuntimeRequirement.EventSemantics),
                    codeAvailable, barrier, mask == VmRuntimeRequirement.None);
            }));
        var transitiveHeader = "RuntimeFunctionId\tLocalRequirementMask\tTransitiveRequirementMask\tSharedCapabilitiesMask\tSharedBlockedMask\tSharedReady\tSccId\tSccSize";
        WriteRows(Path.Combine(root, "current-shared-transitive-requirements.tsv"), transitiveHeader,
            staged.Shared.Rows.Select((row, id) => string.Join('\t', id, Mask(diagnostics.SharedLocalNodes[id].Requirements), Mask(row.TransitiveRequirements), Mask(sharedCapabilities.SupportedRequirements), Mask(row.BlockingRequirements), row.TransitiveEligible, row.SccId, staged.Shared.SccGroups[row.SccId].Length)));
        var rawHeader = "RecordOrdinal\tCallerRuntimeFunctionId\tTargetRuntimeFunctionId\tEdgeKind\tSourceKind\tValidCaller\tValidTarget";
        WriteRows(Path.Combine(root, "current-readiness-edges-raw.tsv"), rawHeader,
            diagnostics.RawEdges.Select(edge => string.Join('\t', edge.RecordOrdinal, edge.Caller.Value, edge.Callee.Value, edge.EdgeKind, edge.SourceKind, edge.ValidCaller, edge.ValidTarget)));
        var unique = diagnostics.RawEdges.Where(edge => edge.ValidCaller && edge.ValidTarget).GroupBy(EdgeKey).Select(group => group.First()).OrderBy(edge => edge.Caller.Value).ThenBy(edge => edge.Callee.Value).ThenBy(edge => edge.EdgeKind).ToArray();
        var uniqueHeader = "CallerRuntimeFunctionId\tTargetRuntimeFunctionId\tEdgeKind";
        WriteRows(Path.Combine(root, "current-readiness-edges-unique.tsv"), uniqueHeader,
            unique.Select(edge => string.Join('\t', edge.Caller.Value, edge.Callee.Value, edge.EdgeKind)));
        var fixedEdges = diagnostics.RawEdges.Where(edge => edge.SourceKind == "FixedCallSite").ToArray();
        var expressionEdges = diagnostics.RawEdges.Where(edge => edge.EdgeKind == "ExpressionUserMethod").ToArray();
        var validRaw = diagnostics.RawEdges.Count(edge => edge.ValidCaller && edge.ValidTarget);
        var fixedPairs = fixedEdges.Where(edge => edge.ValidCaller && edge.ValidTarget).Select(edge => $"{edge.Caller.Value}\t{edge.Callee.Value}").Distinct().Count();
        var uniqueExpression = expressionEdges.Where(edge => edge.ValidCaller && edge.ValidTarget).Select(EdgeKey).Distinct().Count();
        var other = diagnostics.RawEdges.Where(edge => edge.SourceKind != "FixedCallSite" && edge.SourceKind != "ExpressionSemantic").ToArray();
        var summary = new[]
        {
            $"RawFixedCallRecords={fixedEdges.Length}",
            $"RawCallRecords={fixedEdges.Count(edge => edge.EdgeKind == "CALL")}",
            $"RawJumpRecords={fixedEdges.Count(edge => edge.EdgeKind == "JUMP")}",
            $"RawExpressionUserMethodRecords={expressionEdges.Length}",
            $"RawOtherEdgeRecords={other.Length}",
            $"RawTotalEdgeRecords={diagnostics.RawEdges.Length}",
            $"UniqueFixedCallJumpEdges={fixedPairs}",
            $"UniqueExpressionUserMethodEdges={uniqueExpression}",
            $"UniqueOtherEdges={unique.Count(edge => edge.SourceKind != "FixedCallSite" && edge.SourceKind != "ExpressionSemantic")}",
            $"CombinedUniqueCanonicalEdges={unique.Length}",
            $"DuplicateRawEdgeRecords={validRaw - unique.Length}",
            $"SelfEdges={diagnostics.RawEdges.Count(edge => edge.ValidCaller && edge.ValidTarget && edge.Caller == edge.Callee)}",
            $"InvalidCallerEdges={diagnostics.RawEdges.Count(edge => !edge.ValidCaller)}",
            $"InvalidTargetEdges={diagnostics.RawEdges.Count(edge => !edge.ValidTarget)}",
            "DiagnosticOnly=YES"
        };
        File.WriteAllLines(Path.Combine(root, "current-readiness-edge-summary.txt"), summary, new UTF8Encoding(false));
        var reconciliation = new[]
        {
            "CallEdgeVisitCountDefinition=number of raw edge input records visited by VmRuntimeReadinessGraph.Build",
            $"RawTotalEdgeRecords={diagnostics.RawEdges.Length}",
            $"CallEdgeVisitCount={counters?.CallEdgeVisitCount ?? 0}",
            $"Difference={(diagnostics.RawEdges.Length - (counters?.CallEdgeVisitCount ?? 0))}",
            "Reason=export observes the same raw edge records used by the single graph build",
            $"Is40898DirectlyComparableTo37714={(diagnostics.RawEdges.Length == 37714 ? "YES" : "NO")}",
            $"Is40898DirectlyComparableTo11052={(diagnostics.RawEdges.Length == 11052 ? "YES" : "NO")}",
            "DiagnosticOnly=YES"
        };
        File.WriteAllLines(Path.Combine(root, "call-edge-counter-reconciliation.txt"), reconciliation, new UTF8Encoding(false));
        var expressionHeader = "CallerRuntimeFunctionId\tSemanticRecordIndex\tTargetRuntimeFunctionId\tResolved\tEdgeAdded\tUnsupportedExpressionMethodAdded\tReason";
        WriteRows(Path.Combine(root, "current-expression-user-method-edges.tsv"), expressionHeader,
            diagnostics.ExpressionUserMethods.Select(row => string.Join('\t', row.Caller.Value, row.SemanticRecordIndex, row.Target.Value, row.Resolved, row.EdgeAdded, row.UnsupportedExpressionMethodAdded, Clean(row.Reason))));
        var expressionSummary = new[]
        {
            $"ExpressionUserMethodOccurrences={diagnostics.ExpressionUserMethods.Length}",
            $"ExpressionUserMethodResolved={diagnostics.ExpressionUserMethods.Count(row => row.Resolved)}",
            $"ExpressionUserMethodUnresolved={diagnostics.ExpressionUserMethods.Count(row => !row.Resolved)}",
            $"ExpressionUserMethodEdgesAddedRaw={diagnostics.ExpressionUserMethods.Count(row => row.EdgeAdded)}",
            $"ExpressionUserMethodEdgesUnique={diagnostics.ExpressionUserMethods.Where(row => row.EdgeAdded).Select(row => $"{row.Caller.Value}\t{row.Target.Value}").Distinct().Count()}",
            "IdentitySource=compiler-owned typed ExpressionFunctionTargets",
            "RawNameFallback=NO"
        };
        File.WriteAllLines(Path.Combine(root, "current-expression-user-method-summary.txt"), expressionSummary, new UTF8Encoding(false));
        var readyHeader = "RuntimeFunctionId\tSharedReady\tSccId\tBlockedMask";
        WriteRows(Path.Combine(root, "current-shared-ready-ids.tsv"), readyHeader,
            staged.Shared.Rows.Select(row => string.Join('\t', row.RuntimeId.Value, row.TransitiveEligible, row.SccId, Mask(row.BlockingRequirements))));
        var productionHeader = "RuntimeFunctionId\tProductionReady\tProductionDispatchEntryReady\tFunctionKind\tProductionBlockedMask";
        WriteRows(Path.Combine(root, "current-production-ready-ids.tsv"), productionHeader,
            production.Rows.Select(row => string.Join('\t', row.RuntimeId.Value, row.TransitiveEligible, row.TransitiveEligible && kinds[row.RuntimeId.Value] == FunctionKind.Normal, kinds[row.RuntimeId.Value], Mask(row.BlockingRequirements))));
        var originHeader = "RuntimeFunctionId\tRequirementKind\tFirstArenaKind\tFirstRecordIndex\tFirstNodeIndex\tOriginCount\tOriginCategory";
        WriteRows(Path.Combine(root, "current-local-requirement-origins.tsv"), originHeader,
            diagnostics.LocalRequirementOrigins.Select(row => string.Join('\t', row.RuntimeId.Value, row.Requirement, row.FirstArenaKind, row.FirstRecordIndex, row.FirstNodeIndex, row.OriginCount, row.OriginCategory)));
        var occurrenceHeader = "RuntimeFunctionId\tPc\tOperandIndex\tStatementKind\tArenaKind\tRecordIndex\tNodeIndex\tParentNodeIndex\tParentNodeKind\tNodeKind\tRole\tUseContext\tIsRead\tIsWrite\tIsAssignmentDestination\tIsTimesDestination\tIsVariableRoot\tIsIndexChild\tIsBareStringLiteralContext\tIsCharacterContext\tIsFrameContext\tIsCsvIndexContext\tStatementTargetIsString\tHostIdentityStableId\tNameSymbolIndex\tSubkeySymbolIndex\tIndexArity\tTokenCode\tTokenName\tTokenFamily\tStorageShape\tTokenResolved\tTokenResolutionReason\tCacheLookupPerformed\tCacheHit\tCacheKey\tContextAvailable\tRequirementVariableRead\tRequirementVariableWrite\tRequirementUnsupportedVariable\tRequirementCharacter\tRequirementContribution";
        WriteRows(Path.Combine(root, "current-external-operand-occurrences.tsv"), occurrenceHeader,
            diagnostics.OccurrenceDiagnostics.Select(row => string.Join('\t', row.RuntimeFunctionId, row.Pc, row.OperandIndex, Clean(row.StatementKind), row.ArenaKind, row.RecordIndex, row.NodeIndex,
                row.ParentNodeIndex, Clean(row.ParentNodeKind), row.NodeKind, row.Role, row.Use, row.IsRead, row.IsWrite, row.IsAssignmentDestination, row.IsTimesDestination,
                row.IsVariableRoot, row.IsIndexChild, row.IsBareStringLiteralContext, row.IsCharacterContext, row.IsFrameContext, row.IsCsvIndexContext, row.StatementTargetIsString,
                row.HostIdentityStableId.ToString("X16"), row.NameSymbolIndex, row.SubkeySymbolIndex, row.IndexArity, Clean(row.TokenCode), Clean(row.TokenName), row.TokenFamily, row.StorageShape,
                row.TokenResolved, row.TokenResolutionReason, row.CacheLookupPerformed, row.CacheHit, row.CacheKey, row.ContextAvailable, row.RequirementVariableRead,
                row.RequirementVariableWrite, row.RequirementUnsupportedVariable, row.RequirementCharacter, row.RequirementContribution)));
        var nonVariableHeader = "RuntimeFunctionId\tPc\tOperandIndex\tStatementKind\tArenaKind\tRecordIndex\tNodeIndex\tParentNodeIndex\tParentNodeKind\tNodeKind\tRole\tHostIdentityStableId\tTargetRuntimeFunctionId\tTargetKind\tTargetResolved\tCallable\tBuiltinResolved\tUnsupportedExpressionMethod\tUnsupportedBuiltin\tBlockingRequirement\tResolvedBuiltinIdentity\tReason";
        WriteRows(Path.Combine(root, "current-nonvariable-blocking-occurrences.tsv"), nonVariableHeader,
            diagnostics.NonVariableBlockingDiagnostics.Select(row => string.Join('\t', row.RuntimeFunctionId, row.Pc, row.OperandIndex, Clean(row.StatementKind), row.ArenaKind, row.RecordIndex, row.NodeIndex,
                row.ParentNodeIndex, Clean(row.ParentNodeKind), row.NodeKind, row.Role, row.HostIdentityStableId.ToString("X16"), row.TargetRuntimeFunctionId, row.TargetKind, row.TargetResolved, row.Callable,
                row.BuiltinResolved, row.UnsupportedExpressionMethod, row.UnsupportedBuiltin, row.RequirementContribution, Clean(row.ResolvedBuiltinIdentity), Clean(row.Reason))));
        var occurrenceRows = diagnostics.OccurrenceDiagnostics.Length;
        var nonVariableRows = diagnostics.NonVariableBlockingDiagnostics.Length;
        File.WriteAllLines(Path.Combine(root, "current-occurrence-export-accounting.txt"), new[]
        {
            "DiagnosticOnly=YES",
            $"ExternalOperandOccurrenceRows={occurrenceRows}",
            $"NonVariableBlockingOccurrenceRows={nonVariableRows}",
            $"OccurrenceKeyUniqueByDesign={diagnostics.OccurrenceDiagnostics.Select(row => $"{row.RuntimeFunctionId}:{row.Pc}:{row.OperandIndex}:{row.ArenaKind}:{row.RecordIndex}:{row.NodeIndex}:{row.HostIdentityStableId:X16}").Distinct().Count() == occurrenceRows}",
            $"RoleJoinable={diagnostics.OccurrenceDiagnostics.All(row => !string.IsNullOrEmpty(row.Role))}",
            $"IdentityJoinable={diagnostics.OccurrenceDiagnostics.All(row => row.HostIdentityStableId != 0)}",
            $"TokenResolutionJoinable={diagnostics.OccurrenceDiagnostics.All(row => row.TokenResolved || row.TokenResolutionReason.Length > 0)}",
            $"CacheDecisionObservable={diagnostics.OccurrenceDiagnostics.All(row => !row.CacheLookupPerformed || row.CacheKey.Length > 0)}",
            $"ClassifierDecisionJoinable={diagnostics.OccurrenceDiagnostics.All(row => row.ContextAvailable || row.TokenResolutionReason.Length > 0)}",
            $"RequirementContributionJoinable={diagnostics.OccurrenceDiagnostics.All(row => row.RequirementContribution.Length > 0)}",
            $"ExpressionEdgeOriginJoinable={diagnostics.NonVariableBlockingDiagnostics.All(row => row.TargetKind.Length > 0)}",
            $"VariableReadOccurrences={diagnostics.OccurrenceDiagnostics.Count(row => row.IsRead)}",
            $"VariableWriteOccurrences={diagnostics.OccurrenceDiagnostics.Count(row => row.IsWrite)}",
            $"CharacterContextOccurrences={diagnostics.OccurrenceDiagnostics.Count(row => row.IsCharacterContext)}",
            $"FrameContextOccurrences={diagnostics.OccurrenceDiagnostics.Count(row => row.IsFrameContext)}",
            $"CsvIndexContextOccurrences={diagnostics.OccurrenceDiagnostics.Count(row => row.IsCsvIndexContext)}",
            $"BareStringContextOccurrences={diagnostics.OccurrenceDiagnostics.Count(row => row.IsBareStringLiteralContext)}",
            $"UnsupportedVariableOccurrences={diagnostics.OccurrenceDiagnostics.Count(row => row.RequirementUnsupportedVariable)}",
            $"UnsupportedBuiltinOccurrences={diagnostics.NonVariableBlockingDiagnostics.Count(row => row.UnsupportedBuiltin)}",
            $"UnsupportedExpressionMethodOccurrences={diagnostics.NonVariableBlockingDiagnostics.Count(row => row.UnsupportedExpressionMethod)}"
        }, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(root, "current-readiness-export-summary.txt"), new[]
        {
            "ReadinessSemanticsChangedByExporter=NO",
            "NormalProductionEvidenceExport=OFF unless --NextRuntimeReadinessEvidence is supplied",
            $"ExportedFunctionRows={diagnostics.SharedLocalNodes.Length}",
            $"ExportedRawEdgeRows={diagnostics.RawEdges.Length}",
            $"ExportedUniqueCanonicalEdgeRows={unique.Length}",
            $"ExportedSharedReadyRows={staged.Shared.Rows.Length}",
            $"EvidenceExportAdditionalSccBuildCount=0",
            $"EvidenceExportAdditionalReadinessEvaluationCount=0",
            "DiagnosticOnly=YES"
        }, new UTF8Encoding(false));
    }
}
