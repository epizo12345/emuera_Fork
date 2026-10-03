using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.UI.Game.Image;
using System.Globalization;
using System.Buffers.Binary;
using System.Text;

namespace MinorShift.Emuera.Web.Runtime;

public enum BrowserInputKind { Integer, String, Enter, AnyKey, MouseKey, TimedVoid }
public enum BrowserRuntimeStatus { Running, WaitingForInput, Persisting, BootstrapReady, Succeeded, Failed }

public sealed record BrowserInputPrompt(long RequestId, BrowserInputKind Kind, bool OneInput, long TimeLimit = -1, string? TimedInputName = null, bool StopMessageSkip = false, bool DisplayTime = false, string? TimeUpMessage = null);
public sealed record BrowserMessageSkipState(bool Active, long OperationId, long StartRequestId, int Continuations, long ElapsedMilliseconds, string StopReason);
public sealed record BrowserRuntimeFailureDiagnostic(string ErbPosition, string CurrentFunction, string CurrentLabel, string ExceptionType, string ExceptionMessage, string CallStack, string OutputTail, BrowserInputPrompt? PendingInput, BrowserRuntimeStatus Status);
public enum SaveMutationKind { Put, Delete }
public sealed record SaveMutation(string OperationId, string LogicalFilename, SaveMutationKind Kind, byte[]? Bytes);
public sealed record BrowserPersistenceDiagnostic(string OperationId, string LogicalFilename, SaveMutationKind Kind, int Bytes, long CommitMilliseconds, long AckMilliseconds);
public sealed record BrowserPersistenceAckDiagnostic(long Sequence, DateTimeOffset Timestamp, string OperationId, BrowserRuntimeStatus Status, int PendingCount, string? HeadOperationId, string? HeadLogicalFilename, long SessionGeneration, long? RequestId, string ErbPosition, bool Accepted);
public sealed record GlobalCodecDiagnosticResult(bool LoadSucceeded, int SavedVariableCount, string ValueSha256, string ValueRows, string InputSha256);
public sealed record SaveCodecDiagnosticResult(bool LoadSucceeded, int Slot, int CharacterCount, long LastLoadVersion, string LastLoadText, string StateSha256, string InputSha256);
public sealed record SaveStateDiagnosticResult(bool LoadSucceeded, int Slot, int CharacterCount, long LastLoadVersion, string LastLoadText, int SavedVariableCount, int NonDefaultValueCount, string StateSha256, string ValueSha256, string ValueRows, string InputSha256);
public sealed record BrowserCodecValidationTiming(string Operation, long InputHashMilliseconds, long LoadMilliseconds, long SummaryMilliseconds, long InputBytes, string InputSha256, bool Succeeded);
public sealed record BrowserDisplayPerformanceSnapshot(
    long PublishCalls,
    long PublishElapsedTicks,
    long PublishSourceLines,
    long PublishResultLines,
    long ActivationProjectionCalls,
    long ActivationProjectionElapsedTicks,
    long ActivationProjectionSourceLines,
    long ActivationProjectionParts,
    long AnimationRefreshCalls = 0,
    long AnimationRefreshElapsedTicks = 0,
    long AnimationLinesScanned = 0,
    long AnimationPartsScanned = 0,
    long AnimationImagesScanned = 0,
    long AnimationImagesChanged = 0,
    long AnimationLinesChanged = 0,
    long AnimationPublishes = 0,
    long AnimationNoChangePublishes = 0,
    long AnimationAllocatedBytes = 0);
public sealed record BootstrapResourceSummary(int ParentImageCount, int SpriteCount, int AnimationCount, int AnimationFrameCount, string IdentitySha256 = "", string ParentIdentitySha256 = "", string SpriteIdentitySha256 = "", string SpriteNameSha256 = "", int NonZeroOffsetCount = 0, long WidthTotal = 0, long HeightTotal = 0, long OffsetXTotal = 0, long OffsetYTotal = 0, long SourceXTotal = 0, long SourceYTotal = 0, long AnimationSourceXTotal = 0, long AnimationSourceYTotal = 0, long AnimationOffsetXTotal = 0, long AnimationOffsetYTotal = 0, long AnimationDelayTotal = 0);
public sealed record BootstrapScriptSummary(int CsvFileCount, int ErhFileCount, int ErbFileCount, int CharacterTemplateCount, int VariableTokenCount, int LabelCount, string LabelIdentitySha256, int LazyErbFileCount, int LazyErbFallbackFileCount, int DeferredEagerCount, string ConfigurationIdentitySha256 = "", string VariableRegistryScope = "", int VariableSchemaCount = 0, string VariableSchemaSha256 = "", string VariableSchemaRows = "", int WarningCount = 0, int WarningKindCount = 0, string WarningIdentitySha256 = "", string WarningKindCounts = "", string WarningExamples = "");

/// <summary>
/// 同期実行を前提とするEmuera Processと、ブラウザの非同期入力・IndexedDB保存をつなぐ境界。
/// 非同期処理の完了は、入力または保存ACKとして同期Processへ戻してから再開する。
/// </summary>
public sealed class BrowserRuntimeSession
{
    public static bool SupportsErbExecutionProfiler
    {
        get
        {
#if ERB_EXECUTION_PROFILE
            return true;
#else
            return false;
#endif
        }
    }

    static long nextSessionGeneration;
    readonly EmueraConsole console = new();
    readonly RuntimeInputGate gate = new();
    readonly StringBuilder output = new();
    readonly Process process;
    readonly List<BrowserPersistenceDiagnostic> persistenceDiagnostics = [];
    readonly Queue<BrowserPersistenceAckDiagnostic> persistenceAckTimeline = new();
    readonly List<BrowserCodecValidationTiming> codecValidationProfile = [];
    bool captureCodecValidationProfile;
    long persistenceAckSequence;
    Task? persistenceDrain;
    BrowserInfiniteLoopContinuation? infiniteLoopContinuation;
    BrowserLongRunningPrompt? longRunningPrompt;
    long doScriptElapsedTicks;
#if ERB_EXECUTION_PROFILE
    public Action<string>? GameplayTrace { get => console.GameplayTrace; set => console.GameplayTrace = value; }
    public long[] ReadGameplayPoint()
    {
        var token = GlobalStatic.IdentifierDictionary.GetVariableToken("FLAG", null, false);
        return [token.GetIntValue(GlobalStatic.EMediator, [201]), token.GetIntValue(GlobalStatic.EMediator, [202]), token.GetIntValue(GlobalStatic.EMediator, [203])];
    }
    int erbProfileRunScriptCallsAtStart;
    long erbProfileRunScriptTicksAtStart;
#endif
    long nextMessageSkipOperationId;
    long messageSkipStartedTimestamp;
    BrowserMessageSkipState messageSkip = new(false, 0, 0, 0, 0, "not-started");
    IReadOnlyList<BrowserDisplayLine>? activatedDisplayLines;
    long activatedDisplayGeneration = -1;
    long activatedDisplayRequestId = -1;
    bool displayPerformanceMetricsEnabled;
    long activationProjectionCalls;
    long activationProjectionElapsedTicks;
    long activationProjectionSourceLines;
    long activationProjectionParts;

