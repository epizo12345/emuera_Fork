# Next Runtime 全体設計書

## 1. Next Runtimeとは

Next Runtimeは、既存EmueraのERB・セーブ・入力・表示互換を保ったまま、起動、解析、コンパイル、CALL中心の実行、常駐メモリ、二回目以降の起動を改善するための独立した実行基盤である。Legacy Runtimeを一度に置き換えるのではなく、Legacyを互換性の基準（oracle）として、検証済みの境界から段階的に機能を移す。

## 2. 目標

目標は、同じゲームが同じ意味で動くことを前提に、不要なオブジェクトグラフと再解析を減らし、測定可能なstartup・parse/compile・execution・retained memory・warm startupを改善することである。Legacyと同程度の性能に留まる実装は、採用理由にならない。

## 3. 現行Emueraが重くなりやすい理由

Legacyは長年のERB互換、warning/error、評価順、RNG、セーブ、UI、画像、入力を一つの実行系で扱ってきた。その結果、ファイル、行、トークン、式、命令、シンボルが相互参照するオブジェクトグラフを起動時に構築しやすい。互換性のための履歴は価値がある一方、全ファイルを読むこと、全関数を解析すること、触れたコードを解放しないことが、ゲーム規模に比例してstartupと常駐メモリを押し上げる。

## 4. 実行の流れ

```text
ERB/ERH/CSV
    │ read-only scan
    ▼
Source Index（ファイル、関数名、物理byte span、fallback flag）
    │ 必要な関数だけロード
    ▼
関数単位のparse / compile
    │ compact IRへ変換
    ▼
compact instruction / expression IR
    │ bounded cacheから取得
    ▼
Next VM ── Host boundary ── UI / Graphics / Input / Save
    │
    └── Legacy oracleとの差分検証
```

## 5. Source Index

Phase 0AのSource Indexは実行系ではなく、ERBを読むための小さな索引である。UTF-8 BOM（EF BB BF）を必須とし、厳格なUTF-8検証を行う。関数は識別子として読める`@`行だけを採用し、`@FUNC(ARG)`や`@FUNC, ARG`では`FUNC`だけを名前にする。`@"文字列"`は関数でない。関数ごとにBOMを含む物理byte offset、1-based行範囲、fallback flagを保持し、本文やLegacyの行オブジェクトは保持しない。

`[IF...]`等のPreprocessor、宣言directive、関数metadata、Rename、行継続、その他の意味的に安全でない構造はflagとして記録する。Preprocessorは後続の有効ソースを変えるためファイル単位のfallbackであり、そのファイルの関数にも伝播する。IndexAuditは索引構築時間・allocationと、後続の集計・診断を別々に測る。

## 6. Lazyだけでは不十分な理由

必要になった関数だけを遅延ロードしても、ロードした本文を大きな文字列・トークン・ASTのまま保持すれば、触った関数数に応じて常駐メモリが増え続ける。また、毎回parse/compileを繰り返せば実行時の待ち時間になる。したがって最終形は、関数単位lazy load、compact compile、上限付きcache、eviction（再生成可能なデータの追い出し）の組み合わせとする。

## 7. メモリの三分類

* Permanent：ゲーム状態、変数・キャラクター状態、Source Index、symbol table、セーブ互換metadata。実行中に必要で、勝手に追い出さない。
* Evictable：source cache、compiled code、IR、再生成可能な画像・layout。上限を超えたら追い出せる。
* Transient：lexer/parser/compilerの作業buffer。処理単位の終了後に再利用または解放する。

この分類をcache設計と計測項目の共通語彙にする。unbounded compile cacheは許可しない。

## 8. 完了済み作業と今後の起動計画

完了はPhase 0AのSource Index、IndexAudit、SelfTest、0A-R1のLegacy境界に沿ったflag分類、0A-R2の関数ヘッダー境界と計測分離、0B-R2のLegacy oracle差分・PPState disabled range診断・性能baseline、1A-R4の関数単位compiler prototypeと保持メモリ計測・artifact整合確定である。Phase 1A-R4ではまだVMを開始していない。

