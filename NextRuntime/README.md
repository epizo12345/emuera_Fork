# Next Runtime

Next Runtimeは、既存ゲーム・セーブ・ERB互換を最優先にしつつ、Legacy Runtimeでは難しいstartup、ERB parse/compile、CALL-heavy execution、retained memory、warm startupの大幅な高速化・軽量化を実測で目指すための独立領域です。Legacyと同等の速度・メモリなら採用する意味はありません。

## Phase 0A

このPhaseでは新VMや既存Runtimeの置換を行わず、side-effect freeなERB Source Index、IndexAudit、SelfTestだけを実装します。Source IndexはUTF-8 BOM付きERBだけを受理し、offsetはBOM 3 byteを含むファイル先頭からの物理byte offsetです。本文全体、1行ごとのLegacy object、Legacy parser graphは保持しません。semanticを安全に推測できない構造はfallback理由として記録します。

`Emuera.Next.Core`はWindows UI、Graphics、Gamepad、save、game state、任意のfilesystem mutationへ直接依存しません。IndexAuditは指定ERB directoryをread-only recursive scanし、SelfTestは一時ディレクトリだけを書き換えます。

全体の目的・互換性境界・将来ロードマップは[Next Runtime全体設計書](../プロジェクト資料/NextRuntime/NextRuntime_全体設計書.md)を参照してください。技術用語を避けた概要は[かんたん説明](../プロジェクト資料/NextRuntime/NextRuntime_かんたん説明.md)です。正式資料の正本は`プロジェクト資料/NextRuntime/`です。

## Phase 0Bの結果

実ゲームfixtureを対象に、Legacyの実際の`ErbLoader` / `LogicalLineParser` / `LabelDictionary`をoracleとして、NextのSource IndexとJSONL manifestを照合しました。LegacyとNextはともに9458 ERB・134652関数で、safe 112854関数、fallback 21798関数でした。R2ではLegacy PPStateのdisabled rangeを診断し、BIT_SETTING.ERBの3件も含めてunexplained fallback differencesは0です。missing、extra、name、order、FileOrder、invalid/error mismatchもすべて0です。

fallbackは、DeclarationDirective 6844、FunctionMetadata 6817、LineContinuation 164、OtherSemanticFallback 1425、Preprocessor 234、Rename 12705（重複計上）です。実データの重複関数名は3名称・9定義、定義順差分0です。#PRI/#LATER/#ONLY/#SINGLEを含むevent dispatch semantics自体はNext VM未実装のため、priority完全一致ではなく`DEFERRED / LEGACY FALLBACK`です。全角space、vertical tab/form feed、BOM、invalid UTF-8、引用符付き`@`、PPState disabled rangeの境界はSelfTestと診断値で確認しています。

性能値は同じ処理の比較ではありません。Legacyは実ERB parse/load、Nextはread-only Source Index構築を各5回測定し、runtime speedupは主張しません。Phase 0Bの差分ゲートとPhase 1A-R4のprototype gateを通過しました。

## Phase 1A-R4の結果

現在は、目次から選んだ関数のphysical byte spanだけを`FileStream.Seek`で読み、Legacy parserをCompiler本体へ再利用せず、compactな`PrototypeInstruction` struct列へ変換する試作段階です。R1では関数ごとの64KB FileStream bufferを廃止し、R2ではAuditのfile-scoped sessionで同じERBを1回だけ開くようにしました。実ゲームではcompiler eligible 59,435件中54,200件をcompileし、unique unsupportedは5,235件、compiler error 0件でした（5回実行のunsupported encounterは26,175件で、coverage件数ではありません）。Legacy structural/exact opcode differentialは0 mismatchです。operand semanticsは式/format IRの後Phaseへ残します。

CompilerはPhase0B verified safe、Compiler strict-clean、eligibleを別に判定します。`SourceChanged`、`InvalidSource`、`Unsupported`を明示的に返し、source fingerprintは関数byte spanのSHA-256を32-byte valueとしてCompileResult側だけに返します。PrototypeInstructionは実測16 bytesです。R4では共有name/path参照をPure known payloadから除外し、3回のpure retained中央値は7,050,736 bytes、Pure known payloadは4,808,976 bytes、overheadは2,241,760 bytes、監査込みrootは25,246,200 bytesでした。最新5-run性能中央値はtotal 1,953.059 ms、source read 1,055.302 ms、compiler 60.184 msです。まだVM、VariableStore、Expression/Format IR、disk cache、UI/正式EXE変更はありません。

