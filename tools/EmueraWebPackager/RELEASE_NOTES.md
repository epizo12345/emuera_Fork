# Emuera Web Packager 1.0.6 — Release Notes

採用ソースcommitから生成した1.0.6配布候補の資料です。公開・アップロードは別工程で、旧1.0.5を保持します。

## 変更内容

- AWAITに対応。通常入力を消費せず描画を反映してホストへ制御を返し、指定された待機の後に継続します。省略／0へ固定待ち時間を追加せず、正値の待機中もUIを応答可能にします。Runtime世代・要求IDを照合し、古い／重複完了、保存ACK前の続行を拒否します。
- GDRAWGの通常版と色補正版に対応。画像間コピー、切り出し、拡大縮小、透明度合成、色行列、重なった自己コピーを扱い、親画像の更新を表示済みSpriteGへ反映します。
- .NET10.0.12、既存のマクロ停止・保存保護・Scale Fit・1512×864描画領域を維持します。

## 確認範囲と残件

- AWAITは逆引き合体でユーザー確認済み。他6実ゲーム経路を全て確認済みとはしていません。GDRAWGはユーザーのブラウザ確認で問題なし。具体的キャラ・操作経路は未指定です。
- 最小fixtureでGDRAWG16画像1464画素のNative照合、SpriteG更新、AWAIT継続、マクロ停止後の手動操作、保存ACKを検証します。fixtureの成功を全ゲーム動作保証に置き換えません。
- GDRAWGWITHMASKなど他の未実装は残ります。色行列の添字なしCM指定に共有処理の例外が残り、明示CM:0:0を試験しています。
- NGO上端約2行の見切れは許容済み制約です。実ゲームでのNGO後続進行全体、全ブラウザ、長時間プレイ、OS IME、itch公開後の動作は未確認です。
- 全命令対応済みや未測定の速度改善を宣伝する更新ではありません。

## 配布条件と出自

- Windows x64 self-contained Packager1.0.6、sealed Runtime UX16-08-RC3-Compat106。.NETCore／WindowsDesktop／browser-wasm10.0.12。
- Release / RunAOTCompilation=true / WasmStripILAfterAOT=false / EnableErbExecutionProfiler=false。
- ソースcommit: `{{SOURCE_COMMIT}}`
- ソース一覧SHA-256: `{{SOURCE_TREE_SHA256}}`
- 採用commitからEXE／AOTを再生成し、tool→sealed template→生成Webへbuild-provenanceを継承します。

## 利用方法

1. EmueraWebPackager-1.0.6-win-x64.zipとSHA256SUMS.txtを照合し、ZIP全体を新規フォルダへ展開します。
2. 展開したEmueraWebPackager.exeで使用権のあるゲーム入力と新規出力先を選びます。CLIは `--package <game-folder> <new-output-folder>`。
3. ゲーム用ZIPは利用者がitchへアップロードします。ツールZIPとは別のファイルです。

ゲーム本体・素材・個人セーブ・試験証跡をツールZIPへ含めません。ライセンス／通知はtool→template→生成Webへ継承します。技術検証はゲーム素材等の再配布許諾を保証しません。