今後は関数単位compiler prototype、compact IR、instruction VM、bounded/evictable cache、disk compile cacheへ進む。warm startupでは、変更検出済みのsource indexと検証済みcompile cacheを再利用し、不要なparser再実行を避ける。cacheは再生成可能であり、配布Runtime本体とは別扱いにする。

## 9. 互換性の境界

ERBを第一対象とし、ERH、resourcesのCSV、`sav`、`global.sav`、SAVECHARA、LOADCHARA、式評価（eval）、RNG、入力、WAIT、右クリック、ESC、Gamepadを互換性検証の対象とする。評価順、乱数消費順、warning/error、未定義・境界値の挙動も対象である。Legacy Save CodecとLegacy executionは当面adapter/oracleとして残す。Graphics/UIとfilesystemはHost/Platform boundaryの外側に置き、Next CoreへWinForms依存を持ち込まない。

## 10. UTF-8 BOM

ERBの入力契約はUTF-8 BOM付きである。BOMは文字データではなく、SourceSpanの物理offsetではファイル先頭の3 byteとして数える。BOMなしは推測変換せず拒否し、invalid UTF-8も拒否して診断する。これは環境依存の文字化けとoffsetずれを早期に検出するためである。

## 11. 画像・UIとPhase 14A–14N

既存の画像・UI・native ownership改善であるPhase 14A–14NはWindows Host資産として再利用する。Next Coreは画像描画、WinForms、Gamepad、saveファイル書き込みを直接所有しない。移行時はHost APIを通じて互換性を確認し、UIの見た目や入力操作を性能改善の都合で暗黙に変更しない。

## 12. 最終配布

最終Windows版のユーザー向けRuntimeは、現行Emueraと同様にframework-dependent single-file publishを基本とする。publish設定は`PublishSingleFile=true`、`SelfContained=false`とし、ゲームフォルダへ手動配置する本体は原則`Emuera.exe`一ファイルとする。compile cacheや診断ログは再生成可能な補助物であり、配布本体に含めるRuntime componentとは区別する。

## 13. 現在の状態

Phase 0A、0A-R1、0A-R2、0A-R3、0B、1Aを完了とする。0A-R3では、Legacyの`{`単独行から`}`単独行までの行連結を安全側fallbackとして検出し、連結内部の`@`を通常の関数境界として扱わない。nested・異常終了・未閉鎖も安全側へ倒す。識別子delimiterには`\\`を含め、先頭のvertical tab/form feedはLegacy互換のため空白として飛ばさない。全角spaceは`SystemAllowFullSpace`依存のためCoreへ設定を持ち込まずfallbackを付ける。保持メモリはindex構築前baselineとindexだけを保持したforced-GC後の差として診断する。0Bでは実際のLegacy `ErbLoader` / `LogicalLineParser` / `LabelDictionary`をoracleとして9458 ERB・134652関数を照合し、safe 112854、fallback 21798、unexpected missing/extra/name/order/invalid-error mismatch 0を確認した。1AではSource IndexのFunctionIndexを入口に、FileStreamの関数byte spanだけを読み、compiler-supportedな関数をcompact struct列へ変換した。正式mainへの統合やGitGudへのpushは別の明示的な作業である。

## 14. ロードマップ

```text
0A Source Index
 → 0B Legacy oracle differential / performance baseline
 → 1A function-level compiler prototype
 → 2 compact instruction VM
 → 3 compact expression / format IR
 → 4 bounded compiled cache / eviction
 → 5 disk compile cache / warm startup
 → 6 Next VariableStore
 → 7 Host I/O / security sandbox
 → 8 full compatibility expansion
```

各段階は差分検証と実測で継続・修正・停止を判断する。先の段階を理由に互換性検証を省略しない。

## 15. 性能目標

目標はstartup、parse/compile、CALL-heavy execution、retained memory、warm startupの改善である。ただし、Phase 0AのSource Index測定だけからRuntime全体の高速化を保証しない。数値保証は、同一fixture、同一設定、同一測定境界、Legacy比較、複数回測定が揃った後に定義する。旧計測と後処理境界が異なる値は直接比較しない。