    BrowserRuntimeSession()
    {
        SessionGeneration = Interlocked.Increment(ref nextSessionGeneration);
        process = new Process(console);
        GlobalStatic.Console = console;
        GlobalStatic.Process = process;
        console.TextPrinted += value => output.Append(value);
    }

    public static void ReleaseBootstrapResources()
    {
        ParserMediator.Initialize(null!);
        VariableParser.ReleaseBootstrapReferences();
        StrForm.ReleaseBootstrapReferences();
        GlobalStatic.Reset();
        AppContents.ResetBootstrapResources();
        Preload.Clear();
    }

    public string Output => output.ToString();
    public IReadOnlyList<BrowserDisplayLine> RetainedDisplayLines => console.DisplayLines;
    public IReadOnlyList<BrowserDisplayLine> DisplayLines
    {
        get
        {
            BrowserInputPrompt? prompt = PendingInput;
            if (prompt is null || prompt.Kind is BrowserInputKind.Enter or BrowserInputKind.AnyKey) return console.DisplayLines;
            if (activatedDisplayLines is not null
                && activatedDisplayGeneration == console.DisplayStructureGeneration
                && activatedDisplayRequestId == prompt.RequestId)
                return activatedDisplayLines;
            var activation = new BrowserInputActivation(SessionGeneration, console.DisplayStructureGeneration, prompt.RequestId);
            IReadOnlyList<BrowserDisplayLine> source = console.DisplayLines;
            long buttonGeneration = console.LastButtonGeneration;
#if ERB_EXECUTION_PROFILE
            GameplayTrace?.Invoke("activation-context-start");
#endif
            activatedDisplayLines = new BrowserActivatedDisplayLines(source, line =>
            {
                if (line.MaxButtonGeneration != buttonGeneration) return line;
                long started = displayPerformanceMetricsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                long parts = 0;
                var result = line with { Parts = ActivateMeasured(line.Parts, activation, buttonGeneration, ref parts) };
                if (displayPerformanceMetricsEnabled)
                {
                    activationProjectionCalls++;
                    activationProjectionElapsedTicks += System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks;
                    activationProjectionSourceLines++;
                    activationProjectionParts += parts;
                }
                return result;
            });
            activatedDisplayGeneration = console.DisplayStructureGeneration;
            activatedDisplayRequestId = prompt.RequestId;
            DisplayProjectionCount++;
#if ERB_EXECUTION_PROFILE
            GameplayTrace?.Invoke("activation-context-end");
#endif
            return activatedDisplayLines;
        }
    }

