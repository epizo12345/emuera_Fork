# Emuera.NET 
著作者：VVII, MinorShift, 妊）|дﾟ)の中の人,epizo,CRER  
頒布者：epizo  
連絡先：eraMegaten Discordサーバー（https://discord.gg/yQRYkNMuWr） epizo 宛  

# 前書き
https://gitlab.com/alnatiyan/EmueraDotNet/-/tree/BugFix_Test?ref_type=heads  
を独自にForkしたバージョンです。  
上記にCRER氏がVVII氏のダークモードパッチや.NET10への更新などを取り込んだ版をForkしています。
ChatGPTsolを使用して修正しているため動作は保証できません。  
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
『配布/Emuera.NET_最終通常版』には、2026-08-15以降のmainに対応する.NET 10正式single-file EXEを置いています。

- 対象: Windows 10 Version 2004以降 / Windows 11（x64）
- 必要ランタイム: .NET 10 Desktop Runtime（x64）
- 形式: フレームワーク依存・単一EXE
- 『Emuera.exe』: 配布用実行ファイル（24,643,306バイト、SHA-256 `983713FB3FBB1E805291DAA11EFC9D17FE5F5600518803F6D7E3DD15407B6673`）
- 『README.md』: 導入方法と採用機能
- 『SHA256SUMS.txt』: 配布物の改ざん確認用ハッシュ

変更の流れは[更新履歴](更新履歴.md)、現在の変更理由と検証結果は`プロジェクト資料/修正履歴/#今回の修正説明_2026-08-22.md`を参照してください。

## 起動後メモリ整理

大規模ERB構成では、起動完了時のmanaged memoryが2 GiB以上の場合に限り、Aggressive / blocking / compacting GCを1回実行します。2 GiB未満ではtrimをskipします。大規模fixtureではWorking Set約1.50GB削減を確認した一方、同fixtureでは起動完了まで約1秒増加しました。削減量はゲーム構成によって異なり、全ゲームで同じ結果になるものではありません。

# 追加機能
実行ファイルと同じ階層に『patch_versions』というフォルダがある場合、
フォルダ内のテキストファイルの中身をログ出力時に書き出します。  
複数ファイルがある場合はファイル名順に書き出します。

# 不具合等連絡先
eraMegaten Discordサーバー（https://discord.gg/yQRYkNMuWr） にて、epizo宛にメッセージを送ってください。

※MinorShift氏、 妊）|дﾟ)の中の人氏、VVII氏、CRER氏は本バージョンの開発には携わっておりません