## Phase 1Bの結果

R4のunsupported 5,235関数をLegacy FunctionCode・source syntax単位で再集計した。主因は代入構文（4,836関数、30,032 occurrences）、次いでCALLFORM（201）、RESETCOLOR（150）だった。Tier Aとして、source spanだけで意味を失わないSET（代入）、RESETCOLOR、CUSTOMDRAWLINE、SETCOLOR、SETFONTを追加した。Tier BのCHKFONT/GETFONT/RESULT等、Tier CのCALLFORM/TRYCALLFORM、CATCH/ENDCATCH、LOCAL、式・動的名前依存は延期した。

実ゲームfixtureでは、Previously compiled 54,200、Newly compiled 4,893、Total compiled 59,093、Remaining unsupported 342、Compiler errors 0となった。baseline lost 0、baseline/expanded structural mismatch 0、Exact Opcode mismatch 0、PrototypeInstruction 16 bytesである。5-runのbaseline subset中央値はtotal 1,810.836ms、source read 1,000.763ms、compiler 55.406ms、allocation 75,036,176 bytes。expanded set、Pure retained、known payload、overhead、audit-inclusiveは最終artifactへ記録した。

Phase 1B-R1では、未対応Legacy命令のoperand中の`=`をSETへ誤認しない予約語ガードと、比較・引用符・コメントの負例を追加した。Run IDは`20260827_Phase1B_R1_Final`。実ゲーム結果は59093 compiled / 342 unsupported / compiler errors 0、Phase1B baseline lost 0、Exact Opcode mismatch 0、expanded raw instruction count 190482、Pure retained 9227928 bytesである。VM、VariableStore、Expression/Format IR、disk cache、正式EXE変更は行わない。次候補は、残る342件のTier Bをsource例とLegacy oracleで精査するPhase 1Cである。

Phase 1B-R2では、R1の暫定予約語をLegacy `BuiltInFunctionCode` と `FunctionMethodCreator` の実在名を統合した固定表へ置換し、架空tokenへの依存を除去した。Run IDは`20260827_Phase1B_R2_Final`。指定command負例・予約表整合性を含むCompilerSelfTestは56/56、実ゲーム結果は59093 compiled / 342 unsupported / errors 0、expanded raw instruction count 190482、Pure retained 9227872 bytesである。Phase 1Bを正式完了・HOLDとし、VM等は開始しない。

## 固定する設計

長期目標は次の順です。

```text
ERB source → lightweight Source Index → function-level lazy load
→ function-level compile → compact expression/instruction IR
→ compact bytecode → high-speed VM
```

固定CALLはFunctionId、GOTO/$labelはfunction-local label idまたはPCへ解決可能にし、最終VMはFunctionId + Program Counter + compact contiguous instruction storageを中心にします。既存Legacyは互換性・warning/error・RNG・evaluation-order・saveのoracleとして残します。.NET 10を継続するのは、Legacyとの比較、既存ERB/save仕様、Windows Host資産を同じ環境で検証できるためです。

Memory lifetimeはPermanent（game/variable/character state、index、symbol table、save互換metadata）、Evictable（source cache、compiled code、IR、再生成可能image/layout）、Transient（lexer/parser/compiler work buffer）に分けます。Lazyだけでなく、function-level lazy + compact compile + bounded cache + evictionを最終形とし、unbounded compile cacheや一度触れたcodeの永久常駐は許可しません。disk compile cacheは将来候補、warm startupは可能な限りparser再実行を避ける方向です。

VariableStore、save format、Legacy ERB execution、Graphics/UI、Gamepad、Web/WASM、security production implementationは今回変更しません。Legacy Save Codecは将来もadapter/oracleとして再利用し、Host/Platform boundaryでSAVECHARA、LOADCHARA、GCREATEFROMFILE、resources CSV、external editorなどのsandboxを導入する方針です。

Phase 14A～14NのGraphics/native ownership改善はWindows Host資産として維持します。Next CoreへWinForms依存を持ち込みません。

## Roadmap

0A Source Index foundation → 0B Legacy oracle differential + performance baseline → 1A function-level compiler prototype → 1B compiler coverage expansion → 2 compact instruction VM → 3 compact expression/format IR → 4 bounded/evictable compiled cache → 5 disk compile cache/warm startup → 6 Next VariableStore prototype → 7 Host I/O/security sandbox → 8 full compatibility expansion

