# MAIN_OPT-M1 — Rooted ERB graph / string ownership

調査日: 2026-09-13
対象branch: `main-opt/diag-01-save219`
基礎HEAD: `82e0231710957e70d97f7a4cfed746e6d54a8902`

## 結論

既存H1 dumpのサンプル調査では、parserの`Word`系と`InstructionLine`の多くが、`LabelDictionary`からparsed graphへ至るroot chain上にあった。`CharStream`と`ExpressionParser+TermStack`の選択サンプルはrootを持たなかった。したがって、parser全体をsnapshot残骸とみなす根拠はなく、型全体のretained量もこの調査だけでは確定しない。

重点対象の`FixedVariableTerm`はroot確認した20件すべてがparsed graphから到達可能だった。100件のIdentifier調査では主に1D tokenが見つかり、50件の実transportersは全て別々の`Int64[3]`、各48 bytesだった。全411,737件がrootedと仮定した場合、FixedVariableTerm本体と専有transporterの合計は約40.84 MiBになる。ただし、これは全件のretained実測値ではなく、表現改善を検討する候補量である。

`System.String`の上位20件はownerが混在し、うち11件は`InstructionLine`が直接保持するstring、5件はstatic 1D string variable backing、1件はruntime dictionary/cache、3件はこのsnapshotでroot未検出だった。`System.String[]`上位10件もstatic variable、ERB定数、識別子・ファイル名一覧などに分かれた。型全体の約208.0 MiBをどのownerが保持するかは未確定であり、上位サンプルから比率を外挿しない。

次のproduction候補としては、5-character条件で削減上限が約2.06 MiBのCDFLAG C1-Wより、`FixedVariableTerm`と専有transporterの表現を先に評価する。ただし実装前に、transporter参照の外部流出、添字の意味、通常parsed termとthread-static pool利用termの区別を静的監査する。文字列については、`InstructionLine.argumentStorage`とstatic string variable backingの合計所有量を別途測る必要がある。**このsummaryは候補順位の記録であり、production変更の承認ではない。**

## 方法と範囲

- 解析対象は既存の`artifacts/diag_tests/m0-heap/run-20260913-071330-622/H1/output/H1.dmp`のみ。新規dump、ゲーム起動、macro、N10/N100、buildは実施していない。
- `dotnet-dump analyze`で既存dumpを読み取り、型統計、複数アドレスの`gcroot`、`dumpobj`を確認した。`System.String` / `System.String[]`の型はMethodTableで特定し、同名部分文字列による別型の混入を避けた。
- root判定では`gcroot <address> -limit 1`を用い、各objectの最初に見つかるroot chainを調べた。ROOTEDはrootが少なくとも1つ見つかったこと、UNROOTEDはこのsnapshotでroot未検出を表す。MIXEDは複数sample間で両方が見つかった場合に限る。`-limit 1`のため、同一objectに別ownerからの追加rootがあるかは網羅していない。
- parser系サンプルは複数SOH範囲から採り、高頻度型は10件、低頻度型は5件。FixedVariableTermは20件をroot確認し、Dimension確認用に同じheap範囲から別sampleを加えて計100件とした。100件は無作為抽出ではないため、全411,737件のdimension比率を表さない。
- 文字列は実`System.String`から大きい順の20件、文字列配列は実`System.String[]`から大きい順の10件をroot確認した。本文はsummaryに転載していない。
- `InstructionLine.argumentStorage`のsource保持、`FixedVariableTerm`の生成・pool経路はsource上でも確認した。source変更はしていない。

## Parser系objectのroot判定

