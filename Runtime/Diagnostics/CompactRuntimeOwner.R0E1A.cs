#if R0_E1A
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal readonly record struct CompactFunctionHandle(CompactRuntimeOwner? Owner, RuntimeFunctionId Id)
{
    internal QueryValue Execute(CompactRuntimeOwner expectedOwner, object host, ReadOnlySpan<QueryValue> arguments, int stepLimit = 1_000_000)
        => Owner?.Execute(this, expectedOwner, host, arguments, stepLimit)
            ?? throw new InvalidOperationException("Flat explicit FAIL: default handle");
}

internal sealed partial class CompactRuntimeOwner
{
#if R0_E2S
    internal readonly record struct StartupStage(double Milliseconds, long AllocatedBytes);
    internal sealed record StartupBreakdown(StartupStage EnvironmentAndHeaders, StartupStage SourceIndex,
        StartupStage ExactCohortValidation, StartupStage CatalogIdentity, StartupStage ExternalAliasScan,
        StartupStage OtherAdmission, double TotalMilliseconds, long TotalAllocatedBytes,
        R0E2SSourceIndexCensus SourceIndexCensus, int AliasFileCount, long AliasCharactersScanned,
        int AliasRegexSearchCount, int AliasNameCount, int SelectedAliasCount,
        int HeaderFileCount, long HeaderBytes, long AliasBytes, int TotalFileOpens,
        int UniqueFilesOpened, int SameFileReopenCount, int FullFileReads);
    internal StartupBreakdown? R0E2SBreakdown { get; private set; }
    internal long R0E2SFunctionSliceBytesRead { get; private set; }
    internal long R0E2SFingerprintBytes { get; private set; }
#endif
    private enum EntryState : byte { Unloaded, Building, Ready, Blocked }
    private sealed class Entry(Spec spec)
    {
        internal readonly Spec Spec = spec;
        internal EntryState State;
        internal int ProgramId = -1;
        internal string? Blocker;
    }
    private sealed record Spec(int Id, string Name, string Path, int Line, string Fingerprint,
        bool[] Strings, int[] Cells);
    private sealed record BuildResult(QueryProgram[] Programs,
        Dictionary<string, int> ProgramIds, WeakReference[] TemporaryRoots);

    private static readonly Spec[] Specs =
    [
        new(0,"HAVE_SKILL","関数/組み込み関数/キャラクタデータ参照／ABL/HAVE_SKILL.ERB",8,"775827EA2E984866D3B2AD367B2A1472D08BB0D24A126ED6A1641FC71A42ACFF",[false,false,false,true],[0,1,2,3]),
        new(1,"CHARA_SKILLCOUNT","関数/私家版追加関数/CHARA_SKILLCOUNT.ERB",6,"CCEDC11B553B2FF30D546260C22DA5AB1E8E6B56AAE474256E51EF7D22F1A132",[false,true],[0,1]),
        new(2,"FINDCHARA_LINK","関数/組み込み関数/キャラクタ検索/FINDCHARA_LINK.ERB",5,"02FCFB860509E1263B641D55FFB4B76447B1BF27F73868F82321F079242545C4",[false,false],[0,1]),
        new(3,"FINDCHARA_TIMEID","関数/組み込み関数/キャラクタ検索/FINDCHARA_TIMEID.ERB",5,"14B457CE4B6BDA9A5A10E923A341DF00A338BEABD542292E20AF36823AE43242",[false],[0]),
        new(4,"COUNT_SPLIT","関数/汎用組み込み関数/SPLIT/COUNT_SPLIT.ERB",21,"6114786D825DA3D21D506A36CE6B814A5218B0AF179E71720705306589361FEC",[true,true,true],[1,2,3])
    ];

    private readonly object hostIdentity;
    private readonly object mediatorIdentity;
    private readonly StructuralSemanticEnvironment environment;
    private readonly Entry[] entries = Specs.Select(spec => new Entry(spec)).ToArray();
    private readonly bool[] externalAliasBlocked;
    private IReadOnlyList<SourceFileIndex>? sourceFiles;
    private FunctionCatalog? catalog;
    private QueryProgram[]? programs = [];
    private FlatQueryContext? context;
    private readonly List<WeakReference> temporaryRoots = [];
    private bool active = true;
    private bool everRevoked;
    private int activeCalls;
    private long retiredContextCalls;

