using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Loader;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Game.Image;
using Runtime.SQL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MinorShift.Emuera.UI.Framework;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace MinorShift.Emuera.GameProc;

#if WEB_RUNTIME
internal sealed record WebInfiniteLoopPromptInfo(
    string Title,
    string Message,
    string File,
    int Line,
    int ExecutedLines,
    long ElapsedMilliseconds);
#endif

internal sealed record BootstrapRuntimeSummary(
    int CsvFileCount,
    int ErhFileCount,
    int ErbFileCount,
    int CharacterTemplateCount,
    int VariableTokenCount,
    int LabelCount,
    string LabelIdentitySha256,
    int LazyErbFileCount,
    int LazyErbFallbackFileCount,
    int DeferredEagerCount,
    string ConfigurationIdentitySha256,
    string VariableRegistryScope,
    int VariableSchemaCount,
    string VariableSchemaSha256,
    string VariableSchemaRows,
    int WarningCount,
    int WarningKindCount,
    string WarningIdentitySha256,
    string WarningKindCounts,
    string WarningExamples);

internal sealed record GlobalValueSummary(int SavedVariableCount, string ValueSha256, string ValueRows);
internal sealed record SavedStateValueSummary(
    int CharacterCount,
    int SavedVariableCount,
    int NonDefaultValueCount,
    string StateSha256,
    string ValueSha256,
    string ValueRows);


internal sealed partial class Process(EmueraConsole view)
{
#if WEB_RUNTIME
    internal enum WebSaveMutationKind { Put, Delete }
    internal sealed record WebSaveMutation(string OperationId, string LogicalFilename, WebSaveMutationKind Kind, byte[] Bytes);

    readonly Queue<WebSaveMutation> persistenceQueue = new();
    readonly string persistenceSessionId = Guid.NewGuid().ToString("N");
    long nextGlobalPersistenceOperation;
    bool globalPersistenceEnabled;
    string persistenceRoot;
    string characterPersistenceRoot;
    Exception deferredPersistenceException;
    LogicalLine deferredPersistenceErrorLine;
    bool deferredPersistenceSystemProc;
    Func<WebInfiniteLoopPromptInfo, bool> webInfiniteLoopPrompt;

    internal void SetWebInfiniteLoopPrompt(Func<WebInfiniteLoopPromptInfo, bool> prompt) => webInfiniteLoopPrompt = prompt;
#endif

    Exception lastRuntimeException;

    public LogicalLine getCurrentLine { get { return state.CurrentLine; } }
    // Diagnostic-only snapshot for browser hosts. Script execution never reads it.
    public string CurrentFunctionName => state.Scope ?? string.Empty;
    public string CurrentLabelName => getCurrentLine?.ParentLabelLine?.LabelName ?? string.Empty;
    public string LastRuntimeExceptionType => lastRuntimeException?.GetType().FullName ?? string.Empty;
    public string LastRuntimeExceptionMessage => lastRuntimeException?.Message ?? string.Empty;
    public string LastRuntimeExceptionStack => lastRuntimeException?.StackTrace ?? string.Empty;
    public string RuntimeCallStack
    {
        get
        {
            var lines = new List<string>();
            for (int depth = 0; depth < 256; depth++)
            {
                LogicalLine line = state.GetReturnAddressSequensial(depth);
                if (line is null) break;
                if (line.Position is not null)
                    lines.Add($"{line.ParentLabelLine.LabelName} ({line.Position.Value.Filename}:{line.Position.Value.LineNo})");
            }
            return string.Join('\n', lines);
        }
    }

    /// <summary>
    /// @~~と$~~を集めたもの。CALL命令などで使う
    /// 実行順序はLogicalLine自身が保持する。
    /// </summary>
    LabelDictionary labelDic;
    public LabelDictionary LabelDictionary { get { return labelDic; } }

    /// <summary>
    /// 変数全部。スクリプト中で必要になる変数は（ユーザーが直接触れないものも含め）この中にいれる
    /// </summary>
    private VariableEvaluator vEvaluator;
    public VariableEvaluator VEvaluator { get { return vEvaluator; } }
    public int ExecutedLineCount => state?.lineCount ?? 0;
    internal GlobalValueSummary GetGlobalValueSummary()
    {
        IReadOnlyDictionary<string, VariableToken> variables = idDic?.GetBootstrapVariableTokens()
            ?? throw new InvalidOperationException("GLOBAL values require an initialized IdentifierDictionary.");
        string[] rows = variables
            .Where(pair => pair.Value.IsGlobal && pair.Value.IsSavedata && !pair.Value.IsCharacterData)
            .Select(pair =>
            {
                Array values = (Array)pair.Value.GetArray();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (object value in values)
                {
                    string encoded = pair.Value.IsInteger
                        ? $"i:{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}"
                        : $"s{(value as string)?.Length ?? -1}:{value}";
                    hash.AppendData(Encoding.UTF8.GetBytes(encoded));
                    hash.AppendData([0]);
                }
                string lengths = string.Join(',', Enumerable.Range(0, values.Rank).Select(values.GetLength));
                return $"{pair.Key}\t{(pair.Value.IsInteger ? "Int64" : "String")}\t{lengths}\t{Convert.ToHexString(hash.GetHashAndReset())}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string valueRows = string.Join('\n', rows);
        return new(rows.Length, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valueRows))), valueRows);
    }

    internal SavedStateValueSummary GetSavedStateValueSummary()
    {
        VariableData data = vEvaluator.VariableData;
        var rows = new List<string>();
        int valueCount = 0;
        IReadOnlyDictionary<string, VariableToken> variables = idDic?.GetBootstrapVariableTokens()
            ?? throw new InvalidOperationException("Saved-state values require an initialized IdentifierDictionary.");
        foreach ((string name, VariableToken token) in variables
                     .Where(pair => pair.Value.IsSavedata)
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(name));
            string shape = token.Dimension == 0
                ? string.Empty
                : string.Join(',', Enumerable.Range(0, token.Dimension).Select(token.GetLength));
            rows.Add($"VAR\t{encodedName}\t{(token.IsInteger ? "Int64" : "String")}\t{(token.IsCharacterData ? "Character" : "Global")}\t{shape}");
            int characterCount = token.IsCharacterData ? data.CharacterList.Count : 1;
            for (int character = 0; character < characterCount; character++)
            {
                if (token.Dimension == 0)
                {
                    object value = token.IsInteger
                        ? token.GetIntValue(exm, token.IsCharacterData ? [character] : [])
                        : token.GetStrValue(exm, token.IsCharacterData ? [character] : []);
                    AddValue(value, character, -1);
                    continue;
                }
                Array values = (Array)(token.IsCharacterData ? token.GetArrayChara(character) : token.GetArray());
                int index = 0;
                foreach (object value in values)
                    AddValue(value, character, index++);
            }

            void AddValue(object value, int character, int index)
            {
                if (token.IsInteger)
                {
                    long number = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (number == 0) return;
                    rows.Add($"VAL\t{encodedName}\t{(token.IsCharacterData ? character : -1)}\t{index}\tI\t{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                }
                else
                {
                    string text = value as string ?? string.Empty;
                    if (text.Length == 0) return;
                    rows.Add($"VAL\t{encodedName}\t{(token.IsCharacterData ? character : -1)}\t{index}\tS\t{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}");
                }
                valueCount++;
            }
        }
        string valueRows = string.Join('\n', rows);
        return new(
            data.CharacterList.Count,
            rows.Count(row => row.StartsWith("VAR\t", StringComparison.Ordinal)),
            valueCount,
            vEvaluator.GetBenchmarkStateHash(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valueRows))),
            valueRows);
    }
