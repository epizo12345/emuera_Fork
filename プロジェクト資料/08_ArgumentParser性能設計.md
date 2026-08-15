# ArgumentParser / INT_EXPRESSION 性能設計（Phase 3 - Phase 7 ReadFirstIdentifier Candidate D Production Adoption）

最終更新: 2026-08-15（ReadFirstIdentifier Candidate D Production Adoption）。これは `07_ERB起動詳細Profiling.md` の Phase 3〜7 詳細資料であり、過去の測定値と現在の採用状態を併記する調査記録である。Phase 4-Aの`ReduceArguments` List化、Phase 5-C3のWordCollection Compact Representation、Phase 6-C2.2のzero-overhead Lexer-only Lazy Capacity 8、Phase 6-EのArgument / argprimitive One-Reference化、ReadFirstIdentifier Candidate DはADOPT済みである。Strict Decimal Fast PathはLOW VALUE / not implemented、旧Phase 6-C2 field実装はHOLD / superseded、速度はUNPROVENである。

## ReadFirstIdentifier Candidate D Production Adoption（2026-08-15）

行頭命令名解析で、`LogicalLineParser` が `.Code` だけを必要とする経路の一時 `IdentifierWord` wrapper生成を除去した。`ReadFirstIdentifierWord` は他の呼出しのため残し、共有cache・pool・static mutable stateは追加していない。Release build、Normal/Kojo startup compatibilityはPASSした。

既存allocation測定では、Normalが約 -30.0MB（-1.59%）、Kojoが約 -138.5MB（-2.44%）となり、両構成で明確な削減を確認したためSTRONG PASSと判定した。GC countは構成ごとに混在したため改善とは主張しない。startup時間の単発値から速度向上または完全同速とは断定しない。

## Clean Release trace

`PERFORMANCE_METRICS` なしの Release を `--StartupTest` で直接起動し、EventPipe の `Microsoft-DotNETCore-SampleProfiler` と CLR allocation / GC events を外側から採取した。対象は通常構成と大量口上構成を各1回である。CPU比率は inclusive sampling のため重複し、allocation bytes は `GCAllocationTick` の sampled estimate である。

大量口上の CPU 上位は `ParseFunctionWithCatch` 17.99%、`setArgument` 14.51%、`ArgumentParser.SetArgumentTo` 13.70%、`INT_EXPRESSION_ArgumentBuilder.CreateArgument` 11.46%、`ArgumentBuilder.popTerms` 8.78%、`LexicalAnalyzer.Analyse` 6.67%、`ExpressionParser.ReduceArguments` 5.79%、`reduceTerm` 5.41% だった。通常構成は短いため同経路の sample が少ないが、`ParseFunctionWithCatch` 5.35% を確認した。大量口上での主経路という Phase 2-B の結論は clean Release でも再現した。

大量口上 allocation の上位は String 1,167,959,848 bytes (25.04%)、InstructionLine 479,446,000 (10.28%)、`LinkedListNode<Word>` 370,741,304 (7.95%)、Char[] 358,453,248 (7.69%)、CharStream 288,791,120 (6.19%)、IdentifierWord 160,807,352 (3.45%)、AExpression[] 106,834,936 (2.29%) だった。`INT_EXPRESSION_ArgumentBuilder` 経由だけでも `LinkedListNode<Word>` 207,908,344 bytes、識別子・整数・記号 Word、WordCollection、ExpressionArgument、`LinkedListNode<AExpression>`、`LinkedList<AExpression>` が上位 call site に出る。通常構成でも `LinkedListNode<Word>` 123,694,016 bytes、WordCollection 24,558,544 bytes、TermStack 24,471,912 bytesを確認した。

GC event は allocation trace に有効化したが、既存 `TraceAllocationAnalyzer` は allocation tick の型・call site 集計のみで pause 時間を出力しない。clean Release の `--BenchmarkLog` 1回は通常構成が allocation 2,474,098,888 bytes、Gen0/1/2 = 25/14/10、大量口上が 6,894,296,208 bytes、33/16/12 だった。GC pause 時間は未取得であり、Phase 2-B の値を流用しない。

## 実際の呼出経路

`ErbLoader.setArgument` が `ArgumentParser.SetArgumentTo` を呼ぶ。後者は既に Argument がある行、エラー行、非 Debug 時の Debug 命令を除外した後、`line.Function.ArgBuilder.CreateArgument` を呼び、`CodeEE` を Lv2 警告・`InstructionLine.IsError`・`ErrMes` に変換し、成功時に `InstructionLine.Argument` へ保存する。

`INT_EXPRESSION_ArgumentBuilder.CreateArgument` は `ArgumentBuilder.popTerms` を呼ぶ。`popTerms` は `PopArgumentPrimitive` → `LexicalAnalyzer.Analyse(...EoL...)` → `ExpressionParser.ReduceArguments(...EoL...)` の汎用「カンマ区切り複数式」経路である。結果を `checkArgumentType` が検査し、整数型なら各 `AExpression.Restructure(exm)` を実行する。正常な単一式は `ExpressionArgument` として保存され、定数なら `ConstInt` / `IsConst` も設定される。REPEAT は COUNT 用の `SpForNextArgment` を別途作る。

`LexicalAnalyzer.Analyse` は `CharStream` を読み、`WordCollection` 内の `LinkedList<Word>` に LiteralIntegerWord、IdentifierWord、OperatorWord、SymbolWord、文字列 / 書式文字列 Word を追加する。空白、括弧深度、コメント、文字列、macro 展開、演算子、rename 残骸、括弧不整合もここで扱う。Word と各 LinkedList node が入力 token ごとに割り当てられる。

`ExpressionParser.ReduceArguments` は comma / 括弧終端を扱い、`LinkedList<AExpression>` に各式を追加して最後に List へ展開する。`ReduceExpressionTerm` / `reduceTerm` は `TermStack` を使って優先順位、単項・二項・三項演算子、関数呼出し、配列添字、変数解決を処理し、AExpression 木を作る。関数引数と配列添字は再帰的に `ReduceArguments` を呼ぶ。`WordCollection` の LinkedList は、現在 node の前後参照、`Insert` / `InsertRange` / `Remove`、macro 展開に必要であるため、全体を List に置換する設計は別 Phase の大規模変更となる。

`Restructure` は単なる最適化ではない。各 Term 派生型で定数化、関数 / 変数の解決、型に応じた再構成を行う。整数専用経路でも省略してはいけない。

## INT_EXPRESSION を使う命令

直接登録は PRINT_ABL / PRINT_TALENT / PRINT_MARK / PRINT_EXP / PRINT_PALAM / CUPCHECK / LOADDATA / FONTSTYLE(nullable) / REDRAW / CALLTRAIN / DOTRAIN / RESET_STAIN / FORCEKANA / SKIPDISP / ASSERT。Instruction 側は PRINT_SPACE / CLEARLINE / RANDOMIZE(nullable) / DELDATA / TOOLTIP_SETDELAY / TOOLTIP_SETDURATION / REPEAT / WHILE / SIF / IF / ELSEIF / LOOP である。IF と ELSEIF は同じ INT_EXPRESSION、CASE は `CASE_ArgumentBuilder` で `CASE 1,2,3`、`IS`、`TO` を含む複数 CaseExpression 用の別経路である。CALL / CALLFORM / CALLF は `SP_CALL_ArgumentBuilder` 等で、関数名、添字、可変個の引数を解析する別候補であり、Candidate 1 には含めない。ENDIF / ENDSELECT / DO は VOID で余剰引数を検証するため、対象外である。

## popTerms と Fast Path

INT_EXPRESSION は論理的には単一整数式だが、既存実装では top-level comma も許容し、`checkArgumentType` が「引数が多すぎます」を Lv1 で出して先頭式を採用する。空引数は 0 として警告する。従って `popTerms` を無条件に削除することは互換性を壊す。括弧内 comma、関数引数、文字列中 comma は top-level comma と区別する必要がある。

Fast Path は**可能だが条件付き**である。`PopArgumentPrimitive()` は一度だけ消費できるため、LexicalAnalyzer 後に `popTerms()` を呼び直す fallback は不可能である。将来の候補は INT_EXPRESSION の `CreateArgument` で一度だけ得た同じ `WordCollection` を使い、top-level comma がなければ `ReduceExpressionTerm(wc, EoL)`、comma があれば `ReduceArguments(wc, EoL, false)` へ渡す分岐である。既存と同じ整数型検査、`Restructure`、定数化、命令固有警告は維持する。空入力・comma・想定外の残 token の既存意味を変えない。tokenization、WordCollection、Word node、TermStack、式木は維持されるので、`LinkedListNode<Word>` を削減する案ではない。

互換性の危険は、空引数、末尾 comma、余剰引数、型不一致、括弧・演算子・unknown variable・関数呼出し失敗の警告種別・位置である。Fast Path は同じ `LexicalAnalyzer` / `reduceTerm` / `Restructure` を使い、fallback を先に判定してから一度だけ解析しなければならない。解析後に再解析する設計は副作用、警告重複、性能悪化の危険がある。

## Phase 4 candidates（履歴。Phase 5-Aで再評価済み）

この節の候補順位は当時の設計案であり、現時点の実装候補は末尾の Phase 5-A Candidate 1 だけである。特に旧Candidate 1の「同じWordCollectionを用いるgeneric parser分岐」はWord node allocationを減らさず、Phase 5-Bの最優先候補には採用しない。

### Candidate 1

