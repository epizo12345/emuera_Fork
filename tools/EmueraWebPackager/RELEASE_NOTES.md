# Emuera Web Packager 1.0.2 — Release Notes

掲載文の準備版です。GitHub Releaseはまだ公開していません。公開を決定した際にRelease説明欄へ貼り付けてください。

- Windows x64向けself-contained single-file Packagerと、Web Runtime UX16-08-RC3のsealed runtime-templateを同梱します。
- Packagerは利用者が選んだゲームフォルダを読み取り、ブラウザ配布用のWeb packageを生成します。
- ShinEra本体、ゲームデータ、セーブデータは含まれません。利用者が使用権を持つゲームフォルダを別途指定してください。
- ZIP全体を展開して利用してください。EXE単体配布ではありません。
- 生成元source commit: `{{SOURCE_COMMIT}}`

## 1.0.2の変更

- 会話枠・SHOP/PARTY表示、履歴中の位置指定部品の更新、スキル名枠、HTML islandを維持。
- 入力・マクロ・IME保護、起動／取込状況表示、取込成功／上書きキャンセル後の通常タイトル復帰を維持。セーブ自動LOADは行いません。
- WARNING1画像を収録できる従来の全画像収集と、アニメ開始時刻修正を維持。
- 小さいPCブラウザ窓へゲーム枠を一様に縮小し、論理座標とクリック位置を維持。
- 配布EXEの `--package <game-folder> <new-output-folder>` でGUIと同じ生成処理を実行できます。
- Webの配布ビルド条件はRelease / RunAOTCompilation=true / WasmStripILAfterAOT=false / EnableErbExecutionProfiler=falseです。IL保持はAOTの無効化ではありません。既定IL-strip後処理の失敗を避ける条件を維持し、IL削除版とのサイズ・速度差は未測定です。
- 解析済みIR・MEMFS低水準move試作は含みません。SHA/root-prefixの割当削減は含みますが、起動時間の改善は未確認です。
- 受入済みバイナリはコンパイル入力のSHA一覧とビルド条件を照合して再利用します。採用commitと元ビルドのsource revisionを区別し、release-bundle.jsonとSOURCE-PROVENANCE.jsonに対応を記録します。新しいcommitの作成によってEXE内のrevisionが更新されたものとは扱いません。
- 公開前監査でSkia／.NET Runtimeの通知を補い、生成WebにもLICENSE.mdとlicenses/を同梱する経路を修正しました。この通知修正時にはRuntime・ゲームpackage・レイアウトを変更していません。
- この.NET 10.0.12版でLOADと操作、スキル名枠、縮小クリック、セーブ書き出し／取込とタイトル復帰、実ドウマン戦のWARNING点滅をユーザーが確認済みです。長時間プレイ、実OS IME、itch公開後の動作は未確認です。全ゲーム・全ブラウザの互換性や起動時間の改善を保証しません。

Windows Packagerのself-contained NETCore／WindowsDesktopとbrowser-wasm実行資材を10.0.12へ更新しました。SDK 10.0.112／wasm-tools 10.0.112を使用し、SkiaSharp等の依存と採用済みレイアウトは維持しています。

## 使い方と注意

1. `EmueraWebPackager-1.0.2-win-x64.zip`と`SHA256SUMS.txt`を取得し、ZIPのSHA-256を確認して全体を展開します。
2. `EmueraWebPackager.exe`を起動し、使用権のあるゲームフォルダと新規出力先を選びます。itch.io用ZIPを必要に応じて作成します。
3. 生成Webをローカルで確認してから、生成したゲーム用ZIPを利用者自身がitch.ioへアップロードします。ツールZIPをゲームとしてアップロードしないでください。

RuntimeのgameId／save namespaceにはShinEra向けの制約があるため、別ゲームは別origin／itchページで配信してください。ゲーム・画像・フォント等の再配布許諾は技術検証とは別に確認してください。ツールZIPにゲーム素材、セーブ、試験ログは含みません。
