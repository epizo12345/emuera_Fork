# MAIN_OPT-M0 — save219ロード後のmanaged heap調査

調査日: 2026-09-13
対象branch: `main-opt/diag-01-save219`
基礎HEAD: `82e0231710957e70d97f7a4cfed746e6d54a8902`

## 結論

H0（起動完了・タイトル画面）とH1（save219ロード直後・マクロ開始前）のheap dumpを、fixtureから作った別々のfresh working copyで採取した。canonical `save219.sav`のSHA-256は採取前後および両working copyで一致し、H0/H1ともmacro、N10、N100は実行していない。

この採取のH1 `GC.GetTotalMemory(false)`は693,773,272 bytes（661.63 MiB）だった。以前のR2で記録した約955,636,616 bytesは今回再現していない。今回のH1ではGC committed heapが1,108,688,896 bytes（約1,057.3 MiB）、プロセスPrivate bytesが1,526,325,248 bytes（約1,455.6 MiB）である。snapshot・指標ごとに値が異なるため、以前の値との差は未解決として保持し、同一視・補正しない。

`dumpheap -stat`ではH1の非Free objectのshallow合計が697,993,324 bytes（665.52 MiB）だった。これは型ごとのheap上のshallow合計であり、rooted/retained合計ではない。SOSで利用可能なrooted aggregateは確認できなかったため、代表objectの`gcroot`からownershipの証拠を得た。Script namespaceのH1 shallow合計は約276.2 MiBで、`FunctionLabelLine`等のparsed graphの一部がrootedであることは確認済みだが、Script namespace全体のrooted / retained量は未確定である。次に調査するownerはERB parsed graphおよび文字列・source/cacheの用途である。CDFLAG whole-array lazyの5人条件での削減上限は2,160,000 bytes（2.06 MiB、今回H1 `GC.GetTotalMemory(false)`の約0.31%）なので、現時点では優先しない。

## 測定方法と安全性

- `dotnet-dump` 10.0.745401（実行表示: `10.0.745401+cef304c50763bf24f99566cb31d55540842e7ae9`）を使用した。`dotnet-gcdump`と`dotnet-trace`は使用していない。
- M0専用`Collect-M0Heap.ps1`で、`MAIN_OPT/fixture`からH0/H1それぞれ独立のworking copyを作った。既存macro runnerは使用せず、canonical fixtureを直接起動していない。
- 起動前に各working copyの`save219.sav`をSHA-256確認し、一致したcopyだけを起動した。canonical値は`6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B`。source fixtureの同ファイルも前後で同じ値だった。
- H0は`Init:End`、入力欄取得後、タイトル画面で「1」を押す前。H1は別processで「1」→「219」を入力し、diagnostic JSONLの`layout`（`saveIndex=219`, `charanum=5`）確認直後。固定sleepをload完了条件には使っていない。
- dump直前のprocess情報を記録し、`dotnet-dump collect --type Heap`で各1回採取した。人為的なGCは追加していない。H0 processはH1へ再利用していない。
- `eeheap -gc`、`dumpheap -stat`、`gcheapstat`をH0/H1で実行した。
- raw dumpと分析出力はignoredの`artifacts/diag_tests/m0-heap/`に保持し、Git管理対象へ追加していない。

| 項目 | H0 | H1 |
|---|---:|---:|
| PID | 43024 | 37160 |
| 採取時刻（UTC） | 2026-09-13 07:14:18.182 | 2026-09-13 07:15:19.049 |
| save219ロード | いいえ | 完了、`saveIndex=219` / 5 characters |
| dump size | 1,646,429,440 bytes | 1,534,554,176 bytes |
| working copyのsave219 SHA-256 | `6C26E53C…F3E4814B` | `6C26E53C…F3E4814B` |
| macro / N10 / N100 | 0 / 0 / 0 | 0 / 0 / 0 |

## process metrics