## 16. Phase 0A-R2の実測値

最終値はレビューartifactの`audit/real-game.txt`に記録する。対象fixtureは`E:\GAME-2\テスト版\eramegaten_p_口上有り`であり、値はSource Index構築だけの時間・allocationと、後処理の時間・allocationに分ける。これらはIndexAuditの実装と実行環境に依存し、Legacy Runtime全体との性能比較を意味しない。特にPhase 0Aの旧allocation値とは測定境界が異なるため、NOT DIRECTLY COMPARABLEである。

## 17. Phase 0Bの差分と性能baseline

対象は`E:\GAME-2\テスト版\eramegaten_p_口上有り\Data`である。Legacy oracleは診断ビルドの実際のERB parse/load経路から取得し、Nextは同じERB directoryのSource Indexから取得した。manifestはファイル順・関数順・名前・1-based行・byte span・fallback flagを比較した。

LegacyとNextは9458ファイル・134652関数で一致した。Nextのsafe functionは112854、fallback functionは21798。R2ではLegacy PPStateのdisabled rangeを出力し、unexpected missing、extra、name、order、FileOrder、invalid/error mismatchとunexplained fallback differencesを0にした。fallback reasonはDeclarationDirective 6844、FunctionMetadata 6817、LineContinuation 164、OtherSemanticFallback 1425、Preprocessor 234、Rename 12705（重複計上）。BIT_SETTING.ERBの`SETTING_IS_XXX`（150行）、`SETTING_INVERT_XXX`（166行）、`SETTING_SET_XXX`（178行）は`[SKIPSTART]` 139行から`[SKIPEND]` 186行のDisabled rangeへ対応した。

重複はcase-insensitive groupingで3名称・9定義、最大4定義、定義順差分0である。`#PRI`等を含むevent priority semantic verificationは`DEFERRED / LEGACY FALLBACK`であり、Next VMでの完全一致確認ではない。Legacyの比較設定は`IgnoreCase=True`、`StringComparison=OrdinalIgnoreCase`、`SystemAllowFullSpace=True`である。

性能は各5回の診断baselineで、Legacy actual ERB parse/loadの中央値は8340.524 ms、allocation中央値は6282642256 bytes、forced-GC後のretained estimate中央値は1618849120 bytes。Next Source Index構築の中央値は1841.047 ms、allocation中央値は38044280 bytes、retained index中央値は17163408 bytes。処理境界が異なるため、速度比・runtime高速化・起動高速化は主張しない。p95、最大、標準偏差とメモリ境界はレビューartifactに記録する。

0B-R2の差分ゲートと1Aのcompiler prototype gateは通過した。1AはChatレビュー可能な基準点であり、VM、VariableStore、Expression/Format IR、disk cache、Process.ScriptProc置換、正式EXE変更を含まない。Phase 1B/Phase 2は別の明示的承認まで開始しない。

## 18. Phase 1A-R1 関数単位compiler prototype（履歴基準値）

`FunctionSourceReader`は`SourceFileIndex + FunctionIndex`のfile length / last-write time snapshotを検証してから、`FileStream.Seek(StartOffset)`と必要byte数のreadだけを実行する。不一致は`SourceChanged`としてcompileしない。UTF-8はstrict validationし、BOMはfile offsetに含むが関数sliceには含めない。ERB全体のstring/行配列、Legacy `ErbLoader` hydration、Legacy parser再利用はCompiler本体では行わない。

`FunctionCompiler`はIndexSafeを入口とし、さらにevent/system/method/duplicate ambiguityやCompiler未対応命令を除外する2段階eligibilityを持つ。結果は`Compiled`、`Unsupported`、`SourceChanged`、`InvalidSource`、`CompilerError`に分け、unsupported理由をenumで診断する。新形式は`CompiledFunction`と連続した`ImmutableArray<PrototypeInstruction>`で、1命令1class instanceやLegacy `LogicalLine` / expression object graphのコピーを行わない。明示的なLegacy command → `PrototypeOpcode` mappingを1か所に持つ。

