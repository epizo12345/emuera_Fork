# Emuera Web Packager 1.0.11 — Release Notes

1.0.11は、package内のPNGをWebPへ圧縮するオプションと、`.br`／`.gz`を出力しないオプションを追加します。1.0.10の機能と既存の保存保護を維持します。

## PNGをWebPに圧縮

- GUIの「PNGをWebPに圧縮（名前は.pngのまま・非可逆）」、またはCLIの`--png-webp`で有効になります。既定は無効で、無効時の出力は1.0.10と同じです。
- package内のPNGを品質90のWebPへ変換します。ERBが`.png`のファイル名を直接指定していても動くよう、ファイル名は変えず中身だけを置き換えます。Runtime（SkiaSharp）は中身で形式を判別します。
- WebPのほうが大きくなる画像、読めない画像は元のまま入れます。入力ゲームフォルダは変更しません。
- ShinEra 0.5.10では、画像388MiB→98MiB、Web出力（展開後）536MB→231MBとなり、itch.ioの500MB制限に収まりました。
- 非可逆圧縮のため、半透明のふちなどで画質がわずかに変わります。配布前にPreviewで確認してください。

## .br/.gzを出力しない

- GUIの「.br/.gzを出力しない（itch.io向け）」、またはCLIの`--no-precompressed`で有効になります。既定は無効です。
- `.br`／`.gz`は、配信サーバーが圧縮版を出し分けるためのファイルです。itch.ioは出し分けをしないため使われません。有効にすると出力とitch ZIPから除きます（約26MB）。ゲームの動作は変わりません。

## ビルド条件と出自

- Packager 1.0.11／Runtime UX16-08-RC3-Compat110。SDK 10.0.112、Windows／browser-wasm runtime 10.0.12、SkiaSharp 4.150.1を維持。
- Runtimeソースcommit: `{{SOURCE_COMMIT}}`
- Runtimeソース一覧SHA256: `{{SOURCE_TREE_SHA256}}`
- Packager版・資料commit: `{{PACKAGER_COMMIT}}`
- Packagerソース一覧SHA256: `{{PACKAGER_SOURCE_TREE_SHA256}}`
- Runtime（C#／Wasm／静的資材）は1.0.10と同一のため、1.0.10のruntime-templateを再利用しています。変更はPackager EXEのみです。

## 利用

EmueraWebPackager-1.0.11-win-x64.zipとSHA256SUMS.txtを照合し、新規フォルダへ展開してください。実EXEでクリーンな配布用ゲーム入力と新しい出力先を指定します。CLIは `--package <game-folder> <new-output-folder> [--png-webp] [--no-precompressed]` です。ツールZIPとゲーム入りitch ZIPは別物です。itchへのアップロードは利用者が行います。技術検証はゲーム素材の再配布許諾を保証しません。

## 既知の制約

1.0.10の既知制約は変わりません。WebP変換後のファイルはWeb出力専用です。その他の既知制約は同梱READMEと使い方を参照してください。