| 指標 | H0 | H1 |
|---|---:|---:|
| PID | 43024 | 37160 |
| WorkingSet64 | 1,450,414,080 B | 1,446,969,344 B |
| PrivateMemorySize64 | 1,627,951,104 B | 1,526,325,248 B |
| PeakWorkingSet64 | 1,629,986,816 B | 1,616,695,296 B |
| TotalProcessorTime | 19,781.25 ms | 20,390.625 ms |
| Responding | true | true |
| Benchmark `startup`: `GC.GetTotalMemory(false)` | 721,976,944 B | 639,747,912 B |
| Benchmark `startup`: allocated bytes cumulative | 4,117,233,840 B | 4,114,656,168 B |
| Benchmark `startup`: Working Set | 1,450,405,888 B | 1,407,787,008 B |
| Benchmark `startup`: Gen0 / Gen1 / Gen2 collection count | 31 / 15 / 10 | 29 / 15 / 9 |

H0のstartup markerはInputReady 4,382.519 ms、ErbParsed 4,238.911 ms、MemoryTrimSkippedだった。H1の同一process内では、startupからsave219ロード完了layoutまで`GC.GetTotalMemory(false)`が639,747,912 bytesから693,773,272 bytesへ54,025,360 bytes（約51.5 MiB）増えた。H0/H1は別processなので、両者間の差をsave219 loadの因果deltaとは扱わない。

## GC heap occupancy

以下の`eeheap -gc`サイズはGC segmentの割当範囲・commitであり、live bytesではない。`dumpheap -stat`のFree分も併記する。

| 指標 | H0 | H1 |
|---|---:|---:|
| GC Allocated Heap Size | 787,716,264 B | 1,071,728,872 B |
| GC Committed Heap Size | 857,260,032 B | 1,108,688,896 B |
| `dumpheap -stat`全object shallow合計 | 778,176,978 B | 1,063,295,988 B |
| うちFree | 65,188,408 B | 365,302,664 B |
| 非Free object shallow合計 | 712,988,570 B | 697,993,324 B |
| `GC.GetTotalMemory(false)` | 721,976,944 B（startup） | 693,773,272 B（layout） |

Gen別のallocated / committed bytes:

| heap | H0 allocated | H0 committed | H1 allocated | H1 committed |
|---|---:|---:|---:|---:|
| Gen0 | 41,255,904 | 71,303,168 | 258,736,352 | 269,156,352 |
| Gen1 | 114,765,904 | 125,829,120 | 228,739,184 | 251,658,240 |
| Gen2 | 526,875,720 | 553,648,128 | 424,243,832 | 426,471,424 |
| LOH | 104,225,776 | 105,697,280 | 159,412,736 | 160,489,472 |
| POH | 57,192 | 192,512 | 57,192 | 323,584 |

H1のPrivate bytesとGC committed heapの差は417,636,352 bytes（398.3 MiB）。H0では770,691,072 bytes（735.0 MiB）。これは単なる指標差であり、native/runtime/UI等の特定ownerに帰属できた量ではない。Working Set、Private bytes、GC committed、GC.GetTotalMemory、shallow object合計を足し合わせてはいけない。

## bucket別のshallow型合計

H1の`dumpheap -stat`非Free合計697,993,324 bytes（約665.52 MiB）を、型名による互いに重ならない便宜的なbucketに分けた。用途やroot retained量を示す数値ではない。

| bucket / 型群 | H0 bytes（MiB） | H1 bytes（MiB） | 解釈上の注意 |
|---|---:|---:|---|
| `MinorShift.Emuera.Runtime.Script.*` | 329,959,824（314.67） | 289,642,168（276.22） | parsed graph/AST等。H1の代表root調査で一部が強くrootedと確認できたが、全量のretained集計ではない。 |
| `System.String` + `System.String[]` | 222,370,016（212.05） | 218,130,878（208.03） | 文字列と参照配列。ERB source/cacheだけとは限らず、H1では大きなユーザー変数文字列のroot例もあった。 |
| `System.Int64[]`, `[,]`, `[,,]` | 45,028,848（42.94） | 90,549,064（86.35） | 配列型のshallow合計。variable payload指標と重ねて加算しない。 |
| `System.Collections.*` | 93,436,296（89.11） | 78,310,336（74.68） | collection本体・entry等の型別shallow合計。 |
| UI / display / WinForms系 | 618,492（0.59） | 944,900（0.90） | 大きなmanaged UI ownerは確認されなかった。 |
| `SkiaSharp.*` | 20,424（0.02） | 382,800（0.37） | managed wrapper shallow合計のみ。native側の量は表さない。 |
| その他 | 21,554,670（20.55） | 20,033,178（19.11） | 上記以外。 |