1A-R1の実ゲーム結果は9,458 ERB、Index functions 134,652、Index safe 59,438、Compiler considered 59,438、Compiler eligible 59,435、compile succeeded 54,200、unique unsupported 5,235、compiler errors 0である。5回のunsupported encountersは26,175で、coverage件数とは分離した。関数source sizeはmin 15、median 79、p95 275、p99 587、max 49,278 bytes、instruction 83,761件。`PrototypeInstruction`の実測サイズは16 bytes、payloadは1,340,176 bytes、function metadata theoretical payloadは1,734,400 bytes、forced-GC後のretained managed medianは7,001,824 bytes、推定overheadは3,927,248 bytesである。source read allocation中央値35,169,832 bytes、compiler allocation中央値60,421,536 bytes、total allocation中央値96,067,968 bytesで、R1は関数ごとの64KB FileStream bufferをbuffer 1 + RandomAccessへ変更した。最大関数は2,259,654-byte spanを直接readでき、全file 3,666,631 bytesを保持しない。operand semanticsは未比較で、Expression/Format IRとcontrol-flow loweringは後Phaseで行う。

function source bytesのSHA-256 fingerprintは同一sourceで決定的で、1文字変更では変更する。synthetic `FUNC_A/FUNC_B`でA unchanged、B changedを確認した。fingerprintは32-byte valueとしてCompileResultに分離し、CompiledFunctionへ文字列を保持しない。R1ではdisk cache、dependency graph、bounded cache、VM、VariableStore、Process.ScriptProc置換、正式EXE変更を行わない。代表関数とunsupported理由はCompilerAudit reportへ出し、巨大本文はreview ZIPへ含めない。

## 19. Phase 1A-R2 計測と関数読み込みの改善

R1のretained値はbenchmark run間の前回結果をbaselineへ含み得たため、R2ではbenchmarkと分離した独立測定にした。前回resultを解放してfull GCした後、54,200 compiled functionsだけを`List<CompiledFunction>`でroot保持し、`managedBeforeCompile`、`managedImmediatelyAfterCompile`、`managedAfterDiagnosticGc`を記録する。R2のcompiledRetainedManagedEstimateは25,197,560 bytesで、runごとの中央値ではない。

Known payloadは、instruction payload 1,340,176 bytes、CompiledFunction descriptor field theoretical payload 3,468,800 bytes、unique function-name UTF-16 payload 1,844,032 bytes、unique file-path UTF-16 payload 575,948 bytes、合計7,228,956 bytesと分離した。actual retained managedは25,197,560 bytes、estimated managed overheadは17,968,604 bytesであり、理論値とGC後保持量を混同しない。`sizeof(PrototypeInstruction)`は16 bytesである。

coverage funnelは、Index total 134,652 → Phase0B verified safe 112,854 → Phase0B fallback/unsafe 21,798 → Compiler strict-clean 59,438 → Phase0B safe but compiler excluded 53,416 → Compiler eligible 59,435 → Compile succeeded 54,200 / Unsupported unique 5,235 / Errors 0とする。strict-cleanはSourceIndexのfunction flagsがNoneで、かつfile-level fallbackがない関数である。`IndexFunctionNotMatched` 8,379はfile欠落ではなく、8,376件が`[[...]]` rename後のLegacy名と物理ヘッダー名の違い、3件がstart-line/name join差分で、代表例をCompilerAudit reportへ出す。

CompilerAuditのbatch pathでは、eligible functionsをfileごとにstart offset順へ並べ、1 file sessionを開いて複数spanを読む。R2は3,552 file sessions / runで、single-function open相当59,435回を置き換えた。ERB全体をstringやstring[]へ保持せず、sessionはboundedに1 fileずつ閉じる。SourceChangedはopen/read時のlength・last-write time snapshotで検出する。R1中央値 total 6,607ms / source read 5,929ms / compiler 145msに対し、R2は1,725ms / 932ms / 54ms、total allocationは96,067,968 bytesから87,163,032 bytesへ悪化しなかった。

