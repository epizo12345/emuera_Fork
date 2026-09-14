# MAIN_OPT-I2 — M3A / M4A 日本語資料・コードコメント整備

## コードコメント

- `Runtime/Script/Parser/WordCollection.cs`と`Runtime/Script/Statements/LogicalLine.cs`へ`[Emuera改修:MEM-M3A]`を追加した。
  - SET左辺を初回parseまでexact-size `Word[]` snapshotで保持すること
  - `Word`の参照同一性と順序を保つこと
  - 一時復元後にone-shot slotを解放すること
- `Runtime/Script/Statements/Variable/VariableTerm.cs`と`Runtime/Script/Statements/Variable/VariableToken.cs`へ`[Emuera改修:MEM-M4A]`を追加した。
  - `FixedVariableTerm`がexact-size transporterを共有すること
  - 欠けた添字は論理0であること
  - 書込み時だけcopy-on-writeすること

## 資料

`更新履歴.md`、`01_仕様書.md`、`02_引き継ぎ書.md`、`05_採用済み変更ファイル一覧.md`、`06_コード案内.md`、2026-09-15修正説明を更新した。M3A/M3A1/I0/I1の調査記録も公開用資料として整理した。

M3Aの23.90 MiBは直接対象型のshallow保持量であり、ゲーム全体のメモリ削減量ではない。M4Aの約6.225 MiB retained見積りと約18.806 MiB startup allocation見積りは対象が異なるため合算しない。`System.Int64[]`約7.69 MiBはM4A単独の効果ではない。

## 検証

- normal Release / `PERFORMANCE_METRICS` Release: 30 warnings / 0 errors
- M3A focused snapshot tests: PASS
- M4A shared exact transporter focused tests: PASS
- 決定論的N100: PASS
- canonical `save219.sav` SHA-256: `6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B`

この段階でruntime実装の変更はない。
