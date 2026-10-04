# Emuera Web Packager 1.0.5 — Release Notes

Web Packager 1.0.5の正式配布資料です。旧1.0.4とその配布物は保持します。ゲーム入りZIPのitchアップロードは利用者が行います。

## 1.0.5の変更

- 位置指定DIV／HTML Islandで実機が行わない自動折返しを抑え、ターンエンド確認の質問・はい／いいえが枠外へ出る問題を修正。通常文章や日記帳の称号一覧の物理行折返しは維持します。
- 入れ子のabsolute-lefttop／absolute-leftbottomを実機と同じゲーム画面基準で配置し、神格習合のカード位置ずれを修正。relative配置は親を基準とする従来動作を維持します。
- 論理描画領域を1512×864pxへ揃え、神格習合の下部「決定／戻る」を表示・操作できるようにしました。CLIENTHEIGHT、絶対配置、Scale Fit、独自マウス入力座標の基準を一致させています。小窓では論理解像度を削らず一様に縮小します。
- NGOイベントで停止していたHTML_TAGSPLITをNative互換で実装。文字・空白・引用符・エンティティ表記を保持し、引用符内でも最初の「>」で区切ります。不完全なタグは失敗とし、RESULT=-1と出力配列未変更の既存契約を維持します。

## 確認済みと既知の制約

- ターンエンド確認、神格習合のカード・下部ボタン・戻る操作はユーザーが実ゲームで確認済みです。
- NGOは実ゲームで表示復帰を確認済みです。上端約2行の見切れは既知の許容制約で、864pxを維持します。NGOの実ゲームでの後続進行全体は確認済みとはしていません。短いfixtureでは右クリック後の次入力まで確認しています。
- 1.0.4のPRINTC／PRINTLCとHTMLボタン折返し、tooltip、QUIT後のタイトル復帰、1.0.3の赤いマクロ停止、保存ACK／Web Locks／Scale Fitを維持します。
- 全ゲーム・全ブラウザ、長時間プレイ、実OS IME、未到達ゲームオーバー／エンディング、itch公開後の動作は保証しません。速度改善率は主張しません。

## 配布条件と出自

- Windows x64 self-contained Packager 1.0.5とsealed Runtime UX16-08-RC3-Compat105を同梱。.NETCore／WindowsDesktop／browser-wasm実行資材は10.0.12です。
- Web: Release / RunAOTCompilation=true / WasmStripILAfterAOT=false / EnableErbExecutionProfiler=false。IL削除版との速度・サイズ差は未測定です。
- 生成元の採用source commit: `{{SOURCE_COMMIT}}`
- 最終source一覧SHA-256: `{{SOURCE_TREE_SHA256}}`
- 採用commitから再生成した成果物を使用し、EXEの埋込みrevisionとソース一覧SHAを照合します。tool→sealed template→生成Webにbuild-provenance.jsonを引き継ぎます。
- MAIN側の描画・TINPUT・フォント変更、IR復元、MEMFS move試作、固定seed／時刻、比較policyは含みません。通常の保守用診断は維持します。

## 使い方

1. `EmueraWebPackager-1.0.5-win-x64.zip`と同じ配布一式の`SHA256SUMS.txt`を照合し、ZIP全体を新しいフォルダへ展開します。
2. `EmueraWebPackager.exe`で使用権のあるゲームフォルダと新しい出力先を指定します。`--package <game-folder> <new-output-folder>`も使用できます。
3. 生成Webをローカル確認し、利用者自身が生成したゲーム用ZIPをitch.ioへアップロードします。ツールZIPをゲームとしてアップロードしないでください。

ツールZIPにゲーム本体・素材・個人セーブ・試験証跡は含みません。LICENSEと依存・フォントの通知をtool→template→生成Webへ継承します。技術検証はゲーム等の再配布許諾を保証しません。別ゲームは別origin／itchページで配信してください。