single-function `FunctionSourceReader.Read`はlazy runtime向けに維持し、batch sessionはCompilerAudit専用である。flat instruction arenaは短関数のdescriptor/array overheadをさらに下げる候補だが、R2では実装せずPhase 1B以降の候補として保留した。

### Phase 1A-R3 保持メモリ純化と残差分

R2の25,197,560 bytesはcompiled listに加えて差分辞書、fingerprint、key文字列を同じrootで保持した監査込み値だった。R3ではそれらを解放してから、`List<CompiledFunction>`だけをrootにした別測定を行った。54,200 compiled functionsについて、`managedBeforePureCompile`、`managedImmediatelyAfterPureCompile`、`managedAfterPureDiagnosticGc`を記録し、pure retained managed estimateは7,050,712 bytesとなった。instruction payload 1,340,176 bytes、descriptor theoretical payload 3,468,800 bytes、name payload 1,844,032 bytes、path payload 575,948 bytes、known payload合計7,228,956 bytesは実測保持量と別の理論値として扱う。監査用list・compiledByKey・fingerprintByKey・key stringsを含むaudit-inclusive retainedは25,246,200 bytesである。

IndexFunctionNotMatchedの8,379件は、8,376件がLegacy oracleの`[[...]]` rename後の名前とSource Indexの物理ヘッダー名の違い、3件がstart-line差分である。3件は`CARD_BATTLE_AI_SET.ERB:3794`の`CB_MAX_SUM`、`戦闘NPC.ERB:10963`の`CS_CARD_DELIVERY`、同:11081の`CS_SUMMONER_DECK_SET`で、いずれもLegacyが`{`から`}`までのinline brace-style宣言を認識し、`@NAME`の行ではなく閉じ括弧行をLegacyStartLineとして記録する。Source Indexにはその閉じ括弧行から始まる関数境界がないため、無理にjoinせずLegacy fallbackとして残す。

R3の5-run compiler batch性能はtotal中央値1,954.712 ms、source read中央値1,062.277 ms、compiler中央値62.108 ms、total allocation中央値87,163,032 bytesである。R2と同じ処理境界・fixtureであり、純粋保持計測を追加したこと以外に性能実装を変更していない。非自明なEmuera固有Production改修には、理由・互換性・lifetime・ownership・評価順・性能を日本語で記すコメント規則を`07_AI作業ルール.md`へ正式追加した。

### Phase 1A-R4 計測定義とartifactの最終確定

R4ではPure known payloadからbaseline以前に存在するSource Index由来の共有function-name/path参照を除外した。3回のPureRetainedBytesは7,050,736、7,050,736、7,050,712 bytes、中央値は7,050,736 bytesである。InstructionPayloadは1,340,176 bytes、NewFunctionDescriptorKnownPayloadは3,468,800 bytes、OtherNewCompiledOwnedPayloadは0 bytes、PureKnownPayloadTotalは4,808,976 bytes、PreExistingSharedPayloadReferencedは2,419,980 bytes、PureEstimatedManagedOverheadは2,241,760 bytesであり、negative overhead gateはPASSである。監査辞書を含むAuditInclusiveRetainedは25,246,200 bytesで、PureRetainedとは別値である。

R4の5-run raw TSVを正本とし、自動集計した性能はtotal中央値1,953.059 ms（mean 1,972.434 / min 1,929.404 / max 2,082.478）、source read中央値1,055.302 ms、compiler中央値60.184 ms、total allocation中央値87,163,032 bytesである。Run IDは`20260827_Phase1A_R4_Final`で、raw TSV・pure runs・summary・final-status・Review READMEへ共通記録する。Phase 1Aはこの計測定義、benchmark aggregation、Git証跡、canonical docsの整合確認をもって正式COMPLETEとする。

### Phase 1B Compiler Coverage Expansion

Run IDは`20260827_Phase1B_R1_Final`。R4のcompiler eligible 59,435関数を再走査し、baseline success setをR4 compiler manifestの54,200関数として固定した。実装前のunsupportedは5,235関数・30,978 blocker occurrencesで、主要familyはSET/代入4,836関数、CALLFORM 201、RESETCOLOR 150、TRYCALLFORM 55、SETCOLOR 53だった。R1では未対応Legacy命令のoperand中の`=`をSETへ誤認しない予約語ガードを追加し、負例を自己テストへ固定した。累積機会は関数重複を除外して算出し、分析artifactにはfirst reason、all blockers、代表例、Tier分類を分離して保存した。

