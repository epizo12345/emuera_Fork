# MAIN_OPT-I1 — 採用済み変更のmain統合

## 結論

M4A commitとM3A commitを、merge commitなしのfast-forwardでlocal `main`へ統合した。push、release publish、tag、version bumpは行っていない。

## Initial State

### Optimization branch

- branch: `main-opt/diag-01-save219`
- HEAD: `57a160e6f2871f28951e95bad68e3279f59e05a0`
- main / origin/main: `a7e3962795161b53596e8daaed9bbadab671b909`
- staged: 0
- tracked dirty: M3Aの次の2 fileのみ
  - `Runtime/Script/Parser/WordCollection.cs`
  - `Runtime/Script/Statements/LogicalLine.cs`

### Main worktree

- path: `E:\GAME-2\emuera大改造\作業用フォルダ\MAIN_OPT\emuera_fork`
- branch: `main`
- HEAD: `a7e3962795161b53596e8daaed9bbadab671b909`
- tracked / staged diff: 0
- untracked: 0

## M3A Final Diff

I0保存の`M3A-production.patch`（SHA-256 `B20B0B6865058E6C0A97AF0C259BA4045E763974184A42C976BFF99E2BD0DD71`）とのreverse apply checkがPASSした。差分は次のM3A経路だけである。

- `WordCollection.FreezeSetSnapshot()`
  - SET左辺のWord参照と順序をexact-size `Word[]`へ凍結する
  - Word clone、compact→linked promotion、pointer変更を行わない
- `WordCollection.ThawSetSnapshot(Word[])`
  - 初回SET parse時だけ同一参照・同一順序で一時`WordCollection`を構築する
- `InstructionLine`
  - constructorで`dest?.FreezeSetSnapshot()`を使う
  - `PopAssignmentDestStr()`は先に`auxiliaryData = null`としてone-shot semanticsを保ち、`Word[]` thawと旧`WordCollection` fallbackを持つ

禁止識別子（M3A/M3B diagnostic、S1 cache、R1 CALLF cache、precise allocation）を対象2 fileで検索し0件だった。M3A/M4A focused testもcommit直前状態でPASSした。

## M3A Commit

```text
7fbac4be214cd5ae61a5c4d653033ee79028c489 Optimize retained SET assignment storage
```

explicit stageしたのは次の2 fileだけである。

```text
Runtime/Script/Parser/WordCollection.cs
Runtime/Script/Statements/LogicalLine.cs
```

historical report、fixture、artifactはstageしていない。

## Optimization Branch Verification

M3A commit後、optimization branchで確認した。

| 項目 | 結果 |
|---|---|
| production tracked diff | 0 |
| staged diff | 0 |
| `git diff --check` | PASS |
| normal Release fresh non-incremental build | 30 warnings / 0 errors |
| metrics Release fresh non-incremental build | 30 warnings / 0 errors |
| official deterministic N100 | PASS |

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

## Main Fast-Forward Eligibility

main worktreeはcleanだった。merge前の確認結果は次のとおり。

```text
merge-base(main, main-opt/diag-01-save219)
= a7e3962795161b53596e8daaed9bbadab671b909

main..main-opt/diag-01-save219
= 57a160e Optimize fixed variable transporter storage
= 7fbac4b Optimize retained SET assignment storage
```

mainはoptimization branchの直接祖先であり、差分はM4AとM3Aの2 commitだけだった。fetch / pullは行っていない。

## Main Fast-Forward Result

実行:

```text
git merge --ff-only main-opt/diag-01-save219
```

結果:

```text
Updating a7e3962..7fbac4b
Fast-forward
```

merge commitは生成されていない。mainの履歴順は次のとおり。

```text
7fbac4b Optimize retained SET assignment storage
57a160e Optimize fixed variable transporter storage
a7e3962 docs: close MAIN_OPT C1-W runtime audit
```

main sourceでM3Aのfreeze/thaw/one-shot経路と、M4Aのfixed variable transporterが存在することをspot-checkした。

## Main Build

mainでfresh non-incremental buildを実行した。

| build | 結果 |
|---|---|
| normal Release (`EnablePerformanceMetrics=false`) | 30 warnings / 0 errors |
| metrics Release (`EnablePerformanceMetrics=true`) | 30 warnings / 0 errors |

## Main Startup Smoke

canonical fixtureから作成したfresh copyでnormal Releaseを1回実行した。

```text
status = OK
exit = 0
InputReady = YES
parser exception = 0
Lv2 warning = 0
Init = 4412 ms
ERB load = 1946 ms
```

速度比較には使用しない。

## Main N100

main metrics binary、fresh copy、seed `314159265358979`、deterministic clock、`BenchmarkDiagnostics`、`InternalMetrics`でofficial N100を実行した。

```text
ExpandedInput / InputDispatch = 201 / 201
ErbRunCount = 1000
RandomCallCount = 5550
RandomTraceHash = F1A61293AEF35FF7
StateSha256 = 6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8
DisplaySha256 = 83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7
SaveTo / save401 / failure = 49 / 49 / 0
```

結果: PASS。macro elapsedは7,281.552 msであり、性能比較には使用しない。

## Main N1000

main metrics binaryと別fresh copyでN1000 final integration smokeを実行した。

```text
ExpandedInput / InputDispatch = 2001 / 2001
ErbRunCount = 9931
RandomCallCount = 55057
RandomTraceHash = B2FE06966216CF89
StateSha256 = 17210E4BD7087C5A699C02D6456ECDA190D790C09E91541E32B8AA4C429D2592
DisplaySha256 = B2B150BBAB1B1610AE945990321252544B72519CF28EF929BD6F42856FC5CE90
SaveTo / save401 / failure = 496 / 496 / 0
```

結果: PASS。macro elapsedは48,887.374 msであり、性能比較には使用しない。

## Canonical Save

integration前後、および全fresh copyで確認したcanonical `fixture/Data/sav/save219.sav`のSHA-256:

```text
6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B
```

canonical fixtureをrunnerへ直接渡していない。

## Final Git State

```text
branch      main
main HEAD   7fbac4be214cd5ae61a5c4d653033ee79028c489
origin/main a7e3962795161b53596e8daaed9bbadab671b909
```

local mainはorigin/mainよりM4A/M3Aの2 commitだけ先行している。

- `git diff --check`: PASS
- staged diff: 0
- production tracked diff: 0
- I1 reportはuntrackedであり、この統合commitへ含めていない
- S1 / R1 / M3B / M3C zero-leak search: 0

## Push State

`git push`は実行していない。release publish、配布EXE更新、tag、version bumpも実行していない。
