# MAIN_OPT-M4A — Shared Exact Transporter

最終更新: 2026-09-14
採用日: 2026-09-14

## Measured / Proven

`VariableTerm.Restructure()` から生成される retained `FixedVariableTerm` に限り、元の exact-size `long[]` を共有する bounded prototype を実装した。通常の pool / empty constructor / `Reset` は対象外であり、従来どおり3要素を確保する。

- `FixedVariableTerm(VariableToken, long[])` は `new long[3]` とcopyを行わず、渡された配列をそのまま保持する。
- `Index1` ～ `Index3` の読取りは、存在しない添字を論理値 `0` として返す。
- short transporter の存在しない添字へ書き込む場合だけ、FixedVariableTerm側で `long[3]` にcopy-on-writeする。元の `VariableTerm` の配列は変更しない。
- `UserDefinedVariableToken.IsArrayRangeValid()` は不足添字を `0` として既存の `CheckBounds` に渡す。
- `PERFORMANCE_METRICS` 限定で `shortTransporterCowExpansionCount` を記録し、macro JSONにも出力した。通常Releaseにはcounter/fieldを含めない。

TDDのfocused harnessでは、変更前に「length 0 transporter was copied instead of shared」でREDを確認後、shared identity、length 0～3、logical zero、setter COW、再 `Restructure`、戻り値破棄、poolの3-slot維持をGREEN確認した。productionへtest accessorは追加していない。

### H0/H1 construction census

候補診断buildのH0は `fromArgsTotal=410,828`、length 0/1/2/3 = `4,597 / 396,879 / 9,344 / 8`、unexpected=0で、M4D0 authorityと一致した。H1は `410,889`。M3AのH1 `assignmentFrozenSnapshotCount=214,621`、`assignmentWordCollectionCount=0` も維持した。

H0/H1/N100の `shortTransporterCowExpansionCount` はすべて0だった。

### Candidate H0 heap

同一H0条件のM4A dumpでは以下となった。

| type | M4D0 Before | M4A candidate | 差 |
| --- | ---: | ---: | ---: |
| `FixedVariableTerm` | 410,344 / 22,979,264 B | 410,366 / 22,980,496 B | +22 / +1,232 B |
| `System.Int64[]` | 636,110 / 34,130,768 B | 588,330 / 26,072,488 B | -47,780 / -8,058,280 B |

`System.Int64[]` は他用途も含む補強証拠であるが、約7.69 MiBの減少はM4D0の retained transporter見積り約6.225 MiBと方向・規模が整合する。

### Startup allocation（diagnostic Release, AB/BA/AB）

`allocatedBytes` の Before → candidate差は -13,261,296 B、-18,962,832 B、-16,252,752 Bで、3/3とも候補が小さい。M4D0の「約18.806 MiBの追加 `long[3]` allocation回避候補」と方向整合した。`managedBytes` とWorking SetはGC時機を含むため、allocation値と同じ意味には扱わない。

### Normal startup（6 pair / 12 fresh process）

| metric | Before median ms | candidate median ms | paired median差 ms | 改善 / 退行 |
| --- | ---: | ---: | ---: | ---: |
| Init | 3941.23 | 3772.28 | -179.89 | 4 / 2 |
| ERB | 3829.19 | 3688.87 | -180.48 | 5 / 1 |
| Wall | 3941.23 | 3772.28 | -179.89 | 4 / 2 |
| PrimaryParse | 987.5 | 966.5 | -28.5 | 4 / 2 |
| LabelSetup | 132 | 135 | +8.5 | 3 / 3 |
| ScriptParse | 401 | 353 | -66.5 | 4 / 2 |

全12 runは `ExitCode=0`。Initの退行gateには触れず、`ScriptParse` の一貫した退行も観測されなかった。

### Deterministic N100

条件は `RepeatCount=100`、seed `314159265358979`、`BenchmarkDiagnostics` / deterministic clock有効。candidate単独runとbalanced 5 pair（10 run）の全てで以下が完全一致した。

```text
ExpandedInputCount=201, InputDispatchCount=201, ErbRunCount=1000
RandomCallCount=5550, RandomTraceHash=F1A61293AEF35FF7
StateSha256=6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8
DisplaySha256=83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7
SaveToCount=49, Save401Count=49
```

N100 5pairの中央値は以下。最初の3pairにcandidate +837.102 msの単発外れ値があったため、追加2pairを取得した。

| metric | Before median | candidate median | paired median差 |
| --- | ---: | ---: | ---: |
| MacroMilliseconds | 6899.720 ms | 6790.933 ms | -108.787 ms (-1.58%) |
| AllocatedBytes | 1,131,394,752 B | 1,130,137,192 B | -1,229,904 B |
| ErbMilliseconds | 6678.078 ms | 6597.495 ms | -107.027 ms (-1.60%) |

candidateが遅いpairは2/5であり、規定の「3/3かつ+3%」退行条件には該当しない。candidate 5runのCOW countはすべて0。

## Static Proven

- `FixedVariableTerm(VariableToken, long[])` のproduction callsiteは `VariableTerm.Restructure()` のみ。
- `FixedVariableTerm(VariableToken)`、pool、`Reset` は今回変更していない。
- 元 `VariableTerm` から配列を奪うmoveは行わない。共有後も戻り値を破棄する既存callsiteが安全である。
- `long[]` identityをfield/collection/return/async captureで外部へescapeさせるtoken経路はM4D0監査で見つかっていない。

## Inferred / Estimated

- M4D0モデルのretained削減候補は約6.225 MiB、startup allocation回避候補は約18.806 MiBであり、同じ値として加算しない。
- heapの `System.Int64[]` 差約7.69 MiBは全配列を含むため、M4Aだけの厳密なpayload値ではない。constructor length distributionと共有実装が直接の構造根拠である。

## Unknown

- Runtime reload smokeは安全な既存自動testがないため **NOT MEASURED**。
- heap dumpの保持グラフを型別以上に分解していない。今回の変更はterm graph内の共有だけで、global/static cacheは追加していない。

## Status

`STRONG_GO_M4A_ADOPT`

semantic、memory、startup、runtimeの各gateを満たしたため、M4Aのproduction behaviorを採用した。constructorのexact-size配列共有、short indexのlogical zero、setter時だけのcopy-on-write、`IsArrayRangeValid()`のlogical zero処理を保持し、pool経路は3-slotのままとした。

2026-09-14のadoption cleanupでは、M4D0/M4A向けconstruction censusとCOW counter、およびmacro/heap診断hookをproduction sourceから除去した。M5D0 runtime-growth専用instrumentationとrunner pass-throughも計測完了後に除去した。M4A behavior自体は変更していない。

cleanup後verification:

```text
M4A focused test: PASS
M3A focused test: PASS
normal Release: PASS, 30 existing warnings, 0 errors
PERFORMANCE_METRICS Release: PASS, 30 existing warnings, 0 errors
official deterministic N100: PASS, authority完全一致
canonical save219 SHA-256: 6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B (unchanged)
git diff --check: PASS
```

stage、commit、pushは実施していない。
