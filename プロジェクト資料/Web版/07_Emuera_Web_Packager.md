# Emuera Web Packager

現行Packagerは1.0.8、同梱Runtime templateはUX16-08-RC3-Compat108です。今回の範囲は採用commit由来のローカル配布候補まで。既存1.0.7の機能と.NET10.0.12／Release／AOT=true／WasmStripILAfterAOT=false／EnableErbExecutionProfiler=falseを維持します。

- 冬眠装置向けSAVECHARA／LOADCHARA／FIND_CHARADATA／CHKCHARADATAと、上部「キャラdat」の一覧・メモ・取り込み・書き出し。再読込時にもコミット済みdatを復元。
- CLEARTEXTBOXを入力欄だけの消去へ修正し、表示履歴・Island・LINECOUNTを保持。
- HTML_GETPRINTEDSTR／HTML_POPPRINTINGSTRで検証済みの画像・図形・書式情報を保持。全HTML形式の再構成対応ではありません。
- フォーカス枠をゲーム表示の外側へ配置し、文字上端の薄さを解消。
- 仮想表示する部分をsurface基準で描画し、小数倍率と履歴変化に伴う文字の画素位置・行間の揺れを抑制。履歴閲覧と末尾追従を維持。

- 通常セーブ／globalとキャラdatは別の保存対象です。通常セーブの書き出しにdatやmacroは含まれません。必要なものを各パネルから書き出してください。
- 保存先はorigin・gameId・profileIdごと。DB schema2へ更新した同じoriginを、旧schema1のWeb版で開くとVersionErrorになる場合があります。旧版確認は別origin/profileで行い、更新前に必要なファイルをバックアップしてください。
- HTML履歴は画像／図形等の検証済み範囲のみ。Group/DIV/Island、特殊寸法などの完全再構成は未対応・保留です。
- surface下端の1px背景帯に微小なRGB差が残ります。文字領域の安定と画面全体の完全一致は区別します。
- ユーザー受入は今回環境の冬眠dat・入力/HTML・文字上端・ダンジョン移動。全ブラウザ・DPR/zoom・長時間動作を保証しません。NGO上端約2行の許容済み見切れ、GDRAWGWITHMASK等の残件も維持。
- キャラdatはNative binary形式。Web資源上限は64MiB/ファイル、256キャラ、起動時4096ファイル/512MiB、portable leaf200文字。同期式関数内のSAVECHARAは安全に中断できないため保存前に拒否します。
- 配布入力には個人macro.txt、セーブ、キャラdat、ログを置かないでください。Packagerがすべて自動除外する保証はありません。利用権のあるクリーンな配布用コピーを用意します。


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



## Fキーとmacro.txt

- ゲームのタブがアクティブでページ／ゲーム入力へフォーカスがある間、F1～F12で選択グループのマクロを入力欄へ呼び出し、Shift＋F1～F12で非空の入力内容を登録します。Ctrl＋0～9で10グループを切り替えます。通常INPUTでは呼出後にEnterで実行します。ONEINPUT／AnyKey、ゲーム側キー入力待ちでは実機の入力契約が優先されます。
- ブラウザがFキーやCtrl＋数字を使う場合は、上部「マクロ操作」からグループ選択・呼出・登録を行えます。設定や編集欄、IME変換中、別タブには誤発火しない設計です。全ブラウザ／OS IME／itchでの動作は保証していません。
- 「macro.txtを読み込む」は全内容の検証とプレビュー後、確認して全10グループ・120スロットを置換します。未指定スロットは空、未指定グループ名は初期名。選択中グループは維持し、取消・不正・保存失敗では既存の登録を維持します。上限1MiB、不正行・重複・範囲外は全体拒否です。
- 「macro.txtを書き出す」は実機形式のCP932／BOMなし／CRLFでダウンロードします。CP932に表現できない内容、往復で別文字になる内容、改行等は拒否し、黙って?に置換しません。読込・書出だけでは実行しません。
- 通常登録はブラウザのlocalStorageへ自動保存し、origin・gameId・profileIdごとに分かれます。失敗時は未保存を表示します。ブラウザ間や公開先の移動にはmacro.txtの書出・読込を使えます。
- **セーブデータの書き出しにはマクロは含まれません。** セーブ管理とは別にmacro.txtを保存してください。個人macro.txt・セーブ・登録内容を配布物へ含めないでください。Packagerの既存入力収集はData/macro.txtを含め得るため、配布用ゲーム入力には個人ファイルを置かないでください。
