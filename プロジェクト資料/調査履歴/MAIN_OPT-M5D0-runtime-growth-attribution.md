# MAIN_OPT-M5D0 — Runtime Growth Attribution

最終更新: 2026-09-14
判定: `M5D0_COMPLETE`

M5D0は診断だけのphaseであり、production memory最適化、lazy ERBのeviction、明示GCは実施していない。M4A adoption cleanupはこの採取完了後に別phaseで行い、M5D0用instrumentationをsourceから除去した。

## Static Proven

- lazy ERBは起動時にstubをindexし、実行時に `EnsureLazyLoaded` でfile単位にhydrateする。M5D0はindex済みfile数、loaded file数、pending label数、hydrate済みsource bytes / logical lines / `InstructionLine`数、parse済みfunction数を記録する。
- `displayLineList` は `Config.MaxLog` を上限として保持する。engine sourceのdefaultは5,000のままである。今回のA/Bはfixture側user設定 `Data/emuera.config` の「履歴ログの行数」だけを50,000と5,000に分けた。
- HTML islandは `_htmlElementListDict` 内の保持数を、graphicsは `AppContents.gList` のslot数、created数、幅×高さ×4の概算pixel bytesを記録する。これらの観測は `PERFORMANCE_METRICS` buildだけに存在する。
- 起動終了、lazy file hydration、lazy function parse、表示5,000行刻み、HTML island閾値、graphics slotの2べき閾値、100回ごとのinput待機、macro終了、shutdownでJSON Lines snapshotを出す。`--RuntimeGrowthLog` 単独ではbenchmark deterministic seed / clock / diagnosticsを有効化しない。

## Automatic MaxLog Measured

canonical `fixture` から作ったfresh working copyを使用した。`save219.sav` は全copyで次と一致した。

```text
6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B
```

条件は `N1000`、seed `314159265358979`、`BenchmarkDiagnostics` / deterministic clock有効、AB→BAの1 pairである。Aは `MaxLog=50,000`、Bは `MaxLog=5,000`。比較対象外の表示hash・表示line count以外は全4runで一致した。

| run | MaxLog | macro ms | Expanded / Dispatch / ERB | Random / trace | StateSha256 | display lines | MacroEnd managed bytes |
| --- | ---: | ---: | --- | --- | --- | ---: | ---: |
| A1 | 50,000 | 62,043.320 | 2,001 / 2,001 / 9,931 | 55,057 / `B2FE06966216CF89` | `17210E4B…429D2592` | 50,000 | 822,746,360 |
| B1 | 5,000 | 62,156.901 | 2,001 / 2,001 / 9,931 | 55,057 / `B2FE06966216CF89` | `17210E4B…429D2592` | 5,000 | 672,158,576 |
| B2 | 5,000 | 60,934.085 | 2,001 / 2,001 / 9,931 | 55,057 / `B2FE06966216CF89` | `17210E4B…429D2592` | 5,000 | 700,181,992 |
| A2 | 50,000 | 62,050.302 | 2,001 / 2,001 / 9,931 | 55,057 / `B2FE06966216CF89` | `17210E4B…429D2592` | 50,000 | 850,160,456 |

同じ設定2runの中央値（偶数2点の中間値）は、50,000行が 836,453,408 bytes、5,000行が 686,170,284 bytesであり、差は **150,283,124 bytes（約143.32 MiB）** だった。これはMaxLogを小さくした同条件での観測値であり、display保持以外の全要因を排除した原因確定ではない。

全runのMacroEndではlazy loaded file=4、parsed function=9、hydrate source bytes=2,180,716、hydrate logical lines=43,428、hydrate `InstructionLine`=43,350で一致した。graphicsもslot=1,729、created=79、概算pixel bytes=37,950,728で一致した。したがってこのN1000区間では、MaxLog差に対応して変化した直接観測値は表示line countであり、lazy hydration / graphicsの累積観測値は同一だった。

runtime-growth record数はAが各78、Bが各69だった。Aは表示5,000〜50,000の10個のdisplay milestoneを通過し、Bは5,000で上限に達するため、その後のdisplay milestoneを生成しない。これは記録件数の差であってゲーム進行差ではない。

## Manual Capture Measured

