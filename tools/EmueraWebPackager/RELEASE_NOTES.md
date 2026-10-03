# Emuera Web Packager 1.0.4 — Release Notes

ローカル正式化・配布準備版です。GitGud／GitHubへのpush、タグ、Release公開、itchアップロードは未実施です。

- Windows x64 self-contained PackagerとUX16-08-RC3-Compat104のsealed runtime-templateを同梱します。実同梱.NETCore／WindowsDesktopとbrowser-wasmは10.0.12です。
- 生成元source commit: `{{SOURCE_COMMIT}}`

## 1.0.4の変更

- 連続PRINTC／PRINTLCの選択肢を物理表示行へ分割し、次の見出し・罫線への重なりを修正。論理LINECOUNTとCLEARLINEの契約は維持します。
- 説明付きnonbuttonの文字・画像へhoverできるようにし、HTML Island内のtooltipも表示します。配置用の空領域全体やクリック権限は広げません。
- QUITを正常終了として案内し、保存が正常完了した後に「タイトルに戻る」が使えるように修正。保存失敗と正常終了を区別し、transaction／ACK、既存Process再利用、二重復帰防止を維持します。セーブ400の自動LOAD／削除は行いません。
- タイトル画像のキャラ情報、習得予定スキル説明、日記帳／悪魔調教典、中断セーブ後の復帰をユーザーが実ゲームで受入済みです。ゲームオーバー／エンディングの終了選択は未到達・未確認です。

## 維持する動作とビルド条件

- 1.0.3のマクロ入力・待機互換性、赤いマクロ停止、通常操作への復帰を維持。通常操作とスキップなしマクロの強制待機、StopMesskip、保存完了待ち、Web Locksは省略しません。
- 1.0.2の表示・セーブ入出力とタイトル復帰、WARNING、スケールフィットを維持。等倍上限・上寄せのレイアウトを維持します。
- Release / RunAOTCompilation=true / WasmStripILAfterAOT=false / EnableErbExecutionProfiler=false。IL保持AOTであり、IL削除版との速度・サイズ差は未測定です。
- SHA／root-prefix再利用は割当削減として含みます。速度改善率は主張しません。IR復元・MEMFS move試作、固定seed／時刻、比較policy、試験専用ログは含みません。通常の保守用診断は維持します。
- 確定commitからEXE・AOTを生成し、ProductVersion、source一覧、seal、release-bundle.jsonで対応を記録します。旧チェックサムは流用しません。

## 使い方・制限

1. `EmueraWebPackager-1.0.4-win-x64.zip`と同じ配布一式の`SHA256SUMS.txt`を照合し、ZIP全体を展開します。EXE単体では使えません。
2. `EmueraWebPackager.exe`で使用権のあるゲームフォルダと新しい出力先を指定します。`--package <game-folder> <new-output-folder>`も利用できます。
3. 生成ゲームWebをローカル確認し、利用者自身がゲーム用ZIPをitch.ioへアップロードします。ツールZIPをゲームとしてアップロードしないでください。

ツールZIPにゲーム本体・素材・セーブ・試験証跡は含みません。通知をtool→template→生成Webへ引き継ぎます。技術検証はゲーム等の再配布許諾を保証しません。gameId／save namespaceの制約があるため、別ゲームは別origin／itchページで配信してください。全ゲーム・全ブラウザ、長時間プレイ、実OS IME、itch公開後の動作は未確認です。旧1.0.3と1.0.2の配布物・タグは保持します。
