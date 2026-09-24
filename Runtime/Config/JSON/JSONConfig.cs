//新設したコンフィグ設定のロード、セーブ、公開を担当する。
using MinorShift.Emuera;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;


namespace MinorShift.Emuera.Runtime.Config.JSON;
static class JSONConfig
{
    const string _lazyErbKey = "起動時に読み込まないERBフォルダ";
    const string _legacyLazyErbKey = "LazyErb";
    const string _enabledKey = "有効";
    const string _legacyEnabledKey = "Enabled";
    const string _directoriesKey = "フォルダ";
    const string _legacyDirectoriesKey = "Directories";

    public static JSONGameConfigData Game;
    public static JSONUserConfigData User;
    static JsonObject _userJson;
    static JSONLazyErbConfigData _activeUserLazyErb;
    static bool _hasActiveUserLazyErbOverride;
    static JSONLazyErbConfigData _configuredUserLazyErb;
    static bool _hasConfiguredUserLazyErbOverride;
    static JSONLazyErbConfigData _pendingUserLazyErb;
    static bool _pendingHasUserLazyErbOverride;
    static bool _hasPendingUserLazyErbChange;

    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        // [Emuera改修:MEM-13R40.2 2026-08-23]
        // setting.jsonのLazyErb directoryを日本語のまま保存し、手編集時に対象範囲を確認できるようにする。
        // JSONの引用符・バックスラッシュ・制御文字のescapeはJavaScriptEncoderに維持させる。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };
    static JsonObject _gameJson;

    const string _gameConfigFileName = "setting.json";
    static string _gameConfigFilePath = Program.ExeDir + _gameConfigFileName;
    const string _userConfigFileName = "setting_user.json";
    static string _userConfigFilePath = Program.ExeDir + _userConfigFileName;
    static readonly Encoding _utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static SKSamplingOptions SamplingOptions { get; private set; }

    public static JSONLazyErbConfigData EffectiveLazyErb
        => _hasActiveUserLazyErbOverride ? _activeUserLazyErb : Game?.LazyErb;

    public static bool HasUserLazyErbOverride => _hasConfiguredUserLazyErbOverride;
    public static JSONLazyErbConfigData UserLazyErbOverride
        => _hasConfiguredUserLazyErbOverride ? _configuredUserLazyErb : null;

    public static void SetUserLazyErbOverride(bool enabled, string[] directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        _pendingUserLazyErb = new JSONLazyErbConfigData
        {
            Enabled = enabled,
            Directories = (string[])directories.Clone(),
        };
        _pendingHasUserLazyErbOverride = true;
        _hasPendingUserLazyErbChange = true;
    }

    public static void ResetUserLazyErbOverride()
    {
        _pendingUserLazyErb = null;
        _pendingHasUserLazyErbOverride = false;
        _hasPendingUserLazyErbChange = true;
    }

    public static void SetSamplingOptions()
    {
        SamplingOptions = Game.ImageSamplingOption switch
        {
            Resampler.NearnestNeighber => new SKSamplingOptions(SKFilterMode.Nearest),
            Resampler.Linear => new SKSamplingOptions(SKFilterMode.Linear),
            Resampler.Cubic => new SKSamplingOptions(SKCubicResampler.CatmullRom),
            _ => throw new Exception(),
        };
    }

