using System.Text.Json.Serialization;

namespace MinorShift.Emuera.Runtime.Config.JSON;
//JSONの定義
enum Resampler
{
    NearnestNeighber,
    Linear,
    Cubic,
}

enum FontAntialias
{
    None,
    Normal,
    Full,
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

    public bool CheckUTF8withBOM { get; set; } = false;

    public FontAntialias FontAntialias { get; set; } = FontAntialias.Normal;

    public bool UseRenameInCharaCSV = false;
}

sealed class JSONUserConfigData
{
    public int[] WatchListWidth { get; set; } = [-2, -2];

    public bool CBUseClipboard { get; set; }
    public bool CBIgnoreTags { get; set; } = true;
    public string CBReplaceTags { get; set; } = ".";
    public bool CBNewLinesOnly { get; set; } = true;
    public bool CBClearBuffer { get; set; }
    public bool CBTriggerLeftClick { get; set; } = true;
    public bool CBTriggerMiddleClick { get; set; }
    public bool CBTriggerDoubleLeftClick { get; set; }
    public bool CBTriggerAnyKeyWait { get; set; }
    public bool CBTriggerInputWait { get; set; } = true;
    public int CBMaxCB { get; set; } = 25;
    public int CBBufferSize { get; set; } = 300;
    public int CBScrollCount { get; set; } = 5;
    public int CBMinTimer { get; set; } = 800;

}