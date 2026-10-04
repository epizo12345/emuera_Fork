# 時限入力中の画像アニメーション・DIV・代替フォントの修正説明

## 背景

前回採用した`TINPUT`タイマーの再入防止とは別に、残り時間表示を消した時限入力中はアニメーションの描画が止まることを、2色のフレームが交互に切り替わる試験で確認した。また、背景色を指定しないDIVが透明な矩形を描くために`SKPaint`を作っていた。代替フォントが混じる文字列では、書体のrun境界、分割後の情報、折返し幅とクリック範囲に不整合があった。

## 実装

- `EmueraConsole.Draw()`では、残り時間表示ありの`TINPUT`は従来の表示更新経路へ任せ、表示なしの場合はアニメーションtimerからの描画を通す。期限処理、入力ごとのtimer instance、再入防止、旧通知の無効化は変更しない。
- `HtmlManager`では、背景未指定DIVの背景値をnullable `null`とし、不要な`SKPaint`生成と透明矩形描画を避ける。明示背景の色・描画は維持する。
- `ConsoleStyledString`と`PrintStringBuffer`では、代替フォントへの切替時に前runを確定してから境界文字を次runへ追加する。補助文字を壊さず分割し、分割後も書体情報を保持する。折返しは実際の描画runの幅を使い、Hinting/Edgingも引き継ぐ。

同じカウントダウン文字列の描画省略、mutable G snapshot、CSV lockの試作は含めない。

## 効果

残り時間を表示しない時限入力中も複数フレームのアニメーションが進む。代替フォントを含む表示の文字・幅・折返し・クリック範囲の整合が改善する。背景未指定DIVでは、前回の限定試験で**1個・1描画あたり88 bytes**のmanaged割当減を観測した。実ゲーム全体の速度やGPU使用率の改善率は未測定である。

## 検証

- 基準版へテストのみを移したREDで対象不具合を検出し、統合版の`ConfigRegressionTests`は60/60 PASS、失敗0、skip 0。TINPUT表示ON/OFF中と次入力後の赤・青2フレーム、フォントの実際のfallback、補助文字、AAAA😀／BBBB境界、折返し、選択色、バックログ、クリック範囲を含む。
- DIVの実ウィンドウfocused試験は22検証/22 PASS。従来の「23/23」の1件は座標情報の表示だけだったため、検証件数から除いた。背景あり・なし、hover、tooltip、ボタンID 101/202、空白miss、重なり順、画面を戻した後の再操作を確認した。
- ユーザーは統合候補を実機で確認し、問題なしと判断した。候補EXEのSHA-256は`1BC73512542E513348B22865F6ACD8564BC7505B2FC183E134D23054DBA106FA`。正式版は採用source commitから再生成した別バイナリである。
- 180秒`TINPUT`の完走・次入力受理はタイマー単独候補の既存記録。統合候補では短い早期入力→次の時限入力期限→通常入力受理を確認した。正式EXEで長時間試験を機械的に繰り返したとは扱わない。
- 配布先の正式EXEを新しいUTF-8 BOM付きERBコピーから短く起動し、`Init:End`とプロセスの応答を確認した。画面の長時間操作や元ゲームのセーブロードではない。

## 採用状態

source commit `5e7f62e463f30f057f65d19617a1491368e7e4fb`から、Release / win-x64 / framework-dependent / single-file / `PERFORMANCE_METRICS`無効で正式EXEを生成した。24,700,650 bytes、SHA-256 `B3EE2D4D0BF454AFF95E42323AB5DAD80B942F475C211E4EE32815BFD63756A3`、ProductVersion `0.2.6.0+5e7f62e463f30f057f65d19617a1491368e7e4fb`。配布更新commitはEXE生成元とは区別する。旧EXE・README・SHA一覧は`artifacts/graphics-gpu-official-20261004/backup-previous-4e79e11-20261004/`へSHA照合して退避した。