    // [Emuera改修:LAZY-01]
    // ゲーム推奨値とプレイヤー上書きを別JSONで管理し、user blockの存在を完全上書きとして扱う。
    // unknown propertyを保持しながら、旧LazyErb形式の読み込みも互換維持する。
    public static void Load()
    {
        bool gameExists = File.Exists(_gameConfigFilePath);
        bool userExists = File.Exists(_userConfigFilePath);
        string gameSource = gameExists ? File.ReadAllText(_gameConfigFilePath) : null;
        string userSource = userExists ? File.ReadAllText(_userConfigFilePath) : null;
        JsonObject gameJson = gameExists
            ? ParseJsonObject(gameSource, "setting.json")
            : JsonSerializer.SerializeToNode(new JSONGameConfigData(), _jsonOptions).AsObject();
        JsonObject userJson = userExists
            ? ParseJsonObject(userSource, "setting_user.json")
            : JsonSerializer.SerializeToNode(new JSONUserConfigData(), _jsonOptions).AsObject();

        // 既存両ファイルを読み、型検証がすべて済むまではどちらも書き換えない。
        bool migrated = MigrateLazyErbSettings(gameJson);
        string validatedGameJson = gameJson.ToJsonString(_jsonOptions);
        JSONGameConfigData loadedGame = JsonSerializer.Deserialize<JSONGameConfigData>(validatedGameJson)
            ?? throw new JsonException("setting.json must contain a valid game configuration");
        ValidateUserLazyErb(userJson, out JSONLazyErbConfigData userLazyErb, out bool hasUserLazyErb);
        JSONUserConfigData loadedUser = JsonSerializer.Deserialize<JSONUserConfigData>(userJson.ToJsonString(_jsonOptions))
            ?? throw new JsonException("setting_user.json must contain a valid user configuration");

        if (!gameExists)
            WriteJsonFile(_gameConfigFilePath, validatedGameJson);
        else if (migrated || !HasUtf8Bom(_gameConfigFilePath))
            WriteJsonFile(_gameConfigFilePath, migrated ? validatedGameJson : gameSource);

        if (!userExists)
            WriteJsonFile(_userConfigFilePath, userJson.ToJsonString(_jsonOptions));
        else if (!HasUtf8Bom(_userConfigFilePath))
            WriteJsonFile(_userConfigFilePath, userSource);

        _gameJson = gameJson;
        _userJson = userJson;
        Game = loadedGame;
        User = loadedUser;
        _activeUserLazyErb = userLazyErb;
        _hasActiveUserLazyErbOverride = hasUserLazyErb;
        _configuredUserLazyErb = userLazyErb;
        _hasConfiguredUserLazyErbOverride = hasUserLazyErb;
        _pendingUserLazyErb = null;
        _pendingHasUserLazyErbOverride = false;
        _hasPendingUserLazyErbChange = false;

        SetSamplingOptions();
    }

    public static void Save()
    {
        {
            // [Emuera改修:MEM-13R40 2026-08-23]
            // Save後もtop-level/nested unknown propertyを維持し、setting.jsonの将来拡張を破壊しない。
            JsonObject gameJson = _gameJson ?? JsonSerializer.SerializeToNode(Game, _jsonOptions).AsObject();
            JsonObject knownJson = JsonSerializer.SerializeToNode(Game, _jsonOptions).AsObject();
            foreach (KeyValuePair<string, JsonNode> property in knownJson)
            {
                if (property.Key == _lazyErbKey
                    && property.Value is JsonObject knownLazy
                    && gameJson[_lazyErbKey] is JsonObject existingLazy)
                {
                    foreach (KeyValuePair<string, JsonNode> lazyProperty in knownLazy)
                        existingLazy[lazyProperty.Key] = lazyProperty.Value?.DeepClone();
                }
                else
                    gameJson[property.Key] = property.Value?.DeepClone();
            }
            WriteJsonFile(_gameConfigFilePath, gameJson.ToJsonString(_jsonOptions));
        }
        {
            // [Emuera改修:LAZY-01]
            // 既存JSONを土台に既知値だけ更新し、unknown propertyを保ってuser側LazyERBを保存する。
            JsonObject userJson = _userJson ?? JsonSerializer.SerializeToNode(User, _jsonOptions).AsObject();
            JsonObject knownUserJson = JsonSerializer.SerializeToNode(User, _jsonOptions).AsObject();
            foreach (KeyValuePair<string, JsonNode> property in knownUserJson)
                userJson[property.Key] = property.Value?.DeepClone();

            if (_hasPendingUserLazyErbChange)
            {
                if (_pendingHasUserLazyErbOverride)
                    MergeLazyErbObject(userJson, _pendingUserLazyErb);
                else
                    userJson.Remove(_lazyErbKey);
            }

            WriteJsonFile(_userConfigFilePath, userJson.ToJsonString(_jsonOptions));
            _userJson = userJson;
            if (_hasPendingUserLazyErbChange)
            {
                _configuredUserLazyErb = _pendingUserLazyErb;
                _hasConfiguredUserLazyErbOverride = _pendingHasUserLazyErbOverride;
                _hasPendingUserLazyErbChange = false;
            }
        }
    }