Tier Aとして、`SET`（変数代入）、`RESETCOLOR`、`CUSTOMDRAWLINE`、`SETCOLOR`、`SETFONT`を、Legacy FunctionCodeと同名のdistinct `PrototypeOpcode`およびraw operand spanだけで対応した。scannerは比較演算子を代入と誤認せず、instructionはsource line・operand offset・operand lengthを持つ。Legacy parser、LogicalLineParser、ExpressionParser、InstructionLineはproduction compiler pathで再利用しない。Tier Bは`CHKFONT`、`GETFONT`、`RESULT`、`RESET_STAIN`等のcommand/state境界、Tier Cは`CALLFORM`、`TRYCALLFORM`、`CATCH`/`ENDCATCH`、`LOCAL`、式・macro・preprocessor・dynamic name依存として延期した。`GETFONT`/`CHKFONT`を一度候補化した際にLegacy oracleの`__NULL__`との差分が出たため、対応から外し、Exact差分0を優先した。

実ゲーム結果は、Previously compiled 54,200、Newly compiled 4,893、Total compiled 59,093、Remaining unsupported 342、Compiler errors 0である。Phase0B verified safe比は52.36%、Compiler eligible比は99.42%。baseline lost 0、baseline instruction count/order/exact opcode mismatchは各0、expanded mismatchも各0、Phase1B baseline lostも0、`sizeof(PrototypeInstruction)`は16 bytesである。5-run baseline subsetはR1 artifactの実測値を正本とし、expanded raw instruction countは190,482である。Pure retained、audit-inclusive retained、性能・allocationはR1 artifactへ記録した。

Expanded pure retainedは9,261,512 bytes、instruction payload 3,062,384 bytes、descriptor theoretical payload 3,794,560 bytes、PureKnownPayloadTotal 6,856,944 bytes、estimated overhead 2,404,568 bytes。監査辞書等を含むaudit-inclusive retainedは`audit-inclusive-retained-memory.txt`へ分離した。Pure測定では`List<CompiledFunction>`だけをGCHandleでroot化し、同一rootに対する3回のfull-GC読値を採用する。これは監査辞書をretainedへ混ぜないための測定境界である。

Phase 1B後の最上位残件は`TRYCALLFORM` 55、`CATCH`/`ENDCATCH`/`TRYCCALLFORM`各37系、`CHKFONT` 30、`GETFONT` 29、`RESULT` 29である。次候補は、Tier Bのfont/state命令をLegacy source例とoracleで精査するPhase 1C。VM、VariableStore、Expression/Format IR、disk cache、正式EXE、distributionはPhase 1Bでは開始しない。

### Phase 1B-R2 完了判定

`20260827_Phase1B_R2_Final`で、Legacy `BuiltInFunctionCode` と `FunctionMethodCreator` の実在名を統合した予約表をNext compiler metadataへ固定した。未対応Legacy命令のoperand中の`=`はSETへ誤認せず、架空tokenは予約表へ登録していない。実ゲームは59,093 compiled / 342 unsupported / compiler errors 0、baseline lost 0、instruction count/order/exact opcode mismatchはすべて0、expanded raw instruction countは190,482、`sizeof(PrototypeInstruction)`は16 bytesである。CompilerSelfTest 56/56、Core SelfTest 52/52、DifferentialAudit SelfTest PASS。Phase 1BはCOMPLETE/HOLDとし、VM、Expression IR、Format IR、Phase 1Cは開始しない。

## 20. Phase履歴

