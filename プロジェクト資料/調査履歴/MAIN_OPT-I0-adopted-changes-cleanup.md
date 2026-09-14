# MAIN_OPT-I0 — 採用済み変更の整理とmain統合準備

## 結論

`READY_FOR_I1_MAIN_INTEGRATION`

dirty差分を所有phaseごとに監査し、HOLD済みprototypeとphase専用diagnosticを撤去した。HEADにcommit済みのM4Aを維持し、未commitのproduction source差分はM3A Frozen SET Snapshotの2ファイルだけになった。UNKNOWN差分は0件である。

## Initial Git State

- branch: `main-opt/diag-01-save219`
- HEAD: `57a160e6f2871f28951e95bad68e3279f59e05a0`
- main: `a7e3962795161b53596e8daaed9bbadab671b909`
- origin/main: `a7e3962795161b53596e8daaed9bbadab671b909`
- `git status --short`: 29 entry（tracked変更10、未追跡entry 19）
- 未追跡実ファイル: 10,066（`fixture_450` 10,045、資料16、prototype source 5）
- tracked diff: 10 files、645 insertions / 50 deletions
- staged diff: 0
- 開始時`git diff --check`: PASS

cleanup前に`artifacts/diag_tests/i0-main-integration-prep/`へ次を保存した。

- `pre-cleanup.patch`: `git diff --binary HEAD`の全tracked差分
- `status-before.txt`
- `diff-stat-before.txt`
- `diff-name-status-before.txt`
- `untracked-before.txt`
- `head-before.txt`
- `untracked-backup/`: 未追跡source 5件と資料16件を相対path付きで退避

`fixture_450`と巨大artifactは複製していない。既存artifact・trace・historical reportは削除していない。

## Full Dirty Inventory

### Tracked file / hunk分類

| file | 分類 | 判定と処置 |
|---|---|---|
| `Runtime/Script/Parser/WordCollection.cs` | `KEEP_M3A_PRODUCTION` + `REMOVE_PHASE_DIAGNOSTIC` | `FreezeSetSnapshot` / `ThawSetSnapshot`を保持し、`GetBenchmarkSnapshotStorage`と`WordSnapshotBenchmarkStorage`を撤去 |
| `Runtime/Script/Statements/LogicalLine.cs` | `KEEP_M3A_PRODUCTION` + `REMOVE_PHASE_DIAGNOSTIC` | SET constructor / one-shot thawを保持し、M3A/M3B用census拡張を撤去 |
| `Runtime/Script/Process.cs` | `REMOVE_R1_HOLD` + `REMOVE_PHASE_DIAGNOSTIC` | R1 generation invalidationとM3A/M3B source/snapshot censusを撤去。HEADのgeneric metricsは維持 |
| `Program.cs` | `REMOVE_S1_HOLD` + `REMOVE_R1_HOLD` | S1 cache/self-test CLIとR1 cache/precise allocation CLIを撤去 |
| `Runtime/Script/Loader/ErbLoader.cs` | `REMOVE_S1_HOLD` | S1 cache begin/complete/abort、decode/restore、lazy fallback prototypeを一式撤去。正式Lazy ERBはHEADどおり維持 |
| `Runtime/Script/Statements/Argument.cs` | `REMOVE_R1_HOLD` | CALLF last-target sidecarを撤去 |
| `Runtime/Script/Statements/Instraction.Child.cs` | `REMOVE_R1_HOLD` | dynamic CALLF cache分岐を撤去しHEAD動作へ復帰 |
| `Runtime/Utils/PerformanceMetrics.cs` | `REMOVE_S1_HOLD` + `REMOVE_R1_HOLD` | S1 cache記録とR1 cache/precise allocation記録を撤去 |
| `Emuera以外(開発補助ツールなど)/マクロ性能テスト.ps1` | `REMOVE_S1_HOLD` + `REMOVE_R1_HOLD` | cache、candidate、precise allocation、trace providerのoptional pass-throughを撤去 |
| `Emuera以外(開発補助ツールなど)/TraceAllocationAnalyzer/TraceAllocationAnalyzer.csproj` | `SEPARATE_DEV_TOOL_CHANGE` | R1D0のmachine-local 10系tool path変更をM3A/M4A統合対象から除外しHEADへ復帰 |

KEEPを含まない上記8 tracked fileは、各`git diff HEAD -- <file>`を全文確認してからfile名を明示してHEADへ戻した。M3Aを含む2 fileはhunk単位で編集した。一括restore/reset/checkout/stash/cleanは使用していない。

### Untracked分類

