# Emuera Web Packager — 開発者向け

現行ソースはversion 1.0.7です。配布候補をローカル検証中で、公開は別工程です。利用者の操作は[使い方](使い方.md)、package形式は[Web版パッケージ仕様](../../プロジェクト資料/Web版/03_ゲームデータパッケージ仕様.md)を参照してください。

1.0.7はFキー／上部マクロ操作とCP932 macro.txtの入出力を追加し、Runtime UX16-08-RC3-Compat107を同梱します。ユーザーはport63187の候補で物理キーも確認済みですが、全ブラウザ・OS IME・itchの受入には一般化しません。

既存1.0.6はAWAITの非入力待機とGDRAWGの通常／色補正画像コピーを追加し、Runtime UX16-08-RC3-Compat106を同梱します。AWAITは逆引き合体でユーザー確認済み。GDRAWGはブラウザで問題なしとの確認済みですが具体的操作経路は未指定です。他の実ゲーム全経路・全命令への対応や速度改善は主張しません。GDRAWGWITHMASKと添字なし色行列CMの共有処理例外は保守残件です。

既存1.0.5は位置指定DIV／HTML Islandの不要な自動折返し、入れ子absolute DIVの画面基準配置、1512×864の論理描画領域、Native互換HTML_TAGSPLITを含みます。ターンエンド確認、神格習合のカード・決定／戻る・戻る操作、NGOの実ゲーム表示復帰はユーザー確認済みです。NGO上端約2行の見切れは既知の許容制約で、864pxを維持します。NGOの後続進行は実ゲーム受入済みとはしません。短いfixtureでは右クリック後の次入力まで確認しています。正式成果物の出自は生成元の採用commitとソース一覧SHAで識別します。

1.0.4はQUIT後の正常終了・タイトル復帰、連続PRINTC系の物理行配置、通常フローのHTMLボタン折返し、説明付きnonbuttonとHTML Islandのtooltipを改善しています。従来の4実ゲームケースに加え、日記帳の称号一覧／一覧2／一覧3／EXをユーザー受入済みです。ゲームオーバー／エンディング等の未確認経路は別に記録します。

1.0.3は、受入済みマクロ入力・スキップ互換性と上部のマクロ停止UIを含むUX16-08-RC3-MacroStopを使用します。通常操作とスキップなしマクロの強制待機、StopMesskip、保存・ロック待ちは維持します。診断seed・固定時刻・比較policyは含みません。

1.0.2は表示・入力・セーブ取込／キャンセル後のタイトル復帰、WARNING、スケールフィットを含むUX16-08-RC3を使用します。Webの配布ビルド条件はRelease / RunAOTCompilation=true / WasmStripILAfterAOT=false / EnableErbExecutionProfiler=falseです。IL保持はAOTの無効化ではなく、IL削除版とのサイズ・速度差は未測定です。SHA出力領域・root-prefix再利用は割当削減のみ確認済みで、速度改善を保証しません。

GUIを開かず同じ生成処理を実行する場合は、ZIPを展開したフォルダで `EmueraWebPackager.exe --package <game-folder> <new-output-folder>` を使用できます。新規出力先のみ受け付け、隣接するsealed runtime-templateを使います。未知の引数・生成失敗は終了コード1となります。

## Fキーとmacro.txt

- ゲームのタブがアクティブでページ／ゲーム入力へフォーカスがある間、F1～F12で選択グループのマクロを入力欄へ呼び出し、Shift＋F1～F12で非空の入力内容を登録します。Ctrl＋0～9で10グループを切り替えます。通常INPUTでは呼出後にEnterで実行します。ONEINPUT／AnyKey、ゲーム側キー入力待ちでは実機の入力契約が優先されます。
- ブラウザがFキーやCtrl＋数字を使う場合は、上部「マクロ操作」からグループ選択・呼出・登録を行えます。設定や編集欄、IME変換中、別タブには誤発火しない設計です。全ブラウザ／OS IME／itchでの動作は保証していません。
- 「macro.txtを読み込む」は全内容の検証とプレビュー後、確認して全10グループ・120スロットを置換します。未指定スロットは空、未指定グループ名は初期名。選択中グループは維持し、取消・不正・保存失敗では既存の登録を維持します。上限1MiB、不正行・重複・範囲外は全体拒否です。
- 「macro.txtを書き出す」は実機形式のCP932／BOMなし／CRLFでダウンロードします。CP932に表現できない内容、往復で別文字になる内容、改行等は拒否し、黙って?に置換しません。読込・書出だけでは実行しません。
- 通常登録はブラウザのlocalStorageへ自動保存し、origin・gameId・profileIdごとに分かれます。失敗時は未保存を表示します。ブラウザ間や公開先の移動にはmacro.txtの書出・読込を使えます。
- **セーブデータの書き出しにはマクロは含まれません。** セーブ管理とは別にmacro.txtを保存してください。個人macro.txt・セーブ・登録内容を配布物へ含めないでください。Packagerの既存入力収集はData/macro.txtを含め得るため、配布用ゲーム入力には個人ファイルを置かないでください。