- Phase 0A: Source Indexの基礎。実ゲーム9,458 ERB / 134,652関数。
- Phase 0B: Legacy実parserとのoracle差分検証。verified safe 112,854、予期しない差分0。
- Phase 1A: Function-level compiler prototype。54,200関数compile。
- Phase 1A-R1: 64KB/function bufferを修正し、allocation約4GBから約96MBへ削減。Exact Opcode化。
- Phase 1A-R2: retained/metadata/coverageを分離し、file-scoped batch I/Oでsource read約5.93秒から約0.93秒へ削減。正式資料を`プロジェクト資料/NextRuntime/`へ移管。
- Phase 1A-R3: compiled rootだけのpure retainedを監査辞書から分離し、pure 7,050,712 bytes、audit-inclusive 25,246,200 bytesを記録。3件のIndexStartLineMismatchはLegacyのbrace-style inline declarationの閉じ括弧行とSource Index境界の差として説明した。非自明なEmuera固有Production改修の日本語理由コメント規則を正式化した。
- Phase 1A-R4: baseline以前の共有payloadをPure known payloadから除外し、negative overhead gateを有効化。3回pure retained、5回benchmarkの自動集計、Run ID、artifact/Git証跡の最終整合を確定した。

## 21. Phase 0B-R1 差分更新とcache無効化

R1では差分更新を実装せず、root相対path・length・last-write time・content hashをfile identityとする。変更ファイルは関数spanと行連結blockを再計算し、関数content hashの変更を起点に、確定したcall/reference dependencyの逆向き到達範囲を再評価する。ERH、Rename、Preprocessor、宣言directiveは安全側にfile-level invalidationとする。

画像・CSVなどの非ERB assetは独立namespaceでcacheし、ERB変更では無効化しない。cache headerにはengine version、index schema version、parser rule version、設定値、root identityを含め、変更時はfull rebuildする。一時manifestをLegacy oracle互換のFileOrder・function order・span・fallback理由で検証してからswapし、失敗時は前回の完全なindexを維持する。R1はこの設計と検証契約までであり、差分cache、dependency graph、runtime統合は未着手である。

### CSV変更（Phase 0B-R2設計）

CSVは依存性で分類する。表示・名称・説明などcompile時意味解析へ影響しないものはERB compile cache全破棄を不要とする。変数・定数・ID・構造など解析時意味へ影響するものは関連compiled functionを無効化する。影響範囲不明のCSVは、依存関係が確定するまで安全側に広くcache invalidationする。R2では分類本実装を行わず、後のdependency graph設計へ残す。PNG/JPG等だけの変更でERB compile cacheを全破棄しない既存方針、ERB小変更のfile-level変更検出からfunction hash比較へ進む方針、ERH/Renameの広い無効化、engine/compiler/IR version変更時のfull rebuildも維持する。

### Duplicate event semantics

Phase 1以降のFunctionId/event dispatchでは、LegacyのFileIndex、source order、`#PRI`、`#LATER`、`#ONLY`、`#SINGLE`、`CompatiCallEvent`等を再現する必要がある。Phase 0B-R2で検証したのは定義集合・FileOrder・StartLine・定義順までであり、event dispatch semanticsの完全差分は実行系が存在するPhaseで行う。

## 20. 用語集

* Source Index：ソース本文を持たず、ファイルと関数の位置を示す索引。
* SourceSpan：物理byte offsetと行範囲。
* fallback：安全に意味を推測できないため、Legacy側の解釈へ委譲する印。
* oracle：新実装の正しさを照合する既存実装。
* lazy load：必要になった時点で読む方式。
* IR：ソースと最終命令の中間表現。
* bounded cache：容量上限を持つcache。
* eviction：再生成可能なcache項目を追い出すこと。
* warm startup：一度準備した索引やcompile結果を再利用する起動。
* Host boundary：UI、画像、入力、save、filesystemとCoreを分ける境界。

### Phase 1B-R3 完了判定

Run IDは`20260827_Phase1B_R3_Final`。Legacyの`FunctionIdentifier.GetInstructionNameDic()`を実行時oracleとして診断exportし、`Method == null`のstatement command 275件だけをNext予約表へ固定した。全件diffはMissing 0、Extra 0、Duplicate 0、Empty 0、method-only false reservation 0。SET判定はexact opcode、Legacy statement reservation、assignmentの順で、未対応statementのoperand中の`=`をSETへ誤認しない。CHKFONT/GETFONTは実辞書上のmethod-onlyで、statement予約falseとした。

