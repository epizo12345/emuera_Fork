# Emuera.NET 
著作者：VVII, MinorShift, 妊）|дﾟ)の中の人,epizo,CRER  
頒布者：epizo  
連絡先：eraMegaten Discordサーバー（https://discord.gg/yQRYkNMuWr） epizo 宛  

# 前書き
https://gitlab.com/alnatiyan/EmueraDotNet/-/tree/BugFix_Test?ref_type=heads  
を独自にForkしたバージョンです。  
上記にCRER氏がVVII氏のダークモードパッチや.NET10への更新などを取り込んだ版をForkしています。
個人用に作成しているため、使用は自己責任でお願いします。  

# 動作環境
- Windows 10 Version 2004（build 19041）以降の64bit版
- .NET 10 Desktop Runtime（x64）

.NET 10が入っていない場合は、起動時に表示される案内に従って
.NET 10 Desktop Runtime（x64）をインストールしてください。

Windows 11ではメニューや設定画面にダークモードが適用されます。
ゲーム本文の背景色・文字色は、従来どおりゲームやEmueraの設定が優先されます。

# 使用方法
『Emuera.exe』をERBフォルダ・CSVフォルダのある階層に置いたのち、
『Emuera.exe』をダブルクリックして起動してください

現在の配布用ビルドは必要なSkiaSharpの部品を『Emuera.exe』内にまとめるため、
『libSkiaSharp.dll』を別に置く必要はありません。
また、Windows API全体の大きな補助部品は同梱せず、SkiaSharpが必要とする
『WinRT.Runtime』だけをEXE内へ残して、配布サイズを抑えています。