    static IReadOnlyList<BrowserDisplayPart> ActivateMeasured(IReadOnlyList<BrowserDisplayPart> parts, BrowserInputActivation activation, long buttonGeneration, ref long count)
    {
        BrowserDisplayPart[]? activated = null;
        for (int index = 0; index < parts.Count; index++)
        {
            BrowserDisplayPart part = parts[index];
            count++;
            BrowserInputActivation? active = part.Input is not null && part.ButtonGeneration == buttonGeneration ? activation : null;
            IReadOnlyList<BrowserDisplayPart>? children = part.Children is { } source ? ActivateMeasured(source, activation, buttonGeneration, ref count) : null;
            if (part.Activation != active || !ReferenceEquals(children, part.Children))
                (activated ??= parts.ToArray())[index] = part with { Activation = active, Children = children };
        }
        return activated ?? parts;
    }
    public long SessionGeneration { get; }
    public long DisplayGeneration => console.DisplayGeneration;
    public long DisplayStructureGeneration => console.DisplayStructureGeneration;
    public long ScriptOutputGeneration => console.ScriptOutputGeneration;
    public long CurrentDisplayLineId => console.CurrentDisplayLineId;
    public int ClientWidth => console.ClientWidth;
    public int ClientHeight => console.ClientHeight;
    public int DisplayLineHeight => Config.LineHeight;
    public int DrawingPositionShift => Config.DrawingParam_ShapePositionShift;
    public Func<string, string, int, double>? TextWidthMeasurer
    {
        get => console.TextWidthMeasurer;
        set => console.TextWidthMeasurer = value;
    }
    public string TooltipForeground => console.TooltipForeground;
    public string TooltipBackground => console.TooltipBackground;
    public string BackgroundColor => console.BackgroundColor;
    public int TooltipDelayMilliseconds => console.TooltipDelayMilliseconds;
    public int TooltipDurationMilliseconds => console.TooltipDurationMilliseconds;
    public int AnimationIntervalMilliseconds => console.AnimationIntervalMilliseconds;
    public long LineCount => console.LineCount;
    public BrowserInputPrompt? PendingInput => Status == BrowserRuntimeStatus.WaitingForInput ? Map(console.PendingInput) : null;
    public SaveMutation? PendingPersistence
    {
        get
        {
            var pending = process.GetPendingGlobalPersistence();
            return pending is null ? null : new(
                pending.OperationId,
                pending.LogicalFilename,
                pending.Kind == Process.WebSaveMutationKind.Put ? SaveMutationKind.Put : SaveMutationKind.Delete,
                pending.Kind == Process.WebSaveMutationKind.Put ? pending.Bytes : null);
        }
    }
    public int PendingPersistenceCount => process.PendingGlobalPersistenceCount;
    public IReadOnlyList<BrowserPersistenceDiagnostic> PersistenceDiagnostics => persistenceDiagnostics;
    public IReadOnlyCollection<BrowserPersistenceAckDiagnostic> PersistenceAckTimeline => persistenceAckTimeline;
    public string? PersistenceError => console.PersistenceError;
    public BrowserRuntimeStatus Status => console.Status;
    public bool IsComplete => Status == BrowserRuntimeStatus.Succeeded;
    public bool IsFailed => Status == BrowserRuntimeStatus.Failed;
    public int DoScriptCallCount { get; private set; }
    public long DoScriptElapsedMilliseconds => (long)TimeSpan.FromTicks(doScriptElapsedTicks).TotalMilliseconds;
    public long CurrentManagedMemoryBytes => GC.GetTotalMemory(forceFullCollection: false);
    public int OutputLength => output.Length;
    public long DisplayProjectionCount { get; private set; }
    public BrowserDisplayPerformanceSnapshot DisplayPerformance
    {
        get
        {
            BrowserDisplayPerformanceSnapshot publish = console.DisplayPerformance;
            return publish with
            {
                ActivationProjectionCalls = activationProjectionCalls,
                ActivationProjectionElapsedTicks = activationProjectionElapsedTicks,
                ActivationProjectionSourceLines = activationProjectionSourceLines,
                ActivationProjectionParts = activationProjectionParts
            };
        }
    }
    public void EnableDisplayPerformanceMetrics()
    {
        displayPerformanceMetricsEnabled = true;
        console.EnableDisplayPerformanceMetrics();
    }
    public static string RunErbExecutionProfilerSelfCheck()
    {
#if ERB_EXECUTION_PROFILE
        return Process.RunErbExecutionProfilerSelfCheck();
#else
        return "[]";
#endif
    }
    public void BeginErbExecutionProfile(string mode, long macroStartedTimestamp)
    {
#if ERB_EXECUTION_PROFILE
        erbProfileRunScriptCallsAtStart = DoScriptCallCount;
        erbProfileRunScriptTicksAtStart = doScriptElapsedTicks;
        process.BeginErbExecutionProfile(mode, macroStartedTimestamp);
#endif
    }
    public string FinishErbExecutionProfile()
    {
#if ERB_EXECUTION_PROFILE
        return process.FinishErbExecutionProfile(
            DoScriptCallCount - erbProfileRunScriptCallsAtStart,
            doScriptElapsedTicks - erbProfileRunScriptTicksAtStart);
#else
        return "{}";
#endif
    }
    public string CurrentErbPosition => process.getCurrentLine?.Position is { } position ? $"{position.Filename}:{position.LineNo}" : string.Empty;
    public BrowserRuntimeFailureDiagnostic FailureDiagnostic => new(
        CurrentErbPosition,
        process.CurrentFunctionName,
        process.CurrentLabelName,
        process.LastRuntimeExceptionType,
        process.LastRuntimeExceptionMessage,
        process.RuntimeCallStack,
        OutputTail(32768),
        PendingInput,
        Status);
    public string OutputTail(int maximumCharacters)
    {
        if (maximumCharacters <= 0) return string.Empty;
        int start = Math.Max(0, output.Length - maximumCharacters);
        return output.ToString(start, output.Length - start);
    }
    public BrowserMessageSkipState MessageSkip => messageSkip with { ElapsedMilliseconds = MessageSkipElapsedMilliseconds() };
    public BrowserInfiniteLoopContinuation? InfiniteLoopContinuation => infiniteLoopContinuation;
    public BrowserLongRunningPrompt? LongRunningPrompt => longRunningPrompt;
    public bool RefreshAnimations(int firstLine = 0, int? endLine = null) => console.RefreshAnimations(firstLine, endLine);
    public string BootstrapLog { get; private set; } = string.Empty;
    public long BootstrapElapsedMilliseconds { get; private set; }
    public long ConfigLoadMilliseconds { get; private set; }
    public long PreloadMilliseconds { get; private set; }
    public long ProcessInitializeMilliseconds { get; private set; }
    public ProcessInitializeProfile? ProcessInitializeProfile { get; private set; }
    public IReadOnlyList<BrowserCodecValidationTiming> CodecValidationProfile => codecValidationProfile;
    public long ManagedMemoryBytes { get; private set; }
    public IReadOnlyDictionary<string, string> EffectiveConfiguration { get; private set; } = new Dictionary<string, string>();
    public BootstrapResourceSummary Resources { get; private set; } = new(0, 0, 0, 0);
    public BootstrapScriptSummary Scripts { get; private set; } = new(0, 0, 0, 0, 0, 0, "", 0, 0, 0);

    public static async Task<BrowserRuntimeSession> StartAsync(string fixtureRoot)
    {
        var session = await StartCoreAsync(fixtureRoot, loadConfig: false);
        session.RunScript();
        return session;
    }

    public static async Task<BrowserRuntimeSession> StartPersistentAsync(string fixtureRoot, byte[]? initialGlobalBytes)
    {
        var session = await StartCoreAsync(fixtureRoot, loadConfig: true, enableGlobalPersistence: true, initialGlobalBytes);
        session.RunScript();
        return session;
    }

    public static async Task<BrowserRuntimeSession> StartSavePersistentAsync(string fixtureRoot, IReadOnlyDictionary<string, byte[]>? initialFiles)
    {
        var session = await StartCoreAsync(fixtureRoot, loadConfig: true, enableGlobalPersistence: true, initialSaveFiles: initialFiles);
        session.RunScript();
        return session;
    }

    public static async Task<BrowserRuntimeSession> StartSavePersistentBootstrapAsync(string fixtureRoot, IReadOnlyDictionary<string, byte[]>? initialFiles, bool captureProcessInitializeProfile = false)
    {
        var session = await StartCoreAsync(fixtureRoot, loadConfig: true, enableGlobalPersistence: true, initialSaveFiles: initialFiles, enableCheckData: true, captureProcessInitializeProfile: captureProcessInitializeProfile);
        session.console.MarkBootstrapReady();
        return session;
    }

    public static async Task<BrowserRuntimeSession> StartPersistentBootstrapAsync(string fixtureRoot, byte[]? initialGlobalBytes)
    {
        var session = await StartCoreAsync(fixtureRoot, loadConfig: true, enableGlobalPersistence: true, initialGlobalBytes);
        session.console.MarkBootstrapReady();
        return session;
    }

    public static async Task<BrowserRuntimeSession> StartTitleBootstrapAsync(string fixtureRoot, byte[]? initialGlobalBytes, bool captureProcessInitializeProfile = false)
    {
        var session = await StartCoreAsync(fixtureRoot, loadConfig: true, enableGlobalPersistence: true, initialGlobalBytes, enableCheckData: true, captureProcessInitializeProfile: captureProcessInitializeProfile);
        session.console.MarkBootstrapReady();
        return session;
    }