## Release一式

Packagerの配布はGitHub Releasesへruntime-template付きZIPを添付する方針です。EXE単体では起動できないためZIP全体を使います。ShinEra本体やゲームデータは付属せず、利用者が自分のゲームフォルダを別途選択します。公開済み[1.0.4 Release](https://github.com/epizo12345/emuera_Fork/releases/tag/web-packager-v1.0.4)と旧版の配布ZIP・チェックサムを保持します。[1.0.5 Release](https://github.com/epizo12345/emuera_Fork/releases/tag/web-packager-v1.0.5)でゲームなしのツールZIPとチェックサムを配布します。1.0.1の公開履歴は保持します。

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
.\tools\EmueraWebPackager\build.ps1 -Mode Publish -Name release-1-0-7-check
```

省略可能な`-OutputDirectory`と`-ArtifactsPath`で試験結果とMSBuild成果物の保存先を分けられます。コミット済みsourceから隔離buildするときは、どちらもsource export外の新しいタスク専用directoryを指定してください。指定しない場合、従来どおり`artifacts/EmueraWebPackager/<name>/`とrepo内`artifacts/`を使います。PublishはWindows `win-x64` self-contained single-file GUIを生成します。公開・uploadはこのscriptでは行いません。

コミット済みsourceからAOT RuntimeとPackager EXEを作成した後は、`New-ReleaseBundle.ps1`でsealed `runtime-template`、利用手順、ライセンスを含むZIPとSHA256SUMS、Release Notesをまとめます。コンパイル入力とビルド条件が一致する受入済み成果物を再利用する場合は、入力ファイルのSHA一覧、元ビルドのsource revision、採用commitとの対応を別途記録します。commitしただけではEXEやWasm内のsource revisionは更新されません。このscriptもローカル生成だけを行い、GitHubへ接続しません。出力directoryは新規とし、ビルド成果物はsource export外へ指定してください。

`PackagerExePath`には`build.ps1 -Mode Publish`の出力、`RuntimeTemplatePath`にはproduction AOT publishの`wwwroot`を指定します。`OutputDirectory`はsource exportの外にある新規directory、`ArtifactsPath`はsource exportの外にある同じビルドの中間・最終出力先、`SourceCommit`は生成元の40桁commit SHAです。Release用ZIP、`SHA256SUMS.txt`、`RELEASE_NOTES.md`が作られます。既存のPackager Core `seal` commandでmanifestを作り、licenseは`license.md`、`licenses/`、runtime-templateのOFL noticeから含めます。

```powershell
.\tools\EmueraWebPackager\New-ReleaseBundle.ps1 `
  -PackagerExePath <isolated-publish>\EmueraWebPackager.exe `
  -RuntimeTemplatePath <isolated-aot-publish>\wwwroot `
  -OutputDirectory <new-release-directory> `
  -ArtifactsPath <isolated-build-artifacts> `
  -SourceCommit <40-digit-commit-sha> `
  -SourceTreeSha256 <64-digit-source-file-table-sha256>
```

検証範囲の恒久的な要約は[Web版の検証済み機能](../../プロジェクト資料/Web版/08_検証済み機能.md)を参照してください。個々の実行ログはローカルの受入記録であり、このソース配布には含めていません。

1.0.2では依存のライセンス通知をsealed runtime-templateへ同梱し、生成Webへ引き継ぎます。AOT=true／WasmStripILAfterAOT=falseはIL保持の配布条件で、AOT無効化ではありません。依存監査は確認日時と対象版に限るため公開直前にも確認し、ゲーム・画像・口上の再配布権は別途確認してください。

Windows Packagerのself-contained NETCore／WindowsDesktopとbrowser-wasm実行資材は10.0.12です。SDK 10.0.112／wasm-tools 10.0.112を使用し、SkiaSharp等の版と採用済みレイアウトを維持します。この10.0.12版はLOADと操作、縮小クリック、セーブ入出力後のタイトル復帰、実ドウマン戦のWARNING点滅をユーザーが確認済みです。全ゲーム・全ブラウザの互換性、長時間プレイ、実OS IME、itch上の動作を保証するものではありません。