# 主な修正
- LazyERBのゲーム推奨設定とプレイヤー設定を分けました。設定画面の「起動」タブから安全な対象フォルダを変更でき、ゲーム推奨設定へ戻すこともできます。未知の設定項目を通常保存で保持し、変更は次回起動後に反映されます。
- Phase 13R39で`Data\ERB\口上\口上まとめ\`配下だけをLazy ERB Hydration化しました。起動時は関数stub／metadataを登録し、本文は初回実行直前にERBファイル単位でhydrateします。KOJO startup Managedは`1,762,740,872`から`1,174,959,288 bytes`へ実測削減（-33.34%）。通常ERB、AnalysisMode、DebugModeは従来どおりeagerです。
- Phase 13R39.1で、通常モードの口上まとめ対象reloadだけをfull reloadへ昇格し、DebugMode / AnalysisModeでは従来のpartial/folder reloadを維持するよう修正しました。preprocessorはeager fallback、`[[...]]` renameは従来どおりです。
- Phase 13R40.3で、`Data\setting.json`から設定できる日本語名のLazy ERB設定を正式採用しました。旧`LazyErb`形式から自動移行し、Data基準の`ERB\`配下だけを対象に、不正パスやERB root指定を拒否します。Preload時の不要なbulk cacheを抑制し、従来互換性とreload safetyを維持しています。
- Phase 13R41B1で、`ERB\RPG\依頼`をLazy ERBの新しい既定対象へ追加しました。依頼5「教授の隠れ家」の一覧・本編・初回hydration、save/load、save219実戦闘を実機確認済みです。既存の明示的な`setting.json`のLazy対象は自動変更せず、欠落・null・新規設定のdefaultだけを更新します。Repeat1000では安定したruntime性能悪化を確認していません。
- Phase 14N1で、Sprite / DIVの長寿命SKPaintを整理し、GraphicsImageをBrush/Penのownerへ統一しました。起動A/BではPrivate Memory / Working Setの削減方向を確認していますが、環境依存の結果です。
- Phase 14N2で、ColorMatrix、polygon、default font、negative flipが使うtemporary Skia native resourceを描画スコープ内で明示解放するよう整理しました。描画結果、sampling、flip semanticsは変更していません。native memoryの削減量は未測定です。
- Phase 13R37で`LogicalLine`内部の`ScriptPosition`をfileId/lineNoへ圧縮し、外部位置情報とsave互換性を維持したまま`InstructionLine` shallow sizeを80 bytesから72 bytesへ削減
- Phase 13R38で通常表示ログの`displayLineList`をring buffer化し、`MaxLog=50000`到達後の先頭破棄をO(1)化。論理index順、描画、バックログ、選択肢、save/ERB semanticsを維持
- Phase 13R36で`InstructionLine`の`FunctionIdentifier`専用参照slotを削除し、`FunctionCode`とassignment `OperatorCode`をpackしました。built-in lookup、SET、method-as-instruction identity、lazy parsing、save互換性を維持し、shallow sizeを88 bytesから80 bytesへ削減
- Phase 13R35で`InstructionLine`のerror messageをR34のargument storageへunionし、shallow sizeを96 bytesから88 bytesへ削減。非InstructionLineのerror semantics、lazy parser、save互換性を維持
- Phase 13R34でlazy argument parsing前の`CharStream`をsource/offset snapshotへ変更し、reader由来のretained streamを除去。R32の`InstructionLine` 96-byte layout、lazy parser、save互換性を維持
- Phase 13R32で`LogicalLine`のerror flag/messageを1参照slotへ統合し、lazy parser・InvalidLine semantics・CALL error伝播を維持したまま大量retained行のshallow sizeを削減
- Phase 13R31で`LogicalLine`内部の`ScriptPosition?`専用slotを非nullable sentinelへ変更し、公開位置情報とsave互換性を維持したまま大量retained行のshallow sizeを削減
- 起動時の不定期な偽警告を抑止
- 大量のERBを使用するゲームの起動高速化
- 複数入力マクロの描画集約・内部処理の高速化
- 大量ログを開く画面の表示高速化と、閉じた後のメモリ解放
- マウスのサイドボタンによる前ページ・次ページ操作
- .NET 10への更新とWindows 11向けダークモード(VVII氏パッチをCRER氏が取り込み)
- AngleSharpおよびSQLite関連の既知の脆弱性に対応
- 必要なWinRT部品だけを残し、単一EXEのサイズを約47.2 MiBから約23.5 MiBへ削減
- ERB解析時の一時allocationを削減（CharStream、単純ReadString、行頭IdentifierWord）
- 大規模構成だけ起動完了後のmemory trimを行い、小規模構成ではskip
- HTMLのstrict color-only `FONT`入力を既存表示オブジェクトへ短絡し、対象外HTMLは従来どおりAngleSharpへfallback
- parsed ASTの整数literalでR25の-1〜255 immutable `SingleLongTerm` cacheを再利用し、retained managed heapを約63.2MB削減
- `InstructionLine`のSET左辺専用slotを既存`auxiliaryData`へ統合し、retained shallow sizeを120 bytesから112 bytesへ削減

# 配布フォルダについて
『配布/Emuera.NET_最終通常版』には、2026-09-24更新のLazyERB設定UI対応.NET 10正式single-file EXEを置いています。

- 対象: Windows 10 Version 2004以降 / Windows 11（x64）
- 必要ランタイム: .NET 10 Desktop Runtime（x64）
- 形式: フレームワーク依存・単一EXE
- EXE生成元source commit: `946032e74de19f5ff857430ae8c2c406183f74f7`
- 『Emuera.exe』: 配布用実行ファイル（24,696,554バイト、SHA-256 `C03E45AB5FD720441010EF4236D1C5AB7446387700A4BA310FF8EB5358967344`、ProductVersion `0.2.6.0+946032e74de19f5ff857430ae8c2c406183f74f7`）
- 『README.md』: 導入方法と採用機能
- 『SHA256SUMS.txt』: 配布物の改ざん確認用ハッシュ

変更の流れは[更新履歴](更新履歴.md)、現行正式版の変更理由と検証結果は`プロジェクト資料/修正履歴/#今回の修正説明_2026-09-24.md`を参照してください。

## 起動後メモリ整理

大規模ERB構成では、起動完了時のmanaged memoryが2 GiB以上の場合に限り、Aggressive / blocking / compacting GCを1回実行します。2 GiB未満ではtrimをskipします。大規模fixtureではWorking Set約1.50GB削減を確認した一方、同fixtureでは起動完了まで約1秒増加しました。削減量はゲーム構成によって異なり、全ゲームで同じ結果になるものではありません。

# 追加機能
実行ファイルと同じ階層に『patch_versions』というフォルダがある場合、
フォルダ内のテキストファイルの中身をログ出力時に書き出します。  
複数ファイルがある場合はファイル名順に書き出します。

# 不具合等連絡先
eraMegaten Discordサーバー（https://discord.gg/yQRYkNMuWr） にて、epizo宛にメッセージを送ってください。

※MinorShift氏、 妊）|дﾟ)の中の人氏、VVII氏、CRER氏は本バージョンの開発には携わっておりません