    internal int CompiledProgramCount => programs?.Length ?? 0;
    internal int FunctionSliceReadCount { get; private set; }
    internal int CompileCount { get; private set; }
    internal long Attempts { get; private set; }
    internal long Completed { get; private set; }
    internal long Faults { get; private set; }
    internal long FlatToFlatCalls => retiredContextCalls + (context?.CallsExecuted ?? 0) - Attempts;
    internal FunctionCatalog Catalog => catalog ?? throw new ObjectDisposedException(nameof(CompactRuntimeOwner));
    internal bool Active => active;

    internal CompactRuntimeOwner(object hostIdentity, object mediatorIdentity)
    {
#if R0_E2S
        var totalAllocated = GC.GetTotalAllocatedBytes(true);
        var totalStart = System.Diagnostics.Stopwatch.GetTimestamp();
        static StartupStage Stage(long start, long allocated) => new(
            R0E2Measurement.Milliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - start),
            GC.GetTotalAllocatedBytes(true) - allocated);
        var allocated = GC.GetTotalAllocatedBytes(true);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        this.hostIdentity = hostIdentity;
        this.mediatorIdentity = mediatorIdentity;
        var options = new CompilerCompatibilityOptions(RuntimeConfig.IgnoreCase,
            JSONConfig.Game.UseScopedVariableInstruction, RuntimeConfig.SystemAllowFullSpace, false);
        var rename = RuntimeConfig.UseRenameFile && ParserMediator.RenameDic is not null
            ? new SemanticRenameResolver(ParserMediator.RenameDic) : null;
#if R0_E2S
        var headerPaths = RuntimeConfig.GetFiles(Program.ErbDir,"*.ERH").Select(path => path.Value).ToArray();
        var macros = MacroCatalog.FromHeaderSources(headerPaths
            .Select(path => File.ReadAllText(path, RuntimeConfig.Encode)), options, rename);
#else
        var macros = MacroCatalog.FromHeaderSources(RuntimeConfig.GetFiles(Program.ErbDir,"*.ERH")
            .Select(path => File.ReadAllText(path.Value, RuntimeConfig.Encode)), options, rename);
#endif
        environment = new(options, macros, RuntimeConfig.SystemIgnoreTripleSymbol, rename);
#if R0_E2S
        var environmentStage = Stage(started, allocated);
        allocated = GC.GetTotalAllocatedBytes(true); started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif

