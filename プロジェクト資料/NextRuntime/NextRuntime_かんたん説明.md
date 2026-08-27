# Next Runtime かんたん説明

## 1. 何を作っている？

今のゲームをそのまま動かせる、もっと速く軽いEmueraを作っています。

見た目や遊び方を変えることが目的ではありません。内部の読み方と動かし方を整理することが目的です。

## 2. 何が問題？

今のEmueraは、大量のERBを起動時に詳しく調べます。

そのため、起動に時間がかかることがあります。小さなデータをたくさんメモリに持つこともあります。遊ぶほど、触った処理が増えて重くなる場合もあります。

## 3. どう軽くする？

本にたとえると、今は本を最初から全部詳しく読んでから渡しています。

Next Runtimeでは、最初に目次だけを作ります。これがSource Indexです。Source IndexはERBの「目次」です。

```text
最初は目次だけ作る
        ↓
必要な章だけ読む
        ↓
実行しやすい形へ変換する
        ↓
また作れるものは、必要なら捨てる
```

## 4. 速くする方法

主な方法は4つです。

* 不要な解析をしない。
* 一度変換したものを再利用する。
* 小さくまとまった実行形式を使う。
* 変換済みのERBを動かす新しい実行部分（VM）を作る。

compileとは、ERBを実行しやすい形へ変換することです。cacheとは、また作れるデータを一時的に覚えておく場所です。

## 5. メモリを減らす方法

絶対に必要なゲーム状態だけは保持します。目次も保持します。

一方で、元から作り直せる読み込み結果や変換結果には上限を設けます。古くて不要になったものは捨てます。

これにより、長時間遊んだ後もメモリが増え続けにくい構造を目指します。

## 6. 今どこ？

軽い目次の答え合わせが終わり、現在は目次から必要な関数だけ取り出して、新しい小さい形式へ変換するPhase 1A-R4試作を行っています。関数全体ではなく、Source Indexが示すphysical byte spanだけを読みます。関数ごとの64KB読み取りbufferを廃止し、監査時は同じERBを一度だけ開く方式も追加しました。R4では、差分用の辞書やfingerprint文字列を解放した後の純粋compiled rootを3回測り、共有name/path参照をknown payloadから除外しました。

まだ新しいVMでゲームを動かしていません。0B-R2では旧Emueraの実際の読み込み処理と目次を答え合わせし、134,652件を安全範囲とfallback範囲に分けて予期しない差を0件にしました。1A-R2では実ゲームのcompiler eligible 59,435件のうち54,200件をprototype compileでき、unique未対応は5,235件でした。5回計測の累積遭遇数26,175件をcoverage件数にはしていません。監査のsource read中央値はR1の約5.93秒から約0.93秒、totalは約6.61秒から約1.73秒へ短縮しました。未対応構文は安全にLegacyへfallbackします。

ゲームのERBが更新されたら、将来は変更されたファイルと影響する依存範囲だけをcacheから外す設計です。CSVは表示用か、解析に影響するか、影響範囲不明かで扱いを分けます。設定・parser規則・エンジン版が変わった場合は安全のため全体を作り直します。R2のCSV分類と1Aのfingerprintは診断・設計段階で、disk cacheはまだ実装していません。

Phase 1A-R4でも、VM、VariableStore、Expression/Format IR、UI変更は行っていません。最新5回測定のtotal中央値は1,953.059msです。Phase 1BはChatレビュー後に明示承認されるまで開始しません。

### Phase 1Bの結果

5,235件の未対応関数を調べた結果、主因は変数代入構文4,836関数でした。そこで、代入と、Legacyの命令名をそのまま保持できるRESETCOLOR、CUSTOMDRAWLINE、SETCOLOR、SETFONTだけを小さい命令へ追加しました。54,200件から59,093件へ増え、未対応は342件です。Try/Catch、動的呼出し、ローカル変数、式やフォント状態の複雑な構文は安全側に残しました。

