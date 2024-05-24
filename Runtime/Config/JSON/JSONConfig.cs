//新設したコンフィグ設定のロード、セーブ、公開を担当する。
using MinorShift.Emuera;
using SkiaSharp;
using System;
using System.IO;
using System.Text.Json;


namespace MinorShift.Emuera.Runtime.Config.JSON;
static class JSONConfig
{
    public static JSONConfigData Data;

    const string _configFileName = "setting.json";
    static string _configFilePath = Program.ExeDir + _configFileName;

    public static SKSamplingOptions SamplingOptions { get; private set; }
    public static void SetSamplingOptions()
    {
        SamplingOptions = Data.SamplingOption switch
        {
            Resampler.NearnestNeighber => new SKSamplingOptions(SKFilterMode.Nearest),
            Resampler.Linear => new SKSamplingOptions(SKFilterMode.Linear),
            Resampler.Cubic => new SKSamplingOptions(SKCubicResampler.CatmullRom),
            _ => throw new Exception(),
        };
    }

    public static void Load()
    {
        if (!File.Exists(_configFilePath))
        {
            var defaultData = new JSONConfigData();
            var defaultJson = JsonSerializer.Serialize(defaultData);
            File.WriteAllText(_configFilePath, defaultJson);
        }

        var json = File.ReadAllText(_configFilePath);

        Data = JsonSerializer.Deserialize<JSONConfigData>(json);

        SetSamplingOptions();
    }

    public static void Save()
    {
        var json = JsonSerializer.Serialize(Data);
        File.WriteAllText(_configFilePath, json);
    }
}