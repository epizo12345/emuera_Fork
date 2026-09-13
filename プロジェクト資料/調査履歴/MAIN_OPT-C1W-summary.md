# MAIN_OPT-C1-W — CDFLAG whole-array lazy

調査・実装日: 2026-09-13
対象branch: `main-opt/diag-01-save219`
基礎HEAD: `82e0231710957e70d97f7a4cfed746e6d54a8902`

## 結論

**STRONG_GO**。CDFLAGのみをwhole-array lazy化し、`backing == null`を論理的な全要素0とした。save219ロード直後は5 / 100 / 450人のすべてでmaterialized backingが0件となり、450人の既知payload 194,400,000 bytes（185.39 MiB）を削減した。

セーブ形式・version・汎用2D reader・A1/A2は変更していない。row-lazy、M2、GC設定にも進んでいない。

## 実装

- `CharacterData`のCDFLAG初期allocationだけをskipし、ConstantDataから得た論理寸法を保持する。9×6000はproductionコードにhardcodeしていない。
- materializeはCDFLAG専用`EnsureCFlagBacking()`に集約した。一般2D lazy frameworkには拡張していない。
- null時のread / zero write / plus zero / all-zero bulkはallocationしない。nonzeroまたはraw array requestでのみmaterializeする。
- 一度materializeしたbackingは、全zero化後もnullに戻さない。raw arrayのidentity / alias semanticsを維持する。
- `CopyTo`、`SetSortKey`、binary save/load、extended text save/loadのCDFLAG経路だけをnull-awareにした。
- binary writerに論理all-zero 2Dを書く限定API、readerにCDFLAG専用のbounded lazy readerを追加した。他の2D配列は従来経路のままである。

変更ファイル:

- `Runtime/Script/Statements/Variable/CharacterData.cs`
- `Runtime/Script/Statements/Variable/VariableToken.cs`
- `Runtime/Utils/EraBinaryDataReader.cs`
- `Runtime/Utils/EraBinaryDataWriter.cs`

コードタグ: `[Emuera改修:MEM-C1W]`

## 正しさと互換性

ignored artifactのreflection self-testを、実際のEmuera assemblyに対して実行した。実装前assemblyではconstructorがCDFLAGを即時allocateするためRED、実装後は52 checks PASS。

確認内容:

- scalar read / set / plusと範囲外error
- bulk set / set-allと範囲外error
- raw arrayのshape・全zero・同一instance
- copyのnull / materialized 4組合せ
- sortのvalid zeroとinvalid index
- binaryのall-zero、nonzero、保存shape大小、shape外nonzero、`Zero` / `ZeroA1` / `EoA1`、direct byte zero
- extended textのall-zero出力互換、nonzero roundtrip、旧1802経路

binary byte比較:

- materialized all-zeroとnull logical zero: 0×0 / 0×5 / 3×0 / 3×7の4ケースでbyte完全一致
- nonzero materialized: sparse / denseの2ケースでC1前後のSHA-256完全一致

| ケース | C1前 | C1後 |
|---|---|---|
| sparse | `12E1C37D8AF9EC82A19F8500CFEE1103B5AA4CF0071003A5EAADEC3F9CD92BFE` | 同一 |
| dense | `6C3A003B59BF3B5D08DDB81E974116EC671A8451609532E5FA4F5A071288D177` | 同一 |

N100後`save401.sav`:

| fixture | A2 baseline | C1-W candidate |
|---:|---|---|
| 5人 | `B1FAB7A63206A1DD0ECA44B196EDCF74248A0D41172755292F95B2264C4591ED` | 完全一致 |
| 450人 | `65B0786E057B3CF3F4817064DF021B59769EF866DF60D891B3DB1C5D14FD0E19` | 完全一致 |

## 決定論的N100 semantic gate

5人・450人をbaseline / candidateでそれぞれ3runし、6runすべてで以下が一致した。

| 項目 | 結果 |
|---|---:|
| ExpandedInputCount | 201 |
| InputDispatchCount | 201 |
| ErbRunCount | 1000 |
| RandomCallCount | 5550 |
| RandomTraceHash | `F1A61293AEF35FF7` |
| SaveToCount / save401 count | 49 / 49 |

| fixture | StateSha256 | DisplaySha256 |
|---:|---|---|
| 5人 | `6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8` | `83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7` |
| 450人 | `84949E24B6FC6124DED8949BC6662D18749BBD4438D8E90A41C5ED021966B318` | `A7E5444A07E0C95DB702A38FE6B20D656285502084BAFC0542FF80AD383AE9EF` |

CDFLAG access counterは6runとも全項目0だった。

## メモリ

layoutはsave219ロード完了後・マクロ開始前のrecordをauthorityとした。100人のprocessはrecord取得後にN10へ進んだが、下記の値はN10開始前である。

| fixture | materialized count | CDFLAG payload | built-in backing: C1前 → C1後 | 削減 |
|---:|---:|---:|---:|---:|
| 5人 | 0 | 0 | 2,803,960 → 643,960 bytes | 2,160,000 bytes / 2.06 MiB |
| 100人 | 0 | 0 | 56,079,200 → 12,879,200 bytes | 43,200,000 bytes / 41.20 MiB |
| 450人 | 0 | 0 | 252,356,400 → 57,956,400 bytes | 194,400,000 bytes / 185.39 MiB |

