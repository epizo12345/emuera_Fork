# 今回の修正説明 — RESIZABLE可変長配列

## 背景

固定長の配列で「今の件数」を別の変数に持つ書き方や、`/`区切りの文字列を`SPLIT`し直す書き方は、処理の多いところ（戦闘効果の一覧など）で遅く、書き間違いも起きやすい。既存の配列と同じ速さで読み書きでき、実行中に長さを変えられる配列を追加した。

設計・方式の比較・計測の詳細は`プロジェクト資料/調査履歴/10_RESIZABLE可変長配列_設計.md`。

## 仕様

- `#DIM RESIZABLE 名前, 初期サイズ`／`#DIMS RESIZABLE`／`#DIM CHARADATA RESIZABLE`。1次元のみ。初期サイズ0も可。広域・関数内（`DYNAMIC`／`STATIC`）・`CHARADATA`で使える。
- 範囲外の要素への書き込みで自動的に伸びる（上限1,000,000要素）。範囲外の読み取りは従来どおりエラー。縮むのは`RESIZE`だけ。
- `RESIZE 変数, 新しい長さ`（`RESIZE(変数, 長さ)`も可）… 伸ばすのも縮めるのも可。戻り値は変更前の長さ（命令の形では`RESULT`）。
- `ARRAYSIZE(変数)` … 今の長さ。`RESIZABLE`でない1次元配列は宣言時のサイズ。
- `CHARADATA`は`変数:キャラ:0`でキャラを指定する（添字1つは`TARGET`の要素）。
- `SPLIT`／`HTML_TAGSPLIT`の出力先にすると分割数に合わせて伸びる（縮めない）。
- `SAVEDATA`・`GLOBAL`と併用でき、長さも保存される（バイナリ形式・テキスト形式とも。`CHARADATA`の`SAVEDATA`は従来どおりバイナリ形式が必要）。
- `#DIM REF RESIZABLE 名前`の引数でRESIZABLE変数を参照渡しで受け取れる。関数の中で伸び縮みすると呼び出し元も同じになる。普通の`#DIM REF`にRESIZABLE変数を渡す、`REF RESIZABLE`に固定長を渡すとエラー。
- `CONST`・2次元以上とは併用できない。

## 実装

- `[Emuera改修:ARRAY-01 2026-10-28]` 本体。`VariableToken.Resizable.cs`に容量を倍々で確保する`ResizableBuffer<T>`と専用トークン6種（整数・文字列 × 広域と関数内STATIC・関数内DYNAMIC・CHARADATA）。配列関数に渡すときだけ今の長さへ切り詰めるので、既存の配列関数は書き換えずに動く。既存の固定長トークンには手を入れていない。`ResizableArray.cs`に`RESIZE`／`ARRAYSIZE`。
- `[Emuera改修:ARRAY-02 2026-10-28]` 高速化。`FINDELEMENT`／`FINDLASTELEMENT`／整数の`MATCH`は切り詰めずに内部配列を読む。`ARRAYSIZE`／`RESIZE`はキャラ番号だけを評価する。
- `[Emuera改修:ARRAY-03 2026-10-28]` `SAVEDATA`／`GLOBAL`との併用（`EraBinaryDataReader`に保存されていた長さで読み込み先を返す引数、テキスト形式は`EraDataWriter.WriteExtended`の`keepLength`で末尾の0／空文字も書く）と、`REF RESIZABLE`（`ResizableRef1DToken<T>`。配列ではなく入れ物を持つ）。
- `[Emuera改修:ARRAY-04 2026-10-28]` `RESIZE`の戻り値を、渡した新しい長さから変更前の長さに変更。

## 効果

ERBでの計測（100万回あたり、修正前EXEとの比較）:

- 既存の固定長配列の読み書きは変化なし。RESIZABLEの読み書きは固定長と同等。
- 末尾への追加は固定長＋件数変数より速い。`CHARADATA`への追加は`/`区切り文字列方式の約1.4倍速。
- 追加と`FINDELEMENT`／`MATCH`の交互は、ARRAY-02で約2.2倍速くなった。`ARRAYSIZE`／`RESIZE`は約1.6倍。
- `REF RESIZABLE`経由の読み書き・呼び出しは普通の`REF`と同等。普通の`REF`は修正前と変わらない。
- 現行ゲームのTRYSKILLを書き換えた比較で、戦闘効果10件でステータス計算が約14%、30件で約35%速くなった。

## 検証

`Emuera以外(開発補助ツールなど)/ConfigRegressionTests/ResizableArray/Run-ResizableFixtures.ps1`のERBフィクスチャ9件（起動試験モードで実行）:

- main … 自動伸長、`RESIZE`／`ARRAYSIZE`（戻り値を含む）、配列関数、`CHARADATA`（`COPYCHARA`・`DELCHARA`・`SORTCHARA`）、関数内DYNAMIC／STATIC、`REF RESIZABLE`（再帰・引数のさらなる受け渡し・`#FUNCTION`）
- decl-errors／decl-errors-erb … `CONST`・2次元との組み合わせエラー
- resize-fixed／read-oor … 固定長への`RESIZE`、範囲外読み取りのエラー
- refarg-error／refarg-fixed-error … 参照渡しの型違いのエラー
- save-binary／save-text … セーブ→長さを変える→ロードで長さと中身が戻る（末尾が0／空文字、長さ0を含む）。`RESETDATA`後も`REF RESIZABLE`が同じ変数を指す

修正前のEXEで再現を確認したもの: テキスト形式で末尾が省かれて長さが失われる不具合（save-textで4件FAIL）、`RESIZE`の戻り値（mainで3件FAIL）。

回帰試験は96/96 PASS。`SAVEVAR`／`SAVECHARA`／`LOADCHARA`は同じ読み込み処理を通るが、動かして確認はしていない。

## 採用状態

2026-10-28に正式ソースへ採用（ブランチ`epizo/可変長配列`をマージcommit `a680ca1`でmainへ取り込み、ARRAY-04はmainで追加）。EXE生成元は`1047dfbaf3b70bdaccb0d6ea34298031133602d1`（配布資料の更新commitとは別）。正式EXEは24,721,130 bytes、SHA-256 `FD13999D391AEA6128B741451D933F821ED1B52D65F6745251CA8D28B7C90637`、ProductVersion `0.2.6.0+1047dfbaf3b70bdaccb0d6ea34298031133602d1`、FileVersion `0.2.6.0`。source revision検証PASS、fixtureゲームで起動安定性テスト3/3（失敗・Lv2警告0）、RESIZABLEのERBフィクスチャ9/9 PASS。
