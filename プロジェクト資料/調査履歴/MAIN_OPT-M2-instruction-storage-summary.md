# MAIN_OPT-M2 — InstructionLine保持領域の調査結果

## 結論

M2は診断のみで完了した。`InstructionLine`は約96万件あり、H1では未解析引数のsource文字payloadが最低約24.06 MiB、SET系左辺の`WordCollection`も約21.5万件確認できた。compact backingの参照領域だけでも約14.33 MiBで、主要`Word`型のshallow sizeも合計約23.76 MiBだった。個別の共有・参照同一性を全件照合した合算値ではないが、raw sourceと代入先snapshotは合わせて数十MiB級の調査候補である。

次phaseは**lazy argument / assignment snapshot compact化の設計監査**を推奨する。実装には進んでいない。次phaseの推奨モデルは **Sol / 推論 高**。

## 測定方法と安全性

- 診断Releaseのfresh working copyを使用し、H0とH1は別copy・別processで取得した。canonical fixtureを直接起動していない。
- H0は`Preload.Clear()`完了後、`Init:End`の直前。H1はsave219の`LoadFromStreamBinary`完了・`LastLoadNo=219`設定後、既存`layout(saveIndex=219,charanum=5)`の直前に記録した。H1のログ順序は census → layout。
- マクロ、N10、N100は実行していない。
- canonical `save219.sav` SHA-256は採取前後とも `6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B`。
- 使用した`dotnet-dump`は`10.0.745401+cef304c50763bf24f99566cb31d55540842e7ae9`。診断binary SHA-256は`99FAC4E05A8C27761570A2D1D84CD30156EAC02AB1A9B4C4DA76D048978F0838`。
- 採取記録・heap dumpは`artifacts/diag_tests/m0-heap/run-20260913-110237-975/`に保持した。H0 dumpは1,651,173,610 bytes、H1 dumpは1,625,896,862 bytes。

全`FunctionLabelLine`を取得し、lazy labelを除外せず、各labelの`NextLine`だけを次の`FunctionLabelLine`または`NullLine`まで走査した。`JumpTo`等の別edgeは追跡していない。labelごとのcycle checkと`ParentLabelLine`の参照一致を確認し、巨大な`HashSet<InstructionLine>`や行ごとのログは作っていない。

`BuildLazyIndex`はstubのlabelを次label/`NullLine`へ接続し、本文`InstructionLine`を作らない。hydrate時は同じlabel identityの`NextLine` chainへ本文を接続する。このため全label走査なら、未hydrate stubは自然に本文0件となり、hydrate済み本文は`lazyErbLabels`にlabelが残っていても取りこぼさない。今回のH0/H1ではlazy fileのLoaded数が0で、pending labelは10,921件だったため、hydrate済み本文そのものは観測していない。

診断APIは`PERFORMANCE_METRICS`下だけにあり、`InstructionLine`のprivate storageを分類して読む。`WordCollection`はcompact/linkedのprivate backingを直接走査し、`Collection` propertyを呼ばないため、compactからlinkedへのpromotionを起こさない。通常Releaseで診断recordは出力されない。

## H0 / H1の全体件数

| 項目 | H0 | H1 | H1−H0 |
|---|---:|---:|---:|
| FunctionLabelLine | 134,652 | 134,652 | 0 |
| NextLine上のInstructionLine出現数 | 959,641 | 959,641 | 0 |
| ParentLabelLine不一致 | 0 | 0 | 0 |
| cycleを検出したlabel chain | 0 | 0 | 0 |
| 親label単位の一意件数 | 959,641 | 959,641 | 0 |
| raw argument source | 522,551 | 522,496 | −55 |
| raw source合計文字数 | 12,618,787 | 12,617,421 | −1,366 |
| raw source最大文字数 | 5,625 | 5,625 | 0 |
| parsed Argument | 437,089 | 437,144 | +55 |
| error string / 文字数 | 0 / 0 | 0 / 0 | 0 / 0 |
| argument storageなし | 1 | 1 | 0 |
| 想定外のargument storage | 0 | 0 | 0 |

H0→H1でrawとparsedの件数が55件入れ替わったほか、raw文字数が1,366減った。この変化の原因はこの診断では確定していない。両地点ともInstructionLine総数・label数・graph sanity checkは一致した。

### auxiliaryData

