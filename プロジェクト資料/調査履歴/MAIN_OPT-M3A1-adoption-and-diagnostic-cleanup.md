# MAIN_OPT-M3A1 — Frozen SET Snapshot Adoption & Phase Diagnostics Cleanup

最終更新: 2026-09-14
対象: 最適化用worktree
branch / authority HEAD: `main-opt/diag-01-save219` / `a7e3962`

## Adoption decision

```text
M3A_FROZEN_SET_SNAPSHOT = ADOPT
M3A_DIAGNOSTIC_CLEANUP = PASS
READY_FOR_M3B_RAW_SOURCE_CANONICALIZATION_DESIGN
```

M3Aで確認済みのSET左辺representationを正式採用する。

```text
WordCollection
  -> FreezeSetSnapshot()
exact-size Word[]
  -> InstructionLine.auxiliaryData
  -> PopAssignmentDestStr() 時だけ ThawSetSnapshot()
temporary WordCollection
```

今回、新しい最適化、raw source canonicalization、source arena、再lex、一般parser改修は追加していない。

## Measured / Proven

### M3Aの採用根拠

M3A H1（save219、5 characters）では `assignmentFrozenSnapshot=214,621`、`assignmentWordCollection=0`、frozen words=881,353、snapshot mismatch=0だった。直接対象のheap type shallow差分は次であり、M3D0 model `-25,130,504 bytes (-23.97 MiB)` と整合する。

```text
WordCollection / List<Word> / Word[] 合計
-25,065,528 bytes
-23.90 MiB
```

### cleanup後のfocused / build / startup

- `artifacts/diag_tests/m3a-focused/M3AFocused.csproj`: PASS
  - compact / linked freezeの参照identity・順序、pointer非変更、thaw、主要Word subtype、`Word.IsMacro`
- normal Release: 30 warnings、error 0。M3A前後の既存warning数との差は0。
- `PERFORMANCE_METRICS` Release: 30 warnings、error 0。M3A前後の既存warning数との差は0。
- normal startup smoke（fresh copy）: exit code 0、parser exception 0、warning Lv2 0、`Init=3914 ms`、`ERB=1930 ms`

### cleanup後H0 / H1 とN100

diagnostic fresh copyでdeterministic N100を1回実行した。同一jsonl中のH0/H1について、更新後の `Test-M2InstructionStorage.ps1 -BenchmarkLogPath` はPASSした。

| 項目 | H0 | H1 |
| --- | ---: | ---: |
| `assignmentFrozenSnapshotCount` | 214,642 | 214,621 |
| `assignmentWordCollectionCount` | 0 | 0 |
| frozen word count | 881,380 | 881,353 |
| `rawSourceDuplicates` | あり | あり |
| `assignmentSnapshotDifferential` | なし | なし |
| `firstParseTransitions` | なし | なし |

H1のraw source duplicate censusは、`rawCount=522,496`、`uniqueByValueCount=195,148`、`duplicateValueCharsAvoidable=5,720,350` を維持した。

N100 semantic authorityも完全一致した。

```text
ExpandedInputCount = 201
InputDispatchCount = 201
ErbRunCount        = 1000
RandomCallCount    = 5550
RandomTraceHash    = F1A61293AEF35FF7
StateSha256        = 6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8
DisplaySha256      = 83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7
saveToCount        = 49
save401Count       = 49
```

canonical `save219.sav`、startup fresh copy、N100 fresh copyのSHA-256はすべて次と一致した。

```text
6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B
```

## Static Proven

- `FreezeSetSnapshot()` / `ThawSetSnapshot()` はproductionに残した。freezeは同じ `Word` reference・順序をexact-size `Word[]`へ保持し、compact→linked promotionやpointer変更を行わない。
- `PopAssignmentDestStr()` はslotをnullにしてからthawする。legacy `WordCollection` fallbackも維持した。
- `InstructionLine` の共通fieldは追加していない。
- static/global snapshot cache、fileId lookup、generation共有は導入していない。snapshotは各`InstructionLine` graphが直接所有する。
- `SP_SET_ArgumentBuilder`、ExpressionParser、LexicalAnalyzer、macro、save/load形式は変更していない。

## Cleanup

以下のM3D0/M3A限定diagnosticを撤去した。

- `LogicalLine.cs`: 初回parse state / one-shot marker / `InstructionLineBenchmarkFirstParseState`
- `ArgumentParser.cs`: parse前state取得とparse成功後のfirst-parse record hook
- `PerformanceMetrics.cs`: first-parse counters、FunctionCode別dictionary、record/reset/getter、macro record property
- `Process.cs`: censusごとの全SET thaw/rebuild semantic differentialと `assignmentSnapshotDifferential`、`firstParseTransitions`
- `WordCollection.cs`: differential専用のbenchmark snapshot / recursive sequence comparer
- `Test-M2InstructionStorage.ps1`: 上記二recordの必須検証。かわりに削除済みfieldが存在しないことを検証し、単一benchmark jsonlも入力できるようにした。

## Temporarily retained for M3B

- `RawSourceDuplicateCensus` と `rawSourceDuplicates`（global / per-file / reference identity）
- `assignmentFrozenSnapshot` を含む軽量instruction storage census、Word type count、FunctionCode別assignment count
- `WordSnapshotBenchmarkStorage`。frozen `Word[]` を読むだけで、thawや追加allocationをしない。

## Unknown

Runtime reload smokeは未測定である。既存の安全な自動reload testは見つからず、新しいGUI automationは作成していない。static/global cacheを持たず各graphがsnapshotを直接所有するため、static ownership safetyはPASSとするが、実機reloadは未測定のままである。

## Git / safety

- `git diff --check`: PASS（完了時再実行）
- fixture原本とcanonical `save219.sav` は変更していない。
- stage、commit、pushは実施していない。
- M3B実装には自動で進まない。
