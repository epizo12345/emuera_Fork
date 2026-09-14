# ERB 起動詳細 Profiling

最終更新: 2026-08-21（Phase 13R14 TermStack正式採用）

## 目的と背景

通常構成と大量口上構成の起動時間差を、ERB解釈を変えずに実測で分解する。対象は `PrimaryParse` と `ScriptParse` だけであり、高速化実装はこの Phase には含めない。

fixture は通常側に `Data/ERB/口上/口上まとめ` が無く、大量口上側には存在する。大量口上は 488 ERB、153,028,391 bytes（145.94 MiB）、4,171,006 行だった。口上まとめと実行時ログを除くファイル一覧は同一だった。

## 起動経路

`EmueraConsole.Initialize()` → `Preload.Load()` → `Process.Initialize()` → `ErbLoader.LoadErbDir()` の順で進む。`LoadErbDir()` は ERB 列挙、ファイル単位並列 `loadErb()`、`setLabelsArg()`、`ParseScript()` を順に実行する。`ParseScript()` は CALLFORM 系を検知後に残り関数を並列解析する。

## 有効化と出力

通常 Release には詳細計測コードを含めない。次の明示的な計測ビルドだけが `--ErbStartupProfile <directory>` を受け付ける。

```powershell
dotnet build -c Release -p:EnablePerformanceMetrics=true
Emuera.exe --ExeDir <ゲームData> --StartupTest --ErbStartupProfile <directory> --ErbStartupProfileMode timing
```

出力先には次を作る。

- `primary-parse.txt`: 物理行、`ReadEnabledLine` 戻り、LogicalLine、種別、Rename 経路・候補数
- `longest-observed-loadErb-wall-spans.tsv`: 最長20 `loadErb` elapsed span と各行数
- `script-timing.txt`: `setArgument` / `nestCheck` / `setJumpTo` の関数単位 aggregated elapsed span
- `script-counters.txt`: `SetArgumentTo` と `FunctionCode` 別件数

ファイル計測は1 ERBにつき1回だけ時刻を読む。`timing` は1関数につき3区間だけ計測し、命令別カウンタを更新しない。`counters` は時刻を読まず、スレッドローカル固定配列へ `FunctionCode` の件数だけを加算する。行単位の共有 `Interlocked` や `ConcurrentDictionary` は追加しない。並列時の累積値は CPU time ではなく aggregated elapsed span であり、wall time を超え得る。

## Phase 2-B: 測定モードと制約

- OFF: `PERFORMANCE_METRICS` なしの Release。通常性能の基準値。
- Timing-only: `--ErbStartupProfileMode timing`。関数境界の粗い時刻だけを取り、性能比較に使う。
- Counter-only: `--ErbStartupProfileMode counters`。命令数・Rename候補数を取り、時間を性能値として解釈しない。

PrimaryParse の `ParseLine` 等の行単位 sampling は撤去した。各ファイルで sampling 位相がリセットされる偏りと、時刻取得の汚染を避けるためである。`longest-observed-loadErb-wall-spans.tsv` は並列実行、GC、scheduler の影響を受ける observed elapsed span であり、ファイル固有CPUコストの順位ではない。

CPUは `dotnet-trace` の `Microsoft-DotNETCore-SampleProfiler`、allocation / GCは CLR EventPipe の `gc+gcsampledobjectallocationhigh+gcsampledobjectallocationlow+gcheapandtypenames+stack` を使う。`TraceAllocationAnalyzer` は `GCAllocationTick` を入力に型・call site 別の sampled allocation を出す。GC pause は本 Phase では取得していないが、`--BenchmarkLog` で process全体の allocation bytes と Gen0/1/2 collection count を記録する。

## 互換性と範囲外

ERB・CSV・設定・ゲームロジック、解析順、並列度、Rename、Macro、DEFINE、ArgumentParser、データ構造は変更しない。`addLine()` と `LabelDictionary.AddLabel()` の既存 `Interlocked` は計測対象として残すが変更しない。`PERFORMANCE_METRICS` を付けない通常 Release では詳細計測の呼び出し自体がコンパイルされない。

## Benchmark 方法

各 fixture を直接使い、`--StartupTest` を最低5回実行して min / median / mean / max を比較する。主値は median とする。初回が極端に遅い場合は cold/warm 差として扱い、単発値で判断しない。