| 対象 | 実ファイル数 | 分類 | 処置 |
|---|---:|---|---|
| `Runtime/Script/Cache/*.cs` | 4 | `REMOVE_S1_HOLD` | backup後に撤去 |
| `Runtime/Script/Statements/DynamicCallFLastTargetCacheContract.cs` | 1 | `REMOVE_R1_HOLD` | backup後に撤去 |
| M3A～M4A、S1、R1等の調査報告 | 16 | `PRESERVE_HISTORICAL_DOC` | 全て保持 |
| `fixture_450/` | 10,045 | 既存fixture | 変更・削除・複製なし |

`UNKNOWN_DO_NOT_TOUCH`は0件だった。

## M3A Production Diff

最終production差分は次の2 fileだけである。

- `Runtime/Script/Parser/WordCollection.cs`
  - `FreezeSetSnapshot()`でSET左辺の現在順序をexact-size `Word[]`へ保存
  - `ThawSetSnapshot(Word[])`で初回SET parse時だけ一時`WordCollection`へ戻す
- `Runtime/Script/Statements/LogicalLine.cs`
  - constructorで`dest?.FreezeSetSnapshot()`を保持
  - `PopAssignmentDestStr()`で`auxiliaryData = null`を先に行うone-shot semanticsを保持
  - `Word[]` thawと旧`WordCollection` fallbackを保持

Word clone、評価順、`Word.IsMacro`、lexer/parser、save/load、reload architectureは変更していない。M3Aの既存実測保持削減authorityは約23.90 MiBである。

review用patch:

- `artifacts/diag_tests/i0-main-integration-prep/M3A-production.patch`
- SHA-256: `B20B0B6865058E6C0A97AF0C259BA4045E763974184A42C976BFF99E2BD0DD71`
- 2,893 bytes
- 対象は上記2 fileだけ

## M3A Diagnostic Cleanup

dirty追加だった次を撤去し、HEAD由来のgeneric M2 censusは維持した。

- `GetBenchmarkSnapshotStorage(Word[])`
- `WordSnapshotBenchmarkStorage`
- `assignmentFrozenSnapshot` classification
- `RawSource`
- `RawSourcePosition`
- `FileId`
- `AssignmentFrozenSnapshot`
- `Process.cs`のsnapshot/source identity集計

## M4A HEAD verification

HEAD `57a160e6`は`Optimize fixed variable transporter storage`であり、M4A productionは既にHEAD側にある。M4A関連production fileをI0では編集せず、最終dirty diffにも現れない。M4A focused testもPASSした。

## S1 Cleanup

S1P0/S1P1は`HOLD_S1_PERSISTENT_CACHE`に従ってsource・CLI・loader hook・metrics hook・runner pass-throughを撤去した。既存Lazy ERB実装は変更していない。S1 reportとignored artifactは保持した。

## R1 Cleanup

R1P0/R1P0Aは`HOLD_CALLF_LAST_TARGET_CONFIRMED`に従ってcache contract、sidecar、CALLF分岐、reload generation、CLI、metrics、runner pass-throughを撤去した。constant CALLF prebindとHEADのdynamic CALLF動作は維持した。R1D0 tool projectのlocal path差分もproduction統合対象から除外した。

## Other Diagnostic Cleanup

M3A/M3B用の一時censusを撤去した。M3Bの`MEM-13R42` / raw source canonicalization、M3Cの`MEM-13R43` / raw tail snapshotはcleanup開始時点でもproduction sourceに無く、終了時検索でも0件だった。

## Zero-leak Search

`Program.cs`、`Runtime/`、開発補助script/toolを、historical report・artifact・bin/objを除外して次の識別子で検索した。

```text
ErbPrimaryParseCache / CachedErb / CachedWord / CachedSubWord
ErbParseCache / S1P0 / S1P1
DynamicCallFLastTarget / BenchmarkDynamicCallFLastTarget
BenchmarkPreciseAllocatedBytes
ErbResolutionGeneration / InvalidateErbResolution
MEM-13R42 / MEM-13R43
CanonicalizeRawArgumentSource(s) / TrimRawArgumentSourceToTail
```

結果は0件（`rg` exit 1 = no match）。従ってS1 runtime/source refs = 0、R1 runtime/source refs = 0、M3B/M3C production refs = 0である。

## M3A Preservation Search

production sourceの検索結果は次の4箇所で、必要経路がすべて残っている。

```text
WordCollection.cs: FreezeSetSnapshot definition
WordCollection.cs: ThawSetSnapshot definition
LogicalLine.cs: constructor FreezeSetSnapshot call
LogicalLine.cs: PopAssignmentDestStr ThawSetSnapshot call
```

## Focused tests

- M3A focused snapshot tests: PASS
- M4A shared exact transporter focused tests: PASS

## Build

cleanup済みsourceからfresh non-incremental buildを行った。