この表のbucket合計は非Free shallow合計と一致する。H1でheap上の非Free objectとして約665.5 MiBを数えた一方、`GC.GetTotalMemory(false)`は約661.6 MiB。いずれも「rooted/retainedとして説明済みの量」とは呼ばない。Private bytes約1,455.6 MiB全体のownershipもまだ説明できていない。

## rooted / retained evidence

`objsize -aggregate -stat`はSOSの`help`で確認したが、現行SOSでは未対応だった。引数なし`objsize`と`objsize -stat`もobject address 0のparse errorとなった。未導入toolを追加していない。

`objsize 0207e56d9a80 -stat`（単一のVariableData root）では8,978,475 objects、549,037,857 bytesと出た。ただしSOSは共有subgraphが重複計上されると注意しているため、全heapのretained bytesやdeduplicated owner sizeには使わない。

代表objectの`gcroot`結果:

| 代表object | 結果 | 確認できたowner / 限界 |
|---|---|---|
| H1 `CharacterData` | 10 unique roots | WinForms handle/thread context → `MainWindow` → `EmueraConsole` → instruction/expression → `VariableData` → `List<CharacterData>`。 |
| H1 `System.Int64[,,]` | 10 unique roots | `UserDefinedVariableToken[]` → `VariableData+StaticInt3DVariableToken` → 配列。sample tokenはstatic、savedata=true、dimension=3、totalSize=4620。142個すべての3D arrayのownerを証明するものではない。 |
| H1 `FunctionLabelLine` | 9 unique roots | `MainWindow` / `EmueraConsole` → `GameProc.Process` → `ErbLoader` → label dictionary entry → `FunctionLabelLine`。parsed label graphがrootedである証拠。 |
| H1 大きな`System.String` | 10 unique roots | sampleは`StaticStr1DVariableToken` → `System.String[]` → string。用途はstatic user string、ERB source text全体の分類根拠ではない。 |
| H0 `ConsoleDisplayLine` | 0 unique roots | このsampleはunrootedだが未回収の可能性がある。型全体がunrootedという証拠ではない。 |

`InstructionLine`等のparsed graph全体や全Stringのrooted aggregateはまだない。以上のroot chain証拠を型全体のretained sizeへ外挿しない。

## 代表型 top 50

値は`dumpheap -stat`のcountとshallow bytes。`Free`は別掲のため除外。

### H0