新しい命令は実行せず、元ソースの命令名・行・operandの位置だけを保持します。PrototypeInstructionは16 bytesのままです。baseline 54,200件の失敗は0、Legacyとの命令数・順序・Exact Opcode差分も0です。VM、Expression/Format IR、正式EXE変更はまだありません。

Phase 1B-R2では、Legacyに実在するcommand/function名を固定予約表へまとめ、未対応命令のoperand中の`=`を代入と誤認しないようにしました。架空の予約語は使っていません。CompilerSelfTestは56/56で、Phase 1BはCOMPLETE/HOLDです。

## 7. 最終的には？

今と同じように、`Emuera.exe`を1個ゲームフォルダへ置いて起動する形を目指します。

既存ゲームとsaveを、できる限りそのまま使えるようにします。そのうえで、内部は今より速く、軽くします。

## 8. 何をもって成功？

旧Emueraと同じくらいでは意味がありません。次を実測して、はっきり改善しているか確認します。

* 起動速度
* ゲーム中の速度
* 使用メモリ
* 長時間プレイ時の安定性
* 二回目以降の起動速度

明確な改善がなければ、設計を見直します。

### Phase 1B-R3の結果

Legacyが実際に行頭命令として登録している名前だけを正解にしました。statement commandは275件で、Nextの予約表とMissing 0 / Extra 0 / Duplicate 0 / Empty 0です。RAND・ABS・MINやCHKFONT・GETFONTのような式中methodはstatement予約に混ぜていません。命令の引数に`=`があっても、変数代入と取り違えない確認も追加しました。

実ゲームでは59,093関数を変換でき、未対応は342件、エラーは0件でした。pure retainedは別々に3回測定し、各8,703,552 bytes、中央値も8,703,552 bytesです。known payloadは6,829,664 bytes、overheadは1,873,888 bytesです。Phase 1BはCOMPLETE/HOLDです。VM、Expression/Format IR、Phase 1Cはまだ始めません。

### Phase 1B-R4の結果

R3のstatement 275件だけでなく、Legacyが実際に行頭lookupする全456件を正解集合Aとして再確認しました。Aはstatement B=275件とmethod-backed C=181件の和集合で、Nextのassignment guard・statement metadata・method-backed metadataはA/B/C全てMissing 0、Extra 0、B∩C=0です。method-backedのCHKFONT、GETFONT、RAND、ABS、MINをstatement予約へ混ぜず、`X=Y`もSETへ誤認しません。コメントにしか現れないCHKVARDATA、CHKGLOBALDATA、FIND_VARDATAはLegacy A/B/Cに無く、Nextにも登録していません。

実ゲームのeligibleは59,435、compiledは59,093、remaining unsupportedは342、errorsは0。R3成功集合をbaselineにしてbaseline lost 0、命令数・順序・exact opcode差分0を確認しました。R4の5-run expanded中央値はtotal 2,118.147 ms、allocation 125,520,296 bytes、pure retainedは8,703,552 bytesを3回とも再現しました。Phase 1BはCOMPLETE/HOLDで、VM・Expression/Format IR・Phase 1Cは未開始です。

### Phase 1B-R5 最終Gate

Run IDは`20260827_Phase1B_R5_Final`。4構成のLegacy oracleでA=456、B=275、C=181、A=B∪C、B∩C=0、statement map−B=0、map∩C=0を確認しました。`SET`は行頭statement mapから分離し、構造的に確認した代入だけで生成します。識別子delimiter、separator、`//`、SystemAllowFullSpace、複雑lvalue、source spanをLegacy実測と照合し、CompilerSelfTestは80/80です。

実fixtureのeligibleは59,435、compiledは59,093、remaining unsupportedは342、errorsは0。54,200件baselineのlostは0、命令数・順序・exact opcode差分は0です。expanded 5-run中央値はtotal 1,858.849 ms、allocation 119,904,656 bytes、pure retainedは独立3回すべて8,703,552 bytesです。fixture変更はNew 0 / Changed 0 / Deleted 0、fullwidth-space実例は37件です。`Phase1BDecision=COMPLETE`。VM、Expression/Format IR、Phase 1Cは開始しません。

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