    public static async Task<BrowserRuntimeSession> StartBootstrapAsync(string fixtureRoot, bool loadConfig = true)
    {
        var session = await StartCoreAsync(fixtureRoot, loadConfig);
        session.console.MarkBootstrapReady();
        return session;
    }

    public void EnableDiagnosticInfiniteLoopContinuation(Func<long>? clock = null)
    {
        if (longRunningPrompt is not null)
            throw new InvalidOperationException("通常対話と診断継続は同時に有効化できません");
        infiniteLoopContinuation = new(clock);
        process.SetWebInfiniteLoopPrompt(info => infiniteLoopContinuation.ShouldAbort(info.Title, $"{info.File}:{info.Line}"));
    }

    public void EnableInteractiveInfiniteLoopPrompt(Func<BrowserLongRunningNotice, BrowserLongRunningDecision> confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        if (infiniteLoopContinuation is not null)
            throw new InvalidOperationException("診断継続と通常対話は同時に有効化できません");
        longRunningPrompt = new(SessionGeneration, confirm);
        process.SetWebInfiniteLoopPrompt(longRunningPrompt.ShouldAbort);
    }

    static async Task<BrowserRuntimeSession> StartCoreAsync(string fixtureRoot, bool loadConfig, bool enableGlobalPersistence = false, byte[]? initialGlobalBytes = null, bool enableCheckData = false, IReadOnlyDictionary<string, byte[]>? initialSaveFiles = null, bool captureProcessInitializeProfile = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureRoot);
        fixtureRoot = Path.GetFullPath(fixtureRoot);
        string csv = ResolveDirectory(fixtureRoot, "CSV", "csv");
        string erb = ResolveDirectory(fixtureRoot, "ERB", "erb");
        if (CompatiblePath.ResolveExistingFile(Path.Combine(csv, "GAMEBASE.CSV")) is null)
            throw new FileNotFoundException("P1A fixtureにCSV/GAMEBASE.CSVがありません");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        // タイトル置換では同じprocess上に新しいRuntimeを開始する。次の実ゲームbootstrapより前に旧process graphを解放する。
        ReleaseBootstrapResources();
        Program.ExeDir = WithSeparator(fixtureRoot);
        Program.CsvDir = WithSeparator(csv);
        Program.ErbDir = WithSeparator(erb);
        Program.DatDir = WithSeparator(Path.Combine(fixtureRoot, "DAT"));
        Program.DebugDir = WithSeparator(Path.Combine(fixtureRoot, "DEBUG"));
        Program.ContentDir = WithSeparator(Path.Combine(fixtureRoot, "resources"));
        string savDirectory = Path.Combine(fixtureRoot, "sav");
        Directory.CreateDirectory(savDirectory);
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var phaseStopwatch = System.Diagnostics.Stopwatch.StartNew();
        ParserMediator.BeginBootstrapDiagnostics();
        if (loadConfig)
        {
            ConfigData.Instance.LoadConfig();
            JSONConfig.Load();
        }
        else
        {
            Config.SetConfig(ConfigData.Instance);
            JSONConfig.Game = new JSONGameConfigData();
            JSONConfig.User = new JSONUserConfigData();
            JSONConfig.SetSamplingOptions();
        }
        if (enableGlobalPersistence)
        {
            string effectiveSavDirectory = Path.GetFullPath(Config.SavDir);
            if (!effectiveSavDirectory.StartsWith(WithSeparator(fixtureRoot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("P1C1の保存先がrun root外です");
            Directory.CreateDirectory(effectiveSavDirectory);
            if (initialGlobalBytes is not null)
            {
                ValidateGlobalFileBounds(initialGlobalBytes);
                File.WriteAllBytes(Path.Combine(effectiveSavDirectory, "global.sav"), initialGlobalBytes);
            }
            if (initialSaveFiles is not null)
            {
                if (initialGlobalBytes is not null)
                    throw new InvalidOperationException("GLOBAL初期値と名前付き保存初期値は同時に指定できません");
                if (initialSaveFiles.Sum(pair => (long)pair.Value.Length) > 512L * 1024 * 1024)
                    throw new InvalidDataException("復元する保存データの総量が上限を超えています");
                foreach ((string logicalFilename, byte[] bytes) in initialSaveFiles)
                {
                    string normalized = NormalizeSaveFilename(logicalFilename);
                    if (bytes.Length is <= 0 or > 256 * 1024 * 1024)
                        throw new InvalidDataException($"保存データのサイズが不正です: {normalized}");
                    if (normalized == "global.sav")
                        ValidateGlobalFileBounds(bytes);
                    File.WriteAllBytes(Path.Combine(effectiveSavDirectory, normalized), bytes);
                }
            }
        }
        var session = new BrowserRuntimeSession();
        session.captureCodecValidationProfile = captureProcessInitializeProfile;
        session.ConfigLoadMilliseconds = phaseStopwatch.ElapsedMilliseconds;
        phaseStopwatch.Restart();
        Preload.Clear();
        await Preload.Load(Program.ErbDir);
        await Preload.Load(Program.CsvDir);
        session.PreloadMilliseconds = phaseStopwatch.ElapsedMilliseconds;

        phaseStopwatch.Restart();
        using var stream = new MemoryStream();
        using var log = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        bool initialized = await session.process.Initialize(log, captureProcessInitializeProfile);
        await log.FlushAsync();
        session.BootstrapLog = Encoding.UTF8.GetString(stream.ToArray());
        if (!initialized)
            throw new InvalidOperationException("実Runtimeの初期化に失敗しました stage=Process.Initialize\n" + session.BootstrapLog + session.Output);
        if (enableGlobalPersistence)
            session.process.EnableSavePersistence(Config.SavDir);
        if (enableCheckData)
            session.process.EnableCheckData(Path.GetFullPath(Config.SavDir));
        session.ProcessInitializeMilliseconds = phaseStopwatch.ElapsedMilliseconds;
        session.ProcessInitializeProfile = session.process.InitializeProfile;
        session.BootstrapElapsedMilliseconds = totalStopwatch.ElapsedMilliseconds;
        session.ManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false);
        session.Resources = new(AppContents.ParentImageCount, AppContents.SpriteCount, AppContents.AnimationCount, AppContents.AnimationFrameCount, AppContents.BootstrapIdentitySha256, AppContents.ParentIdentitySha256, AppContents.SpriteIdentitySha256, AppContents.SpriteNameSha256, AppContents.NonZeroSpriteOffsetCount, AppContents.SpriteWidthTotal, AppContents.SpriteHeightTotal, AppContents.SpriteOffsetXTotal, AppContents.SpriteOffsetYTotal, AppContents.SpriteSourceXTotal, AppContents.SpriteSourceYTotal, AppContents.AnimationSourceXTotal, AppContents.AnimationSourceYTotal, AppContents.AnimationOffsetXTotal, AppContents.AnimationOffsetYTotal, AppContents.AnimationDelayTotal);
        BootstrapRuntimeSummary scripts = session.process.GetBootstrapRuntimeSummary();
        session.Scripts = new(scripts.CsvFileCount, scripts.ErhFileCount, scripts.ErbFileCount, scripts.CharacterTemplateCount, scripts.VariableTokenCount, scripts.LabelCount, scripts.LabelIdentitySha256, scripts.LazyErbFileCount, scripts.LazyErbFallbackFileCount, scripts.DeferredEagerCount, scripts.ConfigurationIdentitySha256, scripts.VariableRegistryScope, scripts.VariableSchemaCount, scripts.VariableSchemaSha256, scripts.VariableSchemaRows, scripts.WarningCount, scripts.WarningKindCount, scripts.WarningIdentitySha256, scripts.WarningKindCounts, scripts.WarningExamples);
        session.EffectiveConfiguration = Process.GetBootstrapEffectiveConfiguration();
        return session;
    }


    void RunScript()
    {
#if ERB_EXECUTION_PROFILE
        GameplayTrace?.Invoke("script-start");
#endif
        DoScriptCallCount++;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { process.DoScript(); }
        finally
        {
            console.CompleteInputGeneration(process.getCurrentLine);
            doScriptElapsedTicks += System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks;
#if ERB_EXECUTION_PROFILE
            GameplayTrace?.Invoke("script-complete");
            GameplayTrace?.Invoke("console-mutation-complete");
            GameplayTrace?.Invoke("next-request");
#endif
        }
    }

    public void StartTitle()
    {
        if (Status != BrowserRuntimeStatus.BootstrapReady)
            throw new InvalidOperationException("タイトル開始はBootstrapReadyでだけ実行できます");
        console.StartRunning();
        RunScript();
        Preload.Clear();
    }

    public void ReturnToTitle()
    {
        if (Status != BrowserRuntimeStatus.WaitingForInput || PendingPersistenceCount != 0)
            throw new InvalidOperationException("入力待ちと保存完了後にだけタイトルへ戻れます");
        StopMessageSkip("title-return");
        console.Resume();
        console.ClearKeyStates();
        console.ClearText();
        console.ResetStyle();
        console.SetTimeOut(false);
        output.Clear();
        activatedDisplayLines = null;
        AppContents.UnloadGraphicList();
        // AOTではProcessを作り直すと前の実行領域を残したまま再初期化し得るため、同じProcessをタイトル開始位置へ戻す。
        process.BeginTitle();
        RunScript();
    }

    public void SetViewport(int width, int height) => console.SetViewport(width, height);
    public void EnableR3R3RawHtmlCapture() => console.EnableR3R3RawHtmlCapture();
    public int IslandLineCount => console.IslandLineCount;
    public void SetMousePosition(int x, int y) => console.SetMousePosition(x, y);
    public void SetKeyState(int keycode, bool down) => console.SetKeyState(keycode, down);
    public void ClearKeyStates() => console.ClearKeyStates();

    public bool SubmitDisplay(BrowserDisplayPart part)
        => SubmitDisplay(part, 0, ClientHeight, 1048576);

    public bool SubmitDisplay(BrowserDisplayPart part, int x, int y, int mouseCode)
    {
        if (part.Input is null || part.Activation is not { } activation)
            return false;
        BrowserInputPrompt? prompt = PendingInput;
        if (prompt is null) return false;
        if (activation.SessionGeneration != SessionGeneration || activation.RequestId != prompt.RequestId
            || activation.DisplayGeneration != console.DisplayStructureGeneration) return false;
        console.SetMousePosition(x, y);
        return prompt.Kind == BrowserInputKind.MouseKey
            ? SubmitPrimitive(new(
                activation.RequestId,
                BrowserPrimitiveInputKind.Click,
                part.Input,
                mouseCode,
                X: x,
                Y: y,
                ButtonIsInteger: part.IsInteger,
                SessionGeneration: activation.SessionGeneration,
                DisplayGeneration: console.DisplayGeneration))
            : Submit(new(
                activation.RequestId,
                part.Input,
                BrowserInputSource.DisplayButton,
                activation.SessionGeneration,
                console.DisplayGeneration));
    }

    public bool Submit(InputEnvelope envelope) => SubmitCore(envelope, startMessageSkip: false);

    public bool SubmitWithMessageSkip(InputEnvelope envelope, bool holdAtTimedPrimitive = false) => SubmitCore(envelope, startMessageSkip: true, holdAtTimedPrimitive);

    // Macro input: Native raw primitive input does not synthesize RESULT mouse/key values.
    public bool SubmitMacro(InputEnvelope envelope, bool messageSkip)
    {
        if (Status != BrowserRuntimeStatus.WaitingForInput || console.PendingInput is not { } request
            || request.ID != envelope.RequestId || request.StopMesskip || request.InputType == InputType.Void)
            return false;
        if (request.InputType != InputType.PrimitiveMouseKey)
            return SubmitCore(envelope, messageSkip, holdAtTimedPrimitive: true, useTimedDefault: false);
        if ((envelope.SessionGeneration != 0 && envelope.SessionGeneration != SessionGeneration)
            || (envelope.DisplayGeneration != 0 && envelope.DisplayGeneration != console.DisplayGeneration)
            || !gate.TryConsume(request.ID, envelope, out _)) return false;
        if (messageSkip) StartMessageSkip(request.ID);
        else StopMessageSkip("macro-value-without-skip");
        console.PrintInputEcho(envelope.RawValue ?? string.Empty);
        console.SetTimeOut(false);
        console.Resume();
        RunScript();
        StopMessageSkipAtBoundary(holdAtTimedPrimitive: true);
        return true;
    }

    bool SubmitCore(InputEnvelope envelope, bool startMessageSkip, bool holdAtTimedPrimitive = false, bool useTimedDefault = true)
    {
        if (Status != BrowserRuntimeStatus.WaitingForInput)
            return false;
        InputRequest? request = console.PendingInput;
        if (request is null || envelope.RequestId != request.ID)
            return false;
        if ((envelope.SessionGeneration != 0 && envelope.SessionGeneration != SessionGeneration)
            || (envelope.DisplayGeneration != 0 && envelope.DisplayGeneration != console.DisplayGeneration))
            return false;
        if (request.InputType == InputType.Void || (startMessageSkip && (request.InputType == InputType.PrimitiveMouseKey || request.StopMesskip)))
            return false;

        string raw = envelope.RawValue ?? string.Empty;
        bool usesDefault = raw.Length == 0 && request.HasDefValue && (useTimedDefault || request.Timelimit <= 0);
        if (usesDefault)
            raw = request.InputType == InputType.IntValue ? request.DefIntValue.ToString(CultureInfo.InvariantCulture) : request.DefStrValue;
        else if (request.OneInput && raw.Length > 1
            && (!Config.AllowLongInputByMouse || envelope.Source != BrowserInputSource.DisplayButton))
            raw = raw[..1];

        long integerValue = 0;
        if (request.InputType == InputType.IntValue
            && !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue))
            return false;
        if (!gate.TryConsume(request.ID, envelope, out _))
            return false;
#if ERB_EXECUTION_PROFILE
        GameplayTrace?.Invoke("gate-accepted");
#endif

        if (startMessageSkip)
            StartMessageSkip(request.ID);
        else if (messageSkip.Active)
            StopMessageSkip("replaced-by-manual-input");

        switch (request.InputType)
        {
            case InputType.IntValue:
                if (request.IsSystemInput)
                    process.InputSystemInteger(integerValue);
                else
                    process.InputInteger(integerValue);
                break;
            case InputType.StrValue:
                process.InputString(raw);
                break;
            case InputType.EnterKey:
            case InputType.AnyKey:
                break;
            default:
                throw new UnsupportedRuntimeFeatureException($"input type {request.InputType}");
        }

        console.PrintInputEcho(raw);
        if (request.TimedInputName == "TWAIT")
            console.SetTimeOut(false);
        console.Resume();
        RunScript();
        StopMessageSkipAtBoundary(holdAtTimedPrimitive);
        return true;
    }