package内の元JSONLをauthorityとし、実プレイcaptureを集計した。プロセス開始からshutdownまで約14.50分（最初のruntime snapshotからshutdownまでは14.43分）で、702件のMacroEndを含む。

| 項目 | 実測 |
| --- | ---: |
| Lazy loaded files | 45 |
| Hydrated source bytes | 18,165,425 B |
| Hydrated logical lines | 333,950 |
| Hydrated `InstructionLine` | 333,153 |
| Parsed functions | 167 |
| Display MaxLog / peak | 50,000 / 50,000 |
| Shutdown display lines | 48,108 |
| HTML island retained lines peak / shutdown | 2 / 0 |
| Graphics created peak / shutdown | 355 / 355 |
| Graphics pixel概算 peak / shutdown | 76,514,504 B（MacroEnd） / 48,848,320 B |
| Shutdown managed bytes | 832,239,528 B |
| Shutdown Working Set | 1,522,683,904 B |
| Shutdown private bytes | 1,689,812,992 B |

実プレイ中に表示行数50,000へ到達し、shutdown時点では48,108行だった。HTML islandの保持lineは最大2、shutdown時0であり、主要な保持要因とは観測されなかった。Graphicsはshutdown時355件がcreatedで、pixel概算は約46.58 MiB残っていた。pixel概算のピークはMacroEnd snapshotの354件・76,514,504 Bだった。

automatic MaxLog A/BのMacroEnd managed中央値差は50,000行側が5,000行側より **150,283,124 B（約143.32 MiB）** 大きかった。A/BのN1000 semantic値、lazy hydration累積値、graphics累積値は一致し、表示line countだけが設定どおり異なった。この診断上、`DISPLAY_BACKLOG = LARGE_CONTRIBUTOR` と分類する。これはmanaged snapshot差による寄与分類であり、表示line objectだけの厳密なretained payload量ではない。

実プレイcaptureではlazy ERBは45 file / 約17.32 MiBのsourceをhydrateし167 functionをparseした。増加は実在するが、このcaptureでは暴走的増加とは判定しない: `LAZY_RUNTIME_GROWTH = REAL_BUT_NOT_RUNAWAY_IN_CAPTURE`。HTML islandは `HTML_ISLAND = NOT_PRIMARY`、Graphicsはshutdown時約46.58 MiBで `GRAPHICS = SECONDARY_DYNAMIC` とする。

managed bytes、Working Set、private bytesは異なる指標であり、同じheap容量として比較・加算しない。

## Unknown

- 実際の長時間プレイ中のlazy ERB、HTML island、graphics、表示bufferのretain lifetimeは未測定である。
- managed heap / committed heap / Working Set / private bytesはGC時機とallocator状態を含むため、単独のsnapshotからowner別のretained bytesを確定できない。
- MaxLog差で観測した約143.32 MiBを、表示line objectだけの厳密payloadとして扱わない。type / root / retained graphが必要なら次のheap調査で確認する。
- `displayLineList` 以外のunbounded ownerを削減するproduction変更は、この結果から自動では開始しない。

## Manual Capture Artifact

実プレイ用packageを作成し、手動captureを完了した。fixtureはcanonical fixtureのfresh copyであり、canonical save219 SHA-256は不変だった。

```text
artifacts/manual_tests/runtime-growth-realplay/
  build/
  fixture/
  01_START_RUNTIME_GROWTH_CAPTURE.cmd
  README.txt
```

launcherはfresh `fixture` の `Data` を `--ExeDir` に渡し、`--RuntimeGrowthLog` だけを追加した。`--BenchmarkLog`、`--BenchmarkDiagnostics`、決定論的seed、決定論的clockは付けていない。集計authorityはpackage直下の `runtime-growth.jsonl`。

## 保全と検証

- runnerには空指定時の既存behaviorを変えない optional `-RuntimeGrowthLog` pass-throughだけを一時追加し、M5D0完了後に除去した。
- diagnostic Releaseとnormal Releaseのbuild、runtime growthログのN10 smoke、N1000 AB→BAの1 pair、実プレイcaptureを完了した。
- fixture原本、production memory representation、A1/A2、C1-W、M4Aのproduction最適化コードは変更していない。
- stage、commit、pushは実施していない。
