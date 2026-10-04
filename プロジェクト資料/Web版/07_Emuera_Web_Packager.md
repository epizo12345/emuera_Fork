# Emuera Web Packager

現行Packagerは1.0.6、同梱Runtime templateはUX16-08-RC3-Compat106です。1.0.6はローカル配布候補であり、公開は別工程です。AWAITの非入力待機とGDRAWGの通常／色補正画像コピー・SpriteG更新を追加します。AWAITの受入は逆引き合体のみ、GDRAWGの具体的ユーザー操作経路は未指定です。GDRAWGWITHMASK等の未実装と添字なしCMの共有例外は残ります。全命令対応・速度改善は主張しません。正式配布物は採用commitから生成し、出自情報を同梱します。DIV／Islandのnowrap、入れ子absoluteの画面基準配置、論理描画領域1512×864、Native互換HTML_TAGSPLITを追加しています。表示・神格習合の戻る・NGO表示復帰はユーザー確認済みです。NGO上端約2行の見切れは許容制約、実ゲームの後続進行は未確認です。連続PRINTC系の物理行配置、説明付きnonbutton／Islandのtooltip、QUIT後の正常終了案内とタイトル復帰を修正し、該当4実ゲームケースはユーザー受入済みです。マクロ入力・スキップ互換性と上部停止UIを含み、通常操作の待機・保存保護を維持します。公開済み1.0.4および旧版の配布物とタグは保持します。Windows Packagerとブラウザの実行資材は.NET 10.0.12です。Packagerのソースは`tools/EmueraWebPackager/`にあります。[1.0.5 Release](https://github.com/epizo12345/emuera_Fork/releases/tag/web-packager-v1.0.5)ではゲームなしのツールZIPとチェックサムを配布します。Webのビルド条件はRelease、`RunAOTCompilation=true`、`WasmStripILAfterAOT=false`、`EnableErbExecutionProfiler=false`です。IL保持はAOTの無効化ではなく、IL削除版とのサイズ・速度差は未測定です。

## 利用者の操作

1. ゲームデータのあるフォルダを選ぶ。
2. Web出力先を選ぶ。
3. 必要ならitch.io用ZIP作成を選び、Web版を作成する。

入力フォルダは読取りだけです。出力には現在選択したデータのsnapshotを保存し、次回の変更・追加・削除を表示します。`.sav`を配布Web packageへ含めません。

## 開発者向け構成

- `Core/Packager.cs`: root解決、snapshot/hash、resource参照、差分、archive生成/検証、safe stagingとatomic promotion。
- `Core/PreviewServer.cs`: 自身が開いたloopback listenerだけを停止できるin-process preview。
- `Gui/MainForm.cs`: folder選択、進捗表示、package作成、preview/stop UI。
- `Tests/`: snapshot、データ差分、template seal、package、safety contract。
- `Gui.Tests/`: GUIの非表示描画とbuildイベント確認。

出力を作る前に入力snapshotと`.template-manifest.json`を検証し、別の`.building-*`へ生成してarchive bytesを再検証します。最後に出力を昇格します。既存の未知出力は上書きせず、置換時の前版は`.previous-*`へ保持します。itch ZIPはWeb出力のroot内容とentryごとのbytesを比較します。Archiveの非圧縮入力上限はpack単位128 MiBです。

詳しいパッケージ境界は[仕様](03_ゲームデータパッケージ仕様.md)、日常の操作は[利用者向け説明](../../tools/EmueraWebPackager/使い方.md)を参照してください。

