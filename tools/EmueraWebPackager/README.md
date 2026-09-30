# Emuera Web Packager — 開発者向け

現行ソースはversion 1.0.1です。利用者の操作は[使い方](使い方.md)、package形式は[Web版パッケージ仕様](../../プロジェクト資料/Web版/03_ゲームデータパッケージ仕様.md)を参照してください。

## Release一式

Packagerの配布はGitHub Releasesへruntime-template付きZIPを添付する方針です。EXE単体では起動できないためZIP全体を使います。ShinEra本体やゲームデータは付属せず、利用者が自分のゲームフォルダを別途選択します。現時点では公開前です。

## 構成

- `Core/Packager.cs`: game root検出、入力snapshot、差分、manifest・ZIP生成/検証、安全なstagingとatomic promotion、前版の保持。
- `Core/PreviewServer.cs`: loopback preview server。GUIが所有するlistenerだけを停止します。
- `Gui/MainForm.cs`: Windows Formsの入力、進捗、build、preview操作。
- `Tests/`: Core package、Runtime extractor、入力保護、失敗時の既存出力保護を確認します。
- `Gui.Tests/`: GUIを表示せず、画面項目とbuildイベントを確認します。

## package作成の境界

1. 選択されたgame rootを解決し、対象ファイルのpath/size/SHAをsnapshotにする。
2. resource参照、case-insensitive path、リンク、save除外などを検証する。
3. 選択したRuntime templateのfile list/SHA sealを検証する。
4. 新しい`.building-*`へ出力し、全packageとitch ZIPを再検証する。
5. 検証後だけ出力を昇格する。未所有の出力を上書きせず、既存owned outputは`.previous-*`へ保全する。

SHAは選択した現在bytesと出力の同一性を確認するための値で、公式release identityの条件ではありません。入力ゲームフォルダは読み取り専用です。

## build / test

`.NET 10 SDK`を用意し、repo rootから名前を一意に指定します。同じ名前の成果が既にあれば上書きせず失敗します。失敗時のログと出力は調査用に残ります。

```powershell
.\tools\EmueraWebPackager\build.ps1 -Mode Test -Name local-check-01
.\tools\EmueraWebPackager\build.ps1 -Mode Publish -Name release-1-0-1-check
```

省略可能な`-OutputDirectory`と`-ArtifactsPath`で試験結果とMSBuild成果物の保存先を分けられます。コミット済みsourceから隔離buildするときは、どちらもsource export外の新しいタスク専用directoryを指定してください。指定しない場合、従来どおり`artifacts/EmueraWebPackager/<name>/`とrepo内`artifacts/`を使います。PublishはWindows `win-x64` self-contained single-file GUIを生成します。公開・uploadはこのscriptでは行いません。

コミット済みsourceからAOT RuntimeとPackager EXEを作成した後は、`New-ReleaseBundle.ps1`でsealed `runtime-template`、利用手順、ライセンスを含むZIPとSHA256SUMS、Release Notesをまとめます。このscriptもローカル生成だけを行い、GitHubへ接続しません。出力directoryは新規とし、ビルド成果物はsource export外へ指定してください。

`PackagerExePath`には`build.ps1 -Mode Publish`の出力、`RuntimeTemplatePath`にはproduction AOT publishの`wwwroot`を指定します。`OutputDirectory`はsource exportの外にある新規directory、`ArtifactsPath`はsource exportの外にある同じビルドの中間・最終出力先、`SourceCommit`は生成元の40桁commit SHAです。Release用ZIP、`SHA256SUMS.txt`、`RELEASE_NOTES.md`が作られます。既存のPackager Core `seal` commandでmanifestを作り、licenseは`license.md`、`licenses/`、runtime-templateのOFL noticeから含めます。

```powershell
.\tools\EmueraWebPackager\New-ReleaseBundle.ps1 `
  -PackagerExePath <isolated-publish>\EmueraWebPackager.exe `
  -RuntimeTemplatePath <isolated-aot-publish>\wwwroot `
  -OutputDirectory <new-release-directory> `
  -ArtifactsPath <isolated-build-artifacts> `
  -SourceCommit <40-digit-commit-sha>
```

検証範囲の恒久的な要約は[Web版の検証済み機能](../../プロジェクト資料/Web版/08_検証済み機能.md)を参照してください。個々の実行ログはローカルの受入記録であり、このソース配布には含めていません。
