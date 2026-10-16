# Emuera Web Packager 1.0.10 — Release Notes

1.0.10は、ローカルフォントに依存していた表示を修正します。1.0.9の機能（GETKEY修正を含む）と既存の保存保護を維持します。

## フォント代替

- 「ＭＳ ゴシック」「MS Gothic」が端末にない、または使えない環境で、ブラウザ既定のプロポーショナル体へ置き換わり文字幅・レイアウトが崩れていました。Mac／Linux／スマートフォン、Braveのフィンガープリント対策（Shields）などが該当します。
- 等幅のBIZ UDゴシック（SIL Open Font License 1.1、woff2 約2.2MB）を同梱し、上記フォント名の代替に割り当てます。端末にMS Gothicがあればそちらを優先します。
- 文字幅計測の前にフォント読込を待つため、MS Gothicのない環境では初回起動時にフォントのダウンロード分だけ時間がかかります。
- ＭＳ ゴシック以外のフォント名を指定するゲームは対象外です（従来どおりローカルフォントへ依存します）。

## ビルド条件と出自

- Packager 1.0.10／Runtime UX16-08-RC3-Compat110。SDK 10.0.112、Windows／browser-wasm runtime 10.0.12、SkiaSharp 4.150.1を維持。
- Runtimeソースcommit: `{{SOURCE_COMMIT}}`
- Runtimeソース一覧SHA256: `{{SOURCE_TREE_SHA256}}`
- Packager版・資料commit: `{{PACKAGER_COMMIT}}`
- Packagerソース一覧SHA256: `{{PACKAGER_SOURCE_TREE_SHA256}}`
- 今回の変更は静的資材（app.css、global-persistence.js、フォント）のみです。C#ソースは1.0.9と同一のため、Wasm／framework資材は1.0.9のAOT出力（Release／RunAOTCompilation=true／WasmStripILAfterAOT=false／EnableErbExecutionProfiler=false）を再利用しています。

## 利用

EmueraWebPackager-1.0.10-win-x64.zipとSHA256SUMS.txtを照合し、新規フォルダへ展開してください。実EXEでクリーンな配布用ゲーム入力と新しい出力先を指定します。CLIは `--package <game-folder> <new-output-folder>` です。ツールZIPとゲーム入りitch ZIPは別物です。itchへのアップロードは利用者が行います。技術検証はゲーム素材の再配布許諾を保証しません。

## 既知の制約

1.0.9の既知制約（GETKEYTRIGGERED、ブラウザ予約キー、X1サイドボタンのBack／Forward競合、VK6の物理キー、全ブラウザ／IME保証）は変わりません。BIZ UDゴシックはMS Gothicと字形が異なります。その他の既知制約は同梱READMEと使い方を参照してください。