実ゲーム結果はcompiled 59,093、remaining unsupported 342、compiler errors 0、baseline lost 0、instruction count/order/exact opcode mismatch 0、Phase0B safe 52.36%、eligible 99.42%。5-run性能中央値はtotal 1,913.485 ms / source read 1,018.398 ms / compiler 83.051 ms / allocation 125,520,296 bytes。Pure retainedは完全独立3回が各8,703,552 bytes、中央値8,703,552、PureKnownPayload 6,829,664、overhead 1,873,888。Audit-inclusive retained 28,602,312 bytesは別項目である。Phase 1BはCOMPLETE/HOLD、VM・Expression IR・Format IR・Phase 1Cは未開始。

### Phase 1B-R4 完了判定

Run IDは`20260827_Phase1B_R4_Final`。Legacyの実行時 `FunctionIdentifier.GetInstructionNameDic()` を全行頭識別子のoracleとし、A=456、B=`Method == null`のstatement 275、C=`Method != null`のmethod-backed 181を取得した。NextはAをB∪Cとしてassignment guardへ登録し、A/B/CのMissing・Extraは0、A.union(B,C)=pass、B.intersection(C)=0、重複・空要素0、method-only false reservation 0である。これによりmethod-backedのCHKFONT、GETFONT、RAND、ABS、MINは行頭ガード対象だがstatement予約ではなく、`X=Y`をSETへ誤認しない。通常のRESULTS/A/日本語代入はSET、比較演算子はSET化しない。`A==B`はLegacy parserがassignmentへ進む実態を記録しつつ、Next prototypeでは比較意味未実装のため安全側へ延期した。CHKVARDATA、CHKGLOBALDATA、FIND_VARDATAはoracleに無いためA/B/Cへ追加していない。

実ゲームはeligible 59,435、compiled 59,093、unsupported 342、errors 0、R3成功集合baseline lost 0。baseline・expandedともinstruction count/order/exact opcode mismatch 0、`PrototypeInstruction` 16 bytes。5-run R4 expanded中央値はtotal 2,118.147 ms、source read 1,171.062 ms、compiler 88.684 ms、allocation 125,520,296 bytes。R3成功集合baseline中央値はtotal 2,109.883 ms、allocation 124,527,416 bytesで、10%超のallocation regressionはない。pure retainedは8,703,552 bytesを3回とも測定し、known payload 6,829,664、overhead 1,873,888、audit-inclusive retained 16,019,832 bytes。Phase 1BはCOMPLETE/HOLDとし、VM、Expression IR、Format IR、Phase 1Cは未開始。

### Phase 1B-R5 最終Gate

Run IDは`20260827_Phase1B_R5_Final`。Legacy実行時oracleを4構成（IgnoreCase × UseScopedVariableInstruction）で起動し、A=456、B=275、C=181を得た。Aは全行頭lookup、Bは`Method == null`、Cは`Method != null`で、A=B∪C、B∩C=0、statement map−B=0、map∩C=0。`SET`はstatement mapから除外し、`TryMapStatementIdentifier`では受理せず、保守的な構造代入の後だけで生成する。scannerはLegacy `ReadSingleIdentifierROS`のdelimiterとseparatorを再現し、`//` shortcutは持たない。

実fixtureはeligible 59,435、compiled 59,093、remaining unsupported 342、compiler errors 0。Phase 1A-R4の54,200件baselineはlost 0、baseline/expandedのinstruction count・order・exact opcode差分は全て0。expanded 5-run中央値はtotal 1,858.849 ms、source read 992.243 ms、compiler 89.554 ms、allocation 119,904,656 bytes。pure retainedは独立3回が8,703,552 bytesで一致し、known payload 6,829,664、overhead 1,873,888。fixture変更は0件、実fixtureの全角空白直後例は37件。`Phase1BDecision=COMPLETE`。VM、Expression IR、Format IR、Phase 1Cは開始しない。
