//新設したコンフィグ設定のロード、セーブ、公開を担当する。
using MinorShift.Emuera;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace MinorShift.Emuera.Runtime.Config.JSON;
static class JSONConfig
{
    public static JSONGameConfigData Game;
    public static JSONUserConfigData User;

    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
    };
    static JsonObject _gameJson;

    const string _gameConfigFileName = "setting.json";
    static string _gameConfigFilePath = Program.ExeDir + _gameConfigFileName;
    const string _userConfigFileName = "setting_user.json";
    static string _userConfigFilePath = Program.ExeDir + _userConfigFileName;

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
                File.WriteAllText(_gameConfigFilePath, defaultJson);
            }

            {
                var json = File.ReadAllText(_gameConfigFilePath);
                JsonNode parsed = JsonNode.Parse(json);
                if (parsed is not JsonObject gameJson)
                    throw new JsonException("setting.json must contain a JSON object");

                // [Emuera改修:MEM-13R40 2026-08-23]
                // 既存setting.jsonへblockを追加する際、既知propertyだけを再Serializeすると将来の未知propertyを失う。
                // JsonObjectへ必要なblockだけを追加して元のpropertyを保持し、壊れたJSONは従来どおり例外として扱う。
                if (!gameJson.TryGetPropertyValue("LazyErb", out JsonNode lazyErbNode) || lazyErbNode is null)
                {
                    gameJson["LazyErb"] = JsonSerializer.SerializeToNode(new JSONLazyErbConfigData(), _jsonOptions);
                    File.WriteAllText(_gameConfigFilePath, gameJson.ToJsonString(_jsonOptions));
                }
                _gameJson = gameJson;

                Game = JsonSerializer.Deserialize<JSONGameConfigData>(gameJson.ToJsonString(_jsonOptions));
                Game.LazyErb ??= new JSONLazyErbConfigData();
                Game.LazyErb.Enabled ??= true;
                Game.LazyErb.Directories ??= ["口上/口上まとめ"];
            }
        }

        {
            if (!File.Exists(_userConfigFilePath))
            {
                var defaultData = new JSONUserConfigData();
                var defaultJson = JsonSerializer.Serialize(defaultData, _jsonOptions);
                File.WriteAllText(_userConfigFilePath, defaultJson);
            }

            {
                var json = File.ReadAllText(_userConfigFilePath);

                User = JsonSerializer.Deserialize<JSONUserConfigData>(json);
            }
        }

        SetSamplingOptions();
    }

    public static void Save()
    {
        {
            // [Emuera改修:MEM-13R40 2026-08-23]
            // Save後もmigration時に残した未知propertyを維持し、setting.jsonの将来拡張を破壊しない。
            JsonObject gameJson = _gameJson ?? JsonSerializer.SerializeToNode(Game, _jsonOptions).AsObject();
            JsonObject knownJson = JsonSerializer.SerializeToNode(Game, _jsonOptions).AsObject();
            foreach (KeyValuePair<string, JsonNode> property in knownJson)
                gameJson[property.Key] = property.Value?.DeepClone();
            File.WriteAllText(_gameConfigFilePath, gameJson.ToJsonString(_jsonOptions));
        }
        {
            var json = JsonSerializer.Serialize(User, _jsonOptions);
            File.WriteAllText(_userConfigFilePath, json);
        }
    }
}
