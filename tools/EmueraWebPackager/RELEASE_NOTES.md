# Emuera Web Packager 1.0.7 — Release Notes

採用ソースcommitから生成したローカル配布候補です。公開・アップロードは別工程で、旧1.0.6を保持します。

## 変更内容

- F1～F12の呼出、Shift＋Fの登録、Ctrl＋数字の10グループ切替、上部の代替マクロ操作UI。
- 実機互換macro.txtのCP932入出力。読込は全体検証・確認後に全置換、書出はCP932／BOMなし／CRLF、表現不可内容は拒否。
- ブラウザ内保存、移行とセーブデータの分離。通常入力・ゲームキー優先・赤い停止・保存ACKの保護を維持。

## Fキーとmacro.txt

- ゲームのタブがアクティブでページ／ゲーム入力へフォーカスがある間、F1～F12で選択グループのマクロを入力欄へ呼び出し、Shift＋F1～F12で非空の入力内容を登録します。Ctrl＋0～9で10グループを切り替えます。通常INPUTでは呼出後にEnterで実行します。ONEINPUT／AnyKey、ゲーム側キー入力待ちでは実機の入力契約が優先されます。
- ブラウザがFキーやCtrl＋数字を使う場合は、上部「マクロ操作」からグループ選択・呼出・登録を行えます。設定や編集欄、IME変換中、別タブには誤発火しない設計です。全ブラウザ／OS IME／itchでの動作は保証していません。
- 「macro.txtを読み込む」は全内容の検証とプレビュー後、確認して全10グループ・120スロットを置換します。未指定スロットは空、未指定グループ名は初期名。選択中グループは維持し、取消・不正・保存失敗では既存の登録を維持します。上限1MiB、不正行・重複・範囲外は全体拒否です。
- 「macro.txtを書き出す」は実機形式のCP932／BOMなし／CRLFでダウンロードします。CP932に表現できない内容、往復で別文字になる内容、改行等は拒否し、黙って?に置換しません。読込・書出だけでは実行しません。
- 通常登録はブラウザのlocalStorageへ自動保存し、origin・gameId・profileIdごとに分かれます。失敗時は未保存を表示します。ブラウザ間や公開先の移動にはmacro.txtの書出・読込を使えます。
- **セーブデータの書き出しにはマクロは含まれません。** セーブ管理とは別にmacro.txtを保存してください。個人macro.txt・セーブ・登録内容を配布物へ含めないでください。Packagerの既存入力収集はData/macro.txtを含め得るため、配布用ゲーム入力には個人ファイルを置かないでください。

## 確認範囲と制約

port63187の候補でFキー操作・上部操作・CP932入出力・物理キーをユーザー確認済み。ブラウザ名／版はユーザーから未指定であり、全ブラウザ・OS IME・itchへ一般化しません。新しいツールから生成した最終候補の短いユーザー確認は別に行います。GDRAWGWITHMASK等の未実装、NGO上端約2行の許容済み見切れは維持。全命令対応や未測定の速度改善率は主張しません。

## ビルドと出自

- Packager1.0.7／Runtime UX16-08-RC3-Compat107。.NETCore／WindowsDesktop／browser-wasm10.0.12、Skia4.150.1。
- Release／RunAOTCompilation=true／WasmStripILAfterAOT=false／EnableErbExecutionProfiler=false。
- ソースcommit: `{{SOURCE_COMMIT}}`
- ソース一覧SHA256: `{{SOURCE_TREE_SHA256}}`
- 同commitのEXE／AOT／sealed templateから実配布EXEで生成。通知・ライセンスを継承。

## 利用

EmueraWebPackager-1.0.7-win-x64.zipとSHA256SUMS.txtを照合し、ZIP全体を新規フォルダへ展開します。実EXEで使用権のあるゲーム入力と新規出力先を選択します。CLIは `--package <game-folder> <new-output-folder>`。ゲームZIPは利用者がitchへアップロードし、ツールZIPとは別です。技術検証はゲーム素材の再配布許諾を保証しません。