Benchmark fixture は口上まとめ・ログ・save・cache・profileを除外した SHA-256 比較で 20,030 ファイルすべて一致した。2026-08-12 の計測結果と詳細プロファイルは Chat review ZIP に収録する。

## Phase 2-A 実測（2026-08-12）

5回の StartupTest の median は、通常構成で PrimaryParse 416 ms / ScriptParse 494 ms / ERB total 1,324 ms、大量口上構成で 1,305 ms / 1,442 ms / 3,054 ms だった。両条件とも Lv2 警告 0 件で起動した。

詳細 profile の大量口上1回では、3,909,550 LogicalLine、1,941,582 回の `ArgumentParser.SetArgumentTo` を確認した。ScriptParse の関数別累積時間は 27,391.855 ms で、そのうち `setArgument` は 24,618.268 ms（89.9%）、`nestCheck` は 1,101.649 ms、`setJumpTo` は 570.134 ms だった。並列処理のため、これらの累積時間は wall time 1,820 ms と比較してはならない。

PrimaryParse の1/64 sampling値は aggregated elapsed span の推定であり、CPU timeではない。Phase 2-Bで撤去し、外部traceへ置き換えた。大量口上では `IF` 474,018、`ENDIF` 474,018、`ELSEIF` 350,655、`CALL` 194,444 回が実引数解析された。

## Phase 2-B 実測（2026-08-12）

`PERFORMANCE_METRICS` なし Release の5回 median は、通常構成で Preload 363 ms / PrimaryParse 367 ms / ScriptParse 456 ms / ERB total 1,137 ms / Startup total 2,336 ms、大量口上構成で 504 ms / 1,023 ms / 1,722 ms / 3,026 ms / 4,442 ms だった。大量口上による差分は PrimaryParse +656 ms、ScriptParse +1,266 ms、ERB total +1,889 ms、Startup total +2,106 ms である。

計測ビルドの各5回 median（ERB total / Startup total）は、通常構成で OFF 1,276 / 2,615 ms、Timing-only 1,246 / 2,450 ms、Counter-only 1,226 / 2,389 ms、大量口上構成で OFF 3,291 / 4,705 ms、Timing-only 3,049 / 4,434 ms、Counter-only 3,148 / 4,566 ms だった。Timing-only / Counter-only の正の上乗せはこの5回測定では再現しなかった。モード別の連続実行、OS file cache、並列 scheduler の揺れを含むため、負の差を計測が速くした効果とは解釈しない。少なくとも旧Phase 2-Aの詳細命令別辞書・行sampling由来の大きな計測負荷は除去できている。

Timing-onlyの大量口上1回では、134,652 functions、`setArgument` 23,185.253 ms、`nestCheck` 1,763.074 ms、`setJumpTo` 1,010.209 ms、関数区間累積 26,372.175 msを記録した。これは並列実行で重なる elapsed span でありCPU timeではないが、`setArgument` はこの粗い関数区間の87.9%を占めた。Counter-onlyでは `SetArgumentTo` / force set argument が各1,941,582回、IF 474,018、ENDIF 474,018、ELSEIF 350,655、CALL 194,444回だった。命令種別別の時間は測定していないため、回数だけから命令別の単価を断定しない。

大量口上のCPU sampleでは `ErbLoader.setArgument` 16.64%、`ArgumentParser.SetArgumentTo` 15.78%、`INT_EXPRESSION_ArgumentBuilder.CreateArgument` 13.15% が上位だった。allocation traceでも `INT_EXPRESSION_ArgumentBuilder` 経由の `LinkedListNode<Word>` が約162.6 MB sample estimate、`ArgumentParser` 経由の expression / word 型が上位だった。

PrimaryParse側では allocation traceに `LogicalLineParser.ParseLine` の `InstructionLine`、`EraStreamReader.ReadEnabledLine` の `CharStream` と `String` が上位call siteとして現れた。RenameそのものはCPU sample上位に現れなかったため、本データでは第一優先候補ではない。`Interlocked.Increment` もCPU sample上位に現れず、優先度は低い。

GC計数（`--BenchmarkLog` の1回）は通常構成が allocation 2,485,282,592 bytes、Gen0/1/2 = 22/13/9、大量口上構成が 6,910,893,296 bytes、37/18/11だった。GC pause timeは本Phaseの取得対象外である。`ENDIF` は `VOID_ArgumentBuilder` を通る `FORCE_SETARG` 命令で、余分な引数を警告し `VoidArgument` を設定する互換性上の処理であるため、計測だけを根拠に省略しない。