    static void WriteJsonFile(string path, string json)
        => File.WriteAllText(path, json, _utf8Bom);

    static bool HasUtf8Bom(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> bom = stackalloc byte[3];
        return stream.Read(bom) == 3
            && bom[0] == 0xEF
            && bom[1] == 0xBB
            && bom[2] == 0xBF;
    }

    static JsonObject ParseJsonObject(string json, string fileName)
        => JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException($"{fileName} must contain a JSON object");

    static void ValidateUserLazyErb(JsonObject userJson, out JSONLazyErbConfigData value, out bool exists)
    {
        if (!userJson.TryGetPropertyValue(_lazyErbKey, out JsonNode lazyNode))
        {
            value = null;
            exists = false;
            return;
        }

        JsonObject lazyJson = RequireObject(lazyNode, _lazyErbKey);
        ValidateKnownLazyErbProperties(lazyJson, _enabledKey, _directoriesKey);
        value = JsonSerializer.Deserialize<JSONLazyErbConfigData>(lazyJson.ToJsonString(_jsonOptions))
            ?? throw new JsonException($"{_lazyErbKey} must contain a valid object");
        exists = true;
    }

    static JsonObject RequireObject(JsonNode node, string propertyName)
        => node as JsonObject ?? throw new JsonException($"{propertyName} must be a JSON object");

    static void ValidateKnownLazyErbProperties(JsonObject lazyJson, string enabledKey, string directoriesKey)
    {
        if (lazyJson.TryGetPropertyValue(enabledKey, out JsonNode enabledNode)
            && (enabledNode is not JsonValue enabledValue || !enabledValue.TryGetValue<bool>(out _)))
            throw new JsonException($"{enabledKey} must be a JSON boolean");

        if (lazyJson.TryGetPropertyValue(directoriesKey, out JsonNode directoriesNode))
        {
            if (directoriesNode is not JsonArray directories)
                throw new JsonException($"{directoriesKey} must be a JSON string array");
            foreach (JsonNode directory in directories)
            {
                if (directory is not JsonValue directoryValue || !directoryValue.TryGetValue<string>(out _))
                    throw new JsonException($"{directoriesKey} must contain only strings");
            }
        }
    }

    static void MergeLazyErbObject(JsonObject owner, JSONLazyErbConfigData value)
    {
        JsonObject lazyJson = owner[_lazyErbKey] as JsonObject ?? new JsonObject();
        JsonObject known = JsonSerializer.SerializeToNode(value, _jsonOptions).AsObject();
        foreach (KeyValuePair<string, JsonNode> property in known)
            lazyJson[property.Key] = property.Value?.DeepClone();
        owner[_lazyErbKey] = lazyJson;
    }