| 型 | H1 object数 | sample | ROOTED | UNROOTED | 主な観測 |
|---|---:|---:|---:|---:|---|
| `Word[]` | 221,996 | 10 | 8 | 2 | `WordCollection` / 命令引数のgraph |
| `IdentifierWord` | 387,697 | 10 | 8 | 2 | 同上 |
| `WordCollection` | 221,600 | 10 | 8 | 2 | 同上 |
| `List<Word>` | 220,954 | 10 | 8 | 2 | 同上 |
| `SymbolWord` | 375,329 | 10 | 7 | 3 | 同上 |
| `LiteralIntegerWord` | 80,041 | 5 | 5 | 0 | parsed graph |
| `OperatorWord` | 64,371 | 5 | 4 | 1 | parsed graph |
| `CharStream` | 10,746 | 5 | 0 | 5 | root未検出 |
| `ExpressionParser+TermStack` | 13,362 | 5 | 0 | 5 | root未検出 |

ROOTED sampleで繰り返し見えた代表経路は次の通り。先頭のWinForms strong handle / `MainWindow`はprocess graphへのrootであり、objectの所有bucketを単独で示すものではない。

```text
strong handle → MainWindow → EmueraConsole → Process
  → LabelDictionary → Dictionary<..., FunctionLabelLine> / Entry[]
  → FunctionLabelLine → InstructionLine → ExpressionArgument
  → WordCollection / List<Word> / Word[] / VariableTerm
```

### InstructionLine

H1には`InstructionLine`が953,552件、68,655,744 bytes（約65.5 MiB）ある。異なるheap範囲から10件を調べ、10件すべてにrootがあり、`LabelDictionary` / `FunctionLabelLine`を経るparsed graphにつながった。10件の範囲ではlazy ERB manager専用rootやunrooted旧graphは見つからなかったが、全件の混在有無は未確定である。

大きいstring sampleのうち11/20は`InstructionLine`がstringを直接保持する経路だった。現行sourceでは`InstructionLine.argumentStorage`が、lazy引数parse前のsource stringを保持し、parse時に`PopArgumentPrimitive`がそれを消費してnullにする。error textにも同じslotを使うため、root chainだけでは個々のstringがsourceかerror textかまでは区別できない。

## FixedVariableTermとtransporter

H1の`FixedVariableTerm`は411,737件、23,057,272 bytes（各56 bytes）。20件を`dumpobj` / `gcroot`で調べ、Identifier・transporterが存在し、全20件にrootがあった。観測した最初のrootはすべてparsed label / instruction / argument graphを経由した。`gcroot -limit 1`のため、poolからの追加rootがないことまでは示さない。

そのうち50件のtransporterをdump上で個別確認した結果:

- 50件すべて`System.Int64[]`、rank 1、length 3、object size 48 bytes。
- 50件すべてtransporter addressが異なった。
- sourceの両`FixedVariableTerm` constructorは各instanceに`new long[3]`を割り当てる。

従って、全411,737件に専有arrayがあり、その全てが保持されると仮定した場合の見積りは次の通り。root sampleとsource auditに基づく候補量であり、retained heapの直接測定値ではない。

| 内訳 | 算出 | 見積り |
|---|---:|---:|
| FixedVariableTerm本体 | 実測type shallow合計 | 23,057,272 bytes（約21.99 MiB） |
| 専有transporter | 411,737 × 48 bytes | 19,763,376 bytes（約18.85 MiB） |
| 合計 | 上記の単純合算 | 42,820,648 bytes（約40.84 MiB） |

### Identifier dimension sample

100件の`Identifier`について`dumpobj`からtoken typeと`Dimension`を読み取った。Dimension別の便宜サンプル内訳は次の通り。

| 分類 | 件数 / 100 |
|---|---:|
| scalar character | 0 |
| 1D（integer/string、static/localを含む） | 93 |
| character 1D | 0 |
| 2D | 3 |
| character 2D | 0 |
| 3D | 0 |
| その他 | 4（`CHARANUM_Token`、Dimension 0） |