## 次 Phase

Phase 3 では、この出力と allocation trace を照合して局所ボトルネックを確定する。Phase 4 以降で初めて、互換性リスクを明示した小さな高速化候補を個別に検証する。

## Phase 3 調査（2026-08-12）

`PERFORMANCE_METRICS` なし Release の clean EventPipe traceでも、大量口上は `ArgumentParser.SetArgumentTo` 13.70%、`INT_EXPRESSION_ArgumentBuilder.CreateArgument` 11.46%、`popTerms` 8.78%、`LexicalAnalyzer.Analyse` 6.67%、`ExpressionParser.ReduceArguments` 5.79% の一本の経路を確認した。allocationでは `LinkedListNode<Word>` が 370.7 MB sample estimateで、INT_EXPRESSION 経由だけでも約207.9 MBだった。CPU比率・allocation値はいずれも sampling値である。

最初のPhase 4候補は、top-level comma を持たない INT_EXPRESSION だけを既存の Lexer / ExpressionParser / Restructure に通しつつ、`ReduceArguments` の複数引数コンテナを省く guarded Fast Path とした。空引数・comma・末尾comma・異常系は既存の `popTerms` を維持する前提で、実装はまだ行っていない。詳細な経路、互換性条件、テスト設計は `08_ArgumentParser性能設計.md` を参照する。

## Phase 4-A（2026-08-12）

最初の局所変更として、`ExpressionParser.ReduceArguments` の一時 `LinkedList<AExpression>` を `List<AExpression>` に置き換え、最後の List 展開コピーも除去した。Parserの受理・拒否、順序、戻り値型、`isDefine` の既存node-null判定の結果、`Restructure`、警告・エラー処理は変更していない。

大量口上 allocation trace では `LinkedList<AExpression>` 28.6 MB、`LinkedListNode<AExpression>` 45.7 MB の sampled type allocation が after で出現しなかった。大量口上5回 median は ScriptParse 1,763 → 1,656 ms、Startup 4,822 → 4,668 msだった。一方、通常構成は ScriptParse 633 → 611 msであるものの、独立区間の揺れによりStartup 2,619 → 2,742 msとなった。採用は通常側再測定後に判断する。

## Phase 4-A Verification（2026-08-12）

Before/Afterを同じPhase 2/3変更上に分離した独立worktreeでReleaseビルドし、`ReduceArguments`だけを `LinkedList` / `List` に変えた。warm-upを除外し、BN→AN→BK→AK と AK→BK→AN→BN を交互に10回ずつ、合計80起動（各variant・fixture 20回）した。clean Release、同じfixture、各runのtime.logを保存した。

paired差（After - Before）の中央値は、通常 ScriptParse -5 ms、通常 Startup -53.5 ms、大量口上 ScriptParse +57 ms、大量口上 Startup +52.5 msだった。大量口上 ScriptParseの前回約-107 ms改善は20組では再現せず、差の符号も6/20のみ改善だった。通常ScriptParseは10/20改善、通常Startupは11/20改善で、いずれも一貫方向ではない。variant別medianは通常 ScriptParse 529→516 ms、大量口上 1,610.5→1,632.5 msだった。

交互80回では全run終了コード0だったが、Lv2警告がBefore 1回、After 1回発生した。追加の隔離5回×4条件ではBefore通常5/5・Before口上5/5・After口上5/5がOK、After通常だけ4/5 OK（1回Lv2）だった。警告内容はrunごとに異なり、今回のList変更だけが原因と断定できないが、互換性差なしの正式判定条件は満たしていない。

独立allocation traceではBeforeの `LinkedList<AExpression>` 22.3 MB、`LinkedListNode<AExpression>` 39.1 MB sampledがAfterで消失し、`List<AExpression>` は48.0→46.9 MBだった。総allocation sampledは4,449→4,360 MB、BenchmarkLog 1回のkojo allocatedBytesは6,913→6,801 MBだったが、GCはBefore 38/18/10、After 32/17/10であり、一回値で確定的な改善とはしない。

判定（当時）は**正式採用推奨ではなく、追加検証**だった。allocation削減は再現したが、性能改善の方向が揃わず、After側のLv2警告も残ったためである。コードはこの検証中変更していない。この仮判定はMacro hash collision調査とFinal Adoption Reviewの結果を反映して更新済みである。

