# MAIN_OPT-M3A — Frozen SET Snapshot Prototype

最終更新: 2026-09-14
対象worktree: `MAIN_OPT/main_作業フォルダ`
branch / authority HEAD: `main-opt/diag-01-save219` / `a7e3962`

## 結論

`READY_FOR_M3A_ADOPTION_REVIEW`

SET代入左辺を長寿命の `WordCollection -> List<Word> -> capacity付き Word[]` から、同じ `Word` 参照を順番に保持する exact-size `Word[]` へ置き換えた。初回のSET引数parse時だけ既存の `WordCollection` を一時再構築する。raw source canonicalization、source arena、再lex、一般parser改修には進んでいない。

## 変更内容

production変更は次の2ファイルだけである。

- `Runtime/Script/Parser/WordCollection.cs`
  - `FreezeSetSnapshot()` はcompact / linkedの既存順序を `Word[]` へコピーする。`Word` はcloneせず、`Collection` / `Pointer` を呼ばない。
  - `ThawSetSnapshot(Word[])` は初回parse用の一時 `WordCollection` を作る。
- `Runtime/Script/Statements/LogicalLine.cs`
  - SET constructorで `auxiliaryData` に直接 `Word[]` を保持する。
  - `PopAssignmentDestStr()` はslotを先にnullにしてからthawする。互換のため既存 `WordCollection` が入っていた場合の読取りだけを残した。

`InstructionLine` のfieldは増やしていない。wrapper、static/global mutable cache、ArrayPool、reflection、save/load形式変更もない。M3D0診断はfrozen representationを `assignmentFrozenSnapshot` として数え、同じsemantic snapshot比較を継続した。

## Measured / Proven

### TDD / focused test

`artifacts/diag_tests/m3a-focused/M3AFocused.csproj` をproduction変更前に実行し、`FreezeSetSnapshot` / `ThawSetSnapshot` が未定義であるためCS1061となるREDを確認した。実装後は次を確認してGREENとなった。

- compact collectionのtoken数・参照identity・順序
- linked collectionのtoken数・参照identity・順序
- thaw後の `IdentifierWord` / `LiteralIntegerWord` / `LiteralStringWord` / `OperatorWord` / `SymbolWord` / `StrFormWord` / `MacroWord` と `Word.IsMacro`

実行結果: `M3A focused snapshot tests: PASS`。

### build / startup

- normal Release: 成功、既存と同じ30 warnings、error 0
- `PERFORMANCE_METRICS` Release: 成功、既存と同じ30 warnings、error 0
- normal startup smoke: `exit code=0`、`Init=3652 ms`、`ERB=1691 ms`、`Lv2=0`

startup用fresh copyとH0/H1/N100用fresh copyのいずれも、canonical `save219.sav` SHA-256は次と一致した。

```text
6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B
```

### H0 / H1 storage census

M3A H0は `assignmentFrozenSnapshot=214,642`、`assignmentWordCollection=0`、frozen words=881,380、snapshot mismatch=0だった。H1（save219、5 characters）は次のとおり。

```text
assignmentFrozenSnapshot = 214,621
assignmentWordCollection = 0
frozenSnapshotWordCount  = 881,353
snapshot mismatch        = 0
```

H0→H1ではSET snapshotが21件減り、同じcensusの初回SET parse遷移は21件増えた。`PopAssignmentDestStr()` がslotを先にclearし、一度だけconsumptionする実行上の整合性として確認した。

### H1 heap/type delta

BeforeはM3D0 formal H1 dump、Afterは同じ5-character save219 fresh H1 dumpである。どちらも強制GCを追加せずに採取したため、type別の直接対象差分とプロセス全体の数値は区別する。

| type | Before count / bytes | After count / bytes | delta bytes |
| --- | ---: | ---: | ---: |
| `WordCollection` | 217,504 / 10,440,192 | 3,382 / 162,336 | -10,277,856 |
| `List<Word>` | 217,153 / 6,948,896 | 3,381 / 108,192 | -6,840,704 |
| `Word[]` | 217,274 / 20,440,656 | 218,138 / 12,493,688 | -7,946,968 |
| 上記3 typeのshallow合計 |  |  | -25,065,528 (-23.90 MiB) |
| `System.String` | 3,008,276 / 189,045,104 | 1,469,289 / 79,207,652 | -109,837,452 |
| non-Free shallow total | 708,795,892 | 602,766,031 | -106,029,861 (-101.12 MiB) |
| `GC.GetTotalMemory(false)` at H1 layout | 752,420,784 | 701,304,616 | -51,116,168 (-48.75 MiB) |

