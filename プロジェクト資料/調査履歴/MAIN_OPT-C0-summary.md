# MAIN_OPT-C0 — CDFLAG残調査

調査日: 2026-09-13
対象branch: `main-opt/diag-01-save219`
基礎HEAD: `82e0231710957e70d97f7a4cfed746e6d54a8902`

## 結論

5 / 100 / 450人すべてで、save219ロード直後のCDFLAGは全セル0だった。450人条件でwhole-array lazyが削減できるpayloadは185.39 MiBと見積もられ、基準の100 MiBを超えるため、次段階は**C1-W（whole-array lazy）の設計・実装を推奨**する。ただし本調査では実装していない。

計測コードは`PERFORMANCE_METRICS`付きの診断Releaseに限定した。CDFLAGの本番表現、reader、セーブ形式は変更していない。A1/A2の最適化コードも変更していない。

## 1. 静的な到達経路

`Runtime`を`rg`で検索し、CDFLAG、`VariableCode.CDFLAG`、`DataIntegerArray2D`、`GetArrayChara`、`CharaInt2DVariableToken`、`setValueAll2D`の参照を追跡した。アクセス経路は次のとおり。

- 通常のscalar read/write/PlusValueは`CharaInt2DVariableToken`から`CharacterData.DataIntegerArray2D`へ到達する。
- `VARSET`は`SetValueAll`から`setValueAll2D`へ到達する。
- `COPYCHARA` / `ADDCOPYCHARA`は`CharacterData.CopyTo`、SORTCHARAの2D keyは`CharacterData.SetSortKey`を通る。
- 保存・状態hashは`CharacterData.SaveToStreamBinary`、ロードは`CharacterData.LoadFromStreamBinary`を通る。
- FINDCHARA / SUMCARRAY / CMATCH / MAXCARRAYの2D character経路はscalar accessだった。

追加のraw-array escapeとして、`CharaInt2DVariableToken.GetArrayChara`から`Creator.Method.ReadColormatrix`へ到達する経路が見つかった。`GDRAWG`および`GDRAWCIMG`の任意カラーマトリクス引数はcharacter 2D配列を受け取り、5×5領域を読む。変数名によるCDFLAG拒否はないため、CDFLAGを渡せばraw backingへの要求が到達可能である。一方、REF引数の汎用経路はcharacter変数が参照元として許可されないため、追加の到達経路には数えない。

今回のdeterministic N100では、5人・450人とも`rawArrayRequestCount=0`だった。この実行経路では上記graphics経路は使われていない。

## 2. fixture ERBのCDFLAG使用

`.ERB`を対象にコメント行・コメント部分を除いた字句ベースの集計を行った。3 fixtureのERB内容と集計は同一だった。読取・書込はcompound assignmentを両方に数える概数である。

| 項目 | 件数 |
|---|---:|
| ERB総数 | 9,458 |
| CDFLAG使用ファイル | 31 |
| CDFLAG参照箇所 | 207 |
| 読取概数 | 130 |
| 書込概数 | 147 |
| compound assignment（読取・書込の両方に含む） | 72 |
| VARSETによるbulk write | 2 |
| row添字（定数 / 動的） | 207 / 0 |
| column添字（数値定数 / 動的） | 67 / 140 |

row 0～8別の参照箇所数:

| row | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 合計 |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 参照数 | 65 | 0 | 0 | 0 | 0 | 0 | 12 | 119 | 11 | 207 |

row 1～5は静的検索上の使用がなかった。これはfixture全ERBの字句上の集計であり、実行時のアクセス回数を意味しない。

## 3. save219ロード後の疎性

各fixtureのfresh run copyを作り、save219のロード完了後に診断Releaseから既存CDFLAG backingをread-only scanした。CDFLAG用のbacking配列は追加allocateしていない。診断用の小さな集計配列・統計リストのみを使用した。

| fixture | totalCells | nonZeroCellCount | density | charactersWithAnyNonZero | charactersAllZero | rowsWithAnyNonZero | rowsAllZero | character別 min / median / p90 / max |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 5人 | 270,000 | 0 | 0% | 0 | 5 | 0 | 45 | 0 / 0 / 0 / 0 |
| 100人 | 5,400,000 | 0 | 0% | 0 | 100 | 0 | 900 | 0 / 0 / 0 / 0 |
| 450人 | 24,300,000 | 0 | 0% | 0 | 450 | 0 | 4,050 | 0 / 0 / 0 / 0 |