| 種類 | H0 | H1 |
|---|---:|---:|
| Assignment WordCollection | 214,642 | 214,621 |
| IfCaseList | 79,081 | 79,081 |
| DataList | 6 | 6 |
| CallList | 0 | 0 |
| JumpTarget | 1,398 | 1,398 |
| FunctionIdentifier | 516 | 516 |
| Other | 0 | 0 |
| null | 663,998 | 664,019 |
| 別フィールドのJumpTo参照 | 365,854 | 365,854 |

## raw argument source

H1の文字payload最低値は`12,617,421 × 2 = 25,234,842 bytes`、約**24.06 MiB**。string header、alignment、参照配列は含まない。本文そのものはログへ出していない。

FunctionCode別の上位30件（文字数降順、H1）:

| FunctionCode | 件数 | source文字数 |
|---|---:|---:|
| SET | 214,620 | 6,080,127 |
| RETURN | 130,245 | 1,868,793 |
| PRINTFORMW | 32,722 | 1,285,382 |
| PRINTFORML | 28,336 | 911,807 |
| PRINTL | 36,038 | 843,520 |
| PRINTW | 15,230 | 503,298 |
| RETURNF | 16,271 | 242,489 |
| PRINTFORM | 5,100 | 168,878 |
| TIMES | 7,041 | 148,089 |
| SETCOLOR | 2,958 | 63,775 |
| SAVECHARA | 50 | 49,457 |
| PRINT | 3,678 | 45,977 |
| CONTINUE | 4,262 | 34,104 |
| VARSET | 1,661 | 32,992 |
| SETBIT | 1,104 | 29,274 |
| NEXT | 6,055 | 24,240 |
| RESETCOLOR | 2,152 | 21,567 |
| __NULL__ | 506 | 21,311 |
| PRINTLC | 988 | 20,709 |
| CUSTOMDRAWLINE | 1,110 | 17,770 |
| CLEARLINE | 838 | 17,424 |
| HTML_PRINT | 312 | 15,250 |
| PRINTBUTTON | 357 | 14,445 |
| DRAWLINE | 1,711 | 13,693 |
| PRINTFORMLC | 236 | 10,754 |
| VARI | 433 | 10,713 |
| HTML_PRINT_ISLAND | 40 | 8,536 |
| SETCOLORBYNAME | 413 | 8,151 |
| WHILE | 299 | 8,135 |
| SWAP | 179 | 6,460 |

## parsed Argument

H1の上位type（全15 type）:

| Argument type | 件数 |
|---|---:|
| ExpressionArgument | 154,880 |
| VoidArgument | 115,192 |
| SpCallArgment | 97,958 |
| CaseArgument | 62,140 |
| SpForNextArgment | 6,059 |
| SpCallFArgment | 612 |
| SpSetArgument | 271 |
| MethodArgument | 10 |
| ExpressionArrayArgument | 8 |
| SpVarSetArgument | 5 |
| HTML_PRINTArgument | 2 |
| IntAsignArgument | 2 |
| SpColorArgument | 2 |
| SpSetArrayArgument | 2 |
| SpSplitArgument | 1 |

## Assignment WordCollection

H1のassignment保持は`SET`だけだった。

| 項目 | H0 | H1 |
|---|---:|---:|
| Assignment WordCollection | 214,642 | 214,621 |
| Word件数 | 881,380 | 881,353 |
| compact collection | 214,294 | 214,273 |
| linked collection | 348 | 348 |
| compact list内Word件数 | 879,686 | 879,659 |
| compact list capacity合計 | 1,878,016 | 1,877,848 |
| linked node | 1,694 | 1,694 |

H1のWord型別occurrence数:

| Word type | 件数 |
|---|---:|
| IdentifierWord | 379,098 |
| SymbolWord | 364,449 |
| LiteralIntegerWord | 75,056 |
| OperatorWord | 60,007 |
| その他 | 2,743 |

compact capacityを64-bit参照8 bytesで換算した格納枠payloadは`1,877,848 × 8 = 15,022,784 bytes`、約**14.33 MiB**。array header/alignmentを含まない。実際の全体`Word[]` shallow sizeは、同一H1 dumpで217,274 objects / 20,440,656 bytesだった。両方を加算してはいけない。

## M1 H1 heap type統計との突合