|順位|object count|shallow bytes|型|
|---:|---:|---:|---|
|1|3,195,431|198,803,384|`System.String`|
|2|953,552|68,655,744|`MinorShift.Emuera.Runtime.Script.Statements.InstructionLine`|
|3|636,431|34,138,912|`System.Int64[]`|
|4|342,939|32,825,896|`MinorShift.Emuera.Runtime.Script.Parser.Word[]`|
|5|597,443|29,008,888|`MinorShift.Emuera.Runtime.Script.Statements.Expression.AExpression[]`|
|6|88,669|23,566,632|`System.String[]`|
|7|410,320|22,977,920|`MinorShift.Emuera.Runtime.Script.Statements.Variable.FixedVariableTerm`|
|8|609,582|19,506,624|`MinorShift.Emuera.Runtime.Script.Parser.IdentifierWord`|
|9|134,678|17,661,592|`MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine[]`|
|10|134,652|17,235,456|`MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine`|
|11|337,742|16,211,616|`MinorShift.Emuera.Runtime.Script.Parser.WordCollection`|
|12|467,348|14,955,136|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.Expression.AExpression>`|
|13|528,942|12,694,608|`MinorShift.Emuera.Runtime.Script.Parser.SymbolWord`|
|14|336,018|10,752,576|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Parser.Word>`|
|15|140,648|10,126,656|`MinorShift.Emuera.Runtime.Script.CalledFunction`|
|16|243,729|9,749,160|`MinorShift.Emuera.Runtime.Script.Statements.Function.FunctionMethodTerm`|
|17|198,540|9,529,920|`System.Collections.Generic.LinkedListNode<MinorShift.Emuera.Runtime.Script.Statements.InstructionLine>`|
|18|2|9,264,296|`System.Int64[,,]`|
|19|184,826|8,871,648|`System.Collections.Concurrent.ConcurrentDictionary<System.String, MinorShift.Emuera.GameData.Variable.LocalVariableToken>+Node`|
|20|248,808|7,961,856|`MinorShift.Emuera.Runtime.Script.Statements.Expression.SingleStrTerm`|
|21|140,647|7,876,232|`MinorShift.Emuera.Runtime.Script.UserDefinedFunctionArgument`|
|22|97,958|7,836,640|`MinorShift.Emuera.Runtime.Script.Statements.SpCallArgment`|
|23|154,864|7,433,472|`MinorShift.Emuera.Runtime.Script.Statements.ExpressionArgument`|
|24|10,480|7,380,096|`System.Collections.Generic.Dictionary<System.Int32, System.Int64>+Entry[]`|
|25|6|7,157,136|`System.Collections.Generic.Dictionary<System.String, MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>+Entry[]`|
|26|98,079|7,061,688|`MinorShift.Emuera.GameData.Variable.VariableData+LocalInt1DVariableToken`|
|27|134,646|6,463,008|`System.Collections.Concurrent.ConcurrentDictionary<System.String, System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>>+Node`|
|28|197,179|6,309,728|`MinorShift.Emuera.Runtime.Script.Statements.Expression.SingleLongTerm`|
|29|99,178|5,553,968|`MinorShift.Emuera.Runtime.Script.Statements.Variable.VariableTerm`|
|30|46,809|4,528,684|`System.Int32[]`|
|31|138,560|4,433,920|`MinorShift.Emuera.Runtime.Script.Parser.LiteralIntegerWord`|
|32|134,664|4,309,248|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>`|
|33|98,239|3,402,936|`MinorShift.Emuera.Runtime.Script.Statements.Expression.SingleTerm[]`|
|34|98,239|3,402,936|`MinorShift.Emuera.Runtime.Script.Statements.Variable.VariableTerm[]`|
|35|79,081|3,163,240|`System.Collections.Generic.LinkedList<MinorShift.Emuera.Runtime.Script.Statements.InstructionLine>`|
|36|62,140|2,982,720|`MinorShift.Emuera.Runtime.Script.Statements.CaseArgument`|
|37|73,577|2,943,080|`MinorShift.Emuera.Runtime.Script.Statements.Expression.CaseExpression`|
|38|122,349|2,936,376|`MinorShift.Emuera.Runtime.Script.Parser.OperatorWord`|
|39|12|2,650,496|`System.Collections.Concurrent.ConcurrentDictionary<System.String, MinorShift.Emuera.GameData.Variable.LocalVariableToken>+VolatileNode[]`|
|40|1|2,595,616|`System.Collections.Concurrent.ConcurrentDictionary<System.String, System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>>+VolatileNode[]`|
|41|6|2,154,608|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>[]`|
|42|62,140|2,079,976|`MinorShift.Emuera.Runtime.Script.Statements.Expression.CaseExpression[]`|
|43|49,497|1,979,880|`MinorShift.Emuera.Runtime.Script.Statements.Function.UserDefinedMethodTerm`|
|44|59,881|1,916,192|`MinorShift.Emuera.Runtime.Script.Statements.Expression.ExpressionParser+TermStack`|
|45|21,690|1,735,200|`System.Collections.Generic.Dictionary<System.Int32, System.Int64>`|
|46|9|1,625,640|`System.Int64[,]`|
|47|18,843|1,480,384|`System.Object[]`|
|48|2,399|1,324,896|`System.Collections.Generic.Dictionary<System.Int32, System.String>+Entry[]`|
|49|40,629|1,300,128|`MinorShift.Emuera.Runtime.Utils.CharStream`|
|50|11,495|1,103,520|`MinorShift.Emuera.GameData.Variable.VariableData+StaticInt1DVariableToken`|

### H1