- 対象: 単一 top-level 整数式の INT_EXPRESSION。
- 変更候補: `ArgumentBuilder.cs` の `INT_EXPRESSION_ArgumentBuilder.CreateArgument` と、必要なら副作用なしの top-level comma 判定 helper 1個。
- 内容: 上記の guarded Fast Path。comma / 空 / 異常境界は generic `popTerms` を維持する。
- 根拠: clean Release CPU で CreateArgument 11.46%、popTerms 8.78%、ReduceArguments 5.79%。allocation で INT_EXPRESSION 経由の Word node 207.9 MB sample estimate と汎用引数コンテナ群を確認。
- 期待: CPU / `ReduceArguments` 用 `List<AExpression>` の扱いは低〜中。Word token allocation は残る。
- 互換性リスク: 中。エラー・警告の完全一致試験を通る場合だけ採用する。
- 推奨モデル: Terra 高。

### Candidate 2

- 対象: `LexicalAnalyzer.Analyse` と WordCollection / Word node。
- 内容: 設計調査のみ。token 容器または token 割当を見直す大規模案。
- 根拠: Word node は clean Release 大量口上で 370.7 MB sample estimate。
- リスク: 高。macro、挿入・削除、隣接走査、例外位置に広く影響する。
- 推奨モデル: Terra 非常に高い。Candidate 1 の結果が不十分な場合だけ。

### Candidate 3

- 対象: CALL 系 `SP_CALL_ArgumentBuilder`。
- 内容: 今回は分離し、関数名・添字・可変引数用の独立 trace / 設計を行う。
- 根拠: call stack / allocation に存在するが、INT_EXPRESSION と互換性条件が異なる。
- リスク: 中〜高。
- 推奨モデル: Terra 高。

PrimaryParse の Rename / CharStream / LogicalLineParser と `Interlocked` は今回変更しない。Rename / Interlocked は CPU sample 上位でなく、第二候補のままとする。

## Phase 4 test design

既存 unit test project は見つからず、既存の実行確認は `起動安定性テスト.ps1` と `--StartupTest` である。Phase 4 では小さな parser 回帰テストを既存プロジェクト構成に最小追加し、少なくとも IF / ELSEIF / SIF / WHILE / REPEAT について、`1`、変数、`A+B`、`(A+B)*C`、関数呼出し、配列添字、優先順位・三項演算子を比較する。異常系は空引数、top-level comma、末尾 comma、不正括弧、不正演算子、余剰 token、文字列式、未知変数、未知関数を generic 現行結果と Fast Path 結果で比較する。行番号、Lv、エラー種別、`InstructionLine.Argument` の型・定数値・評価値を確認する。

性能判定は通常 / 大量口上を各5回、中央値と分布で比較し、大量口上 ScriptParse の一貫した改善、通常構成の悪化なし、StartupTest 全成功、Lv2 警告なしを必須とする。allocation trace と BenchmarkLog の allocation / GC 指標も before / after で比較する。単発数 ms は成功と扱わない。

## Phase 4-A: ReduceArguments内部Collection

この節の当時の速度・採否評価は、後続の `Phase 4-A Final Adoption Review` で更新された。現在の判定は、速度改善はUNPROVEN、allocation削減はCONFIRMED、最終推奨はADOPTである。

`ExpressionParser.ReduceArguments` の内部だけを、`LinkedList<AExpression>` から `List<AExpression>` へ置換した。利用していた LinkedList API は `AddLast`、`Last`、`Last.Value`、最後の List 展開のみであり、順序・戻り値型は維持できる。最後のコピーは `return terms` にして除去した。capacity は実測根拠がないため指定していない。

`isDefine` の `AddLast` 直後にある `terms.Last == null` は node の存在を検査しており、追加後は常に false となる既存挙動である。今回これを `terms.Count == 0` に置換しただけで、最後の値が null かどうかを検査する意味には変えていない。値が null の後続 `GetOperandType()` が例外になる既存挙動も変更していない。

before / after の大量口上 clean Release allocation trace では、`LinkedList<AExpression>` 28.6 MB、`LinkedListNode<AExpression>` 45.7 MB の sampled type allocation が after では出現しなかった。`List<AExpression>` は before 55.2 MB、after 43.7 MB sampledである。trace全体は別実行の sampleなので総量を型差分だけで断定しないが、対象Collectionの削除は確認できた。BenchmarkLog 1回の total allocated bytes は大量口上で約94.3 MiB、通常で約44.9 MiB低下した。GC回数は大量口上で 33/16/12 → 34/17/10、通常で 25/14/10 → 23/12/8 と一回値の揺れがあり、確定的なGC改善とは扱わない。

5回 median は大量口上 ScriptParse 1,763 → 1,656 ms（-107 ms、-6.07%）、Startup 4,822 → 4,668 ms（-154 ms、-3.19%）。通常 ScriptParse は 633 → 611 ms（-22 ms、-3.48%）だったが、PrimaryParse / LabelSetup の独立した揺れにより Startup は 2,619 → 2,742 msとなった。このため採用判断は**採用候補・通常側の再測定要**であり、通常Startupを悪化させない条件をまだ確定的には満たしていない。

## Phase 4-A Verification

20サイクルの交互実行（BN/AN/BK/AK と逆順を各10回、計80起動）で再検証した。paired median（After - Before）は通常 ScriptParse -5 ms / Startup -53.5 ms、大量口上 ScriptParse +57 ms / Startup +52.5 ms。大量口上 ScriptParseの-107 msは再現せず、20組中の改善は6組だった。Before/After各20回のvariant別medianは、通常 ScriptParse 529/516 ms、大量口上 1,610.5/1,632.5 msである。

paired差のばらつきは、通常 ScriptParse IQR 117 ms・SD 72.52 ms、通常 Startup IQR 378 ms・SD 229.35 ms、大量口上 ScriptParse IQR 198 ms・SD 238.32 ms、大量口上 Startup IQR 636 ms・SD 335.67 msだった。外れ値は除外していない。

StartupTestは交互80回で全終了コード0だが、Lv2警告がBefore 1回・After 1回。追加隔離5回ではAfter通常のみ1/5がLv2警告となったため、Parser挙動差なしの判定は保留する。専用parser unit testは存在しない。

判定: **正式採用推奨ではなく追加検証**。`LinkedList<AExpression>` / nodeのallocation消失は再現したが、性能改善は再現せず、Afterの警告もゼロになっていない。次回は警告原因を別途固定してから、必要なら同じ交互測定を再実行する。

## Phase 4-A Compatibility Investigation

低頻度Lv2警告の切り分けでは、`ReduceArguments` のコンテナ変更前後と、Afterを完全逐次化した一時診断版を比較した。大量口上ではBefore + Parallel ON 0/100、After + Parallel ON 1/100、After + Parallel OFF 0/50、通常構成では各条件0/50だった。After ONの警告は `@SKILL_RANK_460, ARG = -1` に対する別名 `@イベントフラグ` の関数引数書式エラーで、同一ERBへの固定再現はしなかった。

`ExpressionParser.ReduceArguments` は `ErbLoader.parseLabel` の関数定義引数と命令引数を構文解析するが、LabelDictionaryへの登録、`SortLabels`、`ParserMediator`の警告キュー、未定義関数の `setJumpTo` 集約を変更しない。今回の警告差をList化の直接結果とみなす根拠はなく、Phase 4-Aとの因果は低い。Parallel OFFで警告が出なかったことは並列性を疑う材料だが、1/100対0/50では根本原因を確定できないため、修正・最適化・Fast Pathの追加は行わない。

次の調査で再現した場合だけ、`ParseLabelWithCatch` / `parseLabel`、`VariableParser.ReduceVariable`、`setJumpTo` に最小のThread ID・Label名・位置記録を一時追加し、警告を出した `FunctionLabelLine` と入力行の対応を確認する。恒久API・公開設定・警告抑制は不要である。

## Phase 4-A Final Adoption Review（2026-08-13）

### Code diff

`ExpressionParser.ReduceArguments` の一時collectionだけを `LinkedList<AExpression>` から `List<AExpression>` へ変更した。利用している操作は末尾追加、末尾参照、順序保持、戻り値のコレクション返却だけで、中間挿入・削除は使っていない。他の `ExpressionParser` の動作は変更していない。

`isDefine` の `terms.Count == 0` は、旧実装の `AddLast` 直後の `terms.Last == null` と同じく、追加直後には実質falseとなる。末尾の値がnullかどうかを判定するような仕様変更は行っていない。タームの順序、`isDefine` の代入・補完、終端処理、戻り値型は維持されるため、Semantic equivalenceは **PASS** と判定する。

### Allocation result

過去の独立traceで `LinkedList<AExpression>` 22.3 MB、`LinkedListNode<AExpression>` 39.1 MBがAfterで消失し、大量口上の一回BenchmarkLogでも総allocationが約90〜110 MB前後低下した。別実行の型別traceでも `LinkedList<AExpression>` 28.6 MB、`LinkedListNode<AExpression>` 45.7 MBがAfterで出現しなかった。同一型の総量を型別差だけで断定しないが、不要なLinkedList node allocationが減ったことは再現済みであり、Allocation reductionは **CONFIRMED** とする。

### Timing result and order effect

5回測定では大量口上ScriptParseが 1,763→1,656 msと見えたが、20組の交互検証では大量口上ScriptParseのpaired medianが +57 msとなり再現しなかった。通常と大量口上の差は分布が大きく、先に起動したbinaryより後に起動したbinaryが遅くなる強いorder effectも確認された。したがってStartup / ScriptParse speedは **UNPROVEN** とし、採用理由にしない。今後測定する場合はbalanced order、order strata、必要に応じてcool-downを用い、単純なpaired medianだけを根拠にしない。

### Compatibility result

Release Buildは0 errors / 既存warning 70件、Normal / Kojo起動は各1/1 OK、Lv2=0だった。専用parser unit test projectは存在しないため、既存起動試験とソース差分監査を用いた。単一引数、複数引数、空引数、数式・文字列・関数呼出し、IF等の既存解析経路は一時collectionのAPI外の論理を変更していない。互換性は **PASS** と判定する。

### Macro warning investigationとの関係