各段階は実測とcorrectness結果で見直します。Phase 0AのIndexAudit性能をNext Runtime全体の性能向上とは主張しません。

## Phase 0A-R1のSource Index契約

### Phase 0A-R2の関数ヘッダー境界

関数ヘッダーはLegacyの識別子読み取り規則に合わせ、行頭の`@`の直後から識別子を読む。したがって`@FUNC(ARG)`、`@FUNC(ARG1, ARG2)`、`@FUNC, ARG`、`@日本語(ARG)`は関数名をそれぞれ`FUNC`、`FUNC`、`FUNC`、`日本語`として記録する。`@"文字列"`や`@'文字列'`は関数ヘッダーではなく、本文中に現れても新しい関数境界を作らない。括弧付きヘッダー数、引用符付き`@`行数、拒否した`@`候補数は診断値として保持する。

R3ではLegacyの`{`単独行から`}`単独行までの物理行連結を検出する。連結範囲の`@`は通常の関数境界として信用せず、範囲を含むfile/functionへ`LineContinuation`を付ける。nested `{`、文字の付いた`}`、未閉鎖は`OtherSemanticFallback`も付ける。これはLegacy parserの再実装ではなく、安全側で委譲範囲を示すだけである。

先頭空白は半角space/tabだけを確定的に飛ばす。vertical tab/form feedは飛ばさない。全角spaceはLegacyの`SystemAllowFullSpace`設定に依存するため、Coreへ設定を持ち込まず、検出・採用時に`OtherSemanticFallback`を付ける。

分類はLegacy `ErbLoader` / parserの境界に合わせます。`[IF_DEBUG]`、`[IF_NDEBUG]`、`[IF ...]`、`[ELSEIF]`、`[ELSE]`、`[ENDIF]`、`[SKIPSTART]`、`[SKIPEND]`はPreprocessorです。`[[...]]`はRenameでありPreprocessorではありません。`#DIM` / `#DIMS`はDeclarationDirective、`#FUNCTION` / `#FUNCTIONS` / `#LOCALSIZE` / `#LOCALSSIZE` / `#PRI` / `#LATER` / `#ONLY` / `#SINGLE`はFunctionMetadataとして別に記録します。未知のSharp directiveや特殊labelはOtherSemanticFallbackです。

Preprocessorは後半の有効sourceを変えるLegacy PPStateを持つためfile-level fallbackです。Auditのfallback file countはsemantic fallback flagを1つ以上持つファイル、fallback function countはそのflagを持つ関数です。Preprocessorを含むファイルでは全関数へPreprocessor flagを伝播させます。DeclarationDirective、FunctionMetadata、Rename、LineContinuationなどは検出位置の関数へ記録します。

ERBはEF BB BFのUTF-8 BOM付きだけを受理し、strict UTF-8 validationを全物理行へ行います。構文markerはraw UTF-8 bytesで判定し、UTF-16 stringを作るのはfunction header名だけです。`SourceSpan`はBOM 3 byteを含むphysical byte offsetと1-based line rangeを保持し、source本文は保持しません。`FunctionIndex`はreadonly record structで、file identityはSourceFileIndexに1回だけ保持し、fallback reasonはflagsからAudit時に生成します。

IndexAuditの`indexBuild*`は`ErbSourceIndexer.IndexDirectory`の直前から直後までだけを測ります。`managedBeforeIndex`を記録し、indexだけを保持した状態でdiagnostic full GCを行った`managedWithIndex`との差を`retainedIndexManagedBytesEstimate`とします。後続のflatten、sort、集計、JSON生成は`auditPostProcess*`へ分離します。この値は厳密なobject sizeではなく診断用推定値で、production runtimeのGC操作ではありません。旧Phase 0Aのallocation値は後処理を含むため、新値との比較はNOT DIRECTLY COMPARABLEです。

## 差分更新の設計方針（Phase 0B-R1）

差分更新はまだ実装せず、変更ファイルの関数span・content hash・確定した依存先だけを無効化する設計とする。ERH、Rename、Preprocessor、宣言directiveは安全側にfile-level invalidation、画像・CSV等の非ERB assetは別cache namespaceとする。cache headerのengine/index schema/parser rule version、設定値、root identityが変われば全再構築し、更新後のLegacy oracle検証に失敗した場合は前回の完全なindexへ戻す。

