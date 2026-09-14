# MAIN_OPT-I3 — 公開前整理・正式配布EXE更新

## 公開用資料の整理

Commit A `a63b594237825f4b6a9e5335ed13f4ea32175c0a`で、M3A/M4Aの2026-09-15修正説明を従来形式へ統一し、公開不要な絶対パス、内部運用状態、環境固有の記述を資料から整理した。

M3A/M4Aの技術判断、採用commit、測定値、互換性検証は保持した。M3Aの約23.90 MiBは直接対象型のshallow保持量であり、ゲーム全体の削減量ではない。M4Aのretained約6.225 MiBとstartup allocation約18.806 MiBは別の見積りであり、合算していない。

## 配布EXE

正式EXEはCommit Aのclean sourceからRelease / win-x64 / framework-dependent / single-file / `EnablePerformanceMetrics=false`でpublishした。

| 項目 | 値 |
|---|---|
| EXE生成元source revision | `a63b594237825f4b6a9e5335ed13f4ea32175c0a` |
| ProductVersion | `0.2.6.0+a63b594237825f4b6a9e5335ed13f4ea32175c0a` |
| EXE size | 24,684,266 bytes |
| EXE SHA-256 | `B68A494119816D4D788F5B7A5B56AC786BA45B179D0EE00304F283E561B388CF` |
| source revision verification | PASS |
| startup smoke | 3/3 PASS、Lv2 warning 0、InputReady到達 |
| save219 / N10 smoke | PASS、応答性確認済み |

検証にはcanonical fixtureを直接起動せずfresh working copyを使用した。canonical `save219.sav` SHA-256は`6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B`を維持した。

## 公開監査

- I3では絶対パス、秘密情報、主要な内部運用記述を整理した。後続のI4監査で引き継ぎ書と編集ルールに対話固有のGit運用表現が残っていることを確認し、追加で一般化した。
- I3時点の公開資料に含まれる利用者固有の絶対パス: 0件
- 秘密情報らしきtracked text / filename: 0件
- game data、fixture、raw trace、benchmark artifact、metrics EXEのtracked追加: 0件

`UI/Framework/Forms/ConfigDialog.cs`の`c:\Program Files`はWindows標準ダイアログの既定値であり、利用者固有の情報ではないため変更していない。

## 配布状態

`配布/Emuera.NET_最終通常版/Emuera.exe`、同梱README、`SHA256SUMS.txt`を更新した。Commit BのSHAはGit履歴を正とする。