以前保留理由だった低頻度cross-name Lv2警告は、Macro辞書のhash-only lookupが本命原因と判断された。Macro Fix後のDiagnostic Kojoは100/100、Lv2=0、StartupFailure=0で、Final Compatibility VerificationもPASSだった。よってPhase 4-A固有の互換性破壊を示す証拠は現時点ないが、Phase 4-Aが全てのバグと無関係だとは断定しない。VariableLocal.localVarTokensのownership候補は別Issueであり、採否に混ぜない。

### Final adoption decision

**Final Recommendation: ADOPT**。速度改善ではなく、同じ用途で不要なLinkedList node allocationを避け、一時allocationを明確に削減できるため、低リスクの局所改善として正式採用を推奨する。次の最適化候補は既存profilingで確認された `setArgument` → `SetArgumentTo` → `INT_EXPRESSION CreateArgument` → `LexicalAnalyzer` / `ExpressionParser` の経路である。今回は実装しない。

Phase 3の「`PopArgumentPrimitive` により入力所有権が移るため、commaで `popTerms` へfallbackするだけのFast Pathは成立しない」という知見も維持する。実際に移るのは `InstructionLine` 所有の `CharStream` であり、WordCollectionではない。加えて、Fast Pathだけでは `LinkedListNode<Word>` の主要allocationは消えない。

## Parallel Warning Investigation Step 2A

`ReduceArguments` を通る関数定義引数は、最終的に `IdentifierDictionary.GetVariableToken` を経由してLOCAL/ARG tokenへ解決される。`localVarTokens` は変数種別ごとの `ConcurrentDictionary<string, LocalVariableToken>` で、通常は親ラベル名だけをkeyにする。`GetNewLocalVariableToken(key, func)` はkeyとサイズ算出元を別々に受け、`GetOrAdd` が先着tokenを残す。明示 `ARG@別名` / `LOCAL@別名` と、異なるLOCALSIZEを持つ同名非イベント定義ではkey/ownerが分離し得るため、コンテナの安全性だけでは正しいtoken所有を保証できない。

一方、前回の実測行は明示`@`を含まないため、`localVarTokens` 単独で別関数名がLv2見出しに現れる説明にはならない。詳細な呼出グラフ・安全性評価・次の診断項目は `07_ERB起動詳細Profiling.md` のStep 2A節に集約した。このPhaseではコード変更・診断実装・起動試験をしていない。

## Phase 5-A: INT_EXPRESSION / SetArgumentTo Investigation（2026-08-13）

### 前提と既存計測の再確認

本Phaseでは production code、profiling code、benchmark を変更・実行していない。既存の clean Release 大量口上traceと、`artifacts/phase2a-profile-kojo/script-parse.txt` を再確認した。

- ScriptParse詳細profiling: LogicalLine 3,909,550、`SetArgumentTo` 1,941,582 calls、Function 134,652。計測時の `setArgument` 累積時間は 24,618.268 ms（並列解析のためwall timeとは比較しない）。
- clean Release CPU sample（inclusive）: `setArgument` 14.51%、`SetArgumentTo` 13.70%、`INT_EXPRESSION_ArgumentBuilder.CreateArgument` 11.46%、`popTerms` 8.78%、`LexicalAnalyzer.Analyse` 6.67%、`ReduceArguments` 5.79%。
- clean Release allocation（sampled estimate）: `LinkedListNode<Word>` 370,741,304 bytes（7.95%）、CharStream 288,791,120 bytes（6.19%）、IdentifierWord 160,807,352 bytes（3.45%）、`AExpression[]` 106,834,936 bytes（2.29%）。このうち `INT_EXPRESSION.CreateArgument -> SetArgumentTo` call site の `LinkedListNode<Word>` は 207,908,344 bytes、同経路の List<AExpression> は 11,265,216 bytesだった。

`System.String` は全体最大（1,167,959,848 bytes、25.04%）だが、Preload、ファイル読み込み、lexer、macro、診断などが混在する。既存traceだけでは INT_EXPRESSION 固有の削減余地を十分に切り分けられないため、本Phaseの第一候補にはしない。

### 実際のhot pathと所有権

```text
ErbLoader.setArgument(FunctionLabelLine)
  -> ArgumentParser.SetArgumentTo(InstructionLine)
    -> line.Function.ArgBuilder.CreateArgument(line, EMediator)
      -> INT_EXPRESSION_ArgumentBuilder.CreateArgument
        -> ArgumentBuilder.popTerms(line)
          -> InstructionLine.PopArgumentPrimitive()
          -> LexicalAnalyzer.Analyse(CharStream, EoL, None)
          -> ExpressionParser.ReduceArguments(WordCollection, EoL, false)
        -> checkArgumentType / AExpression.Restructure
        -> ExpressionArgument (又は REPEAT の SpForNextArgment)
    -> InstructionLine.Argument = result
```

`SetArgumentTo` は既存Argument、既存エラー、非Debug時のDebug命令を除外し、`CodeEE` を Lv2 warning・`IsError`・`ErrMes` に変換する。従って正常系だけを短縮する候補でも、この例外・warning変換の外側は維持する必要がある。

`PopArgumentPrimitive()` が消費するのは **WordCollectionではなく `InstructionLine` が所有する `CharStream`** である。戻り値を取得した直後に `argprimitive` は null へ置換される。CharStreamは pointer と sourceを持つ可変classで、Replace / AppendString等も持つ。このため「先にpopして解析し、条件外なら通常の `popTerms` をもう一度呼ぶ」案は、再解析入力を持たず成立しない。旧資料のWordCollection消費という表現はこの意味に訂正する。

`INT_EXPRESSION` は単一引数用途でも、空引数のwarningと0への補完、top-level commaの余剰引数warningと先頭式採用、全termの型検査・`Restructure`、REPEATの定数化を行う。これら、ならびに構文エラー位置・例外種別・回復を変える候補は採用しない。

### WordCollection / lexer / tokenの評価

WordCollectionは `LinkedList<Word>` と現在nodeのPointerを保持する。解析中に前後へ進むだけでなく、macro展開が `Remove`、`Insert`、`InsertRange` を使い、function-like macroも挿入後にPointerを進める。したがってWordCollectionの全体List/Queue置換は単純なcontainer置換ではなく、cursor・splice・macro展開の再設計になる。

Lexerは各tokenについてWordとLinkedList nodeを作る。数値は `LiteralIntegerWord(ReadInt64(...))`、識別子は `ReadSingleIdentifier(...).ToString()` と `IdentifierWord` を作る。IdentifierWord自身のcodeはreadonlyだが、基底Wordの `IsMacro` は可変で、macro展開でWordCollection全体に設定され得る。グローバルWord/IdentifierWord cacheは所有権・macro状態・case/macro設定を壊すおそれがあり候補外とする。

CharStreamは入力行から既に作られた可変参照型である。pooling/struct化は、並列LabelSetup、例外、nested parse、返却漏れ、copy semanticsまで設計対象を広げる。本経路の局所最適化としては後回しとする。

`AExpression[]` は全体で106.8 MB残るが、INT_EXPRESSIONの正常結果は `ExpressionArgument`（単一AExpression）であり、`ExpressionArrayArgument`のList-to-array copyは主に別の複数引数builderで発生する。INT_EXPRESSION call siteにも AExpression[] は残るが、VariableParser / TermStack等の式木内部配列が主であり、constructor copyだけをこの約194万callの改善として扱う根拠はない。

### Phase 5-A candidate comparison

| Candidate | Target / concept | Allocation benefit | CPU benefit | Risk / complexity | 判定 |
| --- | --- | --- | --- | --- | --- |
| 1 | `INT_EXPRESSION_ArgumentBuilder.CreateArgument` に、**非破壊事前判定済みの純粋な短い10進整数literalだけ**を処理する局所経路を追加する。条件外は一度もpopせず現行 `popTerms`。 | Hit時にWordCollection、LinkedList<Word>/node、LiteralIntegerWord、ReduceArgumentsのList/TermStack等を回避。hit率は未計測。 | Hit時にlexer・generic expression parserを回避。約194万callsの一部に効く。 | 中 / 局所。strict predicate、既存ReadInt64、既存Restructure・warning・REPEAT処理の同一化が必須。 | **推奨（Phase 5-Bで実装候補）** |
| 2 | WordCollection内部のLinkedList/nodeをcursor付き連続storageへ再設計する。 | 非常に大。Word node全体370.7 MB、INT_EXPRESSION call site 207.9 MB。 | 高い可能性。 | 高 / 大規模。macroの中間挿入・削除・Pointer意味を全て再検証する必要。 | 見送り |
| 3 | CharStream pool / struct化、またはIdentifierWord global cache。 | CharStream 288.8 MB、IdentifierWord 160.8 MBだがINT経路限定量は未分離。 | 低〜中。 | 高 / 横断的。可変状態、nested parse、例外返却、Word.IsMacro共有が障害。 | 見送り |

### 推奨候補の設計（実装しない）

推奨は Candidate 1 のみである。対象は `ArgumentBuilder.cs` の `INT_EXPRESSION_ArgumentBuilder.CreateArgument` と、必要な場合だけ同ファイルまたは `InstructionLine` の**読み取り専用**helperである。`SetArgumentTo`、LexicalAnalyzer、ExpressionParser、WordCollectionは変更しない。

1. 現在のCharStreamを変更せず、開始positionから「lexerが空白として扱う文字 + ASCII decimal digitsのみ」「空でない」「上限内でReadInt64が例外を出さないことを保証できる短さ」を確認する。符号、0x/0b、指数、全角数字、macro、identifier、演算子、comma、semicolon、文字列、括弧、長すぎる値はすべて非該当とする。
2. 非該当なら現行の `popTerms -> Analyse -> ReduceArguments` をそのまま一度だけ実行する。ここに再解析・clone・cache・poolは入れない。
3. 該当時だけ `PopArgumentPrimitive()` を一度呼び、既存 `LexicalAnalyzer.ReadInt64` で値を読む。`SingleLongTerm` と `ExpressionArgument` の作成、`Restructure`、定数設定、REPEAT固有処理は現行と同じ順序・意味で行う。