## CSV変更（Phase 0B-R2設計）

CSVは依存性で分類する。表示・名称・説明などcompile時意味解析へ影響しないものはERB compile cache全破棄を不要とする。変数・定数・ID・構造など解析時意味へ影響するものは、依存関係が確定したcompiled functionだけを無効化する。影響範囲不明のCSVは安全側に広くcache invalidationする。R2では分類本実装を行わず、画像だけの変更でERB cacheを破棄しない方針と併せてdependency graph設計へ残す。

## Windows distribution invariant

最終Windows版の正式配布は、現行Emueraと同じく `PublishSingleFile=true`、`SelfContained=false` のframework-dependent single-file publishを基本とします。内部をWindows Host、Next Core、Compiler、VMなどへ分割しても、ユーザーがゲームフォルダへ手動配置するRuntime本体は原則 `Emuera.exe` 1ファイルです。publish時に必要なruntime componentをsingle EXEへまとめ、compile cacheなどの再生成可能cacheを手動配置Runtime componentとは扱いません。

## Phase 1B-R3 完了判定

Run IDは`20260827_Phase1B_R3_Final`。Legacy `FunctionIdentifier.GetInstructionNameDic()` の実登録キーを `Method == null`（実際のstatement command）と `Method != null`（式中method）へ診断exportし、Next予約表をstatement 275件へ完全一致させた。Missing 0、Extra 0、Duplicate 0、Empty 0、method-only false reservation 0である。`SET`は予約表から除外し、対応済みexact opcode、Legacy statement予約、assignmentの順で判定する。CALLFORM、TRYCALLFORM、TRYCCALLFORM、ENDCATCH、RESET_STAIN、VARSET、RESTART、ARRAYSHIFT、SPLITの`X=Y`負例、RESULTS/A/日本語代入、比較・引用符・コメント負例をCompilerSelfTestへ固定した。CHKFONT/GETFONTはLegacy actual dictionaryでmethod-onlyと確認し、Next statement reservationはfalseである。

実ゲームは59,093 compiled、342 remaining unsupported、compiler errors 0。Previously compiled lost 0、instruction count/order/exact opcode mismatch 0、PrototypeInstruction 16 bytes、Phase0B safe 52.36%、eligible 99.42%。5-run performance medianはtotal 1,913.485 ms、source read 1,018.398 ms、compiler 83.051 ms、allocation 125,520,296 bytes。Pure retainedは3回完全独立測定で8,703,552 / 8,703,552 / 8,703,552 bytes、中央値8,703,552、PureKnownPayload 6,829,664、overhead 1,873,888。audit-inclusive retainedは28,602,312 bytesで別計上した。artifact、正式資料、final-status、Git証跡は同じRun IDと数値で整合させ、Phase 1BをCOMPLETE/HOLDとする。VM、Expression IR、Format IR、Phase 1Cは開始しない。

### Phase 1B-R4 完了判定

Run IDは`20260827_Phase1B_R4_Final`。Legacyの全行頭識別子A=456件、statement command B=275件（`Method == null`）、method-backed line head C=181件（`Method != null`）を実測した。NextはAをB∪Cとしてassignment guardへ保持し、A/B/CのMissing・Extraは全て0、A=B∪C、B∩C=0、Duplicate・Empty=0、method-backedのstatement誤予約=0である。CALLFORM、TRYCALLFORM、TRYCCALLFORM、ENDCATCH、RESET_STAIN、VARSET、RESTART、ARRAYSHIFT、SPLITの`X=Y`、およびCHKFONT、GETFONT、RAND、ABS、MINの`X=Y`はSET化しない。RESULTS、A、日本語識別子の通常代入はSET、比較演算子はSET化しない。`A==B`はLegacyの代入意味をNextへ持ち込まず、安全側で比較意味の実装へ延期した。

実ゲームはeligible 59,435、compiled 59,093、remaining unsupported 342、compiler errors 0。R3成功集合baseline lost 0、baseline/expandedのinstruction count・order・exact opcode mismatchは全て0、`PrototypeInstruction`は16 bytes。R4 expanded 5-run中央値はtotal 2,118.147 ms / source read 1,171.062 ms / compiler 88.684 ms / allocation 125,520,296 bytes。R3成功集合baseline allocation中央値124,527,416 bytesに対し10%超回帰はない。pure retainedは8,703,552 bytesを3回とも記録し、known payload 6,829,664、overhead 1,873,888、audit-inclusive retained 16,019,832 bytesを別計上した。Phase 1BはCOMPLETE/HOLDとし、VM、Expression/Format IR、Phase 1Cは開始しない。

