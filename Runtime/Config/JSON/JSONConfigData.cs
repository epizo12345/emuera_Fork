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

    public bool UseRenameInCharaCSV { get; set; } = false;
}

sealed class JSONUserConfigData
{
    public int[] WatchListWidth { get; set; } = [-2, -2];

    // [Emuera改修:MOUSE-01]
    // マウスの「戻る」「進む」ボタンで、現在の選択肢からこの文言を含むボタンを押す。
    // 例: "前のページ|1007|PREV" は、どれかの文言が見つかればよいという意味。
    // 複数候補は | で区切る。空文字にすると無効。ゲーム側ERBの変更は不要。
    // 参照: プロジェクト資料/06_コード案内.md
    public string MouseXButton1ButtonText { get; set; } = "前のページ|前ページ|1007|PREV";
    public string MouseXButton2ButtonText { get; set; } = "次のページ|次ページ|後ろのページ|1009|NEXT";

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
