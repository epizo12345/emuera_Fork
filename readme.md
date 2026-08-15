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

デバッグモードでの起動には、同梱の『デバックモード起動.bat』を上記と同じフォルダに置いてダブルクリックしてください。

# 主な修正
- 起動時の不定期な偽警告を抑止
- 大量のERBを使用するゲームの起動高速化
- 複数入力マクロの描画集約・内部処理の高速化
- 大量ログを開く画面の表示高速化と、閉じた後のメモリ解放
- マウスのサイドボタンによる前ページ・次ページ操作
- Controller / Gamepad対応（XInput、WinMM、Raw Input/HID、PS4系・Xbox系）
- ゲームパッドの× / A決定、○ / B戻る・キャンセル、△ / Y高速送り、D-pad / Left Stick操作、LB / RBページ移動、Start / OPTIONSのEnter相当
- .NET 10への更新とWindows 11向けダークモード(VVII氏パッチをCRER氏が取り込み)
- AngleSharpおよびSQLite関連の既知の脆弱性に対応
- 必要なWinRT部品だけを残し、単一EXEのサイズを約47.2 MiBから約23.5 MiBへ削減

# 配布フォルダについて
『配布/Emuera.NET_最終通常版』には、2026-07-31時点の.NET 10対応・動作確認済みEXEを置いています。

『配布/Emuera.NET_ゲームパッド対応_v1』には、Controller/Gamepad対応を含む最新のRelease単一EXEを置いています。標準操作は、× / Aが決定、○ / Bが戻る・キャンセル、△ / YがESC・マウス右クリック相当の高速送り、□ / Xが未使用、D-pad / Left Stickが移動、LB / RBがページ移動、Start / OPTIONSがEnter相当です。

- 対象: Windows 10 Version 2004以降 / Windows 11（x64）
- 必要ランタイム: .NET 10 Desktop Runtime（x64）
- 形式: フレームワーク依存・単一EXE
- 『Emuera.exe』: 配布用実行ファイル
- 『README.md』: 導入方法と採用機能
- 『SHA256SUMS.txt』: 配布物の改ざん確認用ハッシュ
- 『配布/Emuera.NET_最終通常版_採用済み修正.zip』: 変更したソースをフォルダ階層そのままで収録

変更の流れは[更新履歴](更新履歴.md)、2026-07-31分の変更理由と修正箇所は[今回の修正説明](今回の修正説明_2026-07-31.md)を参照してください。

# 追加機能
実行ファイルと同じ階層に『patch_versions』というフォルダがある場合、
フォルダ内のテキストファイルの中身をログ出力時に書き出します。  
複数ファイルがある場合はファイル名順に書き出します。

# 不具合等連絡先
eraMegaten Discordサーバー（https://discord.gg/yQRYkNMuWr） にて、epizo宛にメッセージを送ってください。

※MinorShift氏、 妊）|дﾟ)の中の人氏、VVII氏、CRER氏は本バージョンの開発には携わっておりません
