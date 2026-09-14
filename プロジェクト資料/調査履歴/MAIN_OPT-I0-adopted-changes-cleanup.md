# MAIN_OPT-I0 — 採用済み変更の整理

## 結論

M3Aのproduction境界を保持し、HOLDと判定したS1 persistent PrimaryParse cache、R1 dynamic CALLF last-target cache、およびphase専用diagnosticをproduction sourceから撤去した。M4Aは採用済みのまま維持した。

## M3A production境界

- `WordCollection.FreezeSetSnapshot()`がSET左辺の`Word`参照と順序をexact-size `Word[]`へ保存する。
- `WordCollection.ThawSetSnapshot(Word[])`が初回SET parse時だけ一時`WordCollection`へ戻す。
- `InstructionLine`は既存`auxiliaryData`でsnapshotを保持し、`PopAssignmentDestStr()`でone-shot消費する。

Word clone、評価順、`Word.IsMacro`、lexer/parser、save/load、reload architectureは変更していない。M3Aの直接対象型に対する保持削減authorityは約23.90 MiBであり、ゲーム全体の削減量ではない。

## HOLD prototypeの撤去

- S1P0/S1P1: 永続PrimaryParse cache、CLI、loader hook、metrics hook、runner pass-throughを撤去した。既存Lazy ERB実装は維持した。
- R1P0/R1P0A: dynamic CALLF cache contract、sidecar、reload generation、CLI、metrics、runner pass-throughを撤去した。既存CALLF動作は維持した。
- M3B raw source canonicalization、M3C raw argument tail snapshotはproduction sourceへ採用していない。

production sourceを対象とするzero-leak searchでは、上記HOLD prototypeとM3B/M3Cの識別子は0件だった。

## 検証

| 項目 | 結果 |
|---|---|
| M3A focused snapshot test | PASS |
| M4A shared exact transporter focused test | PASS |
| normal Release | 30 warnings / 0 errors |
| `PERFORMANCE_METRICS` Release | 30 warnings / 0 errors |
| 決定論的N100 | PASS |
| 決定論的N1000 | PASS |

N100ではExpandedInput / InputDispatch=201 / 201、ErbRunCount=1,000、RandomCallCount=5,550、RandomTraceHash=`F1A61293AEF35FF7`を確認した。N1000でもauthorityのstate / display hashとsave countが一致した。

## 採用状態

M3AとM4Aは正式採用済みである。HOLD prototypeを再検討する場合は、独立した調査、互換性gate、採否判断をあらためて行う。
