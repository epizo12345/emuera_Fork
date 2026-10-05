# Emuera Web Packager 1.0.8 — Release Notes

採用commitから生成したローカル配布候補。公開・アップロードは別工程で、旧1.0.7を保持します。

## 変更内容

- 冬眠装置向けSAVECHARA／LOADCHARA／FIND_CHARADATA／CHKCHARADATAと、上部「キャラdat」の一覧・メモ・取り込み・書き出し。再読込時にもコミット済みdatを復元。
- CLEARTEXTBOXを入力欄だけの消去へ修正し、表示履歴・Island・LINECOUNTを保持。
- HTML_GETPRINTEDSTR／HTML_POPPRINTINGSTRで検証済みの画像・図形・書式情報を保持。全HTML形式の再構成対応ではありません。
- フォーカス枠をゲーム表示の外側へ配置し、文字上端の薄さを解消。
- 仮想表示する部分をsurface基準で描画し、小数倍率と履歴変化に伴う文字の画素位置・行間の揺れを抑制。履歴閲覧と末尾追従を維持。

## 保存・移行・既知の制約

- 通常セーブ／globalとキャラdatは別の保存対象です。通常セーブの書き出しにdatやmacroは含まれません。必要なものを各パネルから書き出してください。
- 保存先はorigin・gameId・profileIdごと。DB schema2へ更新した同じoriginを、旧schema1のWeb版で開くとVersionErrorになる場合があります。旧版確認は別origin/profileで行い、更新前に必要なファイルをバックアップしてください。
- HTML履歴は画像／図形等の検証済み範囲のみ。Group/DIV/Island、特殊寸法などの完全再構成は未対応・保留です。
- surface下端の1px背景帯に微小なRGB差が残ります。文字領域の安定と画面全体の完全一致は区別します。
- ユーザー受入は今回環境の冬眠dat・入力/HTML・文字上端・ダンジョン移動。全ブラウザ・DPR/zoom・長時間動作を保証しません。NGO上端約2行の許容済み見切れ、GDRAWGWITHMASK等の残件も維持。
- キャラdatはNative binary形式。Web資源上限は64MiB/ファイル、256キャラ、起動時4096ファイル/512MiB、portable leaf200文字。同期式関数内のSAVECHARAは安全に中断できないため保存前に拒否します。
- 配布入力には個人macro.txt、セーブ、キャラdat、ログを置かないでください。Packagerがすべて自動除外する保証はありません。利用権のあるクリーンな配布用コピーを用意します。

## ビルドと出自

- Packager1.0.8／Runtime UX16-08-RC3-Compat108。SDK10.0.112、.NETCore／WindowsDesktop／browser-wasm10.0.12、Skia4.150.1。
- Release／RunAOTCompilation=true／WasmStripILAfterAOT=false／EnableErbExecutionProfiler=false。
- ソースcommit: `{{SOURCE_COMMIT}}`
- ソース一覧SHA256: `{{SOURCE_TREE_SHA256}}`
- EXE→AOT→sealed template→実ツール生成Webの通知と出自を継承。

## 利用

EmueraWebPackager-1.0.8-win-x64.zipとSHA256SUMS.txtを照合し、ZIP全体を新規フォルダへ展開。実EXEでクリーンな配布用ゲーム入力と新規出力先を指定。CLIは `--package <game-folder> <new-output-folder>`。ツールZIPとゲーム入りitch ZIPは別物。itchへのアップロードは利用者が行います。技術検証は素材の再配布許諾を保証しません。