#if WEB_RUNTIME
    internal void EnableGlobalPersistence() => EnableSavePersistence(Config.SavDir);
    internal void EnableSavePersistence(string preparedSaveRoot)
    {
        globalPersistenceEnabled = true;
        persistenceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(preparedSaveRoot));
        characterPersistenceRoot = Path.GetFullPath(Program.DatDir);
        vEvaluator.EnableCheckData(persistenceRoot);
    }
    internal void EnableCheckData(string preparedSaveRoot) => vEvaluator.EnableCheckData(preparedSaveRoot);

    internal void RequireGlobalPersistence(string feature)
    {
        if (!globalPersistenceEnabled)
            throw new PlatformNotSupportedException($"P1A未対応: {feature}");
    }

    internal void CaptureGlobalPersistence(string path)
    {
        RequireGlobalPersistence("SAVEGLOBAL");
        string expected = Path.GetFullPath(Path.Combine(Config.SavDir, "global.sav"));
        if (!string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("P1C1の永続保存対象はglobal.savだけです");
        CaptureSavePut(expected, "SAVEGLOBAL");
    }

    internal void CaptureSavePut(string path, string feature)
    {
        string logicalFilename = RequireSavePersistencePath(feature, path);
        byte[] snapshot = File.ReadAllBytes(path);
        persistenceQueue.Enqueue(new($"{persistenceSessionId}-{++nextGlobalPersistenceOperation:D8}", logicalFilename, WebSaveMutationKind.Put, snapshot));
    }

    internal void CaptureSaveDelete(string path, string feature)
    {
        string logicalFilename = RequireSavePersistencePath(feature, path);
        persistenceQueue.Enqueue(new($"{persistenceSessionId}-{++nextGlobalPersistenceOperation:D8}", logicalFilename, WebSaveMutationKind.Delete, []));
    }

    internal string RequireCharacterPersistencePath(string feature, string name)
    {
        RequireGlobalPersistence(feature);
        return MinorShift.Emuera.Web.Runtime.BrowserCharacterFiles.PathForName(characterPersistenceRoot, name);
    }

    internal void CaptureCharacterPut(string path, byte[] bytes)
    {
        RequireGlobalPersistence("SAVECHARA");
        string filename = MinorShift.Emuera.Web.Runtime.BrowserCharacterFiles.NormalizeFilename(Path.GetFileName(path));
        persistenceQueue.Enqueue(new($"{persistenceSessionId}-{++nextGlobalPersistenceOperation:D8}",
            "dat/" + filename, WebSaveMutationKind.Put, bytes));
    }

    internal string RequireSavePersistencePath(string feature, string path)
    {
        RequireGlobalPersistence(feature);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(fullPath) ?? string.Empty);
        string logicalFilename = Path.GetFileName(fullPath);
        bool normal = logicalFilename.StartsWith("save", StringComparison.OrdinalIgnoreCase)
            && logicalFilename.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(logicalFilename.AsSpan(4, logicalFilename.Length - 8), out long index)
            && index >= 0;
        if (!string.Equals(directory, persistenceRoot, StringComparison.OrdinalIgnoreCase)
            || !(string.Equals(logicalFilename, "global.sav", StringComparison.OrdinalIgnoreCase) || normal))
            throw new InvalidOperationException($"P1C3の保存対象外です: {logicalFilename}");
        return logicalFilename.ToLowerInvariant();
    }

    internal int PendingGlobalPersistenceCount => persistenceQueue.Count;

    internal WebSaveMutation GetPendingGlobalPersistence()
    {
        if (!persistenceQueue.TryPeek(out var request))
            return null;
        return request with { Bytes = (byte[])request.Bytes.Clone() };
    }

    internal bool AcknowledgeGlobalPersistence(string operationId)
    {
        if (!persistenceQueue.TryPeek(out var request)
            || !string.Equals(request.OperationId, operationId, StringComparison.Ordinal))
            return false;
        // Only expose character data in MEMFS after the matching durable transaction ACK.
        if (request.LogicalFilename.StartsWith("dat/", StringComparison.Ordinal))
            MinorShift.Emuera.Web.Runtime.BrowserCharacterFiles.WriteAtomic(
                Path.Combine(characterPersistenceRoot, request.LogicalFilename[4..]), request.Bytes);
        persistenceQueue.Dequeue();
        if (persistenceQueue.Count != 0)
            return true;
        if (deferredPersistenceException is not null)
        {
            Exception exception = deferredPersistenceException;
            LogicalLine errorLine = deferredPersistenceErrorLine;
            bool systemProc = deferredPersistenceSystemProc;
            deferredPersistenceException = null;
            deferredPersistenceErrorLine = null;
            if (systemProc)
                handleExceptionInSystemProc(exception, errorLine, true);
            else
                handleException(exception, errorLine, true);
            return true;
        }
        console.CompletePersistence();
        return true;
    }

    internal void FailGlobalPersistence(string message)
    {
        persistenceQueue.Clear();
        deferredPersistenceException = null;
        deferredPersistenceErrorLine = null;
        console.FailPersistence(message);
    }
