# Emuera.NET 最終通常版

- 更新日: 2026-08-21
- 基礎: BugFix_Test `7b7dd3bf240eff4fdfc7094f4175de0e014532b7`
- 対象OS: Windows 10 Version 2004以降 / Windows 11（x64）
- 必要ランタイム: .NET 10 Desktop Runtime（x64）
- 配布形態: フレームワーク依存・単一EXE
- `Emuera.exe` サイズ: 24,643,306バイト（約23.5 MiB）
- `Emuera.exe` SHA-256: `001E0E4E6DB4C0D42EB56294194474A3BF017B38115EC4707FCA0A70CDB4B31D`
- Phase 4A/B/C warning cleanupを反映（Release warning 52件から37件、new warning 0、Normal/Kojo起動確認済み）
- Phase 5A/B/C focused bugfixを反映（VARSIZE/macro、ARRAYMSORT、GGETCOLOR/GSETCOLOR、SPRITEGETCOLOR。Release 0 errors、37 warnings、focused/Normal/Kojo確認済み）
- Phase 6A/B focused bugfixを反映（SPRITEANIMEADDFRAMEの無効Sprite入力時NRE修正、ClipboardのBufferSize/MinTimer 0・負値の安全化、ConfigDialogの最小値1制限とruntime clamp。Release 0 errors、37 warnings、focused/Normal/Kojo確認済み）
- Phase 7A/B/C focused bugfixを反映（ClipboardメニューのON状態復元、Clipboard Ctrl+Up/Downの起動時null安全性、VariableSize.csvの2D/3D/CDFLAG要素数計算overflow、CircularBuffer.Clear()の内部参照解放。Release 0 errors、35 warnings、focused/Normal/Kojo確認済み）
- Phase 8A/B focused bugfixを反映（単項マイナスのoperand二重評価と副作用式の余分な実行を修正、文字列`>=` / `<=`の比較条件を修正。Release 0 errors、35 warnings、focused/Normal/Kojo確認済み）
- Phase 9A/B/C focused bugfixを反映（符号付き小数・16進・指数表記の数値判定、キャラクター入替え後の対象追随、極端なRAND範囲のエラー処理を改善。Release 0 errors、35 warnings、focused/Normal/Kojo確認済み）
- Phase 10A focused bugfixを反映（POWERで大きな整数を扱った際の丸め誤差と、Int64上下限など本来有効な累乗結果がエラーになる問題を修正。Release 0 errors、35 warnings、focused/Normal/Kojo確認済み）
- Phase 10B memory optimizationを反映（大規模ERB/口上を読み込んだ際の常駐メモリをさらに削減。実ゲーム大規模fixtureでmanaged memory約125MiBの削減を確認。測定環境依存の値です）
- Phase 12B1.1 runtime allocation削減を反映（private dynamic 2D整数配列を安全にbounded reuse。対象`System.Int64[,]` allocation rate約92.7%削減、再利用上限32,768要素、既存セーブsmoke済み）
- Phase 12C HTML表示最適化を反映（strict color-only `FONT`入力だけを既存表示オブジェクトへ直接変換し、その他のHTMLはAngleSharpへfallback。C5同一入力測定で対象経路のallocation約80.839%削減、時間約87.346%削減。Release 0 errors / 35 warnings / new warning 0、Normal/Kojo smoke済み）
- Phase 13R14 ERB式解析のTermStack storage最適化を反映。1要素だけを扱う場合は従来の`Stack<object>`と初期配列を必要になるまで生成せず、大規模口上fixtureのScriptParse中の一時allocationを約251.4MB / 9.60%削減しました。Server / Workstationの起動速度は実質neutralで、Normal / Kojo / semantic validation済みです。セーブ形式・ERB評価順・演算子処理は変更していません。
- Phase 13R22 `VariableToken.CheckElement`の全要素check maskを共有し、hot pathで毎回生成される3要素`bool[]` allocationを削減しました。Release buildとsave219ロード・短いmacro smokeで互換性を確認済みです。
- Phase 13R23 `PrivateInt1DVariableToken`のtop-level private dynamic 1D整数配列を最大32,768要素までbounded reuseします。再利用前にclearし、default値を復元します。nested / recursive呼出しは独立配列経路を維持し、大きな配列は保持しません。Release buildとsave219ロード・短いmacro smokeで互換性を確認済みです。
- Phase 13R24 `FixedVariableTerm`の短命runtime caller 10件を既存`RentFixedVariableTerm` lease/pool経路へ移行しました。添字評価順を維持し、lease lifetime外へ参照を出しません。Release buildとsave219ロード・短いmacro smokeで互換性を確認済みです。