## Phase 4-A Compatibility Investigation（2026-08-13）

Phase 4-A benchmarkの低頻度Lv2警告を、同じPhase 2/3変更を含む隔離worktreeで比較した。Afterは `List<AExpression>`、Beforeは `LinkedList<AExpression>` だけが異なる。AfterのERB解析を完全逐次化する診断版は、PrimaryParse、LabelSetup、CALLFORM検出後の残余ScriptParseの3個の `Parallel.ForEach` を `foreach` にする一時変更であり、主作業treeには残していない。

大量口上は、Before + Parallel ONが 0/100、After + Parallel ONが 1/100、After + Parallel OFFが 0/50 のLv2警告だった。通常構成はBefore + Parallel ON、After + Parallel ON、After + Parallel OFFの各50回で0件だった。After ONの1件は `RPG\\スキル関係\\04_回復\\SKILL460_青春の風.ERB:74` の `@SKILL_RANK_460, ARG = -1` に対し、`関数"@イベントフラグ"の引数のエラー:引数の書式が間違っています` と報告された。同じERBを毎回失敗する再現性は確認できず、ログ・time.logを全回保存した。

ソース上の警告経路は、関数引数書式が `ErbLoader.ParseLabelWithCatch` / `parseLabel`、一次元変数の添字過多が `VariableParser.ReduceVariable`、未定義関数名が `ErbLoader.setJumpTo` と CALL/CALLF の `AInstruction.SetJumpTo` である。`ReduceArguments` は式・関数定義引数の構文解析にだけ関与し、Function/Label登録、LabelSetupの実行順、未定義関数警告の集約には書き込まない。よって今回のLv2警告を `LinkedList` → `List` 変更だけで説明する経路は確認できず、Phase 4-Aとの因果関係は**低い**。

並列側では `LabelDictionary`（ConcurrentDictionary＋内側List lock）、`ParserMediator.warningList`（ConcurrentQueue）、`#ONLY`集合（ConcurrentDictionary）、解析中行（ThreadStatic）、LOCAL/ARG token cache（ConcurrentDictionary）、未定義関数警告の抑制集合（ConcurrentDictionary）が既に対策済みである。PrimaryParse完了後にLabelSetup、LabelSetup完了後に `SortLabels` / `Initialized = true`、その後にScriptParseへ進むため、Function/Label辞書を登録途中にScriptParseが読む経路は確認できなかった。一方、並列実行時の警告出力順は決定的でなく、LOCAL/ARG token cacheは同名ラベルの初期化順に依存し得るため、これらは継続調査対象として残る。

この測定だけでは Parallel ON 1/100 と OFF 0/50 の差を統計的に確定できない。したがって、並列Parserとの因果関係は**中（疑い）**、根本原因は**未特定**とする。警告抑制、lock追加、Collection置換、並列方式の変更は実施していない。Phase 4-Aの正式採用判断は引き続き保留し、必要なら警告発生時のLabel名・位置・Thread IDを記録する一時診断で再現回数を増やしてから、最小修正案を別Phaseで検討する。

## Parallel Warning Investigation Step 2A（2026-08-13）

この節はソース調査だけであり、診断コード、修正、ビルド、起動、Benchmarkは行っていない。`VariableData` は `LOCAL` / `ARG` / `LOCALS` / `ARGS` 用に4個の `VariableLocal` を作る。各 `VariableLocal.localVarTokens` は `ConcurrentDictionary<string, LocalVariableToken>` で、keyは `subKey`（通常は現在行の `ParentLabelLine.LabelName`）だけである。Label位置・ファイル・`FunctionLabelLine` の参照・ARG/LOCAL種別以外の情報はkeyに入らない。valueは型別の `Local*1DVariableToken` で、可変 `size` と実配列を持つ。`subID` は保存されるが、tokenの所有者照合には使われない。

通常経路は `ErbLoader.setLabelsArg` → `Parallel.ForEach(labelList, ParseLabelWithCatch)` → `parseLabel` → `ExpressionParser.ReduceArguments` / `ReduceVariableIdentifier` → `IdentifierDictionary.GetVariableToken` → `VariableLocal.GetExistLocalVariableToken` / `GetNewLocalVariableToken` である。CALLFORM検知後の残余ScriptParseも `ParseFunctionWithCatch` → `setArgument` → 同じ式・変数解決経路を `Parallel.ForEach` から呼び得る。解析中行は `Process.parallelScanningLine` の `[ThreadStatic]` であり、通常の無指定LOCAL参照はその親ラベル名をkeyにする。

