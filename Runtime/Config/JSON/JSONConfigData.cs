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

    public bool CheckUTF8withBOM { get; set; } = true;

    public FontAntialias FontAntialias { get; set; } = FontAntialias.Normal;
}

sealed class JSONUserConfigData
{
    public int[] WatchListWidth { get; set; } = [-2, -2];

    public bool CBUseClipboard { get; private set; } = true;
    public bool CBIgnoreTags { get; private set; } = true;
    public string CBReplaceTags { get; private set; } = ".";
    public bool CBNewLinesOnly { get; private set; } = true;
    public bool CBClearBuffer { get; private set; } = false;
    public bool CBTriggerLeftClick { get; private set; } = true;
    public bool CBTriggerMiddleClick { get; private set; } = false;
    public bool CBTriggerDoubleLeftClick { get; private set; } = false;
    public bool CBTriggerAnyKeyWait { get; private set; } = false;
    public bool CBTriggerInputWait { get; private set; } = true;
    public int CBMaxCB { get; private set; } = 25;
    public int CBBufferSize { get; private set; } = 300;
    public int CBScrollCount { get; private set; } = 5;
    public int CBMinTimer { get; private set; } = 800;

}