
using System.Text.Json.Serialization;

namespace DotnetEmuera;
//JSONの定義
class JSONConfigData
{
    //ボタンにカーソルを合わせたときに背景色を変更するか
    [JsonPropertyName("UseButtonFocusBackgroundColor")]
    public bool UseButtonFocusBackgroundColor { get; set; }

    [JsonPropertyName("IgnoreRandamizeSeed")]
    public bool IgnoreRandamizeSeed { get; set; }
}