| build | 結果 | warning | error |
|---|---|---:|---:|
| normal Release (`EnablePerformanceMetrics=false`) | PASS | 30 | 0 |
| metrics Release (`EnablePerformanceMetrics=true`) | PASS | 30 | 0 |

warningは既存30件で、build間の差および新規warningは0件だった。最初のsandbox内buildはWindows SDK検索先の読取権限でコンパイル前に停止したため、同一commandを許可済み環境で再実行して上記結果を得た。

## Startup Smoke

canonicalから作成したstartup専用fresh copyでnormal Releaseを1回実行した。

- status: OK / exit 0
- `InputReady`到達: YES（`Init:End`確認）
- parser exception: 0
- Lv2 warning: 0
- Init: 4,537 ms
- ERB load: 1,868 ms

速度の採用判断には使用しない。

## N100

fresh copy、seed `314159265358979`、deterministic clock、`BenchmarkDiagnostics`、`InternalMetrics`でofficial N100を1回実行した。

| 項目 | 実測 | authority |
|---|---:|---:|
| ExpandedInput / InputDispatch | 201 / 201 | 201 / 201 |
| ErbRunCount | 1,000 | 1,000 |
| RandomCallCount | 5,550 | 5,550 |
| RandomTraceHash | `F1A61293AEF35FF7` | 同左 |
| StateSha256 | `6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8` | 同左 |
| DisplaySha256 | `83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7` | 同左 |
| SaveTo / save401 / failure | 49 / 49 / 0 | 49 / 49 / 0 |

結果: PASS。macro elapsedは7,383.867 msだが、性能比較には使用しない。生成`save401.sav` SHA-256は`B1FAB7A63206A1DD0ECA44B196EDCF74248A0D41172755292F95B2264C4591ED`。

## N1000

N100 PASS後、別のfresh copyで同じdeterministic条件のN1000を1回実行した。

| 項目 | 実測 | authority |
|---|---:|---:|
| ExpandedInput / InputDispatch | 2,001 / 2,001 | 2,001 / 2,001 |
| ErbRunCount | 9,931 | 9,931 |
| RandomCallCount | 55,057 | 55,057 |
| RandomTraceHash | `B2FE06966216CF89` | 同左 |
| StateSha256 | `17210E4BD7087C5A699C02D6456ECDA190D790C09E91541E32B8AA4C429D2592` | 同左 |
| DisplaySha256 | `B2B150BBAB1B1610AE945990321252544B72519CF28EF929BD6F42856FC5CE90` | 同左 |
| SaveTo / save401 / failure | 496 / 496 / 0 | 496 / 496 / 0 |

結果: PASS。macro elapsedは51,000.453 msだが、性能比較には使用しない。生成`save401.sav` SHA-256は`A0EF8768BE1B623601B33FBE592519468032131ABDC40E9747D9CBB600D1AC6D`。

## Canonical Save

canonical `fixture/Data/sav/save219.sav`のcleanup・build・smoke・benchmark後SHA-256は次で、開始時authorityと一致した。

```text
6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B
```

startup / N100 / N1000 fresh copyの`save219.sav`も同一だった。canonical fixtureをrunnerへ直接渡していない。

## Final Git State

- branch: `main-opt/diag-01-save219`（切替なし）
- HEAD: `57a160e6f2871f28951e95bad68e3279f59e05a0`（開始時から不変）
- tracked dirty file: 10 → 2
- `git status --short` entry: 29 → 20（I0 report追加後。内訳はtracked 2、`fixture_450` 1、historical report 17）
- 未追跡実ファイル: 10,066 → 10,062（prototype source 5撤去、I0 report 1追加）
- final production dirty file:
  - `Runtime/Script/Parser/WordCollection.cs`
  - `Runtime/Script/Statements/LogicalLine.cs`
- final tracked diff: 2 files、33 insertions / 3 deletions
- `git diff --check`: PASS
- staged diff: 0
- commit: 未実施
- push: 未実施

## Main/HEAD ancestry

```text
HEAD        57a160e6f2871f28951e95bad68e3279f59e05a0
main        a7e3962795161b53596e8daaed9bbadab671b909
origin/main a7e3962795161b53596e8daaed9bbadab671b909
merge-base  a7e3962795161b53596e8daaed9bbadab671b909
main..HEAD  57a160e Optimize fixed variable transporter storage
```

mainはHEADの直接祖先で、現在のcommit差はM4Aの1 commitだけである。M3Aは引き続き未commitの2-file差分である。

## Next Integration Plan

I1では、まず`M3A-production.patch`と最終2-file diffをreviewし、M3AをM4Aとは分離したcommit単位にするかを決める。その後、`main..HEAD`のM4A 1 commitとM3Aをmainへ取り込む具体的方法・順序を確定する。このI0ではM3A commit、main checkout、merge、cherry-pick、rebase、push、release publishを行っていない。