#endif
    internal BootstrapRuntimeSummary GetBootstrapRuntimeSummary()
    {
        string identity = string.Join('\n', (labelDic?.GetAllLabels(true) ?? [])
            .Select(label => $"{label.LabelName}\t{label.Position?.Filename.Replace('\\', '/')}\t{label.Position?.LineNo}")
            .OrderBy(value => value, StringComparer.Ordinal));
        IReadOnlyDictionary<string, string> configuration = GetBootstrapEffectiveConfiguration();
        string[] configRows = configuration.Select(pair => $"{pair.Key}\t{pair.Value}").OrderBy(value => value, StringComparer.Ordinal).ToArray();
        IReadOnlyDictionary<string, VariableToken> variables = idDic?.GetBootstrapVariableTokens()
            ?? throw new InvalidOperationException("Bootstrap schema requires an initialized IdentifierDictionary.");
        string[] variableRows = variables.Select(pair =>
        {
            VariableToken token = pair.Value;
            string lengths = string.Join(',', Enumerable.Range(0, token.Dimension).Select(dimension =>
            {
                try { return token.GetLength(dimension).ToString(); }
                catch (Exception ex) { return "!" + ex.GetType().Name; }
            }));
            return $"{pair.Key}\t{(token.IsInteger ? "Int64" : "String")}\t{token.Dimension}\t{lengths}\tchara={token.IsCharacterData}\tglobal={token.IsGlobal}\tsavedata={token.IsSavedata}\tprivate={token.IsPrivate}\treference={token.IsReference}";
        }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        ParserMediator.BootstrapWarningSummary warnings = ParserMediator.GetBootstrapWarningSummary();
        static string Hash(IEnumerable<string> rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows))));
        return new(
            Config.GetFiles(Program.CsvDir, "*.CSV").Count,
            Config.GetFiles(Program.ErbDir, "*.ERH").Count,
            Config.GetFiles(Program.ErbDir, "*.ERB").Count,
            GlobalStatic.ConstantData?.CharacterTemplateCount ?? 0,
            vEvaluator?.VariableData.GetVarTokenDic().Count ?? 0,
            labelDic?.Count ?? 0,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),
            erbLoader?.LazyErbFileCount ?? 0,
            erbLoader?.LazyErbFallbackFileCount ?? 0,
            erbLoader?.DeferredEagerCount ?? 0,
            Hash(configRows),
            "IdentifierDictionary.varTokenDic (built-ins and ERH user-defined; function private/local excluded)",
            variableRows.Length,
            Hash(variableRows),
            string.Join('\n', variableRows),
            warnings.Count,
            warnings.KindCount,
            warnings.IdentitySha256,
            warnings.KindCounts,
            warnings.Examples);
    }

    internal static IReadOnlyDictionary<string, string> GetBootstrapEffectiveConfiguration() => new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["IgnoreCase"] = Config.IgnoreCase.ToString(),
        ["UseRenameFile"] = Config.UseRenameFile.ToString(),
        ["UseReplaceFile"] = Config.UseReplaceFile.ToString(),
        ["SearchSubdirectory"] = Config.SearchSubdirectory.ToString(),
        ["SortWithFilename"] = Config.SortWithFilename.ToString(),
        ["SystemSaveInBinary"] = Config.SystemSaveInBinary.ToString(),
        ["UseSaveFolder"] = Config.UseSaveFolder.ToString(),
        ["AllowLongInputByMouse"] = Config.AllowLongInputByMouse.ToString(),
        ["SystemIgnoreTripleSymbol"] = Config.SystemIgnoreTripleSymbol.ToString(),
        ["SavDirRelative"] = Path.GetRelativePath(Program.ExeDir, Config.SavDir).Replace('\\', '/'),
        ["SavDirUnderRunRoot"] = Path.GetFullPath(Config.SavDir).StartsWith(Path.GetFullPath(Program.ExeDir), StringComparison.OrdinalIgnoreCase).ToString(),
        ["UseNewRandom"] = JSONConfig.Game.UseNewRandom.ToString(),
        ["UseScopedVariableInstruction"] = JSONConfig.Game.UseScopedVariableInstruction.ToString(),
        ["UseRenameInCharaCSV"] = JSONConfig.Game.UseRenameInCharaCSV.ToString(),
        ["LazyEnabled"] = JSONConfig.Game.LazyErb.Enabled?.ToString() ?? "null",
        ["LazyDirectories"] = string.Join('|', JSONConfig.Game.LazyErb.Directories)
    };
    // [Emuera改修:MEASURE-03]
    // マクロ後の変数状態を保存形式と同じ並びでハッシュ化し、比較試験に使う入口。
    // セーブファイル自体は作成・変更しない。通常プレイからは呼ばれない。
    internal string GetBenchmarkStateHash() => vEvaluator.GetBenchmarkStateHash();