`#LOCALSIZE` / `#LOCALSSIZE` はPrimaryParse中に各 `FunctionLabelLine` へ書かれる。`ArgLength` / `ArgsLength` は `parseLabel` が引数列を読み終えた末尾で書かれる。したがってLabelSetup中のARG/ARGS token初回生成は通常default sizeであり、`Parallel.ForEach` の完了後に `SortLabels` が非イベント関数の確定先（FileIndex / 行番号でsortした先頭）に合わせてARG/ARGS tokenをresizeする。イベント関数のLOCAL/LOCALSは `SortLabels` が同名イベント群の最大値へ揃える。

ただし `GetNewLocalVariableToken(subKey, func)` のkeyとサイズ算出元は別引数であり、`GetOrAdd(subKey, ret)` は外で生成済みの `ret` の先着値を採用する。明示 `ARG@別名` / `LOCAL@別名` は `GetVariableToken` がLv1警告を出すだけで許可され、現在ラベル `func` のサイズを別名keyに格納できる。同名の非イベント定義が異なる `#LOCALSIZE` / `#LOCALSSIZE` を持ち、その関数定義またはCALLFORM残余解析でLOCAL/LOCALSを参照する場合も、複数 `FunctionLabelLine` が同じkeyへ異なるsizeを同時に渡せる。非イベントの `SortLabels` はARG/ARGSだけをresizeし、LOCAL/LOCALSを確定先へresizeしない。

評価は、コンテナ操作のThread Safeは**Yes**、初期化は**Partial**、Ordering Safeは**Partial**、Semantic Ownership Safeは**No**である。ConcurrentDictionaryは破損を防ぐが、先着tokenが正しいlabel所有者かは保証しない。結論は **A. 有力**（具体的なkey/owner不一致と先着採用経路あり）とする。ただし前回の実測行 `@SKILL_RANK_460, ARG = -1` は明示 `ARG@別名` を含まない。keyは別名へ変換されず、`ParseLabelWithCatch` のLv2見出しも引数の `label.LabelName` を使うため、今回の「`@イベントフラグ` と表示された」cross-name警告を `localVarTokens` だけで説明する経路は**ない**。この候補は別種のownership不整合として分離して扱う。

次Phaseで最小診断を入れる場合は、`GetNewLocalVariableToken` と `ResizeLocalVariableToken` に Thread ID、varCode、dictionary key、cache hit/miss / GetOrAdd採否、current label名・位置・ファイル、`func`名・位置、ARG/ARGS/LOCAL/LOCALSのsize、tokenの生成時ownerを記録する。`GetVariableToken` 側では明示`@`の有無も記録する。実装はこのPhaseでは行わない。

## Parallel Warning Investigation Step 2B（2026-08-13）

目的を `FunctionLabelLine` のName/Position整合性観測に限定し、`PARALLEL_WARNING_DIAGNOSTICS` symbolで診断コードを追加した。`FunctionLabelLine.LabelName` は `protected set` だが、生成後に書き換える実コード経路は見つからない。`LogicalLine.scriptPosition` は外部setterを持たず、`ScriptPosition` は `readonly record struct` のreadonly `Filename` / `LineNo` で、生成後変更経路はない。

診断は `ConditionalWeakTable<FunctionLabelLine, Snapshot>` に `RuntimeHelpers.GetHashCode` のObjectId、生成時Name/Positionを保存する。CREATE（`ParseLabelLine`）、REGISTER（`LabelDictionary.AddLabel`）、LABEL_SETUP（`setLabelsArg`取得時）、PARSE_LABEL（`ParseLabelWithCatch`入口）、関数引数警告直前を同じ形式で観測し、対象Label名 `SKILL_RANK_460` / `イベントフラグ` または対象ファイルだけを通常記録する。生成時と現在値の不一致は対象外でも記録する。記録は `ConcurrentQueue<string>` へ積み、load完了時に一度だけ出力するため、LabelごとのファイルI/Oやlockは行わない。通常Releaseではsymbolが無く、診断コードはコンパイルされない。