この単一EXEにはSkiaSharpのネイティブライブラリも内包されています。別の`libSkiaSharp.dll`を同じフォルダへ追加する必要はありません。
Windows API全体の大きな.NET投影DLLは含めず、SkiaSharpが必要とする`WinRT.Runtime.dll`だけを内包しています。

## 2026-07-31に反映した基盤更新

- .NET 10へ更新
- Windows 11のダークモード対応
- SkiaSharp 4.150.1と現在のOpenTKに合うビルド・垂直同期設定
- 必要なWinRT部品だけを残し、単一EXEのサイズを約47.2 MiBから約23.5 MiBへ削減
- AngleSharp 1.6.0
- SQLiteネイティブライブラリを含むパッケージを2.1.12へ固定
- System.CommandLineを安定版2.0.10へ更新

## 継続して採用している機能

- 起動時の並列ERB解析に起因する不定期な偽警告を抑止
- 起動高速化
- 複数入力マクロの描画集約による高速化
- `MATCH` / `CMATCH`、`VARSET` / `CVARSET`の一時参照再利用によるマクロ高速化
- 単一の埋め込み式を持つ整形文字列の直接連結によるマクロ高速化
- Ctrl+Cまたはメニューから大量ログを開く画面の表示高速化
  - ログ内容は省略せず、テキスト設定中の途中再描画だけを止めます
  - 閉じたログ画面はその場で破棄し、大きなログを不要に保持しません
- マウスXButton1/XButton2による前ページ・次ページ選択
  - 日本語の「前のページ」「次のページ」系
  - 調教画面の`1007` / `1009`
  - ショップ画面の`PREV` / `NEXT`

## 起動後メモリ整理

- 大規模ERB構成では、起動完了時のmanaged memoryが2 GiB以上の場合にmemory trimを行う場合があります。
- 2 GiB未満ではtrimせず、2 GiB以上ではAggressive / blocking / compacting GCを1回実行します。
- 大規模fixtureではWorking Set約1.50GB削減を確認しましたが、同fixtureでは起動完了まで約1秒増加しました。
- 削減量はゲーム構成によって異なり、全ゲームで同じ削減量になるわけではありません。

表示されている文字は部分一致で探します。調教画面のように`div`の内側へ入れ子になったHTMLボタンも再帰的に探し、ゲームへ渡す内部入力値が`1007` / `1009`と完全一致すれば選べます。

一時参照再利用は、ゲームの変数値や解析済みERBを保存するキャッシュではありません。配列検索・一括代入中にだけ使う小さな添字入れ物を処理後に再利用するため、ERBの評価順、乱数、代入結果、セーブ形式は変わりません。

文字列の短経路は、埋め込み式が1個のときだけ作業用バッファへのコピーを省きます。式は従来と同じ位置で1回評価され、複数式は従来経路のままなので、関数呼出・乱数・表示内容は変わりません。

ゲーム別の候補は`Data/setting_user.json`で変更できます。`|`は「いずれか」の区切りで、JSONは1行でも複数行でも構いません。

```json
"MouseXButton1ButtonText": "前のページ|前ページ|1007|PREV",
"MouseXButton2ButtonText": "次のページ|次ページ|後ろのページ|1009|NEXT"
```

実際の設定ファイルで日本語が`\u524D...`のように見える場合も正常です。Shift-JIS誤判定による文字化けを避けるJSON表記で、Emuera内では同じ日本語として扱われます。

## 入れ替え方

1. ゲームを終了する。
2. 既存の`Emuera.exe`を`Emuera.exe.bk`などの名前でバックアップする。
3. このフォルダの`Emuera.exe`をゲームフォルダへコピーする。
4. ゲームを起動して、タイトル画面、セーブ読込、サイドボタン、マクロを確認する。

.NET 10 Desktop Runtimeが入っていない場合は、起動時の案内に従ってMicrosoft公式のx64版をインストールしてください。
問題があれば、新しいEXEを削除してバックアップを`Emuera.exe`へ戻してください。