### Phase 1B-R5 最終Gate

Run IDは`20260827_Phase1B_R5_Final`。Legacy actual oracleの4構成でA=456、B=275、C=181、A=B∪C、B∩C=0、map−B=0、map∩C=0を確認した。`SET`はstatement line-head mapから除外し、`TryMapStatementIdentifier("SET")`はfalse、代入構造の分類後だけでSET opcodeを生成する。Legacy first identifier、separator、全角空白、`//`、複雑lvalue、operand/source spanを照合し、CompilerSelfTestは80/80。

実fixtureはeligible 59,435、compiled 59,093、remaining unsupported 342、errors 0。54,200件baseline lost 0、命令数・順序・exact opcode差分0。expanded 5-run中央値はtotal 1,858.849 ms / source read 992.243 ms / compiler 89.554 ms / allocation 119,904,656 bytes。pure retainedは8,703,552 bytesを独立3回、fixture変更はNew 0 / Changed 0 / Deleted 0、全角空白直後の実例は37件。artifact semantic verifierを含む全GateはPASS、`Phase1BDecision=COMPLETE`。VM、Expression/Format IR、Phase 1Cは開始しない。

<!-- BEGIN NEXT-1B-R6 METRICS -->
RunId=20260827_Phase1B_R6_Final
Phase1BDecision=COMPLETE
LegacyDefaults.IgnoreCase=True
LegacyDefaults.UseScopedVariableInstruction=False
LegacyDefaults.SystemAllowFullSpace=True
LegacyDefaults.DebugMode=False
fixture.IgnoreCase=True source=Data/emuera.config
fixture.UseScopedVariableInstruction=True source=Data/setting.json
fixture.SystemAllowFullSpace=True source=Data/emuera.config
fixture.DebugMode=False source=normal launch without -Debug
matrix.ic-true-scoped-true.A=456 B=275 C=181 unionPass=True intersectionCount=0 mapMinusB=0 mapIntersectionC=0 behaviorMismatches=0
matrix.ic-true-scoped-false.A=454 B=273 C=181 unionPass=True intersectionCount=0 mapMinusB=0 mapIntersectionC=0 behaviorMismatches=0
matrix.ic-false-scoped-true.A=456 B=275 C=181 unionPass=True intersectionCount=0 mapMinusB=0 mapIntersectionC=0 behaviorMismatches=0
matrix.ic-false-scoped-false.A=454 B=273 C=181 unionPass=True intersectionCount=0 mapMinusB=0 mapIntersectionC=0 behaviorMismatches=0
debug.false=exact compiled count=1/1 silentDropped=0
debug.true=fallback Unsupported for ;#; count=4/4 silentDropped=0
eligible=59435 compiled=59093 remainingUnsupported=342 compilerErrors=0 baselineLost=0
baselineInstructionCountMismatch=0 baselineInstructionOrderMismatch=0 baselineExactOpcodeMismatch=0
expandedInstructionCountMismatch=0 expandedInstructionOrderMismatch=0 expandedExactOpcodeMismatch=0
performance.runs=5 performance.totalMedianMs=1964.231 performance.sourceReadMedianMs=1058.747 performance.compilerMedianMs=93.265 performance.allocationMedianBytes=119904656
pureRetained.runs=3 values=8703552,8703552,8703552 median=8703552 valid=True
fixtureMutation.New=0 Changed=0 Deleted=0
semanticVerifier=PASS mutationTests=4/4
<!-- END NEXT-1B-R6 METRICS -->

