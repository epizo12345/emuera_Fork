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

    public static void Load()
    {
        {
            if (!File.Exists(_gameConfigFilePath))
            {
                var defaultData = new JSONGameConfigData();
                var defaultJson = JsonSerializer.Serialize(defaultData, _jsonOptions);
                WriteJsonFile(_gameConfigFilePath, defaultJson);
            }

            {
                var json = File.ReadAllText(_gameConfigFilePath);
                JsonNode parsed = JsonNode.Parse(json);
                if (parsed is not JsonObject gameJson)
                    throw new JsonException("setting.json must contain a JSON object");

                // [Emuera改修:MEM-13R40.3 2026-08-23]
                // 旧LazyErbを日本語設定へ一度だけ移し、旧known fieldは変換、unknown fieldは新objectへ移して保持する。
                // 新旧が同時にある場合は新設定を優先し、欠落known fieldだけ旧設定から補完する。
                bool migrated = MigrateLazyErbSettings(gameJson);
                string validatedJson = gameJson.ToJsonString(_jsonOptions);
                JSONGameConfigData loadedGame = JsonSerializer.Deserialize<JSONGameConfigData>(validatedJson)
                    ?? throw new JsonException("setting.json must contain a valid game configuration");
                loadedGame.LazyErb ??= new JSONLazyErbConfigData();
                loadedGame.LazyErb.Enabled ??= true;
                loadedGame.LazyErb.Directories ??= ["ERB/口上/口上まとめ", "ERB/RPG/依頼", "ERB/RPG/イベント"];

                // [Emuera改修:MEM-13R40.3 2026-08-23]
                // migration結果はtyped validation成功後だけ保存する。設定ミスを先にdefault補完してdiskへ書くと、
                // 後段の型エラー発生時にユーザーの元設定を部分的に書き換えて隠してしまうため。
                if (migrated || !HasUtf8Bom(_gameConfigFilePath))
                    WriteJsonFile(_gameConfigFilePath, migrated ? validatedJson : json);
                _gameJson = gameJson;
                Game = loadedGame;
            }
        }

        {
            if (!File.Exists(_userConfigFilePath))
            {
                var defaultData = new JSONUserConfigData();
                var defaultJson = JsonSerializer.Serialize(defaultData, _jsonOptions);
                WriteJsonFile(_userConfigFilePath, defaultJson);
            }

            {
                var json = File.ReadAllText(_userConfigFilePath);

                User = JsonSerializer.Deserialize<JSONUserConfigData>(json);
                if (!HasUtf8Bom(_userConfigFilePath))
                    WriteJsonFile(_userConfigFilePath, json);
            }
        }

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
            var json = JsonSerializer.Serialize(User, _jsonOptions);
            WriteJsonFile(_userConfigFilePath, json);
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

    static bool MigrateLazyErbSettings(JsonObject gameJson)
    {
        bool changed = false;
        bool hasNew = TryGetObjectOrMissing(gameJson, _lazyErbKey, out JsonObject newJson);
        bool hasLegacyProperty = gameJson.TryGetPropertyValue(_legacyLazyErbKey, out JsonNode legacyNode);
        bool hasLegacy = TryGetObjectOrMissing(gameJson, _legacyLazyErbKey, out JsonObject legacyJson);

        if (hasNew)
        {
            // newJson is validated by TryGetObjectOrMissing; a non-null non-object is a configuration error.
        }
        else if (hasLegacy)
        {
            newJson = new JsonObject();
            gameJson[_lazyErbKey] = newJson;
            changed = true;
        }
        else
        {
            newJson = JsonSerializer.SerializeToNode(new JSONLazyErbConfigData(), _jsonOptions).AsObject();
            gameJson[_lazyErbKey] = newJson;
            if (hasLegacyProperty)
            {
                gameJson.Remove(_legacyLazyErbKey);
            }
            return true;
        }

        JsonObject defaults = JsonSerializer.SerializeToNode(new JSONLazyErbConfigData(), _jsonOptions).AsObject();
        if (hasLegacy)
        {
            CopyKnownIfMissing(newJson, _enabledKey, legacyJson, _legacyEnabledKey, ref changed);
            if (!newJson.ContainsKey(_directoriesKey))
            {
                if (legacyJson.TryGetPropertyValue(_legacyDirectoriesKey, out JsonNode legacyDirectories)
                    && legacyDirectories is not null)
                    newJson[_directoriesKey] = ConvertLegacyDirectories(legacyDirectories);
                else
                    newJson[_directoriesKey] = defaults[_directoriesKey]?.DeepClone();
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
            // nullはmissing/default互換として扱うが、migration後に旧LazyErb keyを残してはいけない。
            gameJson.Remove(_legacyLazyErbKey);
            changed = true;
        }

        foreach (string propertyName in new[] { _enabledKey, _directoriesKey })
        {
            if (!newJson.TryGetPropertyValue(propertyName, out JsonNode property) || property is null)
            {
                newJson[propertyName] = defaults[propertyName]?.DeepClone();
                changed = true;
            }
        }
        return changed;
    }

    static bool TryGetObjectOrMissing(JsonObject gameJson, string propertyName, out JsonObject value)
    {
        value = null;
        if (!gameJson.TryGetPropertyValue(propertyName, out JsonNode property) || property is null)
            return false;
        if (property is JsonObject jsonObject)
        {
            value = jsonObject;
            return true;
        }
        // [Emuera改修:MEM-13R40.3 2026-08-23]
        // 設定keyが存在するのに非null非Objectならmissing扱いでdefaultへ置換せず、誤設定を設定errorとして表面化する。
        throw new JsonException($"{propertyName} must be a JSON object");
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