これなら旧来の「pop後にcomma検出してfallback」案とは異なり、条件外では所有権を移さない。strictな成功集合を意図的に小さくし、数値表記・warning・error recoveryの差が出る入力はgeneric pathへ残す。rollbackはhelperと分岐を戻すだけで、保存形式・公開API・並列化に触れない。

期待効果はhit率に比例するため、現時点で総allocation/起動時間の数値を約束しない。成功hit 1件あたりは少なくともtoken容器・token node・数値Word・汎用term listを避けられる。一方で `ExpressionArgument` と最終 `SingleLongTerm` は意味維持のため残す。CPUはlexerと汎用式構文解析を回避できるhitに限り低〜中程度を期待する。

### Phase 5-B verification design（実行しない）

1. 実装前に一時的なローカル計測で、strict predicateのhit/missを命令種別別に数える。計測は検証branch限定で、採否後は撤去する。hit率が低ければ本候補を採用しない。
2. parser回帰は、純粋literal（`0`、`1`、空白付き、leading zero、最大安全桁）で現行と、新経路のArgument型・ConstInt・評価値・warningなしを比較する。非該当入力（空、comma/末尾comma、符号、hex/bin/exponent、overflow、変数、macro、関数、配列添字、括弧、演算子、文字列、コメント、不正構文）は必ずgeneric pathとなり、warning Lv・位置・例外種別・回復を現行と比較する。IF / ELSEIF / SIF / WHILE / REPEATを含める。
3. Release build後、Normal / Kojoを各最低5run、Before/AfterをAB/BAで均等にしたblockで実行する。中央値だけでなくorder strataとblock差を報告し、Phase 4-Aで確認済みの後続binary遅延を相殺する。StartupTest成功とLv2=0を必須とする。
4. clean Release allocation traceはBefore/After各最低1本。total allocation、Gen0/1/2、`LinkedListNode<Word>`、LinkedList<Word>、WordCollection、LiteralIntegerWord、List<AExpression>、TermStack、ExpressionArgumentを対象call siteと型別の両方で比較する。String総量は混在するため補助指標に留める。

### Phase 4-A / warningの確定状態

Phase 4-A（`ReduceArguments` の `LinkedList<AExpression>` -> `List<AExpression>`）は **ADOPT**、allocation **CONFIRMED**、speed **UNPROVEN**、compatibility **PASS** のままとする。以前のcross-name Lv2 warningはMacro hash collisionを別原因として修正後100/100再発なしであり、Phase 4-Aまたは本Phaseの候補へ帰属させない。`VariableLocal.localVarTokens` も別Issueであり、本調査の対象外である。

## Phase 5-B1: Strict Decimal Literal Eligibility Measurement（2026-08-13）

### 計測範囲とcompile guard

Phase 5-Aで選定したCandidate 1の適用率だけを、既存の `ErbStartupProfiler` の `PERFORMANCE_METRICS` / `counters` モードへ追加した。`ArgumentBuilder` と `InstructionLine` のhookは全て `#if PERFORMANCE_METRICS` 内で、通常Releaseではコンパイルされない。`INT_EXPRESSION_ArgumentBuilder.CreateArgument` 入口でだけTotalを加算し、計測後は必ず既存の `popTerms` へ進む。Pop、lexer、WordCollection、parser、戻り値の挙動は変更していない。

分母は `SetArgumentTo`（Normal 384,555、Kojo 1,941,582）ではない。別のArgumentBuilderも含むため、今回の分母は `INT_EXPRESSION.CreateArgument` 実呼出数だけである。

### Strict Decimal定義

`InstructionLine` が保持する未消費 `CharStream` を非破壊で参照し、現在positionから末尾までの前後にある半角空白またはタブだけを除く。残りが1文字以上のASCII `0`〜`9`だけで、正の`Int64`範囲（`long.MaxValue`以下）に収まる場合をEligibleとした。

- Eligible: `0`、`1`、`10`、`999999`、`01`（leading zeroはLexerの10進値と一致するため含む）。
- Not eligible: 空、`-1`、`+1`、`1+2`、`1 * 2`、`(1)`、`0x10`、`0b10`、指数表記、`1.0`、`1_000`、変数、関数、配列添字、comma、文字列、コメント、macro名、全角数字、その他ASCII以外の数字。
- Overflow文字列はdigits-onlyでもEligibleにしない。Lexerの`ReadInt64`が範囲外例外を出すため、将来のFast Pathでもgeneric error pathを維持する必要がある。
- `Config.SystemAllowFullSpace`によりLexerが全角空白を受け入れる場合でも、今回のstrict測定では保守的にEligibleへ含めない。
- Macro展開後に整数になる入力ではなく、最初からraw引数全体が数字だけの場合だけを数える。

### 実測結果（各fixture 1回、metrics build、Startup timeは評価対象外）

| Fixture | INT_EXPRESSION Total | StrictDecimal Eligible | Miss | HitRate |
| --- | ---: | ---: | ---: | ---: |
| Normal (`eramegaten_p`) | 108,320 | 518 | 107,802 | 0.48% |
| Kojo (`eramegaten_p_口上有り`) | 868,275 | 2,827 | 865,448 | 0.33% |

絶対Eligible数は合計3,345件。全Eligibleは1桁literalだった。FunctionCode別では、Normalが `IF` 466、`LOOP` 36、`ELSEIF` 12、`SIF` 4、Kojoが `IF` 2,775、`LOOP` 36、`ELSEIF` 12、`SIF` 4だった。`LOOP`は32.43%だが111 calls中36件に過ぎず、主な絶対数はIFである。

### Candidate 1の最終評価

判定基準（50%以上 VERY PROMISING、25〜50% PROMISING、10〜25% MARGINAL、10%未満 LOW VALUE）では、Normal 0.48%、Kojo 0.33%とも **LOW VALUE** である。Eligible 3,345件は実測 `INT_EXPRESSION.CreateArgument` 976,595 calls に対して0.34%に過ぎず、現行traceで大きいWord node等を1件ごとに避けられる可能性はあるものの、今回の測定だけで総allocation・startup効果を期待できる規模とは扱わない。

結論は **HOLD**。Candidate 1をPhase 5-B2で直ちに実装せず、Candidate 2/3の実装にも進まない。将来、別のfixtureまたは実際の入力分布が変わりEligible絶対数が増えた場合だけ再評価する。今回のstrict判定は非破壊であり、失敗後fallbackも不要なため、設計自体は互換性上安全な境界として保存する。

### Phase 5-B1実行結果

通常Release buildと`EnablePerformanceMetrics=true` buildはともに0 errors（既存warningのみ）。metrics版Normal/Kojoは各1回、process exit code 0、StartupTest完了、Parser exceptionなし、Lv2 warningなしだった。Startup/ScriptParse時間は計測hookのoverheadを含むため、Phase 5-B1の性能結論には用いない。Phase 4-AのADOPT / allocation CONFIRMED / speed UNPROVEN / compatibility PASS、Macro warning別原因、VariableLocal別Issueの扱いは変更しない。

## Phase 5-C1: WordCollection Mutation / Usage Measurement（2026-08-13）

### 計測定義

`WordCollection` の生成をconstructorで数え、末尾の通常 `Add`、Pointer進行、Current参照、Enumerator的な走査はmutationに含めなかった。LinkedList固有のsplice相当として `Insert`、`InsertRange`、`Remove` と、`LexicalAnalyzer` のfunction-like macroが直接行う `Collection.Remove(Pointer.Previous)` を数えた。同一instanceで何度起きても、Mutatedは最初の1回だけ加算し、operation countは毎回加算した。

token bucketはLexerが返すcollectionに対して、`0 / 1 / 2 / 3-4 / 5-8 / 9-16 / 17-32 / 33+` を記録した。constructorで生成された全collectionが必ずLexer由来とは限らない（macro statementのClone等）ため、Createdとtoken bucket合計は同一分母ではない。これは意図的に追加registry/HashSetを避けた結果である。

### 実測結果

| Fixture | Created | Mutated | NeverMutated | MutationRate | MutationFreeRate |
| --- | ---: | ---: | ---: | ---: | ---: |
| Normal | 661,862 | 1,308 | 660,554 | 0.20% | 99.80% |
| Kojo | 2,252,261 | 1,308 | 2,250,953 | 0.06% | 99.94% |

operation countはNormal/Kojoとも `Insert 0 / InsertRange 1,567 / Remove 1,567` だった。runtime counterはinstanceがspliceされた事実だけを数え、原因をMacroとして直接分類してはいない。一方、`InsertRange` / `Remove` と直接 `Collection.Remove(Pointer.Previous)` の実call siteを調べると、いずれもMacro定義またはMacro展開に集中している。従って「source call site上はMacro系処理に集中」と記録し、runtime値から非Macro mutationが0と断定しない。

Lexer token bucketはNormal `0:12,621 / 1:169,165 / 2:15,158 / 3-4:205,023 / 5-8:162,582 / 9-16:83,923 / 17-32:11,579 / 33+:1,644`、Kojo `0:101,893 / 1:288,629 / 2:15,438 / 3-4:603,477 / 5-8:805,407 / 9-16:279,134 / 17-32:95,359 / 33+:62,757` だった。mutated collectionの初期token bucketは両fixtureで `0:0 / 1:7 / 2:0 / 3-4:52 / 5-8:899 / 9-16:231 / 17-32:106 / 33+:13` で、5-8 tokenが中心だった。

### Pointer / Node identity