<!-- BEGIN NEXT-1B-R7 METRICS -->
RunId=20260827_Phase1B_R7_Final
Phase1BDecision=COMPLETE
LegacyDefaults.IgnoreCase=True
LegacyDefaults.UseScopedVariableInstruction=False
LegacyDefaults.SystemAllowFullSpace=True
LegacyDefaults.DebugMode=False
FixtureOptions.IgnoreCase=True FixtureOptions.UseScopedVariableInstruction=True FixtureOptions.SystemAllowFullSpace=True FixtureOptions.DebugMode=False constructor=explicit-options
matrix.ic-true-scoped-true.A=456 B=275 C=181 aMissing=0 aExtra=0 bMissing=0 bExtra=0 cMissing=0 cExtra=0 union=True intersection=0 mapMinusB=0 mapIntersectionC=0 behavior=0
matrix.ic-true-scoped-false.A=454 B=273 C=181 aMissing=0 aExtra=0 bMissing=0 bExtra=0 cMissing=0 cExtra=0 union=True intersection=0 mapMinusB=0 mapIntersectionC=0 behavior=0
matrix.ic-false-scoped-true.A=456 B=275 C=181 aMissing=0 aExtra=0 bMissing=0 bExtra=0 cMissing=0 cExtra=0 union=True intersection=0 mapMinusB=0 mapIntersectionC=0 behavior=0
matrix.ic-false-scoped-false.A=454 B=273 C=181 aMissing=0 aExtra=0 bMissing=0 bExtra=0 cMissing=0 cExtra=0 union=True intersection=0 mapMinusB=0 mapIntersectionC=0 behavior=0
Debug.false.exact=True Debug.true.fallback=True silentDropped=0
coverage.eligible=59435 compiled=59093 remaining=342 errors=0 coverageRate=0.994246
differential.baselineLost=0 count=0 order=0 opcode=0 classification=0 span=0 SET=False assignmentFalsePositive=0
performance.baselineRuns=5 expandedRuns=5 totalMedianMs=1963.985 sourceMedianMs=1045.655 compilerMedianMs=95.468 allocationMedianBytes=119904656 instructionCount=190482
pureRetained.runs=3 values=8703552,8703552,8703552 median=8703552 knownPayload=6829664 overhead=1873888 valid=True
fixture.New=0 Changed=0 Deleted=0
<!-- END NEXT-1B-R7 METRICS -->





### Phase 1C-R3: Final Evidence Replay Closure

EvidenceClosureRunId=20260827_Phase1C_R3_Final
MeasurementRunId=20260827_Phase1C_R2_Final
EvidenceClosureStartHead=7583926185aa4c30bc2361e67de4e7134af75641
MeasurementStartHead=473ec72c812fd7356c65e5f4ee3d22f21b1c5cbf
ProductionCompilerChanged=NO
Phase1CDecision=COMPLETE
Phase1CompilerStatus=COMPLETE_FOR_SCOPED_BOUNDARY
Phase1Status=COMPLETE
R2 raw replay coverage: eligible=59435; compiled=59103; remaining=332; errors=0; baseline=59093; baselineLost=0; delta=+10; unexpectedNewlyCompiled=0; instructionCount=191273
AddedOpcodes=RESET_STAIN,VARSET,ALIGNMENT,ARRAYSHIFT,SPLIT; existing IDs unchanged; append-only; PrototypeInstruction=16 bytes
Owner replay: MethodBacked 30; DynamicCall 289; FlowControl 3; AssignmentExpression 10; FrontendCompatibility 0; PrimaryUnknown=0; AllCategoriesUnknown=0
R2 raw replay performance: baseline total=2117.106 ms; sourceRead=1131.082 ms; compiler=103.482 ms; allocation=118911944 bytes; expanded total=2082.551 ms; sourceRead=1129.214 ms; compiler=96.101 ms; allocation=120035704 bytes; expandedInstructionCount=191273
R2 raw replay retained: values=8717168,8717168,8717168; median=8717168; knownPayload=6842960; overhead=1874208; valid=3/3
Measurement fixture evidence=R2 (R3 did not create a fresh fixture): before/after=20103/20103; New/Changed/Deleted=0/0/0; manifestSHA=7ca75ad502d8669589037c59c4818bac63534646e283b389e5f72971a43ef194
Tests replay: Core=52/52; Compiler=81/81; required named gates=PASS; Differential=PASS; oracle=5/5; matrix=4/4; Debug PASS silentDropped=0
R3 independently reaggregated immutable R2 raw evidence; no Production semantics, coverage, performance measurement, or retained measurement was changed.

### Phase 2A: Compact VM Skeleton + Control Linker

Phase2ARunId=20260827_Phase2A_Final
StartHEAD=e264a40f3c4ef6af96e1c8a8020909492407c07a
Phase1Status=COMPLETE
Phase1Baseline=eligible59435 compiled59103 remainingUnsupported332 compilerErrors0 instructionCount191273 PrototypeInstruction=16 bytes

