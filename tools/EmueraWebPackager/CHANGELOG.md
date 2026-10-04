# Emuera Web Packager — 更新履歴

## 1.0.5 — 2026-10-04

- DIV／HTML Islandの不要な折返しによるターンエンド確認の枠外表示を修正。
- 入れ子absolute DIVをゲーム画面基準へ揃え、神格習合のカード位置ずれを修正。
- 論理描画領域1512×864pxを描画・CLIENTHEIGHT・Scale Fit・入力座標で統一。神格習合の決定／戻るを表示・操作可能にした。
- HTML_TAGSPLITをNative互換で実装し、NGOイベントの表示中の未対応例外を解消。
- NGO上端約2行の見切れは許容制約。実ゲームの後続進行全体は未確認。
- Packager EXE、template識別、配布資料、出自情報を1.0.5へ統一。

## 1.0.4 — 既存公開版を保持

- PRINTC／PRINTLCと連結HTMLボタンの物理行配置、nonbutton／Islandのtooltipを改善。
- QUIT後の正常終了案内と、保存成功後のタイトル復帰を追加。

## 1.0.3／1.0.2 — 既存履歴を保持

- マクロ入力・待機互換性、赤いマクロ停止と停止後の通常操作。
- 表示、セーブ入出力後のタイトル復帰、WARNING、Scale Fit、.NET 10.0.12。