`Pointer` は `LinkedListNode<Word>` のpublic fieldで、`WordCollection`内部だけのopaque indexではない。`LexicalAnalyzer` は `macroStart` / `macroEnd` nodeを保存し、`macroEnd == wc.Pointer` でidentity比較し、`Pointer.Next` / `Pointer.Previous`を使い、直接 `Collection.Remove(node)` も行う。`WordCollection.Remove`も削除後にnext nodeをPointerへ保持する。したがってNode identity dependencyは **Yes（少なくともparser内部semantic ownership）**、単純なindex List置換は不可である。

### 最終判定

Mutation-free rateはNormal 99.80%、Kojo 99.94%で目安の95%を超えるため、使用実態だけなら **VERY PROMISING**。ただしこれは直ちにLinkedListをListへ置換できるという意味ではない。mutationは少数でもmacroのnode identity・splice・削除後Pointerを必要とし、`LinkedListNode<Word>`全体allocationの大部分を理論削減できるかは、representation設計なしに断定できない。

Recommendationは **INVESTIGATE DESIGN**。次Phase候補は通常lexer出力をcontiguous storageで構築し、最初のmacro splice時だけnode-compatible mutable representationへpromotionする設計調査である。promotion時のPointer identity、iterator invalidation、nested macro、error position、parallel parser、例外時のownershipを設計・比較する。今回も実装しない。

## Phase 5-C2: WordCollection Representation Design（2026-08-13）

### 現在のモデルと制約

`WordCollection` は `LinkedList<Word> Collection`、`LinkedListNode<Word> Pointer`、先頭/終端の曖昧さを補助する `index` を持つ。`LexicalAnalyzer.Analyse` はtokenを順に `Add` した後、呼出元へ返す前に `expandMacro` を実行する。通常の構文解析は `Current` / `ShiftNext` / `EOL` による前方走査であり、Nodeを外部へ保存する経路は確認できなかった。

Node identityを実際に使うのは関数型Macro展開の `LexicalAnalyzer.expandFunctionlikeMacro` である。ここでは `macroStart` / `macroEnd` を `Pointer` から保存し、node比較、`Next` / `Previous`、`Collection.Remove(node)`、Pointer復元を行う。`PointerReset` の外部呼出は `DefineMacro` と `ErhLoader` にあるが、node自体への外部直接アクセスはこのMacro処理に限定される。従って常時indexだけへ置換する設計は安全ではない。

### 候補比較と推奨

推奨は **Candidate A: `List<Word>` compact modeから、最初のspliceまたはlegacy `Pointer` 参照時だけ既存LinkedList modeへ片方向promotionするhybrid** である。通常Lexerは標準 `List<Word>` とindex cursorで構築・走査し、`Insert` / `InsertRange` / `Remove`、または既存public `Pointer` のget/setが必要になった時だけ、同順序でLinkedList/nodeを作り、現在indexに対応するnodeへcursorを合わせる。promotion後は現行のLinkedList semanticsをそのまま使い、compactへ戻さない。

`Pointer` fieldは同じ型の互換propertyに置き換える案とし、get/set時にpromotionを保証する。これにより関数型Macroの既存 `macroStart` / `macroEnd` / `Next` / `Previous` コードはnodeを要求する前に必ずLinked modeへ入る。外部call siteの事前encapsulationは **不要** と判断する。通常pathはPointer propertyに触れないためnodeを作らない。`List<Word>` はtoken数が事前不明なLexerに対する標準のgrowable contiguous storageであり、Word[]の二重走査/custom growth、ArraySegmentのgrow不能、Queueのprevious/splice不適合を避けられる。

Candidate B（常時contiguous＋index splice）は、Macroが保存するcursorの妥当性・挿入削除時のindex調整・error pathを全て再実装する必要があり **VERY HIGH** riskで不採用。Candidate C（Lexer/Macro容器をinterfaceで分離）は、同じnode/cursor問題を残したままdispatchと抽象層を増やすため **HIGH** riskで不採用とする。

### 効果・リスク・次Phase

Normal 99.80%、Kojo 99.94%というmutation-free率は、通常pathがnode allocationを避けられる有力な根拠である。ただし、削減bytesはtraceの370.7MBへ単純に率を掛けてはならない。Word本体とListのbacking arrayは残り、Macroに到達したcollectionはpromotion時にnodeを割り当てる。連続走査によるcache locality改善は期待できるが、CPU改善は未証明であり、主目的はallocation削減である。

representationの状態はinstance-localな `Compact -> Linked` 一方向遷移とし、共有mutable stateを置かない。並列LabelSetup間で状態を共有しないためthread safety上の新規共有点は作らない。主リスクはparser coreのcursor境界、Macro再入/再帰、例外時の位置・warning互換性であり、互換性リスクは **HIGH**、実装複雑度は **MEDIUM** とする。rollbackは `WordCollection` の現行LinkedList実装へ差分を戻すだけであり局所的である。

次Phaseは実装前の広いPointer APIリファクタではなく、`WordCollection` 内部だけの最小prototypeとする。既存のpublic API名・`Pointer` 型を維持し、getter/setterをpromotion fenceにする。検証はNormal/KojoのMacro、DEFINE、関数型/ネスト/再帰Macro、式・関数引数、invalid Macro、syntax errorの互換性と、before/afterの`LinkedListNode<Word>`・total allocationを確認する。実装・build・benchmarkは本Phaseでは行っていない。

## Phase 5-C3: WordCollection Compact Representation Prototype（2026-08-13）

### 実装範囲

`WordCollection` を二状態の最小hybridにした。初期状態は標準 `List<Word>` と `compactPointer` によるCompact modeであり、`Add` / `Current` / `EOL` / `ShiftNext` / `PointerReset` / cloneコピーはこの状態のまま動作する。`Insert`、`InsertRange`、`Remove`、`Clear`、または既存のpublic `Collection` / `Pointer` 取得・設定では `EnsureLinked()` が一度だけ走り、Wordを同順序で既存 `LinkedList<Word>` へ移して同じordinalのnode（EOLならnull）をPointerに設定する。Linked modeになったinstanceはCompactへ戻さない。

public `Pointer` は同じ `LinkedListNode<Word>` 型のpropertyに置換し、get/setの前にpromotionするcompatibility bridgeとした。そのため関数型Macroの既存 `macroStart` / `macroEnd`、`Pointer.Next` / `Previous`、直接 `Collection.Remove(node)` は書き換えていない。`Collection` もLinkedListを本当に要求する時だけpromotionするpropertyである。不要promotionを避けるため `WordCollection.Count` を追加し、`DefineMacro` の2箇所と `LexicalAnalyzer` のMacro引数Count 2箇所だけを `Collection.Count` から置換した。Macro algorithm、Lexer algorithm、pool、custom buffer、capacity tuning、共有mutable stateは追加していない。

### PromotionとPointer semantics

Compact cursorは0始まりの現在token index、`Count`以上をEOLとする。`ShiftNext` は従来の`index`補助状態を維持し、初期null/EOL、末尾読了後、EOL後の再Shift、EOL状態でのAdd、PointerResetの既存分岐を保つ。promotion時はcompact cursorが範囲内なら同ordinalのnode、範囲外ならnullを設定する。従ってpromotion前に外部へnodeを返さず、node identityが必要になる最初のpropertyアクセス後は既存のLinkedList挙動だけを使う。

各collectionのstate遷移はinstance-localな `Compact -> Linked` のみであり、static/shared stateは追加していない。並列LabelSetup間に新しい共有状態はない。

### Build・起動・metrics結果

通常Release Buildと`EnablePerformanceMetrics=true` Buildは各1回、0 errors・既存70 warningsで成功した。専用test infrastructureは存在しないため、focused correctnessはcontainerの境界レビューと既存StartupTestで確認した。通常Release StartupTestはNormal 1/1 OK（Init 2,914ms、ERB 1,467ms、Lv2=0）、Kojo 1/1 OK（Init 4,979ms、ERB 3,257ms、Lv2=0）だった。metrics版もNormal/Kojo各1回、exit code 0、StartupComplete、Parser exceptionなし、Lv2=0だった。計測buildの時間はhookを含むため速度比較には使わない。

metrics countersはNormal `CompactCreated 661,862 / Promoted 1,308 / NeverPromoted 660,554 / 0.20%`、Kojo `2,252,261 / 1,308 / 2,250,953 / 0.06%` だった。promotion数はC1のsplice-mutated instance数と一致し、通常pathがCompactのまま残ることを確認した。

### Allocation結果（Kojo clean Release各1回）

既存Phase 3 clean ReleaseのBefore（`--BenchmarkLog` 1回、6,894,296,208 bytes、Gen0/1/2=33/16/12、EventPipe sampled `LinkedListNode<Word>` 370,741,304 bytes）を基準に、prototype後を同じKojo fixture、通常Release、StartupTest、EventPipe allocation/type-name providerで1回採取した。Afterのtotal allocatedは6,485,157,152 bytes、Gen0/1/2=35/18/12で、差は **-409,139,056 bytes (-5.93%)** だった。この値はPhase 3 baselineとの比較でPhase 4-A等の差が混入する可能性がある**旧参考値**であり、正式C3単独効果には使わない。一回値のGC差は判定材料にしない。

After traceのsampled `LinkedListNode<Word>` は851,520 bytesで、Beforeから **-369,889,784 bytes (-99.77%)**。残存分はpromotion側であり、型別call siteでも通常 `LexicalAnalyzer.Analyse -> WordCollection.Add` のnode allocationは消えた。新規の `List<Word>` は71,591,968 bytes、同Listのbacking `Word[]` は378,148,456 bytesだった。Beforeにはこのstorageが存在しなかったが、増加を含めてもtotal allocationは低下している。EventPipe型別値はsampled estimate、total allocationはprocess counterであり、両者を単純加減算しない。

### Prototype判定