#if PERFORMANCE_METRICS
    // H0/H1のbenchmark診断専用。全FunctionLabelLineからNextLineだけを走査し、
    // ParentLabelLine identityとcycle checkで重複を除き、InstructionLine用の巨大な集合を作らない。
    internal object CreateInstructionStorageCensusRecord(string phase)
    {
        List<FunctionLabelLine> labels = labelDic.GetAllLabels(true);
        var argumentStateCounts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["rawArgumentSource"] = 0,
            ["parsedArgument"] = 0,
            ["errorString"] = 0,
            ["noArgumentStorage"] = 0,
            ["unexpectedArgumentStorage"] = 0,
        };
        var auxiliaryCounts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["assignmentWordCollection"] = 0,
            ["ifCaseList"] = 0,
            ["dataList"] = 0,
            ["callList"] = 0,
            ["jumpTarget"] = 0,
            ["functionIdentifier"] = 0,
            ["other"] = 0,
            ["null"] = 0,
        };
        var rawByFunction = new Dictionary<FunctionCode, (long Count, long TotalChars)>();
        var assignmentsByFunction = new Dictionary<FunctionCode, (long Count, long TotalWords)>();
        var parsedArgumentTypes = new Dictionary<string, long>(StringComparer.Ordinal);

        long instructionLineCount = 0;
        long nextLineInstructionOccurrenceCount = 0;
        long parentLabelMismatchCount = 0;
        long instructionLineWithNullParentLabelCount = 0;
        long instructionLineWithDifferentParentLabelCount = 0;
        long cyclicLabelChainCount = 0;
        long rawArgumentSourceCount = 0;
        long rawArgumentTotalChars = 0;
        int rawArgumentMaxChars = 0;
        long parsedArgumentCount = 0;
        long errorStringCount = 0;
        long errorStringTotalChars = 0;
        long noArgumentStorageCount = 0;
        long unexpectedArgumentStorageCount = 0;
        long assignmentWordCollectionCount = 0;
        long totalWordCount = 0;
        long compactCollectionCount = 0;
        long linkedCollectionCount = 0;
        long compactTotalCount = 0;
        long compactTotalCapacity = 0;
        long linkedNodeCount = 0;
        long identifierWordCount = 0;
        long symbolWordCount = 0;
        long literalIntegerWordCount = 0;
        long operatorWordCount = 0;
        long otherWordCount = 0;
        long separateJumpToReferenceCount = 0;

        foreach (FunctionLabelLine label in labels)
        {
            if (HasNextLineCycle(label))
            {
                cyclicLabelChainCount++;
                continue;
            }

            for (LogicalLine line = label.NextLine;
                 line != null && line is not FunctionLabelLine && line is not NullLine;
                 line = line.NextLine)
            {
                if (line is not InstructionLine instruction)
                    continue;

                nextLineInstructionOccurrenceCount++;
                if (!ReferenceEquals(instruction.ParentLabelLine, label))
                {
                    parentLabelMismatchCount++;
                    if (instruction.ParentLabelLine == null)
                        instructionLineWithNullParentLabelCount++;
                    else
                        instructionLineWithDifferentParentLabelCount++;
                    continue;
                }

                instructionLineCount++;
                FunctionCode functionCode = instruction.FunctionCode;
                InstructionLineBenchmarkStorage state = instruction.GetBenchmarkStorage();
                argumentStateCounts[state.ArgumentKind]++;
                auxiliaryCounts[state.AuxiliaryKind]++;
                if (state.HasJumpTo)
                    separateJumpToReferenceCount++;

                switch (state.ArgumentKind)
                {
                    case "rawArgumentSource":
                        rawArgumentSourceCount++;
                        rawArgumentTotalChars += state.RawSourceLength;
                        rawArgumentMaxChars = Math.Max(rawArgumentMaxChars, state.RawSourceLength);
                        rawByFunction.TryGetValue(functionCode, out var rawCount);
                        rawByFunction[functionCode] = (rawCount.Count + 1, rawCount.TotalChars + state.RawSourceLength);
                        break;
                    case "parsedArgument":
                        parsedArgumentCount++;
                        parsedArgumentTypes.TryGetValue(state.ParsedArgumentType, out long typeCount);
                        parsedArgumentTypes[state.ParsedArgumentType] = typeCount + 1;
                        break;
                    case "errorString":
                        errorStringCount++;
                        errorStringTotalChars += state.ErrorStringLength;
                        break;
                    case "noArgumentStorage":
                        noArgumentStorageCount++;
                        break;
                    default:
                        unexpectedArgumentStorageCount++;
                        break;
                }

                if (state.AssignmentWords is not WordCollection assignmentWords)
                    continue;

                assignmentWordCollectionCount++;
                WordCollectionBenchmarkStorage words = assignmentWords.GetBenchmarkStorage();
                long wordCount = words.CompactTotalCount + words.LinkedNodeCount;
                totalWordCount += wordCount;
                compactCollectionCount += words.CompactCollectionCount;
                linkedCollectionCount += words.LinkedCollectionCount;
                compactTotalCount += words.CompactTotalCount;
                compactTotalCapacity += words.CompactTotalCapacity;
                linkedNodeCount += words.LinkedNodeCount;
                identifierWordCount += words.IdentifierWordCount;
                symbolWordCount += words.SymbolWordCount;
                literalIntegerWordCount += words.LiteralIntegerWordCount;
                operatorWordCount += words.OperatorWordCount;
                otherWordCount += words.OtherWordCount;

                assignmentsByFunction.TryGetValue(functionCode, out var assignmentCount);
                assignmentsByFunction[functionCode] = (assignmentCount.Count + 1, assignmentCount.TotalWords + wordCount);
            }
        }

        var rawFunctionCodeRows = rawByFunction
            .OrderByDescending(pair => pair.Value.TotalChars)
            .ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Take(50)
            .Select(pair => new { functionCode = pair.Key.ToString(), count = pair.Value.Count, totalChars = pair.Value.TotalChars })
            .ToArray();
        var assignmentFunctionCodeRows = assignmentsByFunction
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Take(50)
            .Select(pair => new { functionCode = pair.Key.ToString(), lineCount = pair.Value.Count, wordCount = pair.Value.TotalWords })
            .ToArray();
        var parsedArgumentTypeRows = parsedArgumentTypes
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(20)
            .Select(pair => new { argumentType = pair.Key, count = pair.Value })
            .ToArray();

        return new
        {
            type = "instructionStorageCensus",
            phase,
            utc = DateTime.UtcNow,
            processId = Environment.ProcessId,
            functionLabelCount = labels.Count,
            instructionLineCount,
            graphTraversal = new
            {
                nextLineInstructionOccurrenceCount,
                parentLabelMismatchCount,
                instructionLineWithNullParentLabelCount,
                instructionLineWithDifferentParentLabelCount,
                cyclicLabelChainCount,
                instructionLineCountUniqueByParentLabel = instructionLineCount,
            },
            storageStates = new
            {
                rawArgumentSourceCount,
                rawArgumentTotalChars,
                rawArgumentMaxChars,
                parsedArgumentCount,
                errorStringCount,
                errorStringTotalChars,
                noArgumentStorageCount,
                unexpectedArgumentStorageCount,
            },
            rawFunctionCodeTotalCount = rawArgumentSourceCount,
            rawFunctionCodeTotalChars = rawArgumentTotalChars,
            rawFunctionCodeGroupCount = rawByFunction.Count,
            rawFunctionCodeRows,
            parsedArgumentTypeGroupCount = parsedArgumentTypes.Count,
            parsedArgumentTypeRows,
            auxiliaryDataCounts = auxiliaryCounts,
            separateJumpToReferenceCount,
            assignmentWords = new
            {
                assignmentWordCollectionCount,
                totalWordCount,
                compactCollectionCount,
                linkedCollectionCount,
                compactListCount = compactCollectionCount,
                compactTotalCount,
                compactTotalCapacity,
                linkedNodeCount,
                linkedTotalCount = linkedNodeCount,
                wordTypeCounts = new { identifierWord = identifierWordCount, symbolWord = symbolWordCount, literalIntegerWord = literalIntegerWordCount, operatorWord = operatorWordCount, other = otherWordCount },
                byFunctionCodeTotalCount = assignmentsByFunction.Values.Sum(value => value.Count),
                byFunctionCodeTotalWords = assignmentsByFunction.Values.Sum(value => value.TotalWords),
                byFunctionCodeGroupCount = assignmentsByFunction.Count,
                byFunctionCode = assignmentFunctionCodeRows,
            },
            needReduceArgumentOnLoad = Config.NeedReduceArgumentOnLoad,
            lazyErbFileCount = erbLoader?.LazyErbFileCount ?? 0,
            lazyErbLoadedFileCount = erbLoader?.LazyErbLoadedFileCount ?? 0,
            lazyErbPendingLabelCount = erbLoader?.LazyErbPendingLabelCount ?? 0,
            lazyErbFallbackFileCount = erbLoader?.LazyErbFallbackFileCount ?? 0,
            deferredEagerCount = erbLoader?.DeferredEagerCount ?? 0,
        };
    }

    private static bool HasNextLineCycle(FunctionLabelLine label)
    {
        LogicalLine slow = label.NextLine;
        LogicalLine fast = label.NextLine;
        while (!IsNextLineBoundary(fast))
        {
            fast = fast.NextLine;
            if (IsNextLineBoundary(fast))
                return false;
            fast = fast.NextLine;
            slow = slow.NextLine;
            if (ReferenceEquals(slow, fast))
                return true;
        }
        return false;
    }

    private static bool IsNextLineBoundary(LogicalLine line) =>
        line == null || line is FunctionLabelLine or NullLine;