各fixtureのrow 0～8はすべて同じ結果だった。

| row | 5人: 非zeroキャラ / 非zero cell / density | 100人: 非zeroキャラ / 非zero cell / density | 450人: 非zeroキャラ / 非zero cell / density |
|---:|---:|---:|---:|
| 0 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 1 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 2 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 3 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 4 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 5 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 6 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 7 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |
| 8 | 0 / 0 / 0% | 0 / 0 / 0% | 0 / 0 / 0% |

null backingおよび9×6000以外のshapeは、3 fixtureとも0件だった。

## 4. deterministic N100アクセス集計

5人・450人はseed `314159265358979`、決定論的clock有効、`--BenchmarkDiagnostics`有効で各1回実行した。アクセスごとのログは出さず、集計値だけを記録した。

| 項目 | 5人 | 450人 |
|---|---:|---:|
| readCount | 0 | 0 |
| writeCount | 0 | 0 |
| plusCount | 0 | 0 |
| bulkWriteCount | 0 | 0 |
| setAllCount | 0 | 0 |
| rawArrayRequestCount | 0 | 0 |
| distinctCharactersRead / Written | 0 / 0 | 0 / 0 |
| distinctRowsRead / Written | 0 / 0 | 0 / 0 |

100人fixtureも診断runは行い、6種類のアクセスcounterはすべて0だった。100人のERB実行回数等は5 / 450人のgate対象値とは異なるため、ここではpost-load scan結果のみを比較対象とする。

## 5. semantic gate

5人・450人とも既存のdeterministic N100 gateはPASSした。

| 項目 | 5人 | 450人 |
|---|---:|---:|
| ExpandedInputCount | 201 | 201 |
| InputDispatchCount | 201 | 201 |
| ErbRunCount | 1,000 | 1,000 |
| RandomCallCount | 5,550 | 5,550 |
| RandomTraceHash | `F1A61293AEF35FF7` | `F1A61293AEF35FF7` |
| StateSha256 | `6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8` | `84949E24B6FC6124DED8949BC6662D18749BBD4438D8E90A41C5ED021966B318` |
| DisplaySha256 | `83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7` | `A7E5444A07E0C95DB702A38FE6B20D656285502084BAFC0542FF80AD383AE9EF` |
| SaveToCount / save401 | 49 / 49 | 49 / 49 |

save index別countは両fixtureとも`{"401":49}`、failureは0だった。100人fixtureはload後疎性とアクセス集計に使用し、この5 / 450人semantic gateの対象には含めていない。

## 6. memory projectionと推奨

payloadのみで計算し、配列header・参照などのoverheadは別扱いとして含めていない。

| 方式 | 450人payload | 現状からのpayload削減 |
|---|---:|---:|
| 現状: 450 × 432,000 bytes | 194,400,000 bytes = 185.39 MiB | — |
| whole-array lazy: 非zeroキャラ0 × 432,000 bytes | 0 bytes | 185.39 MiB |
| row-lazy参考値: 非zero row 0 × 6,000 × 8 bytes | 0 bytes | 185.39 MiB |

whole-array lazyだけで100 MiB以上のpayload削減が見込めるため、**C1-Wを推奨**する。raw-array escapeとcopy/save/load/sort経路を設計時に扱う必要がある。C1実装には着手していない。

## 7. build・fixture・Git確認

- diagnostic Release build: PASS
- normal Release build: PASS
- 5人 / 450人 deterministic semantic gate: PASS
- `git diff --check`: PASS
- fixture原本のsave219 SHA-256は実行前後で一致した。

| fixture原本 | save219 SHA-256 |
|---|---|
| 5人 | `6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B` |
| 100人 | `F1B7FCC7E9541E3B456937C154B327BF90F640F987D0EB534747A187D62D19A0` |
| 450人 | `8ED7326315EB6F19880211B8E42E7482094E2C06B3584C809E6EE5C02405D8E4` |

すべての測定は`artifacts/diag_tests/`配下に作成したfresh run copyで実行し、fixture原本のGit statusに変更はない。stage / commit / pushは行っていない。