Compatibility / Normal / Kojo / Macro / Pointer semanticsは **PASS**。`LinkedListNode<Word>` reductionは **CONFIRMED**、total allocationは **REDUCED**、speedは **UNPROVEN**。変更はWordCollection、Count-onlyの既存4 call site、既存metrics profilerのpromotion counterだけで、production sourceは4 filesに収まった。結論は **PROCEED**（次は独立した採用レビュー/追加互換性確認）。本Phaseでは正式採用、commit、push、capacity tuning、pool、Macro rewriteは行わない。

## Phase 5-C3V: Compact Representation Prototype Verification（2026-08-13）

### 同一作業ツリー由来のC3-only baseline

既存のPhase 5-C3記載のBeforeはPhase 3時点の値で、Phase 4-A等の差を含む可能性がある。したがってC3単独効果の根拠には使わない。今回、現作業ツリーを .git・既存build artifactなしで外部一時コピーし、C3固有の4 production source（WordCollection.cs、DefineMacro.cs、LexicalAnalyzer.cs、ErbStartupProfiler.cs）だけをC3前へ戻した。全126 production C# sourceを比較し、C3対象外の差分は0件であった。以後のC3 allocation値はこのBefore/Afterを正とする。

### Differential / compatibility

一時harnessで空、1/multiple token、PointerReset、EOL/末尾追加、Count、promotion前後のfirst/middle/last/EOL、next/reset、Insert、InsertRange、Remove、Clone、enumerator promotion、macro相当spliceをBefore/Afterで比較し、19ケースは一致した。通常・口上あり各1回のStartupTestもBefore/Afterともexit 0、Lv2 warning 0で完走した。Macroは両fixtureの実データを通過したが、case sensitivityはC3差分が触れないため専用入力は追加しなかった。

ただし Add(WordCollection wc) の自己追加は実差分である。旧LinkedList実装は列挙中に同一collectionへ追加して InvalidOperationException、Compact modeの新実装はList.AddRange(this)相当でtokenを一度複製する。現在ソースの実call siteはClone内の ret.Add(this) のみであり、wc.Add(wc) は見つからなかった。実運用リスクは低いが、API上のsemantic differenceである。今回の指示に従い修正はせず、採用前に旧例外semanticsを保持する最小guardを検討する。

### C3-only allocation comparison

同一provider（Microsoft-Windows-DotNETRuntime:0x700001:5）のRelease StartupTestで、process total allocation（--BenchmarkLog）とGCAllocationTick sampled typeを各fixture 1回ずつ取得した。

| Fixture | Before total allocated | After total allocated | Delta | Before sampled | After sampled | LinkedListNode<Word> Before -> After |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Normal | 2,434,169,072 | 2,369,594,408 | -64,574,664 (-2.65%) | 2,458,499,368 | 2,390,483,024 | 144,142,120 -> 425,120 (-99.71%) |
| Kojo | 6,798,623,216 | 6,484,039,664 | -314,583,552 (-4.63%) | 6,821,839,432 | 6,504,721,160 | 698,366,800 -> 744,408 (-99.89%) |

Afterでは通常pathのnodeの代わりにWord[]（Normal 76,877,016 / Kojo 379,206,048 sampled bytes）とList<Word>（19,116,864 / 71,587,240）が現れる。これは意図したcompact storageであり、両者を含めてもprocess totalは低下した。EventPipe型別値はsampled estimateなので、process totalと加減算しない。

### Timing（補助指標）

Normal/KojoともBefore/Afterを交互順で各5回実行し、全20回がStartupTest OK/Lv2=0だった。NormalのERB平均はBefore 1231.2ms / After 1252.8ms、Kojoは3352.6ms / 3329.8msで、符号がfixture間で一貫しない。allocation改善は確認済みだが、CPU/起動時間改善は **UNPROVEN** とする。

### C3V結論

allocationは **CONFIRMED**、Pointer/macro実fixture互換性は **PASS**、ただし自己追加のsemantic differenceが未解決のため最終判定は **HOLD**。C3Vはdiagnosticや新たな最適化を追加せず終了する。次に進む場合は、自己追加を旧実装同様に例外とするか明示的に禁止するかを決めたうえで、同じC3-only baselineで再確認する。

## Phase 5-C3V.1: Self-Add Compatibility Fix & Final Verification（2026-08-13）

前回HOLDの原因だった `WordCollection.Add(WordCollection)` の自己追加だけを修正した。`ReferenceEquals(this, wc)` の場合に限り `EnsureLinked()` 後の旧LinkedList列挙・追加経路へ入り、通常の `a.Add(b)`（`a != b`）ではcompact同士のAddRange経路を維持した。Parser / Lexer / Macro algorithm、IdentifierDictionary、Phase 4-A、metricsの他処理は変更していない。

旧実装の観測結果は、空collectionではreturn・Count 0・内容不変・Pointer null、1 tokenでは最初の追加後に `InvalidOperationException`（message: `Collection was modified after the enumerator was instantiated.`）・Count 2・`T0|T0`・Pointer先頭、3 tokenでは最初の追加後に同じ例外・Count 4・`T0|T1|T2|T0`・Pointer先頭だった。修正後のcompact実装は3ケースすべて例外型、message、途中副作用、Count、内容、Pointerまで一致した。

通常Addの empty+empty / empty+non-empty / non-empty+empty / non-empty+non-empty と既存focused differentialもPASS。通常Release / PERFORMANCE_METRICS buildは0 errors（既存warningsのみ）。Normal / Kojo の通常Releaseおよびmetrics StartupTestは全てexit 0、Parser errorなし、Lv2 warning 0。metrics promotionはNormal `661,862 created / 1,308 promoted / 0.20%`、Kojo `2,252,261 / 1,308 / 0.06%` で前回C3Vと同じだった。

Kojo AFTERのsanity traceは total allocated `6,485,410,568` bytes、sampled `LinkedListNode<Word>` `957,864` bytesだった。前回C3 AFTER `6,484,039,664` bytesおよび `744,408` sampled bytesとの差は小さく、compact allocation効果に影響なしと判定した。正式C3比較値は引き続き Kojo **-314,583,552 bytes / -4.63%**、`LinkedListNode<Word>` **約99.89% reduction** を使用する。速度は引き続き **UNPROVEN**。

Self-add、focused differential、Pointer、Macro fixture、Release/Metrics build、Normal/Kojo Startup、allocation sanityの全条件をPASSとし、C3を **ADOPT** とする。採用理由は速度ではなく、通常99.8〜99.94%のcollectionでnode生成を避け、Macro等だけLazy Promotionし、Kojo総allocationを約314.6MB削減できること。self-add対応は通常pathにReferenceEquals branch一つだけで、通常のallocation経路を変更しない。

## Phase 5-D: Adopted Optimizations / Profiling Cleanup & Consolidation（2026-08-13）

### 計測分類

採用済みの本体変更は維持した。`ReduceArguments` の `List<AExpression>` 化、`WordCollection` の Compact→Linked lazy promotion（自己追加時の旧例外semanticsを含む）、Macro Hash Collision Fix、既存のWARN-01〜06は今回のcleanup対象外である。

汎用計測として `PerformanceMetrics`、`ErbStartupProfiler` のPrimaryParse/file/function timing、`SetArgumentTo` 呼出し数、FunctionCode別引数数、`Program.cs` の汎用 profiling entrypoint、`ErbLoader` の汎用stage/file/function hooksは残した。これらは特定Phaseの判定値を本体挙動へ持ち込まない。

Phase限定で役目を終えたstrict decimal eligibility（Phase 5-B1）とWordCollection mutation/token/promotion counters（Phase 5-C1/C3）は削除した。対象は `INT_EXPRESSION_ArgumentBuilder` のstrict hook、`LogicalLine` のstrict decimal helper、`ErbStartupProfiler` のstrict/C1/C3集計・出力、`WordCollection` と `LexicalAnalyzer` の対応hookである。Lexerのmacro処理とCompact representation本体は変更していない。

### 検証と結論

今回の差分は計測コードの整理だけで、production parser semantics・採用済み最適化・既存の汎用 profiling entrypointを変更しない。Release build、`PERFORMANCE_METRICS` build、Normal/Kojo StartupTest（各1回）をcleanup後に再確認し、結果をChatReview packageへ保存する。strict decimalの既往値（976,595件中3,345件、0.34%）とC1/C3の既往測定値は歴史資料として保持するが、今後の通常metrics出力には再掲しない。

### 更新履歴

Phase 5-Dでは新規最適化・診断・warning抑制を追加せず、採用済み最適化を保持したままPhase限定profiling instrumentationを削除し、汎用計測へ統合した。

## Phase 6-A: Current Optimized Baseline Fresh Profiling（2026-08-13）

現行のADOPT済み状態（Phase 4-A `ReduceArguments` の `List<AExpression>`、Phase 5-C3/C3V.1の`WordCollection` Compact→Linked lazy promotion・self-add互換、Macro Hash Collision Fix）を変更せず、clean ReleaseのNormal/Kojoを各1回だけ外部profilingした。PERFORMANCE_METRICS版はgeneric counters取得のため各1回実行した。以下は単発baseline値であり、起動速度の改善率や次の最適化候補を示すものではない。

### Startup baseline（clean Release）

| Fixture | Startup | ERB | Enumeration | PrimaryParse | LabelSetup | ScriptParse |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Normal | 2622 ms | 1336 ms | 119 ms | 424 ms | 178 ms | 609 ms |
| Kojo | 4275 ms | 2897 ms | 119 ms | 964 ms | 303 ms | 1506 ms |

### Generic counters（PERFORMANCE_METRICS各1回）

| Fixture | SetArgumentTo | Labels | InitiallyParsed | Remaining | ParallelRemaining |
| --- | ---: | ---: | ---: | ---: | ---: |
| Normal | 384,555 | 127,507 | 5,518 | 121,989 | 121,989 |
| Kojo | 1,941,582 | 134,652 | 5,538 | 129,114 | 129,114 |

### Fresh CPU / allocation profile

