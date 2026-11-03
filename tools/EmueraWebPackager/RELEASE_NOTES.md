# Emuera Web Packager 1.0.12 — Release Notes

1.0.12は、文字と位置指定DIVを同じボタンに入れた表示が行の左端へ飛ぶ不具合を修正します。1.0.11の機能（PNGのWebP圧縮、`.br`／`.gz`を出力しないオプション）と既存の保存保護を維持します。

## 修正: ボタン内の位置指定DIV

- `<button>`の中に普通の文字と`<div xpos=…>`を一緒に入れると、ボタン全体が行の左端へ移動していました。デスクトップ版のEmueraでは起きない、ブラウザ版だけの不具合です。
- ShinEraの2Dダンジョンでマップクリック移動をONにすると、自キャラのマス（文字＋回転する＠画像）が画面の左端へ飛び、＠が見えなくなり、その行のマスが1つずつ左に詰まっていました（壁の中にいるように見える）。
- 位置指定DIVだけでできたボタンは、従来どおり行の左端を基準に配置します。

## ビルド条件と出自

- Packager 1.0.12／Runtime UX16-08-RC3-Compat111。Windows／browser-wasm runtime 10.0.12、SkiaSharp 4.150.1を維持。
- RuntimeのAOTビルドは.NET SDK 10.0.303／wasm-tools workload 10.0.11（Emscripten 3.1.56）で行いました。1.0.11までのSDK 10.0.112とは異なります。
- Runtimeソースcommit: `{{SOURCE_COMMIT}}`
- Runtimeソース一覧SHA256: `{{SOURCE_TREE_SHA256}}`
- Packager版・資料commit: `{{PACKAGER_COMMIT}}`
- Packagerソース一覧SHA256: `{{PACKAGER_SOURCE_TREE_SHA256}}`

## 利用

EmueraWebPackager-1.0.12-win-x64.zipとSHA256SUMS.txtを照合し、新規フォルダへ展開してください。実EXEでクリーンな配布用ゲーム入力と新しい出力先を指定します。CLIは `--package <game-folder> <new-output-folder> [--png-webp] [--no-precompressed]` です。ツールZIPとゲーム入りitch ZIPは別物です。itchへのアップロードは利用者が行います。技術検証はゲーム素材の再配布許諾を保証しません。

## 既知の制約

1.0.11の既知制約は変わりません。その他の既知制約は同梱READMEと使い方を参照してください。