Normal Releaseと診断Releaseはともに0エラー（既存警告70件）でBuild成功した。診断版の少数試験は大量口上5回、通常1回で、全6回が終了コード0・起動完了・Lv2警告0件・診断ログ生成成功だった。口上5回の各ログは92行でCREATE/REGISTER/LABEL_SETUP/PARSE_LABELを含み、`SKILL_RANK_460` は全観測点で同一ObjectId・Name・`SKILL460_青春の風.ERB:74`、Mismatch=Falseだった。`イベントフラグ` のcross-name現象は再現しなかった。

次に大量試験を行う場合は、この診断版を維持したまま同じログ形式を収集し、ObjectIdの継続性、Name/Position mismatch、警告直前のlabelと実行行を突き合わせる。今回、VariableLocal候補は別Issueとして保持し、Parser本処理・LabelName/Position・登録方式・並列方式・warning判定は変更していない。

## Parallel Warning Investigation Step 2B.1 Diagnostic Hardening（2026-08-13）

Step 2Bの診断基本設計は維持し、診断器だけを小修正した。主ObjectIdは `RuntimeHelpers.GetHashCode` ではなく、Snapshot初回生成時の `Interlocked.Increment` によるプロセス内 `long` 連番へ変更した。hashは異なるobject間の衝突を理論上排除できないためである。同一 `FunctionLabelLine` はConditionalWeakTableの同じSnapshotを使い、異なるobjectには異なる連番を割り当てる。

Candidate外のREGISTER/LABEL_SETUP/PARSE_LABELでは、まずName/Positionの低コスト判定と既存SnapshotのTryGetValueだけを行い、Candidateでなく未追跡のobjectにはSnapshot、dictionary追加、キュー追加、文字列生成を行わない。CREATEで追跡を開始したobjectは、後段で現在NameがCandidate外へ変化しても既存Snapshot経由で最後まで追跡する。Warning観測は低頻度の対象経路なので、Candidate外も従来どおりObjectIdと現在/Original情報を記録する。通常Releaseでは `PARALLEL_WARNING_DIAGNOSTICS` が定義されず、診断class・呼出し・collectionはhot pathへ残らない。

診断Releaseは口上有り2回（任意確認として通常1回）を実行する。各回は終了コード0、起動完了、診断ログ生成、Lv2=0。`SKILL_RANK_460` のCREATE/REGISTER/LABEL_SETUP/PARSE_LABELは同一連番で、Candidate外を含む重複ObjectIdは確認されない。ログ行数はStep 2Bの92行から対象絞り込み後の92行相当の候補観測範囲で確認し、診断対象外の全Labelを記録していない。これは性能Benchmarkではなく、次の大量再現試験へ進める診断状態の確認である。VariableLocal.localVarTokensのownership候補は別Issueとして保持し、Parser本処理は変更していない。

## Parallel Warning Investigation Step 2C Reproduction Run（2026-08-13）

Step 2B.1診断版をそのまま使い、口上有りfixtureを逐次起動した。最大100回の停止条件に対し、10回目で`警告Lv2`を1件検出したため停止した。実績はOK 9回、WarningLv2 1回、StartupFailure 0回、ProcessFailure 0回で、全10回が終了コード0かつ`Init:End`到達だった。起動完了までの計測値は`run-summary.csv`に保存した。

警告は`RPG\\スキル関係\\90_CSTR専用スキル\\専用スキル\\SKILL_デビチル_高城ゼット_ディープホール展開.ERB:69`の`@SKILL_RANGE_ディープホール展開,ARG`に対する`@ダンジョンフラグ`引数書式エラーだった。診断ログの警告観測はObjectId 24（ThreadId 12）で、Name/Position mismatchはFalse。関連候補`SKILL_RANK_460`（ObjectId 17）はCREATE/REGISTER/LABEL_SETUP/PARSE_LABELで同一ObjectId・位置を維持し、今回の警告とは別Labelだった。原因の確定や修正はこの実行では行っていない。

Step 2CではParser、LabelSetup、VariableLocal、並列方式、警告判定を変更していない。再現証拠（警告全文、raw ERB行、Object trace、診断ログ）は`ChatReview_parallel-warning_Step2C_20260813.zip`に収録する。

## Macro Hash Collision Investigation / Fix（2026-08-13）

