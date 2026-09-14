# MAIN_OPT-C1W-AUDIT1 — null backing安全性と実プレイmaterialize寿命

初回調査日: 2026-09-13
run-04追記日: 2026-09-14
基準source commit: `39e7fd18f5c8e8685b2af23c5045ca0bae7751f3`
基準distribution commit: `9a0383d558a1d77b76ad8c880fdf445f21def126`

## 結論

production sourceの直接参照はすべて分類でき、`UNSAFE_OR_UNPROVEN`は0件。既存C1-W自己テスト72件と通常Releaseの診断hook不在確認はPASSした。

run-04の元JSONLをauthorityとして確認し、実ERB経路でnull backing read、同一キャラへのnonzero write、キャラ単位materialization、およびsave/load後の復元を確認した。判定は **`AUDIT1_RUNTIME_CORE = PASS`**、**`AUDIT1_FORMAL = PASS_WITH_CHECKPOINT_NOTE`**。P4の手動snapshot recordは存在しないため取得済みとは扱わないが、P3後のmaterialization event列とsave/load時の`BinaryLoadNonZero`記録で最終状態を追跡できる。

## 1. 対象と保全

- A1 / A2 / C1-Wは既採用状態を維持。M2、row-lazy、新しいproduction最適化には進んでいない。
- 実行には`fixture_450`のfresh working copy（run-02 / run-04）だけを使った。canonical fixtureをrunnerへ直接渡していない。今回のruntime判定はrun-04の元JSONLをauthorityとした。
- canonical `Data/sav/save219.sav`とrun copyのSHA-256は、実行前後とも一致した。

```text
8ED7326315EB6F19880211B8E42E7482094E2C06B3584C809E6EE5C02405D8E4
```

- `save401.sav`、`time.log`、profileの正本を推測・復元していない。canonical fixture上でゲームを起動していない。
- N10/N100および既存マクロ性能runnerは実行していない。fixture ERBも変更していない。

## 2. production source直接参照監査

`Runtime` / `UI`のC#を検索し、`DataIntegerArray2D[...]`の有効な直接参照10箇所を確認した。コメントアウトされた旧実装1箇所は実行経路から除外した。

| 箇所 | 分類 | 根拠 |
|---|---|---|
| `VariableToken.cs:657` `Int2DVariableToken` | `SAFE_NOT_CDFLAG` | キャラクタ変数用tokenではなく、CDFLAGを参照しない。 |
| `VariableToken.cs:1059` `CharaInt2DVariableToken.GetIntValue` | `SAFE_TOKEN_NULL_AWARE` | CDFLAGのnull backingを範囲検査後に論理0として返す。 |
| `VariableToken.cs:1083` `SetValue(long)` | `SAFE_TOKEN_NULL_AWARE` | CDFLAGは非0書込み時にmaterializeし、nullへの0書込みではallocationしない。 |
| `VariableToken.cs:1087` 非CDFLAG側の代入 | `SAFE_NOT_CDFLAG` | CDFLAG分岐の`else`内だけで使用する。 |
| `VariableToken.cs:1096` bulk `SetValue(long[])` | `SAFE_TOKEN_NULL_AWARE` | null backingへのall-zero入力は範囲検査のみ。非0を含む場合だけmaterializeする。 |
| `VariableToken.cs:1136` `PlusValue` | `SAFE_TOKEN_NULL_AWARE` | null時は範囲検査し、加算0なら0を返す。非0加算でmaterializeする。 |
| `VariableToken.cs:1164` `GetArrayChara` | `SAFE_MATERIALIZES_BEFORE_RAW_ACCESS` | CDFLAG分岐で`EnsureCFlagBacking(RawArrayRequest)`を通してから配列を返す。 |
| `CharacterData.cs`を参照する`PerformanceMetrics.cs:339` | `SAFE_CHARACTERDATA_NULL_AWARE` | 診断集計は外側のbacking参照がnullかだけを数える。 |
| `PerformanceMetrics.cs:601` post-load scan | `SAFE_CHARACTERDATA_NULL_AWARE` | backing参照を直接読み、nullなら走査せず次キャラへ進む。allocation helperを呼ばない。 |
| `VariableData.cs:1183` layout計測 | `SAFE_CHARACTERDATA_NULL_AWARE` | backing参照がnullでない場合だけpayloadを加算する。 |

このほか、`SetValueAll`は`CharacterData.SetCFlagAll`を通る。`COPYCHARA` / `ADDCOPYCHARA`、`SORTCHARA`、binary / extended / old-1802 save-load、state hashも、それぞれCDFLAG用のnull-aware経路またはmaterializeを行う経路を確認した。`FINDCHARA` / `SUMCARRAY` / `CMATCH` / `MAXCARRAY`はscalar accessを通る。

追加raw-array escapeは`GetArrayChara`から`GDRAWG` / `GDRAWCIMG`の任意カラーマトリクス読取りへ到達する。これは明示的なraw backing要求としてmaterializeする既知経路で、nullを直接indexする未保護経路ではない。

**分類結果: `UNSAFE_OR_UNPROVEN = 0`。**

## 3. fixture ERBと好感度候補

C0の全ERB字句集計では、9,458ファイル中31ファイルにCDFLAG参照があり、207箇所（read概数130、write概数147。compound assignmentは両方へ計上）だった。row添字は全207箇所で定数。column添字は数値定数67、動的140。これは静的な使用数であり、実行回数ではない。

好感度の候補は次の通り。

- `Data/ERB/ＳＨＯＰ関連/214_交流.ERB`の`@SHOP_COM_214`は、選択された対象1人に対する`CDFLAG:TARGET:キャラ間依存度:(...) += ...`を持つ（146行付近）。
- `213_集会させる.ERB`の`@SHOP_COM_213`は、複数キャラを処理するループ内にCDFLAG更新を持つ（203行付近）。