    public bool ContinueMessageSkip(bool holdAtTimedPrimitive = false, bool allowForcedTwait = true)
    {
        if (!messageSkip.Active)
            return false;
        if (messageSkip.Continuations >= 2000 || MessageSkipElapsedMilliseconds() >= 30000)
        {
            StopMessageSkip("limit");
            return false;
        }
        if (Status == BrowserRuntimeStatus.Persisting)
            return false;
        InputRequest? request = console.PendingInput;
        if (Status != BrowserRuntimeStatus.WaitingForInput || request is null)
        {
            StopMessageSkip(Status == BrowserRuntimeStatus.Succeeded ? "complete" : Status == BrowserRuntimeStatus.Failed ? "failed" : "not-waiting");
            return false;
        }
        if (!allowForcedTwait && request.InputType == InputType.Void) return false;
        if (holdAtTimedPrimitive && request.InputType == InputType.PrimitiveMouseKey && request.Timelimit > 0)
            return false;
        if (request.NeedValue || request.StopMesskip)
        {
            StopMessageSkip(request.StopMesskip ? "stop-messkip" : request.InputType == InputType.PrimitiveMouseKey ? "primitive-input" : "value-input");
            return false;
        }
        if (request.InputType is not (InputType.EnterKey or InputType.AnyKey or InputType.Void)
            || !gate.TryConsume(request.ID, new InputEnvelope(request.ID, string.Empty), out _))
        {
            StopMessageSkip("request-rejected");
            return false;
        }

        messageSkip = messageSkip with { Continuations = messageSkip.Continuations + 1 };
        console.MesSkip = true;
        console.SetTimeOut(false);
        console.Resume();
        RunScript();
        StopMessageSkipAtBoundary();
        return true;
    }