| 型 | M1 H1 objects | 今回H1 dump objects | 今回assignment occurrence | 今回dump内count比 |
|---|---:|---:|---:|---:|
| Word[] | 221,996 | 217,274 | compact list 214,273 | 98.62% |
| IdentifierWord | 387,697 | 381,657 | 379,098 | 99.33% |
| WordCollection | 221,600 | 217,504 | 214,621 | 98.67% |
| List<Word> | 220,954 | 217,153 | compact list 214,273 | 98.67% |
| SymbolWord | 375,329 | 367,790 | 364,449 | 99.09% |
| LiteralIntegerWord | 80,041 | 75,730 | 75,056 | 99.11% |
| OperatorWord | 64,371 | 60,281 | 60,007 | 99.55% |

「今回dump内count比」はassignmentのoccurrence countを同一H1 dumpの型別object countで割った参考値であり、object identityや唯一のownerを証明しない。M1と今回のobject総数にもsnapshot間の差があるため、M1比からbyte削減量を外挿していない。

今回H1 dumpの主要Word object shallow sizeはIdentifierWord 12,213,024 bytes、SymbolWord 8,826,960 bytes、LiteralIntegerWord 2,423,360 bytes、OperatorWord 1,446,744 bytes。合計24,910,088 bytes（約23.76 MiB）。これもheap全体での型別値であり、assignment側の厳密なretained byteではない。raw source最低payload、compact capacity、Word object shallow sizeを単純合算した値は確定値として扱わない。

M1 H1の`InstructionLine` 953,552 objectsはbase typeのみ。今回のheap dumpではbase `InstructionLine` 953,552件に加えて`LoopInstructionLine` 6,089件を確認し、合計959,641件でcensusのNextLine件数と一致した。parent mismatchとcycleはともに0。

## Lazy / deferred状態

| 項目 | H0 | H1 |
|---|---:|---:|
| `NeedReduceArgumentOnLoad` | false | false |
| `LazyErbFileCount`（起動時index累積数） | 1,258 | 1,258 |
| Loaded lazy file数（診断追加のread-only観測） | 0 | 0 |
| pending lazy label数 | 10,921 | 10,921 |
| `LazyErbFallbackFileCount` | 205 | 205 |
| `DeferredEagerCount` | 0 | 0 |

save219 loadによるLazy hydrationはこのrunでは発生していない。H1はマクロ前という条件どおりである。

## FixedVariableTermとの優先順位

M1のheap sampleでは`FixedVariableTerm` 411,737件、100件のIdentifier調査のうち93件が1D、sampleした50件のtransporterはそれぞれ`long[3]`（48 bytes）だった。今回H1 dumpでも`FixedVariableTerm`は411,499 objects / 23,043,944 bytes。source上、両constructorは`new long[3]`を割り当てる。

ただし`VariableToken`のvirtual APIは`long[] arguments`を要求し、全instanceのtransporter identity/escapeをこの診断で網羅していない。exact-size array化は候補に残すが、削減量は確定していない。M2の結果ではraw sourceとassignment snapshotの保持量が大きく、FixedVariableTermを次の最優先にはしない。

## 判定と次phase

raw source最低payload約24.06 MiBと、assignmentのcompact reference capacity約14.33 MiBに加え、対応比率の高いWord object群が確認できた。測定値の単純加算はしないが、raw source / assignment Word graphは「数十MiB級」の調査候補という判定に足る。

次phaseは**lazy argument / assignment snapshot compact化の設計監査**を推奨する。startup warning/error timing、left-hand lexical validation、macro expansion、Rename、assignment/index evaluation order、uncalled function semantics、runtime file変更非依存を守れるかを先に監査する。**推奨モデル: Sol / 推論 高**。FixedVariableTerm、CDFLAG、parser representationの実装変更には進んでいない。

## 完了確認

- production実行仕様・データ構造、parser representation、FixedVariableTerm、CDFLAGは変更していない。
- 通常Release build: PASS（既存警告30、エラー0）。
- 計測Release build: PASS（既存警告30、エラー0）。
- `Test-M2InstructionStorage.ps1`: PASS。
- `git diff --check`: PASS（最終確認）。
- fixture原本は起動していない。canonical `save219.sav` SHA-256は採取前後一致。
- macro / N10 / N100: 0。
- stage / commit / push: なし。