        sourceFiles = ErbSourceIndexer.IndexDirectory(Program.ErbDir);
        if (sourceFiles.Any(file => file.Error is not null))
            throw new InvalidOperationException("Source index contains an error");
#if R0_E2S
        var sourceIndexStage = Stage(started, allocated);
        allocated = GC.GetTotalAllocatedBytes(true); started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        var found = new (int File, int Function)[Specs.Length];
        var exactCounts = new int[Specs.Length];
        var nameCounts = new int[Specs.Length];
        for (var fileIndex = 0; fileIndex < sourceFiles.Count; fileIndex++)
        {
            var file = sourceFiles[fileIndex];
            var relativePath = Relative(file.FileIdentity);
            for (var functionIndex = 0; functionIndex < file.Functions.Count; functionIndex++)
            {
                var function = file.Functions[functionIndex];
                for (var id = 0; id < Specs.Length; id++)
                {
                    var spec = Specs[id];
                    if (string.Equals(function.Name, spec.Name, options.NameComparison)) nameCounts[id]++;
                    if (!string.Equals(relativePath, spec.Path, StringComparison.OrdinalIgnoreCase)
                        || function.Span.StartLine != spec.Line
                        || !string.Equals(function.Name, spec.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (exactCounts[id]++ == 0) found[id] = (fileIndex, functionIndex);
                }
            }
        }
        for (var id = 0; id < Specs.Length; id++)
        {
            var spec = Specs[id];
            if (exactCounts[id] != 1) throw new InvalidOperationException("Exact source identity missing/ambiguous: " + spec.Name);
            if (nameCounts[id] != 1)
                throw new InvalidOperationException("Function name duplicate/case ambiguity: " + spec.Name);
        }
#if R0_E2S
        var exactStage = Stage(started, allocated);
        allocated = GC.GetTotalAllocatedBytes(true); started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        catalog = FunctionCatalog.FromRuntimeBindings(sourceFiles, Specs.Select((spec, id) =>
            new RuntimeFunctionBinding(new(id), spec.Name, FunctionKind.Method,
                sourceFiles[found[id].File].Functions[found[id].Function].Flags, true,
                new(found[id].File, found[id].Function))), RuntimeConfig.IgnoreCase);
#if R0_E2S
        var catalogStage = Stage(started, allocated);
        allocated = GC.GetTotalAllocatedBytes(true); started = System.Diagnostics.Stopwatch.GetTimestamp();
        var aliasFileCount = 0; long aliasCharacters = 0;
        IEnumerable<string> AliasSources()
        {
            foreach (var path in RuntimeConfig.GetFiles(Program.ErbDir,"*.ERB")
                         .Concat(RuntimeConfig.GetFiles(Program.ErbDir,"*.ERH")))
            {
                aliasFileCount++;
                var source = File.ReadAllText(path.Value, RuntimeConfig.Encode);
                aliasCharacters += source.Length;
                yield return source;
            }
        }
        var aliases = AliasSources().SelectMany(FlatQueryCatalog.FindExternalFrameAliases)
            .ToHashSet(options.NameComparer);
#else

        var aliases = RuntimeConfig.GetFiles(Program.ErbDir,"*.ERB").Concat(RuntimeConfig.GetFiles(Program.ErbDir,"*.ERH"))
            .SelectMany(path => FlatQueryCatalog.FindExternalFrameAliases(File.ReadAllText(path.Value, RuntimeConfig.Encode)))
            .ToHashSet(options.NameComparer);
#endif
        externalAliasBlocked = Specs.Select(spec => aliases.Contains(spec.Name)).ToArray();
#if R0_E2S
        var aliasStage = Stage(started, allocated);
        allocated = GC.GetTotalAllocatedBytes(true); started = System.Diagnostics.Stopwatch.GetTimestamp();
        var census = R0E2SSourceIndexMetrics.Last;
        var headerBytes = headerPaths.Sum(path => new FileInfo(path).Length);
        var aliasBytes = census.SourceBytes + headerBytes;
        var uniqueFiles = census.FileCount + headerPaths.Length;
        var opens = census.FileCount + aliasFileCount + headerPaths.Length;
        var otherStage = Stage(started, allocated);
        R0E2SBreakdown = new(environmentStage, sourceIndexStage, exactStage, catalogStage, aliasStage,
            otherStage, R0E2Measurement.Milliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - totalStart),
            GC.GetTotalAllocatedBytes(true) - totalAllocated, census, aliasFileCount, aliasCharacters,
            aliasFileCount, aliases.Count, externalAliasBlocked.Count(value => value), headerPaths.Length,
            headerBytes, aliasBytes, opens, uniqueFiles, opens - uniqueFiles, opens);
#endif
    }

    internal CompactFunctionHandle Handle(string name)
    {
        var id = Array.FindIndex(Specs, spec => string.Equals(spec.Name, name, environment.Compatibility.NameComparison));
        if (id < 0) throw new InvalidOperationException("Function is outside the exact E1A cohort: " + name);
        return new(this, new(id));
    }

    internal QueryValue Execute(CompactFunctionHandle handle, CompactRuntimeOwner expectedOwner, object host, ReadOnlySpan<QueryValue> arguments,
        int stepLimit = 1_000_000)
    {
        RequireLive(handle, expectedOwner, host);
        Materialize(handle.Id.Value);
        RequireLive(handle, expectedOwner, host);
        var entry = entries[handle.Id.Value];
        var program = programs![entry.ProgramId];
        if (arguments.Length > program.Parameters.Length)
            throw new InvalidOperationException("Flat explicit FAIL: argument arity");
        activeCalls++;
        Attempts++;
        try
        {
            var value = context!.Execute(entry.ProgramId, arguments, stepLimit);
            Completed++;
            return value;
        }
        catch { Faults++; throw; }
        finally { activeCalls--; }
    }

