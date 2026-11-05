# 今回の修正説明 — ARRAYSORTLOCALE（言語別の照合で配列を並べる）

## 背景

ゲーム中で文字列配列を50音順に並べたいという要望。既存の`ARRAYSORT`は不変カルチャ（ICUのroot照合）で比べるため、かなはほぼ50音順になるが、漢字は文字コード順になり、ひらがなとカタカナが同順位のときの並びも決まらない。

最初は日本語（ja-JP）固定の`ARRAYSORTKANA`として追加し、source commit `db3ba10`の正式EXEで配布した。その後、中国語・韓国語のゲームでは日本語の並びになって困ることが分かった。ICUは中国語のピンイン順・画数順、韓国語の読み順のデータも持っているため、照合の言語を指定できる`ARRAYSORTLOCALE`に置き換えた。`ARRAYSORTKANA`は実際のゲームで使われる前だったため、互換用に残さず削除した。

## 仕様

- `ARRAYSORTLOCALE 変数, 言語名 {, FORWARD/BACK {, 開始, 個数}}`。言語名は省略できない文字列式（例: `"ja-JP"`、`"zh-CN"`、`"zh-TW"`、`"ko-KR"`）。変数で渡してもよい。
- 言語名より後の引数と範囲の扱いは`ARRAYSORT`と同じ。エラー文言の引数番号は言語名の分だけ1つずれる（開始が第4引数、個数が第5引数）。
- 文字列は`CultureInfo.GetCultureInfo(言語名, predefinedOnly: true).CompareInfo`で`IgnoreKanaType | IgnoreWidth`を指定して比べる。ひらがな／カタカナ、全角／半角の違いは無視し、同順位は序数比較で並びを固定する。
- 言語別の並び（ICUのデータ）:
  - `ja-JP`: かなは50音順。漢字はJIS X 0208第1水準の並び（その字の代表的な音読み順）で、名前の読みではない（例: 高橋＝こう、山田＝さん、鈴木＝れい）
  - `zh-CN`: ピンイン順（安 北 李 上 中）
  - `zh-TW`: 画数順（上 中 北 安 李）
  - `ko-KR`: 漢字もハングルの読みの位置に並ぶ（가나 金 서울 中国 한국）
- 日本語で名前を読み順に並べるには、読み仮名の配列をキーに`ARRAYMSORT`を使う（キー配列に空文字があると何もしない既存仕様に注意。RESIZABLEなら今の長さだけが対象）。
- 使えない言語名は実行時に「ARRAYSORTLOCALEの第2引数 "…" は使用できる言語名ではありません」のエラー。言語名を書かないとERB読込時のエラー。
- 空文字は昇順で先頭に来る（`ARRAYSORT`と同じ）。整数配列では`ARRAYSORT`と同じ動作（言語名は確認だけ行う）。
- 照合の細部はWindowsのICUのバージョンに依存しうる。

## 実装

`[Emuera改修:ARRAY-05 2026-11-05]`として次のファイルを修正した。

- `Runtime/Script/Statements/BuiltInFunctionCode.cs`: `FunctionCode.ARRAYSORTLOCALE`（`ARRAYSORTKANA`の位置をそのまま改名）。
- `Runtime/Script/Statements/FunctionArgType.cs`／`ArgumentBuilder.cs`／`Argument.cs`: `SP_SORTARRAY_LOCALE`を追加。`ARRAYSORT`の引数解析を共用し、言語名ありのときだけ第2引数に文字列式を読み、以降の警告の引数番号を1つずらす。`SpArraySortArgument.Locale`に言語名の式を持つ。
- `Runtime/Script/Statements/FunctionIdentifier.cs`: `ARRAYSORTLOCALE`を`SP_SORTARRAY_LOCALE`で登録。
- `Runtime/Script/Process.ScriptProc.cs`: `ARRAYSORT`と同じcaseで処理し、言語名から得た`CompareInfo`を`SortArray`へ渡す。エラー文言に実際の命令名と正しい引数番号を出す（既存の誤字「ARRAYRESORT」と、第4引数エラーで第3引数の値を表示していた誤りも直した）。
- `Runtime/Script/Statements/Variable/VariableEvaluator.cs`: `GetSortCompareInfo`（言語名の検査）と、`SortArray`の`CompareInfo`引数。

## 検証

`Emuera以外(開発補助ツールなど)/ConfigRegressionTests/ArraySortLocale/Run-SortLocaleFixtures.ps1 -Exe <Emuera.exe>`のERBフィクスチャ（起動試験モード）:

- main（13項目）: ja-JPのかな昇順・降順、範囲指定、漢字のJIS順、空文字とかなと漢字の順、引数省略、整数配列、既存`ARRAYSORT`が不変、RESIZABLE配列、zh-CNのピンイン順、zh-TWの画数順、ko-KR、変数で渡した言語名とBACK
- range-error: 範囲外指定で「命令ARRAYSORTLOCALEの第4引数 1 と第5引数の和 5…」のエラー
- bad-locale: 使えない言語名で実行時エラー
- missing-locale: 言語名なしで読込時エラー

結果 4/4 PASS（mainは13/13）。RESIZABLEのフィクスチャ9/9 PASS、回帰試験96/96 PASS（いずれも開発ビルド）。

実ゲームERBからの使用、キャラクタ変数（CHARADATA）の文字列配列は確認していない（後者は`ARRAYSORT`と共通の経路）。

## 採用状態

`ARRAYSORTKANA`は2026-11-05にsource commit `db3ba10b4c6258533b3855b9fe4d6fdf9f30539e`で採用し、正式EXE（24,725,226 bytes、SHA-256 `93881DC4C3C77BA9E3BE4AD6C5F136BC6E1E9C779828872A0DE0D352F8AACD5A`）で配布した。続くBINPUT版の正式EXE（生成元`75ec2b4`）にも含まれる。

`ARRAYSORTLOCALE`への置き換えは未配布（正式EXEは未再生成）。
