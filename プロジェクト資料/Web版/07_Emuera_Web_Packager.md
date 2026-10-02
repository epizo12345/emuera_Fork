# Emuera Web Packager

現行Packagerは1.0.2、同梱Runtime templateはUX16-08-RC3です。Windows Packagerとブラウザの実行資材は.NET 10.0.12です。Packagerのソースは`tools/EmueraWebPackager/`にあります。配布ZIPは準備済みで、GitHub Releaseの公開は保留しています。Webのビルド条件はRelease、`RunAOTCompilation=true`、`WasmStripILAfterAOT=false`、`EnableErbExecutionProfiler=false`です。IL保持はAOTの無効化ではなく、IL削除版とのサイズ・速度差は未測定です。

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