#endif
    private ExpressionMediator exm;
    private GameBase gamebase;
    readonly EmueraConsole console = view;
    private IdentifierDictionary idDic;
    ProcessState state;
    ProcessState originalState;//リセットする時のために
    private ErbLoader erbLoader;
    bool noError;
    //色々あって復活させてみる
    bool initialiing;
    public bool inInitializeing { get { return initialiing; } }

    internal ProcessInitializeProfile? InitializeProfile { get; private set; }

    public async Task<bool> Initialize(StreamWriter logWriter, bool captureInitializeProfile = false)
    {
        InitializeProfile = null;
        ProcessInitializeProfileCollector profile = captureInitializeProfile ? new() : null;
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        LexicalAnalyzer.UseMacro = false;
        state = new ProcessState(console);
#if WEB_RUNTIME && ERB_EXECUTION_PROFILE
        state.AttachErbExecutionProfiler(erbExecutionProfiler);
#endif
        originalState = state;
        initialiing = true;
        try
        {
            logWriter.WriteLine($"Proc:Init:Start {stopWatch.ElapsedMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:Parser:Start {stopWatch.ElapsedMilliseconds}ms");
            ParserMediator.Initialize(console);
            //コンフィグファイルに関するエラーの処理（コンフィグファイルはこの関数に入る前に読込済み）
            if (ParserMediator.HasWarning)
            {
                ParserMediator.FlushWarningList();
                if (Dialog.ShowPrompt(LocalizationManager.MsgBox.ConfigError, LocalizationManager.MsgBox.ConfigFileError))
                {
                    console.PrintSystemLine(LocalizationManager.SystemLine.SelectExitConfigMB);
                    return false;
                }
            }
            logWriter.WriteLine($"Proc:Init:Parser:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("parser");

            logWriter.WriteLine($"Proc:Init:Image:Start {stopWatch.ElapsedMilliseconds}ms");
            //リソースフォルダ読み込み
            var err = await Task.Run(AppContents.LoadContents);
            if (err != null)
            {
                ParserMediator.FlushWarningList();
                console.PrintSystemLine(LocalizationManager.SystemLine.ResourceReadError);
                console.Print(err.ToString());
                return false;
            }
            ParserMediator.FlushWarningList();
            logWriter.WriteLine($"Proc:Init:Image:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("resources");
            // [Emuera改修:MEASURE-01] 起動時間を区間別に調べる目印。通常版では空処理。
            PerformanceMetrics.MarkStartup("ResourcesPrepared");


            logWriter.WriteLine($"Proc:Init:KeyMacro:Start {stopWatch.ElapsedMilliseconds}ms");
            //キーマクロ読み込み
            if (Config.UseKeyMacro && !Program.AnalysisMode)
            {
                if (File.Exists(KeyMacro.macroPath))
                {
                    if (Config.DisplayReport)
                        console.PrintSystemLine(LocalizationManager.SystemLine.LoadingMacro);
                    if(!KeyMacro.LoadMacroFile(KeyMacro.macroPath))
                        console.PrintSystemLine(LocalizationManager.Error.MacroLoadingError);
                }
            }
            logWriter.WriteLine($"Proc:Init:KeyMacro:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("key-macro");

            logWriter.WriteLine($"Proc:Init:Replace:Start {stopWatch.ElapsedMilliseconds}ms");
            //_replace.csv読み込み
            if (Config.UseReplaceFile && !Program.AnalysisMode)
            {
                if (File.Exists(Program.CsvDir + "_Replace.csv"))
                {
                    if (Config.DisplayReport)
                        console.PrintSystemLine(LocalizationManager.SystemLine.LoadingReplace);
                    ConfigData.Instance.LoadReplaceFile(Program.CsvDir + "_Replace.csv");
                    if (ParserMediator.HasWarning)
                    {
                        ParserMediator.FlushWarningList();
                        if (Dialog.ShowPrompt(LocalizationManager.MsgBox.ReplaceError, LocalizationManager.MsgBox.ReplaceFileError))
                        {
                            console.PrintSystemLine(LocalizationManager.SystemLine.SelectExitReplaceMB);
                            return false;
                        }
                    }
                }
            }
            Config.SetReplace(ConfigData.Instance);

            logWriter.WriteLine($"Proc:Init:Replace:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("replace");

            //ここでBARを設定すれば、いいことに気づいた予感
            console.setStBar(Config.DrawLineString);

            logWriter.WriteLine($"Proc:Init:Rename:Load:Start {stopWatch.ElapsedMilliseconds}ms");
            //_rename.csv読み込み
            if (Config.UseRenameFile)
            {
                if (File.Exists(Program.CsvDir + "_Rename.csv"))
                {
                    if (Config.DisplayReport || Program.AnalysisMode)
                        console.PrintSystemLine(LocalizationManager.SystemLine.LoadingRename);
                    ParserMediator.LoadEraExRenameFile(Program.CsvDir + "_Rename.csv");
                }
                else
                    console.PrintError(LocalizationManager.SystemLine.MissingRename);
            }
            logWriter.WriteLine($"Proc:Init:Rename:Load:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("rename");

            if (!Config.DisplayReport)
            {
                console.PrintSingleLine(Config.LoadLabel);
                console.RefreshStrings(true);
            }
            //gamebase.csv読み込み
            gamebase = new GameBase();
            if (!await Task.Run(() => gamebase.LoadGameBaseCsv(Program.CsvDir + "GAMEBASE.CSV")))
            {
                ParserMediator.FlushWarningList();
                console.PrintSystemLine(LocalizationManager.SystemLine.GamebaseError);
                return false;
            }
            console.SetWindowTitle(gamebase.ScriptWindowTitle);
            GlobalStatic.GameBaseData = gamebase;
            logWriter.WriteLine($"Proc:Init:MainCSV:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("gamebase-csv");

            logWriter.WriteLine($"Proc:Init:EtcCSV:Start {stopWatch.ElapsedMilliseconds}ms");
            //前記以外のcsvを全て読み込み
            var constant = new ConstantData();
            constant.LoadData(Program.CsvDir, console, Config.DisplayReport);
            GlobalStatic.ConstantData = constant;
            TrainName = constant.GetCsvNameList(VariableCode.TRAINNAME);
            logWriter.WriteLine($"Proc:Init:EtcCSV:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("other-csv");
            PerformanceMetrics.MarkStartup("CsvLoaded"); // CSV読込完了の目印


            vEvaluator = new VariableEvaluator(gamebase, constant);
            GlobalStatic.VEvaluator = vEvaluator;

            idDic = new IdentifierDictionary(vEvaluator.VariableData);
            GlobalStatic.IdentifierDictionary = idDic;

            StrForm.Initialize();
            VariableParser.Initialize();

            exm = new ExpressionMediator(this, vEvaluator, console);
            GlobalStatic.EMediator = exm;
            profile?.CompletePhase("variable-and-identifier-setup");

            logWriter.WriteLine($"Proc:Init:ERH:Start {stopWatch.ElapsedMilliseconds}ms");

            labelDic = new LabelDictionary();
            GlobalStatic.LabelDictionary = labelDic;
            ErhLoader hLoader = new(console, idDic, this);

            LexicalAnalyzer.UseMacro = false;

            //ERH読込
            if (!await Task.Run(() => hLoader.LoadHeaderFiles(Program.ErbDir, Config.DisplayReport)))
            {
                ParserMediator.FlushWarningList();
                console.PrintSystemLine(LocalizationManager.SystemLine.ErhLoadingError);
                return false;
            }
            LexicalAnalyzer.UseMacro = idDic.UseMacro();
            logWriter.WriteLine($"Proc:Init:ERH:End {stopWatch.ElapsedMilliseconds}ms");
            profile?.CompletePhase("erh");
            PerformanceMetrics.MarkStartup("ErhLoaded"); // ERH読込完了の目印


            //TODO:ユーザー定義変数用のcsvの適用

            //ERB読込
            logWriter.WriteLine($"Proc:Init:ERB:Start {stopWatch.ElapsedMilliseconds}ms");
            erbLoader = new ErbLoader(console, exm, this);
            erbLoader.SetThreadUsageCapture(captureInitializeProfile);
            if (Program.AnalysisMode)
                noError = await erbLoader.LoadErbList(Program.AnalysisFiles, labelDic);
            else
                noError = await erbLoader.LoadErbDir(Program.ErbDir, Config.DisplayReport, labelDic);
            erbLoader.SetThreadUsageCapture(false);
            logWriter.WriteLine($"Proc:Init:ERB:Enumeration {erbLoader.EnumerationMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:PrimaryParse {erbLoader.PrimaryParseMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:LabelSetup {erbLoader.LabelSetupMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:ScriptParse {erbLoader.ScriptParseMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:LazyErb files={erbLoader.LazyErbFileCount} fallback={erbLoader.LazyErbFallbackFileCount}");
            logWriter.WriteLine($"Proc:Init:ERB:DeferredEager count={erbLoader.DeferredEagerCount}");
            logWriter.WriteLine($"Proc:Init:ERB:Result success={noError}");
            if (!noError)
                logWriter.WriteLine($"Proc:Init:ERB:FirstError {erbLoader.FirstErrorDiagnostic}");
            logWriter.WriteLine($"Proc:Init:ERB:End {stopWatch.ElapsedMilliseconds}ms");
            PerformanceMetrics.MarkStartup("ErbParsed"); // ERB解析完了の目印

            if (!noError)
                return false;

            profile?.CompletePhase("erb");

#if !WEB_RUNTIME
            SQL.SetUpTempDB();
#endif

            initSystemProcess();
            initialiing = false;
            profile?.CompletePhase("system-process");

            logWriter.WriteLine($"Proc:Init:End {stopWatch.ElapsedMilliseconds}ms");
        }
        catch (Exception e)
        {
#if WEB_RUNTIME
            throw new InvalidOperationException("P1A実Runtimeの初期化に失敗しました", e);
#else
            handleException(e, null, true);
            console.PrintSystemLine(LocalizationManager.Error.InitFatalError);
            return false;
#endif
        }
        if (labelDic == null)
        {
            return false;
        }
        state.Begin(BeginType.TITLE);
        InitializeProfile = profile?.Complete(erbLoader);
        return true;
    }

    // [Emuera改修:MEM-13R39 2026-08-22]
    // 通常モードではactive erbLoaderのLazy表を使い、eagerのDebug/Analysisやreload中のloader不在時は
    // 追加処理なしで従来経路を通す。呼び出し側の引数評価・ScopeInより前に判定できる境界を保つ。
    internal bool EnsureFunctionReady(FunctionLabelLine label) => erbLoader?.EnsureFunctionReady(label) ?? true;

    public async Task ReloadErbAll()
    {
        await Preload.Load(Program.ErbDir);
        await Preload.Load(Program.CsvDir);
        saveCurrentState(false);
        state.SystemState = SystemStateCode.System_Reloaderb;
        erbLoader = new(console, exm, this);
        await erbLoader.LoadErbDir(Program.ErbDir, false, labelDic);
        console.ReadAnyKey();
    }

    public async Task ReloadPartialErb(List<string> paths)
    {
        // [Emuera改修:MEM-13R39.1 2026-08-23]
        // active erbLoaderは通常モードで起動時の未hydrate stubとLazy対応表を所有するため、
        // active Lazy対象を含む再読込ではpartial loaderに置換せず、全体を一体で再構築する。
        // Debug/AnalysisではLazyが無効なので、従来どおりpartial reloadを維持する。
        if (erbLoader?.HasRuntimeLazyState == true || paths.Any(LazyErbPolicy.IsActiveTarget))
        {
            // [Emuera改修:MEM-13R41I 2026-08-24]
            // active loaderのLazy/Deferred表はFunctionLabelLine identityを保持する。
            // 別partialLoaderで一部fileだけ差し替えるとold label参照が残るため、runtime stateがある間はfull reloadへ統一する。
            await ReloadErbAll();
            return;
        }
        saveCurrentState(false);
        state.SystemState = SystemStateCode.System_Reloaderb;
        await Preload.Load(paths);
        // [Emuera改修:MEM-13R39.1 2026-08-23]
        // Lazy対象外のpartial reloadだけはlocal loaderに限定し、active Lazy managerを保持する。
        ErbLoader partialLoader = new(console, exm, this);
        await partialLoader.LoadErbList(paths, labelDic);
        console.ReadAnyKey();
    }

    public async Task ReloadErbFolder(string dirPath)
    {
        // [Emuera改修:MEM-13R41F 2026-08-24]
        // ファイルが削除されて列挙結果から消えていても、configured Lazy directoryとのscope交差で
        // full reloadへ昇格し、古いstubをLabelDictionaryへ残さない。親folderの扱いはSearchSubdirectoryに従う。
        if (erbLoader?.HasRuntimeLazyState == true
            || LazyErbPolicy.RequiresFullReloadForDirectory(dirPath, Config.SearchSubdirectory))
        {
            // [Emuera改修:MEM-13R41I 2026-08-24]
            // folder reloadも同じloader ownership規則に揃え、削除済みLazy fileと旧label identityの双方を残さない。
            await ReloadErbAll();
            return;
        }
        var serachOption = SearchOption.TopDirectoryOnly;
        if (Config.SearchSubdirectory)
        {
            serachOption = SearchOption.AllDirectories;
        }

        string[] erbFiles = Directory.EnumerateFiles(dirPath, "", serachOption)
            .Where(x => Path.GetExtension(x).Equals(".erb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        saveCurrentState(false);
        state.SystemState = SystemStateCode.System_Reloaderb;
        await Preload.Load(dirPath);
        // [Emuera改修:MEM-13R39.1 2026-08-23]
        // active Lazy対象外のfolderだけは従来どおりlocal loaderで部分再読込する。
        ErbLoader partialLoader = new(console, exm, this);
        await partialLoader.LoadErbList(erbFiles, labelDic);
        console.ReadAnyKey();
    }

    public void SetCommnds(Int64 count)
    {
        coms = new List<long>((int)count);
        isCTrain = true;
        Int64[] selectcom = vEvaluator.SELECTCOM_ARRAY;
        if (count >= selectcom.Length)
        {
            throw new CodeEE(LocalizationManager.Error.CalltrainArgMoreThanSelectcom);
        }
        for (int i = 0; i < (int)count; i++)
        {
            coms.Add(selectcom[i + 1]);
        }
    }

    public bool ClearCommands()
    {
        coms.Clear();
        count = 0;
        isCTrain = false;
        skipPrint = true;
        return callFunction("CALLTRAINEND", false, false);
    }

    public void InputResult5(int r0, int r1, int r2, int r3, int r4)
    {
        long[] result = vEvaluator.RESULT_ARRAY;
        result[0] = r0;
        result[1] = r1;
        result[2] = r2;
        result[3] = r3;
        result[4] = r4;
    }
    public void InputInteger(Int64 i)
    {
        vEvaluator.RESULT = i;
    }
    public void SetResultArray(long i, int index)
    {
        vEvaluator.RESULT_ARRAY[index] = i;
    }
    public void InputSystemInteger(Int64 i)
    {
        systemResult = i;
    }
    public void InputString(string s)
    {
        vEvaluator.RESULTS = s;
    }

    public void SetResultsArray(string s, int index)
    {
        vEvaluator.RESULTS_ARRAY[index] = s;
    }
    public void CompileScript()
    {
        var ab = new PersistedAssemblyBuilder(new AssemblyName("MyAssembly"), typeof(object).Assembly);
        var mob = ab.DefineDynamicModule("MyModule");
        var tb = mob.DefineType("MyType", TypeAttributes.Public | TypeAttributes.Class);
        systemProcessDictionary[state.SystemState]();
        var line = state.CurrentLine;

        if (line is FunctionLabelLine functionLabelLine)
        {
            var functionName = functionLabelLine.LabelName;
            var args = functionLabelLine.Arg.Select(x => x.GetOperandType()).ToArray();
            args = [];

            var meb = tb.DefineMethod(functionName, MethodAttributes.Public | MethodAttributes.Static,
                                                                    typeof(object), args);
            var il = meb.GetILGenerator();

            line = functionLabelLine.NextLine;
            while (line != null)
            {
                if (line is InstructionLine instructionLine)
                {
                    il.EmitWriteLine(instructionLine.ToString());
                }
                line = line.NextLine;
            }

            il.Emit(OpCodes.Ldstr, "OK");
            il.Emit(OpCodes.Ret);

            tb.CreateType();
            ab.Save(functionName + ".dll");


            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(functionName + ".dll"));
            var method = assembly.GetType("MyType").GetMethod(functionName);
            Console.WriteLine(method.Invoke(null, []));
        }
    }

    readonly Stopwatch startTime = new();

    public void DoScript()
    {
#if WEB_RUNTIME && ERB_EXECUTION_PROFILE
        long profileStarted = erbExecutionProfiler.StartDoScript();
#endif
        startTime.Restart();
        state.lineCount = 0;
        bool systemProcRunning = true;
        try
        {
            while (true)
            {
                methodStack = 0;
                systemProcRunning = true;
                while (state.ScriptEnd && console.IsRunning)
                    runSystemProc();
                if (!console.IsRunning)
                    break;
                systemProcRunning = false;
                runScriptProc();
            }
        }
        catch (Exception ec)
        {
            lastRuntimeException = ec;
            LogicalLine currentLine = state.ErrorLine;
            if (currentLine != null && currentLine is NullLine)
                currentLine = null;
#if WEB_RUNTIME
            if (globalPersistenceEnabled && persistenceQueue.Count != 0)
            {
                deferredPersistenceException = ec;
                deferredPersistenceErrorLine = currentLine;
                deferredPersistenceSystemProc = systemProcRunning;
                console.BeginPersistence();
                return;
            }
#endif
            if (systemProcRunning)
                handleExceptionInSystemProc(ec, currentLine, true);
            else
                handleException(ec, currentLine, true);
        }
#if WEB_RUNTIME && ERB_EXECUTION_PROFILE
        finally
        {
            erbExecutionProfiler.EndDoScript(profileStarted);
        }
#endif
    }

    public void BeginTitle()
    {
        vEvaluator.ResetData();
        state = originalState;
        state.Begin(BeginType.TITLE);
    }

    public void UpdateCheckInfiniteLoopState()
    {
        startTime.Restart();
        state.lineCount = 0;
    }

    private void checkInfiniteLoop()
    {
        //うまく動かない。BEEP音が鳴るのを止められないのでこの処理なかったことに（1.51）
        ////フリーズ防止。処理中でも履歴を見たりできる
        //System.Windows.Forms.Application.DoEvents();
        ////System.Threading.Thread.Sleep(0);

        //if (!console.Enabled)
        //{
        //    //DoEvents()の間にウインドウが閉じられたらおしまい。
        //    console.ReadAnyKey();
        //    return;
        //}
        var elapsedTime = startTime.ElapsedMilliseconds;
        if (elapsedTime < Config.InfiniteLoopAlertTime)
            return;
        LogicalLine currentLine = state.CurrentLine;
        if ((currentLine == null) || (currentLine is NullLine))
            return;//現在の行が特殊な状態ならスルー
        if (!console.Enabled)
            return;//クローズしてるとMessageBox.Showができないので。
        var text = string.Format(
            LocalizationManager.MsgBox.TooLongLoop,
            currentLine.Position.Value.Filename, currentLine.Position.Value.LineNo, state.lineCount, elapsedTime);
        if (
#if WEB_RUNTIME
            (webInfiniteLoopPrompt?.Invoke(new(
                LocalizationManager.MsgBox.InfiniteLoop,
                text,
                currentLine.Position.Value.Filename,
                currentLine.Position.Value.LineNo,
                state.lineCount,
                elapsedTime))
                ?? Dialog.ShowInfiniteLoopPrompt(LocalizationManager.MsgBox.InfiniteLoop, text))
#else
            Dialog.ShowInfiniteLoopPrompt(LocalizationManager.MsgBox.InfiniteLoop, text)
#endif
            )
        {
            throw new CodeEE(LocalizationManager.Error.SelectExitInfiniteLoopMB);
        }
        else
        {
            state.lineCount = 0;
            startTime.Restart();
        }
    }

    int methodStack;
    public SingleTerm GetValue(SuperUserDefinedMethodTerm udmt)
    {
        methodStack++;
        if (methodStack > 100)
        {
            //StackOverflowExceptionはcatchできない上に再現性がないので発生前に一定数で打ち切る。
            //環境によっては100以前にStackOverflowExceptionがでるかも？
            throw new CodeEE(LocalizationManager.Error.OverflowFuncStack);
        }
        SingleTerm ret = null;
        int temp_current = state.currentMin;
        state.currentMin = state.functionCount;
        udmt.Call.updateRetAddress(state.CurrentLine);
        try
        {
            state.IntoFunction(udmt.Call, udmt.Argument, exm);
            //do whileの中でthrow されたエラーはここではキャッチされない。
            //#functionを全て抜けてDoScriptでキャッチされる。
            runScriptProc();
            ret = state.MethodReturnValue;
        }
        finally
        {
            if (udmt.Call.TopLabel.hasPrivDynamicVar)
                udmt.Call.TopLabel.ScopeOut();
            //1756beta2+v3:こいつらはここにないとデバッグコンソールで式中関数が事故った時に大事故になる
            state.currentMin = temp_current;
            methodStack--;
        }
        return ret;
    }
#if WEB_RUNTIME // R7 direct-call metadata
    public SingleTerm GetDirectValue(DirectUserDefinedMethodTerm udmt)
    {
        methodStack++;
        if (methodStack > 100)
        {
            //StackOverflowExceptionはcatchできない上に再現性がないので発生前に一定数で打ち切る。
            //環境によっては100以前にStackOverflowExceptionがでるかも？
            throw new CodeEE(LocalizationManager.Error.OverflowFuncStack);
        }
        SingleTerm ret = null;
        int temp_current = state.currentMin;
        state.currentMin = state.functionCount;
        udmt.Call.updateRetAddress(state.CurrentLine);
        try
        {
            state.IntoDirectFunction(udmt.Call, udmt.Argument, exm, udmt);
            //do whileの中でthrow されたエラーはここではキャッチされない。
            //#functionを全て抜けてDoScriptでキャッチされる。
            runScriptProc();
            ret = state.MethodReturnValue;
        }
        finally
        {
            if (udmt.Call.TopLabel.hasPrivDynamicVar)
                udmt.Call.TopLabel.ScopeOut();
            //1756beta2+v3:こいつらはここにないとデバッグコンソールで式中関数が事故った時に大事故になる
            state.currentMin = temp_current;
            methodStack--;
        }
        return ret;
    }
#endif // R7 direct-call metadata

    public void clearMethodStack()
    {
        methodStack = 0;
    }

    public int MethodStack()
    {
        return methodStack;
    }

    // [Emuera改修:WARN-04]
    // 並列解析中の「今どの行を解析しているか」を作業スレッドごとに分ける。
    // 1つの共有変数だと、別スレッドの行番号を使って誤警告を出すことがある。
    // 通常の逐次実行時は従来どおりsequentialScanningLineを使う。
    // 参照: プロジェクト資料/06_コード案内.md
    [ThreadStatic]
    private static LogicalLine parallelScanningLine;
    private LogicalLine sequentialScanningLine;
    private volatile bool useThreadLocalScanningLine;
    public LogicalLine scaningLine
    {
        get => useThreadLocalScanningLine ? parallelScanningLine : sequentialScanningLine;
        set
        {
            if (useThreadLocalScanningLine)
                parallelScanningLine = value;
            else
                sequentialScanningLine = value;
        }
    }

    internal void SetParallelScanning(bool enabled)
    {
        useThreadLocalScanningLine = enabled;
        if (!enabled)
            parallelScanningLine = null;
    }
    internal LogicalLine GetScaningLine()
    {
        if (scaningLine != null)
            return scaningLine;
        LogicalLine line = state.ErrorLine;
        if (line == null)
            return null;
        return line;
    }


    private void handleExceptionInSystemProc(Exception exc, LogicalLine current, bool playSound)
    {
        console.ThrowError(playSound);
        if (exc is CodeEE)
        {
            console.PrintError(string.Format(LocalizationManager.Error.FuncEndError));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.Message);
        }
        else if (exc is ExeEE)
        {
            console.PrintError(string.Format(LocalizationManager.Error.FuncEndEmueraError));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.Message);
        }
        else
        {
            console.PrintError(string.Format(LocalizationManager.Error.FuncEndUnexpectedError));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.GetType().ToString() + ":" + exc.Message);
            string[] stack = exc.StackTrace.Split('\n');
            for (int i = 0; i < stack.Length; i++)
            {
                console.PrintError(stack[i]);
            }
        }
    }

    private void handleException(Exception exc, LogicalLine current, bool playSound)
    {
        console.ThrowError(playSound);
        ScriptPosition? position = null;
        if ((exc is EmueraException ee) && (ee.Position != null))
            position = ee.Position;
        else if ((current != null) && (current.Position != null))
            position = current.Position;
        string posString = "";
        if (position != null)
        {
            if (position.Value.LineNo >= 0)
                posString = string.Format(LocalizationManager.Error.ErrorFileAndLine, position.Value.Filename, position.Value.LineNo.ToString());
            else
                posString = string.Format(LocalizationManager.Error.ErrorFile, position.Value.Filename);

        }
        if (exc is CodeEE)
        {
            if (position != null)
            {
                if (current is InstructionLine procline && procline.FunctionCode == FunctionCode.THROW)
                {
                    console.PrintErrorButton(string.Format(LocalizationManager.Error.HasThrow, posString), position);
                    printRawLine(position);
                    console.PrintError(string.Format(LocalizationManager.Error.ThrowMessage, exc.Message));
                }
                else
                {
                    console.PrintErrorButton(string.Format(LocalizationManager.Error.HasError, posString), position);
                    console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                        AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
                    printRawLine(position);
                    console.PrintError(string.Format(LocalizationManager.Error.ErrorMessage, exc.Message));
                }
                console.PrintError(string.Format(LocalizationManager.Error.ErrorInFunc, current.ParentLabelLine.LabelName, current.ParentLabelLine.Position.Value.Filename, current.ParentLabelLine.Position.Value.LineNo.ToString()));
                console.PrintError(LocalizationManager.Error.FuncCallStack);
                LogicalLine parent;
                int depth = 0;
                while ((parent = state.GetReturnAddressSequensial(depth++)) != null)
                {
                    if (parent.Position != null)
                    {
                        console.PrintErrorButton(string.Format(LocalizationManager.Error.ErrorFuncStack, parent.Position.Value.Filename, parent.Position.Value.LineNo.ToString(), parent.ParentLabelLine.LabelName), parent.Position);
                    }
                }
            }
            else
            {
                console.PrintError(string.Format(LocalizationManager.Error.HasError, posString));
                console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                    AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
                console.PrintError(exc.Message);
            }
        }
        else if (exc is ExeEE)
        {
            console.PrintError(string.Format(LocalizationManager.Error.HasEmueraError, posString));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.Message);
        }
        else
        {
            console.PrintError(string.Format(LocalizationManager.Error.HasUnexpectedError, posString));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.GetType() + ":" + exc.Message);
            string[] stack = exc.StackTrace.Split('\n');
            for (int i = 0; i < stack.Length; i++)
            {
                console.PrintError(stack[i]);
            }
        }
    }

    public void printRawLine(ScriptPosition? position)
    {
        string str = getRawTextFormFilewithLine(position);
        if (!string.IsNullOrEmpty(str))
            console.PrintError(str);
    }

    public static string getRawTextFormFilewithLine(ScriptPosition? position)
    {
        string extents = position.Value.Filename[^4..].ToLower();
        if (extents == ".erb")
        {
            return File.Exists(Program.ErbDir + position.Value.Filename)
                ? position.Value.LineNo > 0 ? File.ReadLines(Program.ErbDir + position.Value.Filename, Config.Encode).Skip(position.Value.LineNo - 1).First() : ""
                : "";
        }
        else if (extents == ".csv")
        {
            return File.Exists(Program.CsvDir + position.Value.Filename)
                ? position.Value.LineNo > 0 ? File.ReadLines(Program.CsvDir + position.Value.Filename, Config.Encode).Skip(position.Value.LineNo - 1).First() : ""
                : "";
        }
        else
            return "";
    }

}
