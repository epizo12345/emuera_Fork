# MAIN_OPT-I1 — M3A / M4A統合

## 結論

M4AとM3Aをmerge commitなしのfast-forwardで`main`へ統合した。

```text
57a160e6f2871f28951e95bad68e3279f59e05a0 Optimize fixed variable transporter storage
7fbac4be214cd5ae61a5c4d653033ee79028c489 Optimize retained SET assignment storage
```

## 統合内容

- M3A: SET左辺の`Word`参照と順序をexact-size `Word[]` snapshotで保持し、初回parse時だけ一時`WordCollection`へ戻す。
- M4A: 定数添字の`FixedVariableTerm`がexact-size transporterを共有し、短い配列の欠けた添字を論理0として扱う。書込み時だけcopy-on-writeで3要素配列へ拡張する。

M3AはWord clone、compact→linked promotion、pointer変更を行わない。M4Aの通常pool / `Reset`経路は従来どおり3要素配列を使う。

## 検証

| 項目 | 結果 |
|---|---|
| normal Release | 30 warnings / 0 errors |
| `PERFORMANCE_METRICS` Release | 30 warnings / 0 errors |
| startup smoke | InputReady到達、parser exception 0、Lv2 warning 0 |
| 決定論的N100 | PASS |
| 決定論的N1000 | PASS |

N100 authority:

```text
ExpandedInput / InputDispatch = 201 / 201
ErbRunCount = 1000
RandomCallCount = 5550
RandomTraceHash = F1A61293AEF35FF7
StateSha256 = 6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8
DisplaySha256 = 83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7
SaveTo / save401 / failure = 49 / 49 / 0
```

N1000 authority:

```text
ExpandedInput / InputDispatch = 2001 / 2001
ErbRunCount = 9931
RandomCallCount = 55057
RandomTraceHash = B2FE06966216CF89
StateSha256 = 17210E4BD7087C5A699C02D6456ECDA190D790C09E91541E32B8AA4C429D2592
DisplaySha256 = B2B150BBAB1B1610AE945990321252544B72519CF28EF929BD6F42856FC5CE90
SaveTo / save401 / failure = 496 / 496 / 0
```

canonical `save219.sav` SHA-256は`6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B`である。