このsampleではindex 1個のtokenが多かったが、全体比率とはみなせない。通常の`GetFixedVariableTerm`はnewし、`RentFixedVariableTerm`はthread-staticの`fixedLongTermPool` / `fixedStringTermPool`から再利用する場合がある。今回の最初のroot chainではpoolをownerとして観測していないものの、別rootの有無・transporterのidentityが外部へ漏れないこと・pool termとAST termの同一性は未監査である。

## System.String / System.String[] ownership

H1 type統計は`System.String` 2,991,589件 / 186,576,574 bytes、`System.String[]` 102,732件 / 31,554,304 bytes。合計は218,130,878 bytes（約208.05 MiB）のshallow bytesであり、retained owner別合計ではない。

| 対象sample | ROOTED | UNROOTED | ownerを終端側のroot chainから分類 |
|---|---:|---:|---|
| 大きい`System.String` 20件 | 17 | 3 | `InstructionLine`保持11、static 1D string variable backing 5、runtime dictionary/cache 1 |
| 大きい`System.String[]` 10件 | 10 | 0 | static string variable backing 3、ERB string constant backing 3、通常`Str1DVariableToken` backing 1、identifier/name list 2、filename list 1 |

確認したrepresentative chain:

```text
LabelDictionary → FunctionLabelLine → InstructionLine → System.String
InstructionLine / expression → Str1DVariableToken → VariableData
  → StaticStr1DVariableToken → System.String[] → System.String
Process → IdentifierDictionary → List<System.String> → System.String[]
```

「System.Stringが大きい」だけでは用途を確定できず、static string values、命令行が保持する文字列、定数配列、名前一覧などが同じ型統計に混在している。large-object sampleは合計208 MiB全体の所有割合を推定するサンプルではない。

## Integer array bucketとGC領域

| 型 | H1 shallow bytes | MiB換算 / 解釈 |
|---|---:|---|
| `Int64[,,]` | 47,533,968 | 約45.33 MiB。M0のuser static payload約47.75 MiBと同じ規模だが、完全な一対一照合ではない。 |
| `Int64[,]` | 4,556,416 | 約4.35 MiB。built-in character backingを含み、CDFLAG分を別加算しない。 |

代表`Int64[,,]`がstatic user variable tokenからrootされることはM0で確認済み。CDFLAGの5-character payload削減上限は2,160,000 bytes（約2.06 MiB）で、今回のM1ではCDFLAG表現を変更していない。

M0 H1の各指標はheapの異なる面を表す。

| 指標 | bytes | MiB |
|---|---:|---:|
| GC committed | 1,108,688,896 | 約1,057.33 |
| 非Free objectのshallow合計 | 697,993,324 | 約665.52 |
| GC内Free領域 | 365,302,664 | 約348.38 |
| `GC.GetTotalMemory(false)` | 693,773,272 | 約661.63 |
| process Private bytes | 1,526,325,248 | 約1,455.62 |

非Free shallow合計はrooted/retained合計ではない。GC内Free領域はlive objectではなく、Private bytesとの差にはnative/runtime等も含まれる。これらを同じ「削減可能managed memory」として足し引きせず、production GC.CollectやGC mode変更も提案しない。

## 次の候補と停止条件

1. `FixedVariableTerm` + 専有`long[3]`は、20/20 rooted sampleと約40.84 MiBの条件付きpayload見積りから局所的なrepresentation改善候補とする。実装前にtransporter identity escape、index semantics、AST termとpool termの区別をsourceで監査する。
2. `System.String` / `System.String[]`は合計約208 MiBだがownerが混在し、型全体のretained bytesは未確定。次の診断では`InstructionLine.argumentStorage`、static string variable backing、constant/name/path arraysの所有bytesを集計する。
3. 5-character条件でのCDFLAG C1-Wは最大約2.06 MiBであるため、上記のowner調査より後順位に保留する。450-character時の185.39 MiBというC0見積りを、今回の5-character実ゲームheapへ当てはめない。

本フェーズではproduction最適化・CDFLAG変更・fixture変更・buildを行っていない。stage / commit / pushも行っていない。