Step 2Cで観測されたcross-name warningの候補として、`IdentifierDictionary` のMacro辞書を実ソースで再確認した。変更前の`macroDic`は`Dictionary<int, DefineMacro>`で、`AddMacro`と`GetMacro`の双方が`Keyword.GetHashCode(StringComparison.Ordinal または OrdinalIgnoreCase)`だけをkeyにしていた。Dictionary自身の整数Equalityは元文字列を比較しないため、異なるMacro名のhash collision時に別Macroを返し得る構造であり、仮説は成立する。

修正は`Runtime/Script/Data/IdentifierDictionary.cs`だけに限定した。`macroDic`を`Dictionary<string, DefineMacro> = new(Config.StrComper)`へ変更し、登録を`Add(mac.Keyword, mac)`、取得を`TryGetValue(key, out ...)`へ変更した。`Config.SetConfig`は`Process`が`IdentifierDictionary`を生成する前に実行され、`Config.StrComper`は`IgnoreCase`に応じて`OrdinalIgnoreCase`または`Ordinal`へ設定される。したがって大文字小文字の既存仕様を維持し、`Dictionary.Add`による同Comparerキーの重複例外も維持する。`ErhLoader.analyzeSharpDefine`が登録し、`LexicalAnalyzer.Analyse`の`expandMacro`が取得・展開する。`LogicalLineParser.ParseLabelLine`も同じ`Analyse`を通るため、Label名部分はMacro展開対象になり得る。

現在の2 fixtureから抽出した`#DEFINE`は334件（異なる名前167件、hash group 167件）で、今回のプロセスのhash値では異なる名前同士の衝突は0組だった。これは理論上のhash-only問題を否定するものではない。専用テストframeworkは存在しないため、新規テストは追加せず、fixture内Macroを含む起動で互換性を確認した。Normal Releaseは通常fixture 1回・口上fixture 2回が全てOK、Diagnostic Releaseの口上fixture最大100回も100/100 OK、Lv2=0、ParserFailure=0、StartupFailure=0、ProcessFailure=0だった。平均・中央値などの性能評価は行っていない。

Step 3ではFunctionLabelLine、LabelDictionary、VariableLocal、Parser並列方式、Phase 4-A、診断器を変更していない。`VariableLocal.localVarTokens`のownership候補と、今回のMacro修正後にも再現しなかったcross-name warningの別原因は、別Issueとして保持する。

## Phase 10B～12C後の再baseline（2026-08-21）

Phase 12C正式採用後の大規模口上fixtureを対象に、R10以降のallocation分布を再確認した。ScriptParseが主要なallocation hotspotであることを再確認し、HTML fast path導入後も次の候補はTermStack周辺へ絞った。

R11の`reduceTerm` early-return fast pathは、通常の式形状で互換性FAIL（Lv2診断増加）となったためREJECTし、正式ソースへ残していない。

## Phase 13R12 TermStack inline-one-element candidate（2026-08-21）

`ExpressionParser.TermStack`の既存`Stack<object>` fieldを1つの`object storage`へ置換した。0要素はnull、1要素はinline object、2要素目でだけ`Stack<object>(5)`へpromotionする方式で、評価順・reduceTerm control flow・演算子処理は変更していない。Normal/Kojoとsemantic differentialはPASSだった。

R12のcoverageではTermStack 3,656,271回のうち2,618,989回（71.63%）がpromotion不要だった。ScriptParse一時allocationはServer 251,424,792 bytes、Workstation 251,506,128 bytes（いずれも約9.60%）減少し、inline-only×96 bytesの理論値と0.04%以内で一致した。3-runではServerのwall差が確定しなかったため、採用判断はR13へ継続した。

## Phase 13R13 Repeat Performance Validation（2026-08-21）

Server / Workstation各GC modeでBaseline/Candidateを10回ずつ、`B-C-C-B`×5 blockのABBA順で再測定した。全40 runがInputReady到達・Lv2=0で、allocation削減はServer 251,430,450 bytes、Workstation 251,445,633 bytes（各9.60%）と再現した。Server ScriptParseは平均+26.5ms（ABBA差分の中央値-10.5ms）、Process.Initialize平均-3.4ms、InputReady平均-12.7msで、再現性のある起動退行は確認されなかった。Workstation ScriptParse平均-11.1ms、Process.Initialize平均-16.7msだった。判定はPASSとした。

