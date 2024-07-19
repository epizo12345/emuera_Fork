//新設したコンフィグ設定のロード、セーブ、公開を担当する。
using MinorShift.Emuera;
using SkiaSharp;
using System;
using System.IO;
using System.Text.Json;


namespace MinorShift.Emuera.Runtime.Config.JSON;
static class JSONConfig
{
    public static JSONGameConfigData Game;
    public static JSONUserConfigData User;

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
                var defaultJson = JsonSerializer.Serialize(defaultData);
                File.WriteAllText(_gameConfigFilePath, defaultJson);
            }

            {
                var json = File.ReadAllText(_gameConfigFilePath);

                Game = JsonSerializer.Deserialize<JSONGameConfigData>(json);
            }
        }

        {
            if (!File.Exists(_userConfigFilePath))
            {
                var defaultData = new JSONUserConfigData();
                var defaultJson = JsonSerializer.Serialize(defaultData);
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
            var json = JsonSerializer.Serialize(Game);
            File.WriteAllText(_gameConfigFilePath, json);
        }
        {
            var json = JsonSerializer.Serialize(User);
            File.WriteAllText(_userConfigFilePath, json);
        }
    }
}