    private void RequireLive(CompactFunctionHandle handle, CompactRuntimeOwner expectedOwner, object host)
    {
        if (!active || !ReferenceEquals(handle.Owner, this) || !ReferenceEquals(expectedOwner, this))
            throw new InvalidOperationException("Flat explicit FAIL: owner revoked/mismatch");
        if (!ReferenceEquals(hostIdentity, host) || !ReferenceEquals(GlobalStatic.EMediator, mediatorIdentity))
            throw new InvalidOperationException("Flat explicit FAIL: host identity mismatch");
        if ((uint)handle.Id.Value >= (uint)entries.Length)
            throw new InvalidOperationException("Flat explicit FAIL: RuntimeFunctionId range");
    }

    private void Materialize(int id)
    {
        if (entries[id].State == EntryState.Ready) return;
        var group = id == 4 ? new[] { 4 } : new[] { 0, 1, 2, 3 };
        Reserve(entries, group);
        try
        {
            var built = Build(group, id == 4 ? 4 : 0);
            var offset = programs!.Length;
            if (id == 4 && built.Programs.Any(program => program.Calls.Length != 0))
                throw new InvalidOperationException("COUNT_SPLIT unexpectedly has compact callees");
            programs = programs.Concat(built.Programs).ToArray();
            foreach (var index in group)
            {
                var key = Key(entries[index].Spec);
                if (!built.ProgramIds.TryGetValue(key, out var programId))
                    throw new InvalidOperationException("Partial materialization result: " + entries[index].Spec.Name);
                entries[index].ProgramId = offset + programId;
            }
            retiredContextCalls += context?.CallsExecuted ?? 0;
            context = new FlatQueryContext(programs);
            foreach (var index in group) entries[index].State = EntryState.Ready;
            temporaryRoots.AddRange(built.TemporaryRoots);
        }
        catch (Exception ex)
        {
            foreach (var index in group) { entries[index].State = EntryState.Blocked; entries[index].Blocker = ex.Message; }
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private BuildResult Build(int[] group, int root)
    {
        var compiler = new FunctionCompiler(environment);
        var weak = new List<WeakReference> { new(compiler) };
        var functions = new Dictionary<string, B1Function>(StringComparer.Ordinal);
        foreach (var id in group)
        {
            var spec = entries[id].Spec;
            var sourceRef = catalog![id].SourceRef;
            var file = sourceFiles![sourceRef.FileOrdinal];
            var function = file.Functions[sourceRef.FunctionOrdinal];
            var entry = new B1Function(file, function, Key(spec), []);
            functions.Add(entry.Key, entry);
            weak.Add(new(entry));
        }
        void Analyze(B1Function entry)
        {
            if (entry.Analyzed) return;
            entry.Analyzed = true;
            var read = FunctionSourceReader.Read(entry.File, entry.Function);
            FunctionSliceReadCount++;
            if (read.Status != SourceReadStatus.Read) throw new InvalidOperationException("Source snapshot changed: " + entry.Function.Name);
            entry.Source = read.Source!.Value;
#if R0_E2S
            R0E2SFunctionSliceBytesRead += entry.Source.Value.Bytes.Length;
            R0E2SFingerprintBytes += entry.Source.Value.Bytes.Length;
#endif
            weak.Add(new(entry.Source.Value.Bytes));
            var spec = Specs.Single(value => value.Name == entry.Function.Name);
            RequireFingerprint(entry.Source.Value.Bytes, spec.Fingerprint, spec.Name);
            entry.Compile = compiler.TryCompileRuntime(entry.Source.Value);
            CompileCount++;
            if (entry.Compile.Status != CompileStatus.Compiled)
                throw new NotSupportedException("Compiler: " + entry.Compile.Detail);
            try { entry.Flat = FlatMethodProgram.Compile(entry.Source.Value, entry.Compile.Function!, environment); }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or CodeEE) { entry.FlatBlocker = ex.Message; }
        }
        var blocked = group.Where(id => externalAliasBlocked[id]).Select(id => Specs[id].Name)
            .ToHashSet(environment.Compatibility.NameComparer);
        var builder = new FlatQueryCatalog(functions, environment, Analyze, blocked);
        builder.Link(Key(entries[root].Spec));
        var result = builder.Snapshot();
        if (result.Length != group.Length || !result.Select(program => program.Key).ToHashSet(StringComparer.Ordinal)
                .SetEquals(group.Select(id => Key(entries[id].Spec))))
            throw new InvalidOperationException("Materialization did not produce the exact requested cohort");
        foreach (var program in result) ValidateProgram(program, entries[Specs.Single(spec => Key(spec) == program.Key).Id].Spec);
        return new(result, result.Select((program, id) => (program.Key, id)).ToDictionary(pair => pair.Key, pair => pair.id), weak.ToArray());
    }

    private static void ValidateProgram(QueryProgram program, Spec spec)
    {
        if (program.ReturnsString || program.Parameters.Length != spec.Strings.Length)
            throw new InvalidOperationException("Signature mismatch: " + spec.Name);
        for (var i = 0; i < spec.Strings.Length; i++)
            if (program.Parameters[i].String != spec.Strings[i] || program.Parameters[i].Cell != spec.Cells[i])
                throw new InvalidOperationException("Header target cell/type mismatch: " + spec.Name);
    }

    private static void Reserve(Entry[] values, int[] ids)
    {
        if (ids.Any(id => values[id].State == EntryState.Building))
            throw new NotSupportedException("Building cycle");
        if (ids.Any(id => values[id].State == EntryState.Blocked))
            throw new NotSupportedException("Unsupported callee");
        foreach (var id in ids) if (values[id].State != EntryState.Ready) values[id].State = EntryState.Building;
    }

    internal bool VerifyTemporaryRootsReleased()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        return temporaryRoots.All(reference => !reference.IsAlive);
    }
#if R0_E2
    internal bool R0E2TemporaryRootsReleased => temporaryRoots.All(reference => !reference.IsAlive);
#endif

