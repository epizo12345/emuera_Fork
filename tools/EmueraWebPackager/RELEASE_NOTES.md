# Emuera Web Packager 1.0.9 — Release Notes

1.0.9は、受入済みのGETKEY入力状態修正を新規AOT生成したRuntimeとPackagerへ反映します。1.0.8の機能と既存の保存保護を維持します。

## GETKEY

- 通常のHTML入力欄からの押下状態、左右別のAlt／Shift／Ctrl、マウスボタン状態を参照できます。GETKEY照会は状態を消費しません。
- ゲーム入力欄のDOM更新中は実際の押下状態を保持します。実際のフォーカス喪失、タブ非表示、別の編集欄への移動、IME開始時は状態を解除します。
- ゲーム入力配送とエンジンのFキー／マクロ操作を区別し、既存のマクロ停止・入力契約を維持します。
- GETKEYTRIGGEREDのlow-bit／toggle契約、ブラウザ予約キー、X1／ブラウザBack競合、VK6の物理操作は今回の受入範囲外です。全ブラウザ・全OS IME・itchでの保証はしません。

## ビルド条件と出自

- Packager 1.0.9／Runtime UX16-08-RC3-Compat109。SDK 10.0.112、Windows／browser-wasm runtime 10.0.12、SkiaSharp 4.150.1を維持。
- Runtimeソースcommit: `{{SOURCE_COMMIT}}`
- Runtimeソース一覧SHA256: `{{SOURCE_TREE_SHA256}}`
- Packager版・資料commit: `{{PACKAGER_COMMIT}}`
- Packagerソース一覧SHA256: `{{PACKAGER_SOURCE_TREE_SHA256}}`
- Release／RunAOTCompilation=true／WasmStripILAfterAOT=false／EnableErbExecutionProfiler=false。
- Runtime AOTはRuntimeソースcommitから生成し、Packagerの版・資料更新commitとは別に出自を記録します。

## 利用

EmueraWebPackager-1.0.9-win-x64.zipとSHA256SUMS.txtを照合し、新規フォルダへ展開してください。実EXEでクリーンな配布用ゲーム入力と新しい出力先を指定します。CLIは `--package <game-folder> <new-output-folder>` です。ツールZIPとゲーム入りitch ZIPは別物です。itchへのアップロードは利用者が行います。技術検証はゲーム素材の再配布許諾を保証しません。

## 既知の制約

GETKEYTRIGGERED、ブラウザが受け取らない予約キー、X1サイドボタンのBack／Forward競合、VK6の物理キー対応、全ブラウザ／IME保証は未解決または未確認です。その他の既知制約は同梱READMEと使い方を参照してください。