|順位|object count|shallow bytes|型|
|---:|---:|---:|---|
|1|2,991,589|186,576,574|`System.String`|
|2|953,552|68,655,744|`MinorShift.Emuera.Runtime.Script.Statements.InstructionLine`|
|3|142|47,533,968|`System.Int64[,,]`|
|4|653,629|38,458,680|`System.Int64[]`|
|5|102,732|31,554,304|`System.String[]`|
|6|581,510|28,167,304|`MinorShift.Emuera.Runtime.Script.Statements.Expression.AExpression[]`|
|7|411,737|23,057,272|`MinorShift.Emuera.Runtime.Script.Statements.Variable.FixedVariableTerm`|
|8|221,996|20,927,296|`MinorShift.Emuera.Runtime.Script.Parser.Word[]`|
|9|134,652|17,235,456|`MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine`|
|10|451,947|14,462,304|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.Expression.AExpression>`|
|11|387,697|12,406,304|`MinorShift.Emuera.Runtime.Script.Parser.IdentifierWord`|
|12|134,669|11,632,064|`MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine[]`|
|13|147,759|10,638,648|`MinorShift.Emuera.Runtime.Script.CalledFunction`|
|14|221,600|10,636,800|`MinorShift.Emuera.Runtime.Script.Parser.WordCollection`|
|15|243,587|9,743,480|`MinorShift.Emuera.Runtime.Script.Statements.Function.FunctionMethodTerm`|
|16|198,540|9,529,920|`System.Collections.Generic.LinkedListNode<MinorShift.Emuera.Runtime.Script.Statements.InstructionLine>`|
|17|375,329|9,007,896|`MinorShift.Emuera.Runtime.Script.Parser.SymbolWord`|
|18|147,756|8,274,336|`MinorShift.Emuera.Runtime.Script.UserDefinedFunctionArgument`|
|19|245,577|7,858,464|`MinorShift.Emuera.Runtime.Script.Statements.Expression.SingleStrTerm`|
|20|97,958|7,836,640|`MinorShift.Emuera.Runtime.Script.Statements.SpCallArgment`|
|21|155,035|7,441,680|`MinorShift.Emuera.Runtime.Script.Statements.ExpressionArgument`|
|22|10,480|7,380,096|`System.Collections.Generic.Dictionary<System.Int32, System.Int64>+Entry[]`|
|23|220,954|7,070,528|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Parser.Word>`|
|24|98,079|7,061,688|`MinorShift.Emuera.GameData.Variable.VariableData+LocalInt1DVariableToken`|
|25|134,646|6,463,008|`System.Collections.Concurrent.ConcurrentDictionary<System.String, System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>>+Node`|
|26|196,310|6,281,920|`MinorShift.Emuera.Runtime.Script.Statements.Expression.SingleLongTerm`|
|27|103,033|4,945,584|`System.Collections.Concurrent.ConcurrentDictionary<System.String, MinorShift.Emuera.GameData.Variable.LocalVariableToken>+Node`|
|28|87,250|4,886,000|`MinorShift.Emuera.Runtime.Script.Statements.Variable.VariableTerm`|
|29|115|4,556,416|`System.Int64[,]`|
|30|134,664|4,309,248|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>`|
|31|46,967|4,102,640|`System.Int32[]`|
|32|1|3,754,512|`System.Collections.Generic.Dictionary<System.String, MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>+Entry[]`|
|33|98,239|3,402,936|`MinorShift.Emuera.Runtime.Script.Statements.Expression.SingleTerm[]`|
|34|98,239|3,402,936|`MinorShift.Emuera.Runtime.Script.Statements.Variable.VariableTerm[]`|
|35|79,081|3,163,240|`System.Collections.Generic.LinkedList<MinorShift.Emuera.Runtime.Script.Statements.InstructionLine>`|
|36|62,140|2,982,720|`MinorShift.Emuera.Runtime.Script.Statements.CaseArgument`|
|37|73,577|2,943,080|`MinorShift.Emuera.Runtime.Script.Statements.Expression.CaseExpression`|
|38|1|2,595,616|`System.Collections.Concurrent.ConcurrentDictionary<System.String, System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>>+VolatileNode[]`|
|39|80,041|2,561,312|`MinorShift.Emuera.Runtime.Script.Parser.LiteralIntegerWord`|
|40|62,140|2,079,976|`MinorShift.Emuera.Runtime.Script.Statements.Expression.CaseExpression[]`|
|41|51,175|2,047,000|`MinorShift.Emuera.Runtime.Script.Statements.Function.UserDefinedMethodTerm`|
|42|21,690|1,735,200|`System.Collections.Generic.Dictionary<System.Int32, System.Int64>`|
|43|64,371|1,544,904|`MinorShift.Emuera.Runtime.Script.Parser.OperatorWord`|
|44|5|1,409,120|`System.Collections.Concurrent.ConcurrentDictionary<System.String, MinorShift.Emuera.GameData.Variable.LocalVariableToken>+VolatileNode[]`|
|45|2,399|1,324,896|`System.Collections.Generic.Dictionary<System.Int32, System.String>+Entry[]`|
|46|11,495|1,103,520|`MinorShift.Emuera.GameData.Variable.VariableData+StaticInt1DVariableToken`|
|47|6,860|1,002,336|`System.Collections.Generic.Dictionary<System.String, MinorShift.Emuera.GameData.Variable.UserDefinedVariableToken>+Entry[]`|
|48|39,751|954,024|`System.Object`|
|49|30|856,272|`System.Collections.Generic.Dictionary<System.String, System.Int32>+Entry[]`|
|50|11,188|716,032|`System.Comparison<MinorShift.Emuera.Runtime.Script.Data.CharacterTemplate>`|

## H0→H1 shallow delta上位30型

別process間の型別差分であり、save219 loadの因果差分ではない。順位は`abs(Δ shallow bytes)`順。countとbyteのdeltaはH1−H0。

|順位|Δ count|Δ shallow bytes|型|
|---:|---:|---:|---|
|1|140|38,269,672|`System.Int64[,,]`|
|2|-203,842|-12,226,810|`System.String`|
|3|-120,943|-11,898,600|`MinorShift.Emuera.Runtime.Script.Parser.Word[]`|
|4|14,063|7,987,672|`System.String[]`|
|5|-221,885|-7,100,320|`MinorShift.Emuera.Runtime.Script.Parser.IdentifierWord`|
|6|-9|-6,029,528|`MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine[]`|
|7|-116,142|-5,574,816|`MinorShift.Emuera.Runtime.Script.Parser.WordCollection`|
|8|17,198|4,319,768|`System.Int64[]`|
|9|-81,793|-3,926,064|`System.Collections.Concurrent.ConcurrentDictionary<System.String, MinorShift.Emuera.GameData.Variable.LocalVariableToken>+Node`|
|10|-153,613|-3,686,712|`MinorShift.Emuera.Runtime.Script.Parser.SymbolWord`|
|11|-115,064|-3,682,048|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Parser.Word>`|
|12|-5|-3,402,624|`System.Collections.Generic.Dictionary<System.String, MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>+Entry[]`|
|13|106|2,930,776|`System.Int64[,]`|
|14|-2|-2,154,384|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.FunctionLabelLine>[]`|
|15|-58,519|-1,872,608|`MinorShift.Emuera.Runtime.Script.Parser.LiteralIntegerWord`|
|16|-46,519|-1,488,608|`MinorShift.Emuera.Runtime.Script.Statements.Expression.ExpressionParser+TermStack`|
|17|-57,978|-1,391,472|`MinorShift.Emuera.Runtime.Script.Parser.OperatorWord`|
|18|-7|-1,241,376|`System.Collections.Concurrent.ConcurrentDictionary<System.String, MinorShift.Emuera.GameData.Variable.LocalVariableToken>+VolatileNode[]`|
|19|-29,883|-956,256|`MinorShift.Emuera.Runtime.Utils.CharStream`|
|20|-15,933|-841,584|`MinorShift.Emuera.Runtime.Script.Statements.Expression.AExpression[]`|
|21|-13,360|-824,816|`System.Object[]`|
|22|11,187|715,968|`System.Comparison<MinorShift.Emuera.Runtime.Script.Data.CharacterTemplate>`|
|23|-11,928|-667,968|`MinorShift.Emuera.Runtime.Script.Statements.Variable.VariableTerm`|
|24|-24,858|-628,770|`System.Boolean[]`|
|25|7,111|511,992|`MinorShift.Emuera.Runtime.Script.CalledFunction`|
|26|-15,401|-492,832|`System.Collections.Generic.List<MinorShift.Emuera.Runtime.Script.Statements.Expression.AExpression>`|
|27|-19,374|-464,976|`MinorShift.Emuera.Runtime.Script.Statements.Expression.OperatorCode`|
|28|-13,613|-435,616|`System.Collections.Generic.Stack<System.Object>`|
|29|158|-426,044|`System.Int32[]`|
|30|7,109|398,104|`MinorShift.Emuera.Runtime.Script.UserDefinedFunctionArgument`|

## 既知variable backingとの突合

H1 layout recordの5-char payload値:

| 項目 | bytes | MiB | 重複計上の注意 |
|---|---:|---:|---|
| built-in CharacterData全体 | 2,803,960 | 約2.67 | CDFLAGを含む。 |
| うちCDFLAG | 2,160,000 | 約2.06 | CharacterData全体の内数。別加算しない。 |
| user CHARADATA | 422,360 | 約0.40 | layoutのbacking payload値。 |
| user static全体 | 50,072,672 | 約47.75 | savedata subset 39,184,840 B、non-save 10,887,832 B。 |

H1で`System.Int64[,,]`は47,533,968 bytes（45.33 MiB）、`System.Int64[,]`は4,556,416 bytes（4.34 MiB）。`CharacterData`のfieldsに3D integer backingはなく、代表3D arrayはstatic user variable tokenへrootされていた。`Int64[,]`型合計からCDFLAG backingを差し引いた分を全て特定変数へ割り当てる調査は未完了。

layout backing payloadは型別shallow合計の内訳・ownerと重なり得るため、これらの値を上のheap bucketへ足して「説明量」を増やしてはいけない。

## 約1GB級heapのうち説明できた量と次のowner

このH1採取では、GC segment allocatedが1,071,728,872 bytes（約1,021.9 MiB）だが、Freeが365,302,664 bytes（約348.4 MiB）ある。非Free shallow型合計は697,993,324 bytes（約665.5 MiB）、`GC.GetTotalMemory(false)`は693,773,272 bytes（約661.6 MiB）。よってheap内のobjectを型ごとに数えた総量は約662〜666 MiBまで把握したが、rooted aggregateがないため「retained ownerで説明済み」と確定できる量はこの合計より少なく、厳密値は未確定である。

H1非Free shallowの大きな型群は、Script namespace約276.2 MiB、`System.String`と`System.String[]`約208.0 MiB、Int64配列約86.4 MiB、`System.Collections.*`約74.7 MiB。FunctionLabelLineと大きなStringの代表root chainを確認したが、型全体のowner別retained量は不明である。特に大きなStringの代表例はstatic user stringであり、約208 MiB全体をERB source/cacheと断定しない。

したがって次はproduction変更ではなく、ERB parsed representation/label graphと文字列・source/cache・ユーザー変数のownershipを区別する診断が優先。C1-Wの5-char削減上限2.06 MiBより、parsed/script・string側の調査価値が大きい。C1-Wは保留し、現時点では実装しない。

## macro runnerの安全性メモ

既存`マクロ性能テスト.ps1`にある「ゲームデータやセーブは書き換えない」という趣旨のコメントは実態と合わない。macro runではゲーム側から`save401.sav`、`time.log`、profile等のruntime生成物が更新され得る。M0はrunnerを使わず、別working copy上のload-only経路で採取した。runner本体のコメントはこの調査では変更していない。

依頼どおり`save401.sav`、`time.log`、profileの正本推測による復元・削除はしていない。`MAIN_OPT/fixture`を完全な未実行原本とは呼ばず、canonical save219と静的game dataをload authorityとして扱う。

## completion gate

- production optimization / C1-W / A1 / A2変更: なし
- H0/H1: canonical fixtureを直接起動せず、別々のfresh working copyを使用
- save219 SHA-256: source前後、各copyとも一致
- macro / N10 / N100: 実行なし
- fixture原本: 変更なし
- raw dump: ignored artifact内に保持、Git管理対象への追加なし
- stage / commit / push: なし
- 次フェーズへ自動移行: なし