Phase2Aは実ゲームをNext VMで起動するphaseではなく、FunctionId/catalog、compact linked representation、PC/frame/call/jump invariant、object-free structural linker、synthetic VMを確定する基盤phaseである。Production Legacy semanticsとPhase1 compiler coverageは変更しない。

PC contract=常に次に実行するfunction-local instruction。fetch後にPCを進め、branchはlink済みlocal PCへ設定する。CALLはcallerのcall後PCを保持しcallee PC=0。JUMPはcallerを即popせずPropagate frameでreturn伝播する。
FunctionId=Source Indexのphysical function definitionをfile order/function orderで0-based列挙。definitions=134652、duplicate FunctionId=0、duplicate name definitions=116。name lookupはordered FunctionId definitionsを保持する。
ResolutionとCodeAvailableは分離する。real fixtureのfixed CALL/JUMPはCALL12254、JUMP15、static target parse success12269、resolved12266、missing3、wrongKind0、CodeAvailable true1527/false10739。
Fixed scannerはLegacy SP_CALLと同じくhalf-space/tabをtrimし、( [ , ;までをstatic targetとしてlinkする。CALLFORM/TRYCALLFORM/dynamic callは対象外。
CALL argument contract=target ready/hydration → caller arguments全評価・transport確定 → callee private ScopeIn → ARG/ARGS/REF代入 → frame push → callee entry。Phase2Aではargument executionとScopeInを実装しない。

VmInstruction=ushort Opcode + ushort Flags + int OperandOffset + int OperandLength + int Aux、exact16 bytes。VmFunctionDescriptor=16 bytes、VmFrame=12 bytes。hot representationはreference/string/object/arrayを持たず、global VmInstruction storageとscalar side tablesを使用する。
Linked fixture=functions59103 instructions191273 structuralLinks52638、max instruction/function=12316、max structural nesting=7、max loop nesting=2、invalid structure=0、semantic barriers=69402。IF/ELSEIF/ELSEはordered clauseとENDIF exit、SELECTCASEはselectorを一度だけ扱うordered case/end table、loopsはnearest loopのbreak/continue targetをlocal PCへ変換する。
Memory payload=VmInstruction3060368 bytes、descriptor2154432 bytes、branch/loop side table1263312 bytes、known linked payload6478112 bytes、diagnostic sidecar0。per-instruction object graphは持たない。
Performance 5-run median=SourceIndex2042.147ms、catalog2553.742ms、Phase1 compile3123.325ms、control link78.166ms、total7805.401ms、total allocated766069264 bytes。これは最適化採否の値ではなくPhase2A基盤のbaseline evidenceである。

Legacy source oracleはPASS。Legacy control runtimeとLoopInstructionLine reentrancyの自動oracleは、Production変更なしで利用できるtest seamがないためUNAVAILABLE。REPEAT/FORのLoopCounter/LoopEnd/LoopStepがinstruction object stateであること、BREAK時のLoopStep進行、JUMPのreturn propagationはsource contractとして記録し、推測でframe-local化しない。
VM SelfTest=26/26、Phase1 Core=52/52、Phase1 Compiler=81/81、Differential=PASS、malformed structural tests=PASS。SemanticNotAvailable、UnsupportedControl、CodeNotAvailable、InvalidFunctionId、InvalidLocalPc、StepLimitは明示StopReasonで停止する。

Phase2ADecision=HOLD
Phase2AStatus=SKELETON_IMPLEMENTED_RUNTIME_ORACLE_PENDING
Phase2B/Phase3へ渡すもの=Legacy runtime oracleの自動化、loop reentrancy確定、expression/format/variable semantics、CALLFORM/dynamic call、GOTO/$label、TRY/CATCH、event dispatch、ARG/LOCAL/REF runtime、実ゲーム起動。

### Phase 2A-R1: Semantic Identity / Control Link / Legacy Runtime Oracle Closure

FunctionIdはSourceIndexのphysical definition順で134652件を保持し、Legacy effective nameはgeneric semantic overlayへ分離した。R1ではLegacy diagnostic exporterのfresh実行を一時real-fixture copyで試みたが15分でmanifest生成前timeoutとなったため、audit数値は変更前から保存済みのimmutable Legacy manifestをreplayしている。replayではsemantic rows 134652、exact position join 134649/134652（Legacy inline-braceの既知3件は隠さず記録）、effective duplicateは3 groups / 9 definitions、SET_BASE_5604・SET_BTL_TALENT_5604・SET_LEARN_SKILL_5604はFunctionIdへ解決、CALL/JUMPは12254/15、resolved=12269、missing=0、wrongKind=0となった。resolverはfirst-definition authority（method/eventを後続候補へskipしない）、comparerはcatalog構築時固定である。

FunctionCatalogはvalue record + file/name table + flattened candidate IDs + NameRangeへcompact化した。PCはnext-instruction local PCで、0..CodeLengthを有効域とし、CodeLengthはimplicit function fallthrough returnである。JUMPはPropagate frameでreturn伝播する。real linked instructionは全191273件のOperandOffset/OperandLengthを保持し、mismatch=0。SIF、IF ordered clause、SELECTCASE ordered case、loop descriptorを分離し、CONTINUEはloop kind別、REPEAT/FOR BREAKはcounter advance契約をside metadataへ保持する。

### Phase 2A-R2: Control Ownership / Semantic Identity / Runtime Oracle

R2実装では、FunctionCatalogEntryを`CatalogSourceRef(8)+EffectiveNameId(4)+PackedMetadata(4)`の16 bytesへ縮小し、FunctionId field、SourceSpan/PhysicalNameId duplication、permanent composite path lookupを除去した。SourceIndexをpermanent rootとし、EffectiveName未確定値は`EffectiveNameId=-1 / EffectiveNameKnown=false`でFindByNameから除外する。FixedCallResolverはordered candidateのfirst-definition authorityを維持する。

ControlLinkerはOpenFrameの`LoopControlRecordIndices`だけをloop close時にrewriteし、temporary opener record indexをStructuralLinkRecord.LoopIndexへ入れない。REPEAT/FORのBreakAdvancesCounter、loop kind別ContinueCheckPc、IF/SELECTのwarning-only分類、fatal InvalidStructure分類、SIFのLegacy partial opcode setをside diagnosticsへ分離した。InvalidStructureはVmStopReason.InvalidStructureへ対応する。VM SelfTest=45/45。

実fixture再監査はdefinitions=134652、compiled=59103、instructions=191273、CALL/JUMP=12254/15、resolved=12269、missing/wrongKind=0/0、operandSpanMismatch=0、loopDescriptors=128、maxLoopNesting=2を再現した。ExactPhysicalStartLineMatch=134649、KnownPositionResidual=3は保持した。raw ordinal identityではSourceIndexのpreprocessor定義3件とLegacy inline定義3件がfile単位で入れ替わるため、SemanticIdentityMissing/Extra=3/3、EffectiveNameUnknown=3となり、physical fallbackで0へ合わせていない。

retainedはbaseline→root生成→full GC後after→KeepAliveの順で独立3-runを測定し、9/9 valid、negative=0。性能5-run中央値はCoreNextPipeline=7126.209ms、AuditTotalIncludingAll=19240.320ms。CoreNextは直接stage sumであり、oracle/retained/report writingを含めない。runtime fixtureはProgram.csの実経路どおり`--ExeDir <temp>\Data`へ構成し、real CSVをコピーした16ケースでexit=0、timeout=False、case sentinel、startup-test.log、time.logを確認したが、同一minimal SYSTEM_TITLEの起動確認であり、loop挙動差のoracleではない。

R2 mutation verifierは実artifactを24回変異させ、Detected=24、FalsePass=0。identity 0/0と16個の個別runtime behavior truthは未達のため、`Phase2ADecision=HOLD`、`Phase2B/Phase3=NOT_STARTED`とする。

real fixtureはCodeAvailable=59103、LinkReady/LinkedSemanticPendingをExecutableReadyと分離し、ExecutableReady real=0。actual GC retainedはcatalog/linked/combinedを独立3回で測定した。VM SelfTest=33/33、Phase1 semantic regressionはeligible59435 / compiled59103 / remaining332 / errors0 / instructionCount191273 / baselineLost0。Legacy minimal fixtureのStartupTest oracleは本R1環境でログ生成まで到達せずtimeout/empty-logとなったため、BREAK・reentrant loop・implicit returnのruntime truthは未確定であり、Phase2ADecision=HOLD。Phase2B/Phase3=NOT_STARTED。