## Phase 13R14 TermStack正式採用（2026-08-21）

R13でPASSした`ExpressionParser.cs`のTermStack storage変更だけをProductionへ採用した。Release buildは0 errors / 35 existing warnings、formal single-file publishとNormal/Kojo smokeはPASS。配布EXE、README、SHA256SUMS、引き継ぎ書、採用済み変更ファイル一覧を同一採用commitへ同期した。Workstation GC、GC policy、runtimeconfig、GC設定は変更していない。

この変更の数値はScriptParse中の一時allocationに限定され、常駐memoryや通常プレイplateauが同じ量だけ減ることを意味しない。次の優先課題は、起動後約2.3GBから通常プレイ約3.3GBへ上昇してplateauするmemoryについて、allocation量ではなくlive / retained / committedの内訳を直接profilingすることである。

## Phase 4-A Final Adoption Review（2026-08-13）

`ReduceArguments` の `LinkedList<AExpression>` → `List<AExpression>` は、末尾追加・末尾参照・順序保持だけを使う局所置換であり、`isDefine`、終端処理、戻り値型・順序の意味変更は確認されなかった。追加直後の `terms.Count == 0` は旧 `terms.Last == null` と同じく実質falseで、値null判定への仕様変更ではない。

過去traceで `LinkedList<AExpression>` 22.3 MB、`LinkedListNode<AExpression>` 39.1 MB（別traceでは28.6 MB / 45.7 MB）がAfterで消失し、総allocationも約90〜110 MB前後低下した結果を再利用できるため、不要node allocation削減はCONFIRMEDとした。一方、5回で見えたScriptParse改善は20組交互検証で再現せず、強いorder effectも確認されたため、速度改善はUNPROVENであり採用理由にしていない。今後はbalanced order・order strata・cool-downを考慮する。

Macro hash collision調査後、cross-name Lv2の本命原因はMacro Dictionaryのhash-only lookupと判断され、Macro Fix後Diagnostic Kojo 100/100（Lv2=0、StartupFailure=0）およびFinal Compatibility Verification PASSを確認済みである。Phase 4-A固有の互換性破壊を示す証拠は現時点でなく、Build・Normal/Kojo起動も正常だったため、最終Recommendationは**ADOPT**とする。理由は起動高速化ではなく、同じ用途で不要なLinkedList node allocationを避ける低リスクの局所改善である。VariableLocal候補は別Issueとして扱う。

## Macro Hash Collision Fix — Final Compatibility Verification（2026-08-13）

Step 3のproduction変更を増やさず、最終互換性を確認した。`Config.SetConfig`は`IdentifierDictionary`生成前に実行され、`IgnoreCase=YES`では`StringComparer.OrdinalIgnoreCase`、`NO`では`StringComparer.Ordinal`を`Config.StrComper`へ設定する。修正後の`Dictionary<string, DefineMacro>`はこのComparerを使い、`AddMacro`の`Add`と`GetMacro`の文字列`TryGetValue`を維持する。

実動確認では、ビルド済みproduction `IdentifierDictionary`を一時ヘルパーから呼び出し、YESで`TEST_MACRO`/`test_macro`/`Test_Macro`が同一Macroへ解決、NOで完全一致のみ解決することを確認した。両設定とも同名二重登録は`ArgumentException`となり、旧`Dictionary.Add`の重複挙動を維持した。既存ERHのMacro定義とERBの式中Identifier（`FLAG_白き鋼鉄のX_進行度`、`D3D_PLAYER_XCOORD`等）を含む通常解析も正常終了し、通常Macro展開・式中利用に問題はなかった。Label解析は`LogicalLineParser.ParseLabelLine`から同じ`LexicalAnalyzer.Analyse`を通る構造を再確認し、既存fixture起動で確認した範囲に異常はない。

Normal Releaseは通常fixture 1/1、口上fixture 2/2がOK（Lv2=0、Parser error/Exceptionなし）。Diagnostic Releaseは前回の100/100 OK（Lv2=0）を再確認済みであり、今回のFinal Verificationでは大量再試験を繰り返していない。Final Verificationのproduction changes addedは0で、変更は既存Macro修正と資料更新のみ。判定は**Macro hash collision fix：採用可能**とする。`VariableLocal.localVarTokens`候補、Phase 4-A、Parallel Warning診断器は変更せず別Issue/保持とした。
