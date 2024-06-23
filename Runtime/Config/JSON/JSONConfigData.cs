using System.Text.Json.Serialization;

namespace MinorShift.Emuera.Runtime.Config.JSON;
//JSONの定義
enum Resampler
{
    NearnestNeighber,
    Linear,
    Cubic,
}

sealed class JSONGameConfigData
{
    //ボタンにカーソルを合わせたときに背景色を変更するか
    [JsonPropertyName("UseButtonFocusBackgroundColor")]
    public bool UseButtonFocusBackgroundColor { get; set; }

    [JsonPropertyName("UseNewRandom")]
    public bool UseNewRandom { get; set; }

    public bool UseScopedVariableInstruction { get; set; }
    public Resampler ImageSamplingOption { get; set; } = Resampler.Linear;

    public bool CheckUTF8withBOM { get; set; } = true;
}

sealed class JSONUserConfigData
{
    public int[] WatchListWidth { get; set; } = [-2, -2];
}