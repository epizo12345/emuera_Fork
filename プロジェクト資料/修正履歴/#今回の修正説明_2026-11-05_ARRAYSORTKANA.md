# 今回の修正説明 — ARRAYSORTKANA（50音順の配列ソート）

## 背景

ゲーム中で文字列配列を50音順に並べたいという要望。既存の`ARRAYSORT`は不変カルチャ（ICUのroot照合）で比べるため、かなはほぼ50音順になるが、漢字は文字コード順になり、ひらがなとカタカナが同順位のときの並びも決まらない。

## 仕様

- `ARRAYSORTKANA 変数 {, FORWARD/BACK {, 開始, 個数}}`。引数と範囲の扱い、エラー条件は`ARRAYSORT`と同じ。
- 文字列は`CultureInfo.GetCultureInfo("ja-JP").CompareInfo`（ICUのja照合）で`IgnoreKanaType | IgnoreWidth`を指定して比べる。ひらがな／カタカナ、全角／半角の違いは無視し、同順位は序数比較で並びを固定する。
- 漢字はJIS X 0208第1水準の並び（その字の代表的な音読み順）になるだけで、名前の読みではない（例: 高橋＝こう、山田＝さん、鈴木＝れい）。読み順に並べるには読み仮名の配列をキーに`ARRAYMSORT`を使う（キー配列に空文字があると何もしない既存仕様に注意。RESIZABLEなら今の長さだけが対象）。
- 空文字は昇順で先頭に来る（`ARRAYSORT`と同じ）。整数配列では`ARRAYSORT`と同じ動作。
- 照合の細部はICUのバージョンに依存しうる。

## 実装

`[Emuera改修:ARRAY-05 2026-11-05]`として4ファイルを修正した。

- `Runtime/Script/Statements/BuiltInFunctionCode.cs`: `FunctionCode.ARRAYSORTKANA`を末尾に追加（既存の番号は変えない）。
- `Runtime/Script/Statements/FunctionIdentifier.cs`: `ARRAYSORT`と同じ引数解析（`SP_SORTARRAY`）で登録。
- `Runtime/Script/Process.ScriptProc.cs`: `ARRAYSORT`と同じcaseで処理し、`SortArray`へ`kana`フラグを渡す。エラー文言に実際の命令名を出すようにし、既存の誤字「ARRAYRESORT」と、第4引数が負のときに第3引数の値を表示していた誤りも直した。
- `Runtime/Script/Statements/Variable/VariableEvaluator.cs`: `SortArray`に`kana`引数と比較関数`CompareKana`を追加。`CompareInfo`は初回使用時に取得し、`VariableEvaluator`の静的初期化に巻き込まない。

## 検証

`Emuera以外(開発補助ツールなど)/ConfigRegressionTests/ArraySortKana/Run-SortKanaFixtures.ps1 -Exe <Emuera.exe>`のERBフィクスチャ（起動試験モード）:

- main（9項目）: かなの昇順・降順、範囲指定、漢字のJIS順、空文字とかなと漢字の順、引数省略、整数配列、既存`ARRAYSORT`が不変、RESIZABLE配列
- range-error: 範囲外指定で「命令ARRAYSORTKANAの第3引数…」のエラーになる

結果 2/2 PASS。RESIZABLEのフィクスチャ9/9 PASS、回帰試験96/96 PASS。正式EXEでsource revision検証PASS、fixtureゲームで起動安定性テスト3/3（失敗・Lv2警告0）。

実ゲームERBからの使用、キャラクタ変数（CHARADATA）の文字列配列は確認していない（後者は`ARRAYSORT`と共通の経路）。

## 採用状態

2026-11-05に正式ソースへ採用（mainへ直接commit）。EXE生成元は`db3ba10b4c6258533b3855b9fe4d6fdf9f30539e`（配布資料の更新commit `00b2443`とは別）。正式EXEは24,725,226 bytes、SHA-256 `93881DC4C3C77BA9E3BE4AD6C5F136BC6E1E9C779828872A0DE0D352F8AACD5A`、ProductVersion `0.2.6.0+db3ba10b4c6258533b3855b9fe4d6fdf9f30539e`。
