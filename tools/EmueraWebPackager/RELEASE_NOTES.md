# Emuera Web Packager 1.0.13 — Release Notes

1.0.13は、新しい命令`ARRAYSORTLOCALE`（言語別の文字列配列ソート）に対応します。1.0.12の修正と1.0.11の機能（PNGのWebP圧縮、`.br`／`.gz`を出力しないオプション）、既存の保存保護を維持します。

## 対応: ARRAYSORTLOCALE

- `ARRAYSORTLOCALE 配列, 言語名, FORWARD|BACK, 開始, 個数`をMAIN版から移植しました。eraMegaten／eraTenseiの新しい版がキャラ名の並び替えに使っており、1.0.12以前のRuntimeでは読み込み時にエラーになっていました。
- `ja-JP`は、かなを50音順に並べ、ひらがな／カタカナと全角／半角の違いを無視します。`ko-KR`も含め、デスクトップ版と同じ順になります。
- ブラウザの言語設定には左右されません。Runtimeは全言語のICUデータ（約1.5MB）を読み込みます。
- 範囲外、使えない言語名、言語名の指定なしは、デスクトップ版と同じエラーメッセージになります。

## ビルド条件と出自

- Packager 1.0.13／Runtime UX16-08-RC3-Compat112。Windows／browser-wasm runtime 10.0.12、SkiaSharp 4.150.1を維持。
- RuntimeのAOTビルドは.NET SDK 10.0.303／wasm-tools workload 10.0.11（Emscripten 3.1.56）で行いました。
- Runtimeソースcommit: `{{SOURCE_COMMIT}}`
- Runtimeソース一覧SHA256: `{{SOURCE_TREE_SHA256}}`
- Packager版・資料commit: `{{PACKAGER_COMMIT}}`
- Packagerソース一覧SHA256: `{{PACKAGER_SOURCE_TREE_SHA256}}`

## 利用

EmueraWebPackager-1.0.13-win-x64.zipとSHA256SUMS.txtを照合し、新規フォルダへ展開してください。実EXEでクリーンな配布用ゲーム入力と新しい出力先を指定します。CLIは `--package <game-folder> <new-output-folder> [--png-webp] [--no-precompressed]` です。ツールZIPとゲーム入りitch ZIPは別物です。itchへのアップロードは利用者が行います。技術検証はゲーム素材の再配布許諾を保証しません。

## 既知の制約

- `ARRAYSORTLOCALE`で`zh-CN`（ピンイン順）／`zh-TW`（画数順）など中国語の言語名を使うと、エラーにならず文字コード順になります。ブラウザ版.NETのICUデータに中国語の並び規則がないためです。デスクトップ版では正しく並びます。
- `#DIMS RESIZABLE`（可変長配列）はWeb版に未対応です。
- その他は1.0.12から変わりません。同梱READMEと使い方を参照してください。
