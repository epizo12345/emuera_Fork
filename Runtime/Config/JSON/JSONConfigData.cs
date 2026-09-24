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
    // [Emuera改修:LAZY-01]
    // この値はsetting.jsonに記録するゲーム作者の推奨設定で、プレイヤー上書きは別ファイルで管理する。
    // [Emuera改修:MEM-13R40.3 2026-08-23]
    // ユーザー向けJSONは日本語名で、DataDir相対のERB/配下を指定する。内部型名は既存コードとの互換性のため維持する。
    // 「起動時に読み込まない」はERB本文の解析/hydrationを必要時まで遅らせる意味で、存在・関数metadata等の確認まで省略する設定ではない。
    [JsonPropertyName("起動時に読み込まないERBフォルダ")]
    public JSONLazyErbConfigData LazyErb { get; set; } = new() { Enabled = false, Directories = [] };

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

sealed class JSONLazyErbConfigData
{
    // [Emuera改修:MEM-13R41B2 2026-08-23]
    // イベントはSHOPトップ・一覧表示に必要な入口をfallbackで維持しつつ、本編の大部分を初回利用まで遅延できることを実ゲームで確認したため既定対象へ加える。
    // ユーザーが明示したフォルダ設定は設定値として尊重し、起動時に自動追加しない。
    [JsonPropertyName("有効")]
    public bool? Enabled { get; set; } = true;
    [JsonPropertyName("フォルダ")]
    public string[] Directories { get; set; } = ["ERB/口上/口上まとめ", "ERB/RPG/依頼", "ERB/RPG/イベント"];
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