    public bool StopMessageSkip(string reason = "user-stop")
    {
        if (!messageSkip.Active)
            return false;
        long elapsedMilliseconds = MessageSkipElapsedMilliseconds();
        messageSkipStartedTimestamp = 0;
        messageSkip = messageSkip with { Active = false, ElapsedMilliseconds = elapsedMilliseconds, StopReason = reason };
        console.MesSkip = false;
        return true;
    }

    void StartMessageSkip(long requestId)
    {
        messageSkipStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        messageSkip = new(true, ++nextMessageSkipOperationId, requestId, 0, 0, string.Empty);
        console.MesSkip = true;
    }

    long MessageSkipElapsedMilliseconds() => messageSkipStartedTimestamp == 0
        ? messageSkip.ElapsedMilliseconds
        : (long)System.Diagnostics.Stopwatch.GetElapsedTime(messageSkipStartedTimestamp).TotalMilliseconds;

    void StopMessageSkipAtBoundary(bool holdAtTimedPrimitive = false)
    {
        if (!messageSkip.Active || Status == BrowserRuntimeStatus.Persisting)
            return;
        InputRequest? request = console.PendingInput;
        if (holdAtTimedPrimitive && Status == BrowserRuntimeStatus.WaitingForInput
            && request is { InputType: InputType.PrimitiveMouseKey, Timelimit: > 0 })
        {
            console.MesSkip = false;
            return;
        }
        if (Status == BrowserRuntimeStatus.WaitingForInput && request is not null && !request.NeedValue && !request.StopMesskip)
            return;
        string reason = Status switch
        {
            BrowserRuntimeStatus.Succeeded => "complete",
            BrowserRuntimeStatus.Failed => "failed",
            BrowserRuntimeStatus.WaitingForInput when request?.StopMesskip == true => "stop-messkip",
            BrowserRuntimeStatus.WaitingForInput when request?.InputType == InputType.PrimitiveMouseKey => "primitive-input",
            BrowserRuntimeStatus.WaitingForInput => "value-input",
            _ => "not-waiting"
        };
        StopMessageSkip(reason);
    }