`WordCollection`、`List<Word>`、`Word[]` の三type差分は設計対象そのものなので、長寿命container削減の直接証拠である。三type合計はM3D0のreference/container model `-25,130,504 bytes (-23.97 MiB)` と64,976 bytes差で整合する。

一方、`System.String`、non-Free合計、`GC.GetTotalMemory(false)` は別processかつ非強制GCの観測値である。M3Aが文字列を変更していない以上、これらの大きい差をM3Aの効果として帰属させない。

### deterministic N100 correctness

diagnostic fresh copyでdeterministic N100を一回実行した。authorityと完全一致した。

```text
ExpandedInputCount = 201
InputDispatchCount = 201
ErbRunCount        = 1000
RandomCallCount    = 5550
RandomTraceHash    = F1A61293AEF35FF7
StateSha256        = 6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8
DisplaySha256      = 83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7
saveToCount        = 49 (save401 = 49)
```

N100のH1→macroで初回SET parseは2,578件で、M3D0 authorityと同数だった。M3A N100のtotal allocated bytesは1,131,454,992、GC collection countsはGen0/1/2 = 5/2/1、elapsedは6,739.911 msだった。Beforeとの差は単発・別processなので速度改善またはruntime allocation regressionの根拠には使わない。hash / state / display / save countにsemantic regressionはない。

## Static Proven

- `SP_SET_ArgumentBuilder` は唯一の `PopAssignmentDestStr()` consumerであり、変更していない。
- freezeはcompactなら `List<Word>.ToArray()`、linkedなら `LinkedList<Word>.CopyTo()` を使う。いずれも列挙順を保持し、`Word`参照を複製するだけである。
- thawは既存 `WordCollection.Add()` のみを使用するため、既存SP_SET parserが要求する `Current`、`ShiftNext`、`EOL`、`ReduceArguments` のAPIをそのまま提供する。
- `PopAssignmentDestStr()` は `auxiliaryData=null` の後にthawするので、二回目に同じsnapshotを返せない。
- source/reload graphを横断するstatic cacheは導入していない。

## Inferred / Estimated

- M3D0の23.97 MiB modelは、Afterの対象三type shallow差分23.90 MiBと一致し、長寿命SET container削減の規模見積りとして妥当である。
- 初回parse時のtemporary `WordCollection` allocationは2,578 SET分だけ発生し得る。N100のsemanticは一致したが、allocation/elapsedを結論づけるには複数回測定が必要である。

## Unknown

- reload smoke用の既存自動テストは見つからなかった。新しいGUI automationは本phaseのscope外のため作成していない。static/global snapshot cacheを持たない設計上、old/new graphの共有は起きないが、実機reloadは未測定である。
- 指定された個別SET形（文字列代入、複数RHS、macro左辺、添字式など）の専用fixture testは追加していない。startupとdeterministic代表macroのparser/state/hash完全一致は得たが、専用caseを必要とする場合はadoption reviewで追加する。
- 強制GCなしのwhole-process heap数値は実live retained bytesの厳密比較ではない。M3Aへ帰属できるのは対象三typeのshallow差分である。

## rollback / scope

hash mismatch、snapshot mismatch、unexpected parser warning、save219 SHA不一致、またはreload failureが出た場合はM3Aを採用せず、この2ファイルの変更を戻す。現時点で該当はない。raw source canonicalization（M3D0のglobal duplicate raw chars 5,720,350）は未実装のまま保留し、M3Bへは自動で進まない。

## Verification / Git

- `artifacts/diag_tests/Test-M2InstructionStorage.ps1` : PASS
- `git diff --check` : PASS（完了時再実行）
- fixture原本、canonical `save219.sav`、A1/A2、C1-W、save/load形式は変更していない。
- stage、commit、pushは実施していない。