GC managed memoryとWorking SetはGC timingで揺れるためsecondaryとし、上記のbacking payloadをprimary gateとした。

## 性能

各値は同一seed `314159265358979`、決定論的clock、各3runの中央値。時間はms。

| fixture / 項目 | A2 baseline | C1-W | 変化 |
|---|---:|---:|---:|
| 5人 Macro | 7,725.368 | 7,184.500 | -7.00% |
| 5人 SaveTo | 210.976 | 184.412 | -12.59% |
| 5人 Serializer | 187.841 | 163.878 | -12.75% |
| 5人 CharacterSerialization | 17.946 | 12.405 | -30.88% |
| 5人 NonCharacterSerialization | 168.351 | 151.087 | -10.25% |
| 5人 AllocatedBytes | 1,261,419,840 | 1,130,921,832 | -10.35% |
| 450人 Macro | 10,776.641 | 10,026.259 | -6.96% |
| 450人 SaveTo | 1,170.037 | 724.545 | -38.07% |
| 450人 Serializer | 1,135.959 | 691.197 | -39.15% |
| 450人 CharacterSerialization | 987.693 | 548.777 | -44.44% |
| 450人 NonCharacterSerialization | 147.823 | 142.028 | -3.92% |
| 450人 AllocatedBytes | 1,432,648,696 | 1,302,389,488 | -9.09% |
| 450人 ManagedBytesAfter | 1,011,119,008 | 862,039,352 | -14.74% |
| 450人 WorkingSetAfter | 1,808,646,144 | 1,661,865,984 | -8.12% |

5人のManagedBytesAfterは731,330,792 → 778,880,872 bytesだったが、ManagedBytesBeforeは893,971,144 → 795,233,872 bytesであり、GC timingの揺れを含む。primary payloadとMacro / save経路に重大な退行はない。

## buildと実機確認用EXE

- normal Release: PASS（既存警告30、error 0）
- diagnostic Release: PASS（既存警告30、error 0）
- `git diff --check`: PASS
- ignored candidate: `artifacts/release_candidates/MAIN_OPT-C1W-20260913/Emuera.exe`
- publish条件: Release / win-x64 / framework-dependent / single-file / 計測無効
- size: 24,680,170 bytes
- SHA-256: `A219121A62E18D11F861D7020EC784A87D3C86A44ABBCF291C97680C008BA398`
- ProductVersion: `0.2.6.0+82e0231710957e70d97f7a4cfed746e6d54a8902`
- 起動・save219 load smoke: PASS（fresh working copy上でN10まで完走、応答あり）
- ユーザー実機確認: PASS（上記C1-W candidate）

ProductVersionのsuffixは未commit sourceの内容を識別するものではなく、publish時のHEADを示す。上記candidateは正式配布物ではなく、MAIN_OPT-R1ではsource adoption commitから正式EXEを再publishした。

## MAIN_OPT-R1正式配布

- source commit: `39e7fd18f5c8e8685b2af23c5045ca0bae7751f3`
- publish条件: Release / win-x64 / framework-dependent / single-file / `PERFORMANCE_METRICS`無効
- 正式EXE: 24,680,170 bytes / SHA-256 `EC3F311BB7C58D490F7DD5031205FF3A2A2064472ABD8A90B4FD0FB9E5D2B3D8`
- ProductVersion: `0.2.6.0+39e7fd18f5c8e8685b2af23c5045ca0bae7751f3`
- source revision検証: PASS
- fresh working copyでの正式EXE起動・title到達・save219ロード・N10応答: PASS（スクリーンショットと`save401.sav`生成を確認。速度計測目的ではない）
- canonical 5人fixtureのsave219 SHA-256はsmoke前後で`6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B`のまま。更新されたsave401等はworking copy内のみ。

## 原本と作業状態

canonical `save219.sav` SHA-256:

| fixture | 事前 / 事後 |
|---:|---|
| 5人 | `6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B` |
| 100人 | `F1B7FCC7E9541E3B456937C154B327BF90F640F987D0EB534747A187D62D19A0` |
| 450人 | `8ED7326315EB6F19880211B8E42E7482094E2C06B3584C809E6EE5C02405D8E4` |

原本fixtureは起動せず、測定はfresh working copyだけで行った。MAIN_OPT-R1でC1-Wを正式採用した。M2は通常5人側の次候補として保留し、未実装。

## 次候補

C1-Wで450人のCDFLAG payloadは解消した。M2のlazy argument / assignment snapshot compact化は実装せず保留し、**通常5人側の次候補**とする。本phaseでは次のproduction最適化へ進まない。

## MAIN_OPT-C1W-AUDIT1 実プレイ追跡

run-04の元JSONLによる追跡結果は[`MAIN_OPT-C1W-AUDIT1-null-safety-and-lifetime.md`](MAIN_OPT-C1W-AUDIT1-null-safety-and-lifetime.md)に記録した。`AUDIT1_RUNTIME_CORE = PASS`、`AUDIT1_FORMAL = PASS_WITH_CHECKPOINT_NOTE`（P4 manual snapshot recordなし）。実ERBのnull readは0を返し、readではmaterializeせず、同一キャラのnonzero write時にキャラ単位でmaterializeした。save/load後に復元されたのはnonzeroを持つ7人のみ。7人materialize後も443 / 450人が未materializedで、remaining avoidedは191,376,000 bytes（約182.51 MiB）。これは恒久削減ではなく、未materialized人数に応じて変化する。