CPUは`Microsoft-DotNETCore-SampleProfiler`、allocationは`Microsoft-Windows-DotNETRuntime:0x700001:5`と既存`TraceAllocationAnalyzer`を使用した。Kojo inclusive CPU reportではThreadPool/TaskReplicator、`LexicalAnalyzer.Analyse`、`WordCollection.Add`、`ParseFunctionWithCatch`、`ArgumentParser.SetArgumentTo`、`setArgument`が上位へ現れた。allocation sampled bytesはNormal 2,390,545,984、Kojo 6,506,324,224だった。

Kojoのallocation Top25では`Word[]`が374,952,984 bytes（rank 5）、`List<Word>`が71,274,776 bytes（rank 23）であり、`LinkedListNode<Word>`と`LinkedListNode<AExpression>`はTop25へ戻っていない。Normalでも`Word[]`と`List<Word>`は確認されたが、両LinkedListNode型はTop25にない。これはC3/4-A採用後の現行baseline記録であり、原因分析・改善提案はPhase 6-Bへ分離する。

過去Phase 2-B/3との比較は、条件差を明記した順位観測だけを`ChatReview_Phase6A_FreshProfiling_20260813.zip`へ収録した。旧正式C3単独効果（Kojo -314,583,552 bytes / -4.63%、`LinkedListNode<Word>`約99.89%削減）は再検証せず、current baselineとの照合基準として保持する。

## Phase 6-B: Fresh Profile Analysis & Next Optimization Selection（2026-08-13）

Phase 6-Aのraw traceは再取得せず、Kojo CPU / allocation summary、generic metrics、Phase 5-C1の既存token count分布を分析した。runtime / schedulerのinclusive frameは候補選定から除外し、parser側は `ParseFunctionWithCatch -> setArgument -> SetArgumentTo -> ArgumentBuilder -> LexicalAnalyzer.Analyse -> WordCollection.Add -> List<T>.AddWithResize / Array.Copy` の観測経路として整理した。これらのinclusive値は親子stackで重複するため加算しない。

allocation sampled bytesでは、`Word[]`が374,952,984 bytes（Kojo rank 5）で、callsiteは `WordCollection.Add -> LexicalAnalyzer.Analyse` だった。`InstructionLine`は693,636,784 bytes（rank 2）、`CharStream`は414,722,296 bytes（rank 3）、`String`は1,349,753,648 bytes（rank 1）だが、後二者はPreload・reader・lexer・function evaluation等へ分散する。`InstructionLine`はmutable parser-core objectで、traceだけでは局所的な不要allocationを特定できない。

`WordCollection`のcompact storageはゼロcapacityの`List<Word>`から始まり、対象runtimeの`List<T>`は初回Addで4、以降4→8→16→32…と倍増する。Phase 5-C1のKojo分布では0 token 101,893、1 token 288,629、3-4 token 603,477、5-8 token 805,407、9-16 token 279,134、17-32 token 95,359、33+ token 62,757だった。0〜4 tokenは44.82%、5〜8 tokenは35.76%であり、5-8 tokenはdefault growthによる4→8の対象になり得るが、0〜4 tokenも大きな分母であるため、`new List<Word>(8)`のような固定capacityは現データだけでは選べない。`AddRange`はsource Count既知のため既に標準`List<T>.AddRange`のICollection経路を使う。

候補は最大3件に絞った。第一候補はWordCollection List growth、第二候補はInstructionLine、第三候補はCharStream/String/ReadStringである。局所性・rollback・thread safety・実測根拠を比較し、**次Phaseは実装ではなく `Phase 6-C1 — WordCollection List Growth / Capacity Measurement`** とする。`PERFORMANCE_METRICS`限定で、compact Listのactual resize（old/new capacity、Count、copied references）、final Count/Capacity、unused capacity、creation contextを集計し、固定capacityまたはcontext hintの正当性を確認する。production behavior、Phase 4-A、C3/self-add、Macro Fix、VariableLocalには変更を加えない。

## Phase 6-C1: WordCollection List Growth / Capacity Measurement（2026-08-13）

### 実装範囲

`PERFORMANCE_METRICS` ビルドだけで `WordCollection` compact `List<Word>` の `Add` / `AddRange` 前後のCount・Capacityを比較し、resize回数、遷移、コピー参照数をthread-local集計した。Lexer結果collectionと、全compact collection（macro引数・clone等を含む）を分離し、final Count/Capacity、unused slot、Count bucket、Capacity bucket、collection単位のresize回数も出力した。production buildでは#ifブロックごと除外される。`AddRange` は両fixtureとも0回だった。

### 実測結果（各fixture 1回、counters）

| Fixture / context | Add calls | Resize | Resize/Add | Copied refs | Final collections | Final capacity | Unused | Unused ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Normal / all | 3,035,063 | 1,020,192 | 33.614% | 2,099,876 | 661,695 | 4,695,596 | 1,661,303 | 35.380% |
| Normal / Lexer | 3,034,293 | 1,019,881 | 33.612% | 2,099,300 | 661,695 | 4,695,596 | 1,661,303 | 35.380% |
| Kojo / all | 14,358,772 | 4,050,467 | 28.209% | 13,033,560 | 2,252,094 | 21,633,788 | 7,275,786 | 33.632% |
| Kojo / Lexer | 14,358,002 | 4,050,156 | 28.208% | 13,032,984 | 2,252,094 | 21,633,788 | 7,275,786 | 33.632% |

Kojo Lexerのresize遷移は `0→4: 2,150,201`、`4→8: 1,241,760`、`8→16: 437,061`、`16→32: 158,037`、`32→64: 62,751`、`64→128: 299`、`128+: 47`。コピー参照の中心は `4→8: 4,967,040` と `8→16: 3,496,488` である。final Count bucketは `0:101,893 / 1:289,133 / 2:15,456 / 3:552,597 / 4:51,255 / 5-8:804,699 / 9-16:279,024 / 17-32:95,286 / 33-64:62,452 / 65+:299`。Normalも同じ形式で `5-8:161,874`、`9-16:83,813` が中心だった。

### capacityシミュレーションと判定

固定初期capacity 4 は `0→4` のresizeイベントを消せるが、現行でもこの遷移のコピー参照は0であり、空collectionまで先行確保する実装なら追加slotを生む。固定capacity 8 は `0→4` と `4→8` を避け、Kojo Lexerで最大 `1,241,760` resize・`4,967,040` copied referencesを対象にできる一方、Count 1-4 collectionへ4 slotずつ追加する。Kojoでは該当collectionが908,441件、空を含めないlazy適用でも約3,633,764 Word参照（約29MB相当）を増やす概算である。一方、Count 5以上ではcapacity-4 array自体が不要になり、`4→8` の1,241,760回に対応する4,967,040 slotsを一時allocationから除ける。従ってslotだけの差は `+3,633,764 - 4,967,040 = -1,333,276` と推定できるが、array header/alignmentを含まないsimulationであり、現行unused 7,275,786 slotとのtradeoffも残る。これは実装・実測ではなく分布からの反実仮想である。

結論は **PROMISING**（C1では未採用）。resize/copyは十分大きいが、固定capacity 4/8を全contextへ適用する根拠はなく、特に空・1-4 tokenの多数collectionでメモリを増やす。次に進めるなら、`Lexer`結果だけを対象に初期capacity 8を比較する小さなprototypeを1件だけ作り、Normal/Kojoでtotal allocation・GC・互換性を再測定する。`AddRange`最適化、capacity変更の本体採用、他allocation候補、更新履歴変更は今回行っていない。

## Phase 6-C2: Lexer-only Lazy Capacity-8 Prototype（2026-08-13）

### Prototype設計

`WordCollection` に `firstAddCapacityHint` を追加し、constructorではListをcapacity 0のまま生成する。`LexicalAnalyzer.Analyse` のLexer resultだけを `new WordCollection(8)` で作り、最初の `Add(Word)` 直前に一度だけcapacity 8を確保する。0 token resultはcapacity 0のまま、1〜8 tokenはcapacity 8、9〜16 tokenはcapacity 16となる。通常 `new WordCollection()`、Macro argument、Clone、`Add(WordCollection)`、EnsureLinked、self-addは従来経路を維持した。

### Focused / compatibility

Reflection-based focused matrixで、empty、1/4/5/8/9/17 token、hintless collection、EnsureLinked、Clone hint非継承、self-add例外semanticsをBefore/After双方PASSとした。Afterのcapacityは `0, 8, 8, 8, 8, 16, 32`、Beforeのdefaultは `0, 4, 4, 8, 8, 16, 32` だった。After ReleaseのNormal/Kojo StartupTestはexit 0、Parser exceptionなし、Lv2 warning 0だった。Macro Fix・C3 semanticsは変更していない。

### Growth counters（Kojo Metrics各1回）

| Context | Compact Add | Resize | 4→8 | Copied refs | Final capacity | Unused | Unused ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Before / Lexer | 14,358,002 | 4,050,156 | 1,241,760 | 13,032,984 | 21,633,788 | 7,275,786 | 33.632% |
| After / Lexer | 14,358,002 | 658,195 | 0 | 8,065,944 | 25,267,552 | 10,909,550 | 43.176% |

Afterでは4→8 copied refsが4,967,040減り、0→4・4→8の中間growthがLexer resultで消えた。8→16以降は同じである。Final capacity/unusedはCount 1〜4の追加4 slotsを反映して増えたが、C1 simulationの一時capacity-4削減と合わせたnet allocationはtraceで判定する。

### Kojo allocation trace（clean Release各1回、同一worktree baseline）