run-04のruntime logは実ERBからのCDFLAG read/writeとmaterializationを記録しており、実プレイ経路を通った証拠となる。ただしmaterialization record自体にERB labelは含まれないため、特定の`SHOP_COM_214` / `SHOP_COM_213` labelが実行されたとはこのログだけから断定しない。

## 4. 診断instrumentationとチェックポイント

- materialized数の集計は`CharacterData.DataIntegerArray2D[cflagIndex] != null`という外側の参照判定だけである。`GetArrayChara()`、`EnsureCFlagBacking()`、CDFLAG backingの新規allocationを診断から呼ばない。
- null-read時の追加記録もread-onlyで、値とcharacter indexを集約する。全アクセスごとのログは出さない。
- 初回のnull→backing遷移では、materialize直前・直後を別recordにし、reason、character list index、character NO、materialized数を出す。P0取得後の最初の遷移だけがP2 snapshotを自動記録する。
- P1/P3/P4の手動checkpointは`PERFORMANCE_METRICS`付き診断経路のCtrl+Shift+F12で取得する。ゲーム変数やERB進行を変えず、診断入力として消費する。通常Releaseではhookおよびcheckpoint methodがコンパイルされないことをreflection checkで確認した。

## 5. run-04 450人実プレイ観測

authority: `artifacts/diag_tests/c1w-audit1-realplay-20260913/run-04/fixture_450/c1w-audit1-manual.jsonl`（SHA-256 `5FA652B58A7602F0FD556421B374A93C5F1B4028EE9B90F9B5944C7D7B9F45D6`、22,491 bytes、37 records）。review用txtではなく元JSONLを読み取った。

| checkpoint / 観測 | materialized | remaining avoided payload | 結果 |
|---|---:|---:|---|
| P0: save219ロード直後 | 0 / 450 | 194,400,000 bytes（185.39 MiB） | 取得 |
| P1: 実プレイ開始前 | 0 / 450 | 194,400,000 bytes（185.39 MiB） | 取得 |
| 初回null read | 0 / 450 | 194,400,000 bytes（185.39 MiB） | characterListIndex=38、characterNo=10105、row=7、column=100、value=0 |
| P2-first-materialization | 1 / 450 | 193,968,000 bytes | 初回nonzero writeで取得 |
| P3 | 3 / 450 | 193,104,000 bytes | 取得 |
| save直前まで | 7 / 450 | 191,376,000 bytes（約182.51 MiB） | 443 / 450は未materialized |
| P4手動snapshot | — | — | recordなし。取得済みとは扱わない |

最初のCDFLAG null readは10105のrow 7 / column 100で論理値0を返し、その時点でbackingはnullのままだった。同じcharacter NO 10105に対する`ScalarNonZeroWrite`の直前・直後recordでmaterialized数が0から1へ変わり、`nullReadBeforeMaterialization=true`が記録された。これは、実ERB readがnull backingから0を読み、read自体はmaterializeせず、同一キャラへのnonzero write時に初めてmaterializeしたことを示す。

実プレイ中にmaterializeしたcharacter NOは、順に`10105, 10116, 10300, 10303, 5207, 10120, 10113`。全450人の一括materializationではなく、各キャラの`ScalarNonZeroWrite`ごとに1人ずつ増えた。7人時点では443人が未materializedで、remaining avoidedは`443 × 432,000 = 191,376,000 bytes`（約182.51 MiB）。185.39 MiBは永久削減ではなく、寿命モデルは従来どおり`remaining avoided ≒ (unmaterialized character count) × 432,000 bytes`とする。

save/load後には上記7 character NOだけが`BinaryLoadNonZero`で再materializeされた。load中にそれ以外のCDFLAG materialization eventは記録されていない。したがって複数ターンの代表的実プレイとsave/loadを経た後も、大部分のpayload削減が維持された。

P4 manual snapshot recordそのものは存在しない。P4を取得したとは記載しない。一方、P3以降のmaterialization event列と、後続save/loadにおける7件の`BinaryLoadNonZero`を追跡できるため、指定どおりruntime coreはPASSと判定し、記録上のcheckpoint欠落をformal判定注記として残す。

## 6. Gate・検証

| 項目 | 結果 |
|---|---|
| source direct-access分類 / unsafe件数 | PASS / 0 |
| 診断自己テスト | PASS / 72 checks |
| 通常Release checkpoint method不在 | PASS / 1 check |
| normal / diagnostic Release build | 本AUDIT1作業中の先行build記録では両方PASS（各30既存warning、0 error）。 |
| `git diff --check` | PASS |
| canonical save219 SHA-256 | 実行前後一致 |
| stage / commit / push | なし |

## 7. 判定

- `AUDIT1_RUNTIME_CORE = PASS`
- `AUDIT1_FORMAL = PASS_WITH_CHECKPOINT_NOTE`（P4 manual snapshot recordなし）

実プレイ経路で、(1) null backingへの実ERB readが論理0を返す、(2) readだけではmaterializeしない、(3) 同一キャラへのnonzero write時にのみmaterializeする、(4) materializationは450人一括ではなくキャラ単位、(5) save/load後はnonzeroを持つ7キャラだけ復元される、(6) 複数ターン相当の実プレイ後も最大185.39 MiBのうち約182.51 MiB分の回避状態が維持された、を確認した。

P4取得済みとは主張しない。P4 checkpoint labelの欠落は正式記録上の注記として残すが、P3後のevent列とsave/load eventによる直接追跡を根拠にruntime safetyを未確認扱いにはしない。185.39 MiBは恒久削減ではなく、残存回避量は未materialized character数に比例する。row-lazyおよび新たなproduction変更には進まない。