    internal object RunLiveContractSelfTest()
    {
        static bool Reject(Action action)
        {
            try { action(); return false; }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { return true; }
        }
        var valid = Handle("HAVE_SKILL");
        var defaultRejected = Reject(() => default(CompactFunctionHandle).Execute(this, hostIdentity, []));
        var rangeRejected = Reject(() => new CompactFunctionHandle(this, new(99)).Execute(this, hostIdentity, []));
        var hostRejected = Reject(() => valid.Execute(this, new object(), []));
        var ownerRejected = Reject(() => valid.Execute(null!, hostIdentity, []));
        var local = new[] { new Entry(Specs[0]) { State = EntryState.Building } };
        var cycleRejected = Reject(() => Reserve(local, [0]));
        local[0].State = EntryState.Blocked;
        var unsupportedRejected = Reject(() => Reserve(local, [0]));
        return new { DefaultHandleRejected=defaultRejected, OutOfRangeRejected=rangeRejected,
            HostMismatchRejected=hostRejected, DifferentOwnerRejected=ownerRejected,
            BuildingCycleRejected=cycleRejected, UnsupportedCalleeRejected=unsupportedRejected,
            PartialMaterializationPublished=false };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal WeakReference[] Revoke()
    {
        active = false;
        everRevoked = true;
        if (activeCalls != 0) throw new InvalidOperationException("Cannot release owner roots during an active call");
        var weak = new[] { new WeakReference(sourceFiles), new WeakReference(catalog), new WeakReference(programs), new WeakReference(context) };
        sourceFiles = null; catalog = null; programs = null; context = null;
        return weak;
    }

    internal bool RevokedPermanently => everRevoked && !active;
    internal bool SourceMismatchSelfTest()
    {
        var sourceRef = catalog![0].SourceRef;
        var read = FunctionSourceReader.Read(sourceFiles![sourceRef.FileOrdinal],
            sourceFiles[sourceRef.FileOrdinal].Functions[sourceRef.FunctionOrdinal]);
        if (read.Status != SourceReadStatus.Read) return false;
        try { RequireFingerprint(read.Source!.Value.Bytes, new string('0', 64), Specs[0].Name); return false; }
        catch (InvalidOperationException ex) when (ex.Message.Contains("fingerprint mismatch", StringComparison.Ordinal)) { return true; }
    }
    private static void RequireFingerprint(byte[] bytes, string expected, string name)
    {
        if (!string.Equals(SourceFingerprint.FromBytes(bytes).ToHexString(), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Source fingerprint mismatch: " + name);
    }
    private static string Relative(string path) => Path.GetRelativePath(Program.ErbDir, path).Replace('\\','/');
    private static string Key(Spec spec) => spec.Path.ToUpperInvariant() + "\t" + spec.Line + "\t" + spec.Name;
}
#endif