    public bool SubmitTimeout(long requestId, long sessionGeneration = 0, long displayGeneration = 0)
    {
        if (Status != BrowserRuntimeStatus.WaitingForInput)
            return false;
        InputRequest? request = console.PendingInput;
        if (request is null || request.ID != requestId || request.Timelimit <= 0
            || (request.TimedInputName == "TWAIT" && request.InputType is not (InputType.EnterKey or InputType.Void))
            || (request.TimedInputName is "TINPUT" or "TONEINPUT" && request.InputType != InputType.IntValue)
            || (request.TimedInputName is "TINPUTS" or "TONEINPUTS" && request.InputType != InputType.StrValue)
            || request.TimedInputName is not ("TWAIT" or "TINPUT" or "TINPUTS" or "TONEINPUT" or "TONEINPUTS"))
            return false;
        // A TWAIT timer belongs to the input request, so animation redraws must not invalidate it.
        if (sessionGeneration != 0 && sessionGeneration != SessionGeneration)
            return false;
        if (request.InputType is InputType.IntValue or InputType.StrValue)
        {
            console.SetTimeOut(true);
            return SubmitCore(new(requestId, string.Empty, BrowserInputSource.Keyboard, sessionGeneration), startMessageSkip: false);
        }
        if (!gate.TryConsume(request.ID, new InputEnvelope(requestId, string.Empty), out _))
            return false;
        console.SetTimeOut(true);
        console.Resume();
        RunScript();
        return true;
    }