    static bool MigrateLazyErbSettings(JsonObject gameJson)
    {
        bool changed = false;
        bool hasNew = gameJson.TryGetPropertyValue(_lazyErbKey, out JsonNode newNode);
        bool hasLegacyProperty = gameJson.TryGetPropertyValue(_legacyLazyErbKey, out JsonNode legacyNode);
        bool hasLegacy = hasLegacyProperty && legacyNode is not null;
        JsonObject newJson = hasNew ? RequireObject(newNode, _lazyErbKey) : null;
        JsonObject legacyJson = hasLegacy ? RequireObject(legacyNode, _legacyLazyErbKey) : null;

        if (hasNew)
            ValidateKnownLazyErbProperties(newJson, _enabledKey, _directoriesKey);
        if (hasLegacy)
            ValidateKnownLazyErbProperties(legacyJson, _legacyEnabledKey, _legacyDirectoriesKey);

        if (!hasNew && !hasLegacy)
        {
            JSONLazyErbConfigData initialValue = hasLegacyProperty
                ? new JSONLazyErbConfigData()
                : new JSONLazyErbConfigData { Enabled = false, Directories = [] };
            gameJson[_lazyErbKey] = JsonSerializer.SerializeToNode(initialValue, _jsonOptions);
            if (hasLegacyProperty)
                gameJson.Remove(_legacyLazyErbKey);
            return true;
        }

        if (!hasNew)
        {
            newJson = new JsonObject();
            gameJson[_lazyErbKey] = newJson;
            changed = true;
        }

        if (hasLegacy)
        {
            CopyKnownIfMissing(newJson, _enabledKey, legacyJson, _legacyEnabledKey, ref changed);
            if (!newJson.ContainsKey(_directoriesKey))
            {
                if (legacyJson.TryGetPropertyValue(_legacyDirectoriesKey, out JsonNode legacyDirectories))
                    newJson[_directoriesKey] = ConvertLegacyDirectories(legacyDirectories);
                else
                    newJson[_directoriesKey] = JsonSerializer.SerializeToNode(new JSONLazyErbConfigData(), _jsonOptions)[_directoriesKey]?.DeepClone();
                changed = true;
            }
            foreach (KeyValuePair<string, JsonNode> property in legacyJson)
            {
                if (property.Key != _legacyEnabledKey
                    && property.Key != _legacyDirectoriesKey
                    && !newJson.ContainsKey(property.Key))
                {
                    newJson[property.Key] = property.Value?.DeepClone();
                    changed = true;
                }
            }
            gameJson.Remove(_legacyLazyErbKey);
            changed = true;
        }
        else if (hasLegacyProperty)
        {
            // 旧キーのnullは従来どおりmissing/default互換として扱い、migration後に削除する。
            gameJson.Remove(_legacyLazyErbKey);
            changed = true;
        }

        JsonObject defaults = JsonSerializer.SerializeToNode(new JSONLazyErbConfigData(), _jsonOptions).AsObject();
        foreach (string propertyName in new[] { _enabledKey, _directoriesKey })
        {
            if (!newJson.ContainsKey(propertyName))
            {
                newJson[propertyName] = defaults[propertyName]?.DeepClone();
                changed = true;
            }
        }
        return changed;
    }

    static void CopyKnownIfMissing(JsonObject destination, string destinationKey, JsonObject source, string sourceKey, ref bool changed)
    {
        if (!destination.ContainsKey(destinationKey)
            && source.TryGetPropertyValue(sourceKey, out JsonNode sourceValue))
        {
            destination[destinationKey] = sourceValue?.DeepClone();
            changed = true;
        }
    }

    static JsonNode ConvertLegacyDirectories(JsonNode legacyDirectories)
    {
        if (legacyDirectories is not JsonArray legacyArray)
            return legacyDirectories.DeepClone();

        JsonArray converted = [];
        foreach (JsonNode item in legacyArray)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out string directory))
                converted.Add(ConvertLegacyDirectory(directory));
            else
                converted.Add(item?.DeepClone());
        }
        return converted;
    }

    static string ConvertLegacyDirectory(string directory)
    {
        string normalized = directory.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(directory))
            return normalized;
        if (normalized.Equals("ERB", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ERB/", StringComparison.OrdinalIgnoreCase))
            return normalized;
        return "ERB/" + normalized.TrimStart('/');
    }
}