| Type / metric | Before | After | Delta |
| --- | ---: | ---: | ---: |
| Total allocated bytes（process counter） | 6,490,003,960 | 6,472,377,568 | -17,626,392 (-0.27%) |
| `Word[]` sampled | 360,207,992 | 332,113,672 | -28,094,320 (-7.80%) |
| `List<Word>` sampled | 70,577,856 | 71,985,272 | +1,407,416 (+1.99%) |
| `WordCollection` sampled | 109,941,032 | 124,533,312 | +14,592,280 (+13.27%) |
| `LinkedListNode<Word>` sampled | 852,288 | 745,176 | -107,112 (-12.57%) |
| `System.Object[]` sampled | 240,896,256 | 239,186,096 | -1,710,160 (-0.71%) |
| Gen0 / Gen1 / Gen2 | 31 / 15 / 11 | 32 / 17 / 10 | process counters |

EventPipe型別値はsampled estimate、total allocatedはprocess counterであり、両者を加減算しない。`Word[]`は明確に減少し、total allocationも単発同条件で減少した。CPU traceや複数runは実施せず、速度は **UNPROVEN** とする。

### C2.1 再評価: field実装はHOLD

capacity 8戦略そのものは、4→8 resize 1,241,760回・copied refs 4,967,040の削減と`Word[]` sampled -28,094,320 bytesを示しており**PROMISING**である。一方、`firstAddCapacityHint` の`int`追加は`WordCollection`のRelease object sizeを48から56 bytesへ増やし、sampled `WordCollection` allocationを+14,592,280 bytes (+13.27%)にした。このinstance-size増加は全`WordCollection`に及ぶため、current field-based prototypeは**HOLD（未採用）**とする。

次段階では、通常`WordCollection`のfield layoutを変えず、Lexer resultだけが「compact List未生成」をpending状態として表すzero-overhead設計を検証する。候補は`compactCollection == null && linkedCollection == null`をLexerPendingとするfactory/専用private constructorであり、最初の`Add(Word)`だけが`new List<Word>(8)`を生成する案である。実装前にnullable stateの全caller・macro/promotion/metrics・hot Add branchを最小focused testで確認する。Capacity 16、adaptive/threshold、custom buffer、ArrayPool、InstructionLine/CharStream最適化は対象外とする。

## Phase 6-C2.2: Zero-overhead Lazy Capacity-8 Prototype & Verification（2026-08-13）

### 実装範囲

`WordCollection` の `firstAddCapacityHint` fieldを削除し、通常constructorは`new List<Word>()`を維持した。Lexer resultだけは`WordCollection.CreateLexerResult()`で`compactCollection == null && linkedCollection == null`のLexerPending状態から開始し、最初の`Add(Word)`で`new List<Word>(8)`を生成する。`Count`、`Current`、`EOL`、`ShiftNext`、`PointerReset`、`EnsureLinked`、`Add(WordCollection)`、`InsertRange`、`SetIsMacro`、metrics finalをpending-safeにした。変更production sourceは`WordCollection.cs`と`LexicalAnalyzer.cs`の2ファイルだけである。

### Formal baseline / focused compatibility

Formal BEFOREはC2 capacity optimizationを除いた既存採用状態（Phase 4-A、WordCollection C3/self-add、Macro Fix、WARN対策、C1 metrics）とし、既存`_phase6c2_before` snapshotを使った。Focused matrixはFormal BEFOREとCandidate AFTERの双方でPASS。CandidateではpendingのCount/Current/EOL/ShiftNext/PointerReset/Pointer/Collection、0/1/4/5/8/9/17 token、normal collection、Add(WordCollection)、Clone、self-add、promotion後Insert/Removeを確認した。Normal/KojoのCandidate Release StartupTestは各1回、exit 0、Parser exceptionなし、Lv2 warning 0だった。

## Phase 6-E: InstructionLine Argument / argprimitive One-Reference Production Adoption（2026-08-14）

`InstructionLine` の `Argument` と `CharStream argprimitive` を追加fieldなしの `object argumentStorage` へ統合した。getter/setterと `PopArgumentPrimitive` の既存null・clear・CharStream保持 semanticsを維持し、変更対象は `LogicalLine.cs` のみである。Prototypeのfocused semantic、DEBUG null、PopArgumentPrimitive、targeted runtime smoke、Normal/Kojo startupを再確認済みとし、Release／PERFORMANCE_METRICS buildはいずれも0 errors（既存warning 70件）だった。

既往の測定では `InstructionLine` object size 176→168 bytes、allocationはNormal約-9.8 MB、Kojo約-28.8 MB。既存macroおよびPRINT/FORM、IF/ELSE、CALL、CALLFORM、FOR、REPEATのruntime smokeで挙動一致、Parser error/Lv2/Exceptionは0。速度効果は単発値から断定しないため、判定は**ADOPT**とする。次の候補は `LoopEnd` / `LoopCounter` / `LoopStep` のfeasibility/design分析だけであり、今回それらは変更しない。

### Metrics growth（Kojo各1回）

| Context | Formal BEFORE | Candidate AFTER | Delta |
| --- | ---: | ---: | ---: |
| Lexer CompactAddCalls | 14,358,002 | 14,358,002 | 0 |
| Lexer ResizeCount | 4,050,156 | 658,195 | -3,391,961 |
| Lexer 4→8 | 1,241,760 | 0 | -1,241,760 |
| Lexer CopiedWordReferences | 13,032,984 | 8,065,944 | -4,967,040 |
| Lexer FinalCollections | 2,252,094 | 2,252,094 | 0 |
| Lexer FinalCapacity | 21,633,788 | 25,267,552 | +3,633,764 |
| Lexer TotalUnusedSlots | 7,275,786 | 10,909,550 | +3,633,764 |

Candidateの0-token Lexer collection 101,893件はFinalCapacity 0としてmetricsに残った。4→8とcopied refsはC2 field prototypeと同じ削減で、通常collectionはcapacity policyを変更していない。

### Allocation / object layout

同一条件のKojo 1回で、metrics process counterのTotal AllocationはFormal BEFORE 6,529,862,744 bytes、Candidate AFTER 6,482,760,816 bytes（-47,101,928、-0.72%）。EventPipe sampled allocationは6,512,333,512→6,473,433,160 bytes（-38,900,352、-0.60%）だった。型別sampledでは`WordCollection` 106,014,256→108,559,240 bytes（+2,544,984、+2.40%）と僅かに増えたが、平均object bytesは48→48で、C2 field版の56 bytes化は再発していない。この型別総量は単発sampleの揺れを含むため**NEUTRAL**扱いとする。`Word[]`は372,927,760→347,217,664 bytes（-25,710,096、-6.90%）、`List<Word>`は75,411,696→68,799,144 bytes（-6,612,552、-8.77%）。EventPipe値はsampled estimate、metrics totalはprocess counterであり、単発値のため速度効果は断定しない。

### Final adoption

Candidate Aは、追加instance fieldなし、Lexer 0-tokenでList/Word[]を生成しない、WordCollection平均object size 48 bytes、focused/macro経路、Normal/Kojo Startup、4→8/copy/Word[]削減、Total Allocation削減を確認した。型別WordCollection sampled総量は単発runではNEUTRALとし、object-size penaltyがないことを正式な判定根拠とする。速度は**UNPROVEN**のままとする。結論は**ADOPT**。Phase 6-C2のfield-based prototypeはCandidate Aに**SUPERSEDED**され、正式採用対象はzero-overhead版だけとする。Capacity 16、adaptive/threshold、custom buffer、ArrayPool、InstructionLine/CharStream最適化は行っていない。

## Phase 6-C3: Capacity Measurement Cleanup & Consolidation（2026-08-13）

Phase 6-C1〜C2.2で役目を終えたWordCollectionのresize/capacity専用計測（`RecordWordCollectionAdd` / `RecordWordCollectionFinal`、growth集計、`wordcollection-growth.txt`）を撤去した。C2.2のpending state、初回capacity 8、compact→Linked promotion、cursor/Count、self-add互換は変更していない。`firstAddCapacityHint` は本体に存在しないままである。

汎用 `ErbStartupProfiler` と `PERFORMANCE_METRICS` の `SetArgumentToCalls`、`ForceSetArgumentCalls`、FunctionCode別件数、Labels/Remaining/ParallelRemaining、PrimaryParse/file/function情報は残した。cleanup後のRelease/metrics buildは各0 errors（既存warningsのみ）、Normal/Kojo StartupTestは各モード1回ともexit 0・Lv2 warning 0、generic countersも取得できた。metrics出力にcapacity専用ファイル・resize/copy/unused/final-capacity項目は生成されなかった。C2.2既往のallocation差分（-47,101,928 bytes / -0.72%、Word[] -25,710,096 bytes / -6.90%、copied references -4,967,040 / -38.11%）は履歴値として保持し、速度改善は未証明のままとする。

## CharStream Candidate A Production Adoption（2026-08-15）

`EraStreamReader.ReadEnabledLine` 内で、外部へ返す前の一時 `CharStream` を `Reset` して再利用する最適化をProductionへ採用した。共有pool/cacheは使用していない。Normal allocationは約-71.1MB（-3.04%）、Kojoは約-244.1MB（-3.86%）、CharStream sampled allocationはNormal約-55.1%、Kojo約-62.5%だった。Normal/Kojo互換性はPASSで、Startupでは明確な性能退行を確認しなかった。速度向上や完全同速とは断定しない。

## ReadString Candidate B Production Adoption（2026-08-15）

`LexicalAnalyzer.ReadString` のno-escape fast pathをProductionへ採用した。escape無し文字列では`StringBuilder`生成を回避し、escape有りでは従来相当の経路へfallbackする。共有cache/pool/static mutable stateは使用していない。既知のallocation削減はNormal約-383.2MB（-16.91%）、Kojo約-404.8MB（-6.66%）で、GC pressureも改善した。focused compatibility、Normal/Kojo startupはPASS。Balanced startupではNormal ERBにノイズを含む+9.12%中央値差があったが、Kojoでは再現せず、Startup全体で明確な一貫した性能退行は確認されなかった。高速化や完全同速とは断定しない。