    public bool SubmitPrimitive(PrimitiveInputEnvelope envelope)
    {
        if (Status != BrowserRuntimeStatus.WaitingForInput)
            return false;
        InputRequest? request = console.PendingInput;
        if (request is null || request.InputType != InputType.PrimitiveMouseKey || envelope.RequestId != request.ID)
            return false;
        // Timeout belongs to the request; animation redraws only invalidate pointer/key events.
        if ((envelope.SessionGeneration != 0 && envelope.SessionGeneration != SessionGeneration)
            || (envelope.Kind != BrowserPrimitiveInputKind.Timeout
                && envelope.DisplayGeneration != 0 && envelope.DisplayGeneration != console.DisplayGeneration))
            return false;
        if (!gate.TryConsume(request.ID, new InputEnvelope(envelope.RequestId, envelope.ButtonValue ?? string.Empty), out _))
            return false;

        if (envelope.Kind == BrowserPrimitiveInputKind.Click)
            console.SetMousePosition(envelope.X, envelope.Y);

        int type = envelope.Kind switch
        {
            BrowserPrimitiveInputKind.Timeout => 4,
            BrowserPrimitiveInputKind.Click => 1,
            BrowserPrimitiveInputKind.Key => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(envelope))
        };
        process.SetResultArray(0, 5);
        process.SetResultsArray(string.Empty, 5);
        if (!string.IsNullOrEmpty(envelope.ButtonValue))
        {
            if (envelope.ButtonIsInteger && long.TryParse(envelope.ButtonValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                process.SetResultArray(integer, 5);
            else
                process.SetResultsArray(envelope.ButtonValue, 5);
        }
        int result1 = envelope.Kind == BrowserPrimitiveInputKind.Timeout ? 0 : envelope.Code;
        int result2 = envelope.Kind switch
        {
            BrowserPrimitiveInputKind.Click => envelope.X,
            BrowserPrimitiveInputKind.Key => envelope.Result2,
            _ => 0
        };
        int result3 = envelope.Kind == BrowserPrimitiveInputKind.Click ? envelope.Y - ClientHeight : 0;
        int result4 = envelope.Kind == BrowserPrimitiveInputKind.Click ? envelope.ButtonMapValue : 0;
        process.InputResult5(type, result1, result2, result3, result4);
        console.SetTimeOut(envelope.Kind == BrowserPrimitiveInputKind.Timeout);
        console.Resume();
        RunScript();
        return true;
    }

    public bool AcknowledgePersistence(string operationId)
    {
        // IndexedDB transaction完了後のoperationIdだけを受け入れ、古いACKで同期スクリプトを再開させない。
        long sequence = ++persistenceAckSequence;
        if (Status != BrowserRuntimeStatus.Persisting || !process.AcknowledgeGlobalPersistence(operationId))
        {
            SaveMutation? head = PendingPersistence;
            if (persistenceAckTimeline.Count == 64) persistenceAckTimeline.Dequeue();
            persistenceAckTimeline.Enqueue(new(sequence, DateTimeOffset.UtcNow, operationId, Status,
                PendingPersistenceCount, head?.OperationId, head?.LogicalFilename, SessionGeneration,
                console.PendingInput?.ID, CurrentErbPosition, false));
            return false;
        }
        if (process.PendingGlobalPersistenceCount == 0 && console.IsRunning)
        {
            RunScript();
            StopMessageSkipAtBoundary();
        }
        return true;
    }

    public Task DrainPersistenceAsync(Func<SaveMutation, Task> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        return persistenceDrain is { IsCompleted: false } active ? active : persistenceDrain = DrainPersistenceCoreAsync(persist);
    }

    async Task DrainPersistenceCoreAsync(Func<SaveMutation, Task> persist)
    {
        while (PendingPersistence is { } request)
        {
            long commitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                await persist(request);
            }
            catch (Exception ex)
            {
                process.FailGlobalPersistence(ex.Message);
                StopMessageSkip("persistence-failed");
                throw;
            }
            long commitMilliseconds = (long)System.Diagnostics.Stopwatch.GetElapsedTime(commitStarted).TotalMilliseconds;
            long ackStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!AcknowledgePersistence(request.OperationId))
                throw new InvalidOperationException($"永続保存ackを受理できません: {request.OperationId}");
            persistenceDiagnostics.Add(new(request.OperationId, request.LogicalFilename, request.Kind,
                request.Bytes?.Length ?? 0, commitMilliseconds,
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(ackStarted).TotalMilliseconds));
        }
    }

    static string NormalizeSaveFilename(string logicalFilename)
    {
        if (string.Equals(logicalFilename, "global.sav", StringComparison.OrdinalIgnoreCase))
            return "global.sav";
        if (logicalFilename.Length > 8
            && logicalFilename.StartsWith("save", StringComparison.OrdinalIgnoreCase)
            && logicalFilename.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(logicalFilename.AsSpan(4, logicalFilename.Length - 8), NumberStyles.None, CultureInfo.InvariantCulture, out long index)
            && index >= 0)
            return $"save{index:00}.sav";
        throw new InvalidDataException($"通常保存ファイル名が不正です: {logicalFilename}");
    }

    public GlobalCodecDiagnosticResult RunGlobalCodecDiagnostic(bool resave)
    {
        if (Status != BrowserRuntimeStatus.BootstrapReady)
            throw new InvalidOperationException("GLOBAL codec診断はBootstrapReadyでだけ実行できます");
        string path = Path.Combine(Config.SavDir, "global.sav");
        long inputHashStarted = captureCodecValidationProfile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        bool fileExists = File.Exists(path);
        long inputBytes = captureCodecValidationProfile && fileExists ? new FileInfo(path).Length : 0;
        string inputSha256 = fileExists
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
            : string.Empty;
        long inputHashMilliseconds = captureCodecValidationProfile
            ? (long)System.Diagnostics.Stopwatch.GetElapsedTime(inputHashStarted).TotalMilliseconds : 0;
        long loadStarted = captureCodecValidationProfile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        bool loaded = process.VEvaluator.LoadGlobal();
        long loadMilliseconds = captureCodecValidationProfile
            ? (long)System.Diagnostics.Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds : 0;
        long summaryStarted = captureCodecValidationProfile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        GlobalValueSummary values = process.GetGlobalValueSummary();
        long summaryMilliseconds = captureCodecValidationProfile
            ? (long)System.Diagnostics.Stopwatch.GetElapsedTime(summaryStarted).TotalMilliseconds : 0;
        if (captureCodecValidationProfile)
            codecValidationProfile.Add(new("LoadGlobal", inputHashMilliseconds, loadMilliseconds, summaryMilliseconds, inputBytes, inputSha256, loaded));
        if (resave && loaded)
        {
            process.VEvaluator.SaveGlobal();
            console.BeginPersistence();
        }
        return new(loaded, values.SavedVariableCount, values.ValueSha256, values.ValueRows, inputSha256);
    }

    public SaveCodecDiagnosticResult RunSaveCodecDiagnostic(int slot)
    {
        if (Status != BrowserRuntimeStatus.BootstrapReady)
            throw new InvalidOperationException("通常sav codec診断はBootstrapReadyでだけ実行できます");
        string logicalFilename = $"save{slot:00}.sav";
        string path = Path.Combine(Config.SavDir, logicalFilename);
        long inputHashStarted = captureCodecValidationProfile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        bool fileExists = File.Exists(path);
        long inputBytes = captureCodecValidationProfile && fileExists ? new FileInfo(path).Length : 0;
        string inputSha256 = fileExists
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
            : string.Empty;
        long inputHashMilliseconds = captureCodecValidationProfile
            ? (long)System.Diagnostics.Stopwatch.GetElapsedTime(inputHashStarted).TotalMilliseconds : 0;
        long loadStarted = captureCodecValidationProfile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        bool loaded;
        try
        {
            loaded = process.VEvaluator.LoadFrom(slot);
        }
        catch
        {
            if (captureCodecValidationProfile)
            {
                long failedLoadMilliseconds = (long)System.Diagnostics.Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds;
                codecValidationProfile.Add(new("LoadFrom", inputHashMilliseconds, failedLoadMilliseconds, 0, inputBytes, inputSha256, false));
            }
            throw;
        }
        long loadMilliseconds = captureCodecValidationProfile
            ? (long)System.Diagnostics.Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds : 0;
        var data = process.VEvaluator.VariableData;
        long summaryStarted = captureCodecValidationProfile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        string stateSha256 = process.VEvaluator.GetBenchmarkStateHash();
        long summaryMilliseconds = captureCodecValidationProfile
            ? (long)System.Diagnostics.Stopwatch.GetElapsedTime(summaryStarted).TotalMilliseconds : 0;
        if (captureCodecValidationProfile)
            codecValidationProfile.Add(new("LoadFrom", inputHashMilliseconds, loadMilliseconds, summaryMilliseconds, inputBytes, inputSha256, loaded));
        return new(
            loaded,
            slot,
            data.CharacterList.Count,
            data.LastLoadVersion,
            data.LastLoadText,
            stateSha256,
            inputSha256);
    }

    public SaveStateDiagnosticResult RunSaveStateDiagnostic(int slot)
    {
        if (Status != BrowserRuntimeStatus.BootstrapReady)
            throw new InvalidOperationException("通常sav状態診断はBootstrapReadyでだけ実行できます");
        string path = Path.Combine(Config.SavDir, $"save{slot:00}.sav");
        string inputSha256 = File.Exists(path)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
            : string.Empty;
        bool loaded = process.VEvaluator.LoadFrom(slot);
        SavedStateValueSummary values = process.GetSavedStateValueSummary();
        var data = process.VEvaluator.VariableData;
        return new(
            loaded,
            slot,
            values.CharacterCount,
            data.LastLoadVersion,
            data.LastLoadText,
            values.SavedVariableCount,
            values.NonDefaultValueCount,
            values.StateSha256,
            values.ValueSha256,
            values.ValueRows,
            inputSha256);
    }

    static BrowserInputPrompt? Map(InputRequest? request) => request is null ? null : new(
        request.ID,
        request.InputType switch
        {
            InputType.IntValue => BrowserInputKind.Integer,
            InputType.StrValue => BrowserInputKind.String,
            InputType.EnterKey => BrowserInputKind.Enter,
            InputType.AnyKey => BrowserInputKind.AnyKey,
            InputType.PrimitiveMouseKey => BrowserInputKind.MouseKey,
            InputType.Void => BrowserInputKind.TimedVoid,
            _ => throw new UnsupportedRuntimeFeatureException($"input type {request.InputType}")
        },
        request.OneInput,
        request.Timelimit,
        request.TimedInputName,
        request.StopMesskip,
        request.DisplayTime,
        request.TimeUpMes);

    static void ValidateGlobalFileBounds(ReadOnlySpan<byte> bytes)
    {
        const ulong binaryHeader = 0x0A1A0A0D41524589UL;
        if (bytes.Length < 16 || BinaryPrimitives.ReadUInt64LittleEndian(bytes) != binaryHeader)
            return;
        uint dataCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        if (dataCount > (uint)((bytes.Length - 16) / 4))
            throw new InvalidDataException("global.sav binary headerのdata countがファイル境界を越えています");
    }

    static string ResolveDirectory(string root, string preferred, string fallback)
    {
        string path = Path.Combine(root, preferred);
        return Directory.Exists(path) ? path : Path.Combine(root, fallback);
    }

    static string WithSeparator(string path) => Path.GetFullPath(path) + Path.DirectorySeparatorChar;
}

