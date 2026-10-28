# RESIZABLE 可変長配列 設計メモ

- 作成: 2026-10-28
- ブランチ: `epizo/可変長配列`（mainには入れない）
- 状態: 実装済み。2026-10-28にmainへマージ（ブランチ`epizo/可変長配列`）。中身の持ち方は4方式を比較して「①容量確保」を採用（2026-10-28）。フィクスチャ6/6・既存回帰92/92・現行ゲーム起動Lv2警告0

## 目的

`#DIM`／`#DIMS`で宣言した1次元配列の長さを、実行中に変えられるようにする。

主な用途:

- `SPLIT`などの出力先として、分割数を見越した過剰な配列を確保しなくて済むようにする。はみ出しによる切り捨てもなくす。
- 随時要素が増える一覧を配列で持つ。代表例は現行ゲーム（eramegaten_p）の戦闘効果`C_BTL_EFFECT`／`C_BTL_EFFECT_PERMANENCE`／`C_BTL_EFFECT_CHARGE`（`#DIMS CHARADATA`、1キャラ最大80件程度）。現在は要素0に`/`区切りで連結し、`ADD_SPLIT`で追加、`REPLACE`で削除、`STRCOUNT`で件数、`AUTO_SPLIT`でn番目を取り出しているため、`TRYSKILL`で全件を見るとキャラごとにn²回ぶんの文字列処理が発生している。

処理頻度の高い箇所で使う前提のため、**読み書きの速度を落とす仕様は採用しない**。

## 仕様

### 宣言

```
#DIM RESIZABLE 名前, 初期サイズ
#DIMS RESIZABLE 名前, 初期サイズ
#DIMS CHARADATA RESIZABLE 名前, 初期サイズ
```

- 1次元のみ。初期サイズは0も可（空）。省略時は従来どおり1。
- 使える場所: グローバル、関数内（`DYNAMIC`／`STATIC`）、`CHARADATA`。
- 同時に指定できないもの: `CONST`、2次元以上。
- `SAVEDATA`・`GLOBAL`と併用できる（ARRAY-03）。長さも保存され、ロードすると保存したときの長さに戻る（下記「セーブ・ロード」）。
- `REF`と併用した`#DIM REF RESIZABLE 名前`は、RESIZABLE変数を受け取る参照渡しの引数になる（ARRAY-03、下記「REF RESIZABLE」）。
- 初期値指定（`= 1,2,3`）は従来どおり可。

### 動きのルール: 伸びるのは自動、縮むのは明示したときだけ

| 操作 | 動き |
|---|---|
| 範囲外の要素への書き込み（`X:n = v`、`X:n += v`） | 長さn+1まで自動で伸びる。途中は0／空文字。上限1,000,000要素（`#DIM`の上限と同じ） |
| 範囲外の要素の読み取り | 従来どおりエラー |
| 負の添字 | 従来どおりエラー |
| `SPLIT`／`HTML_TAGSPLIT`の出力先 | 分割数が長さを超えるときは伸びる。縮めない（後ろは従来どおり前回の値が残る）。分割数は従来どおり個数の変数で受け取る |
| `FINDELEMENT`など読むだけの配列関数 | 今の長さを全長として、そのまま動く |
| `ARRAYREMOVE`など中身を動かす配列関数 | 従来どおり。長さは変えない |
| サイズ変更命令 | 伸ばすのも縮めるのも可。縮める唯一の手段 |
| 普通の`REF`引数（`#DIM REF`）に渡す | エラー（古い配列を見続ける問題を防ぐ）。`#DIM REF RESIZABLE`の引数にだけ渡せる |
| `REF`命令の参照先 | エラー（`REF`命令自体が現行・元のEmueraとも書式エラーになる既存不具合あり） |

- 削除は「`FINDELEMENT`で位置を探す → `ARRAYREMOVE`で詰める → サイズ変更命令で1減らす」。専用の削除・追加命令は作らない。
- 末尾への追加は「今の長さの位置へ書き込む」（自動で伸びる）。

### 追加した関数（命令の形でも書ける）

- `RESIZE 変数, 新しい長さ`（`RESIZE(変数, 長さ)`も可）… 長さを変える。伸びた部分は0／空文字。戻り値は新しい長さ。`RESIZABLE`でない変数を渡すと読み込み時エラー。
- `ARRAYSIZE(変数)` … 今の長さを返す。`RESIZABLE`でない1次元配列は宣言時のサイズを返す。
- 既存の`VARSIZE("変数名")`は、`RESIZABLE`の広域・関数内変数では今の長さ、`CHARADATA`では宣言時の初期サイズを返す（キャラを指定できないため）。

### REF RESIZABLE（ARRAY-03、2026-10-28）

```
@APPEND, ARR, V
#DIM REF RESIZABLE ARR
#DIM V
ARR:(ARRAYSIZE(ARR)) = V      ; 呼び出し元の変数が伸びる
```

- `CALL APPEND, LIST, 5`のように、RESIZABLE変数を参照渡しで受け取る。関数の中で伸ばす・縮める（`RESIZE`）と、呼び出し元の変数も同じ長さになる。`ARRAYSIZE`・`FINDELEMENT`などの配列関数もそのまま使える。
- 受け取った引数を、別の関数の`REF RESIZABLE`引数へさらに渡せる。再帰呼び出しでも、呼び出し階層ごとに受け取った変数を退避して戻す。
- 普通の`#DIM REF`にRESIZABLE変数を渡す、または`REF RESIZABLE`に固定長の変数を渡すと、エラーになる（読み込み時の型確認と実行時の両方）。固定長と可変長を同じ関数で受け取る方法（案B）は、既存の`REF`の読み書きに確認を1つ足すことになるため採らなかった。
- 既存の制限どおり、キャラ変数（`CHARADATA`）は参照渡しできない。
- 実装: `ResizableRef1DToken<T>`（`ReferenceToken`の派生）は配列ではなく入れ物（`ResizableBuffer<T>`）を持つ。参照渡しの受け渡し（`TransporterRef`）を`object[]`にし、RESIZABLEは入れ物を渡す。`ReferenceToken.SetRef`は`object`を受け取る仮想メソッドにした（普通の`REF`の動きは同じ）。
- 広域・関数内STATICの`SetDefault`（`RESETDATA`など）は入れ物を作り直さず、中身を宣言時の長さと初期値に戻す（`ResizableBuffer.Reset`）。`REF RESIZABLE`で参照中に`RESETDATA`しても、同じ変数を指し続ける。

### セーブ・ロード（ARRAY-03、2026-10-28）

- バイナリ形式のセーブは1次元配列ごとに要素数を記録するので、今の長さちょうどの配列を書けば長さも保存される。テキスト形式は末尾の0／空文字を省いて書く（すべて0なら変数ごと書かない）ため、RESIZABLEだけは省かずに全要素を書き、長さ0でも名前と終端を書く（`EraDataWriter.WriteExtended`の`keepLength`）。広域変数は既存の書き込みのまま、`CHARADATA`はキャラごとの格納場所から`ResizableArray.AsArray`で取り出して書く。
- ロードは、保存されていた長さに変えてから読み込む。
  - バイナリ形式: `EraBinaryDataReader.ReadIntArray`／`ReadStrArray`に、保存されていた長さを受け取って読み込み先を返す引数（`resizeForLoad`）を足した。使う場所は`VariableData.LoadVariableBinary`（`SAVEDATA`・`GLOBAL`・`SAVEVAR`）と`CharacterData.LoadFromStreamBinary`（`CHARADATA`・`SAVECHARA`）。
  - テキスト形式: `VariableData.LoadFromStreamExtended`／`LoadGlobalFromStream1808`で、読み込む前に保存されていた要素数へ`RESIZE`する。`CHARADATA`の`SAVEDATA`は、従来どおりバイナリ形式の設定が必要。
- 宣言を固定長から`RESIZABLE`に変えたときは、保存されていた長さで読み込む。逆に`RESIZABLE`から固定長へ戻したときは、従来どおり宣言の長さに収める。
- 保存されていた長さが上限（1,000,000）を超えるセーブはロード時にエラーにする。

### CHARADATAの書き方

Emueraのキャラ変数は、添字が1つ（`C_LIST:0`）だと「`TARGET`の0番目の要素」になる（`CFLAG:0`と同じ規則）。キャラを指定するときは添字を2つ書く。

```
C_LIST:ARG:(ARRAYSIZE(C_LIST:ARG:0)) '= "効果"   ; 末尾に追加
LOCAL = FINDELEMENT(C_LIST:ARG:0, "効果", 0, ARRAYSIZE(C_LIST:ARG:0), 1)
RESIZE C_LIST:ARG:0, 0                             ; 空にする
```

`FINDELEMENT`／`MATCH`／`SUMARRAY`／`MAXARRAY`／`INRANGEARRAY`／`VARSET`は、範囲（終了位置）を省略すると、そのキャラの今の長さを使う（`STRJOIN`も同じ対応を入れたが、もともとキャラ変数を受け付けない）。広域・関数内の`RESIZABLE`は、範囲省略時に常に今の長さが使われる。

## 実装方針

### 中身の持ち方（採用: ①容量確保）

**容量を倍々で確保するバッファ`ResizableBuffer<T>`に中身を持ち、今の長さ（`Count`）は別に持つ。配列関数が`GetArray()`／`GetArrayChara()`で取り出すときだけ、今の長さちょうどへ切り詰めて渡す。**

- 切り詰めた配列がそのままバッファの中身になるので、配列関数が中身を書き換えても（`SORT`、`ARRAYSHIFT`など）そのまま反映される。
- 「取り出した配列の全長＝今の長さ」が成り立つので、`.Length`を使う既存の配列関数（約73か所）は書き換えずに動く。
- 末尾への追加は容量が足りる限りコピーなし。容量は倍々（最低4、上限1,000,000）。
- `Items[Count..]`（容量の余り）は常に既定値（0／null）に保つ。縮めたときに消去するので、再び伸ばしても古い値は見えない。
- 読み取りは`Count`との比較を1回行い、範囲外は`IndexOutOfRangeException`→従来どおり`CheckElement`のエラーメッセージ。
- 縮めても容量はすぐには減らない（次に配列関数へ渡したときの切り詰めで減る）。容量は最大で今の長さの約2倍。
- 例外として、範囲`[start, end)`だけを読む`FINDELEMENT`／`FINDLASTELEMENT`と整数の`MATCH`は、切り詰めずに容量込みの内部配列（`IResizableToken.GetBackingArray`）を読む（ARRAY-02）。範囲の終わりは今の長さ以下に確認済みなので、余りの部分は読まない。

比較した4方式（詳細は「方式の比較」）:

| 方式 | 内容 | 結果 |
|---|---|---|
| 無し | 長さを変えるたびにちょうどの長さへ作り直す（`Array.Resize`） | 追加のたびに作り直すため、追加が遅い。CHARADATAへの追加は現行の文字列方式より遅い |
| **①容量確保** | 上記 | **追加が約2倍速い。採用** |
| ②ARRAYPUSH | 「無し」に追加専用の関数を足す | `ARRAYSIZE`で長さを取って書く書き方と速さが変わらない |
| ①と② | 両方 | ①と同等。`ARRAYPUSH`は広域変数ではむしろ遅い |

素の「大きめに確保して長さを別に持つ」方式（配列関数にも大きめの配列を渡す）は、約73か所を今の長さに合わせて直す必要があるため採用しない。①は渡すときだけ切り詰めることで、これを避けている。

### 既存の変数を遅くしない

- `RESIZABLE`用に専用のトークンクラスを6つ作る（整数・文字列 × 広域と関数内STATIC・関数内DYNAMIC・CHARADATA）。既存の`StaticInt1DVariableToken`などには手を入れないので、既存の`#DIM`の読み書きの処理は変わらない（修正前EXEとの比較で確認、下記）。

### 長さの扱い

- 既存のユーザー定義トークンは宣言時のサイズ`sizes`（`UserDefinedVariableData.Lengths`と同じ配列オブジェクトを共有）で範囲チェック・`GetLength`・`IsArrayRangeValid`を行う。`RESIZABLE`トークンはこれらを今の長さで判定するように上書きする。`Lengths`は共有されているため書き換えない。
- 関数内`DYNAMIC`: 再帰に備えてバッファをスタックに退避する（既存の配列スタックと同じ作り）。呼び出しごとに初期サイズのバッファから始める。
- `CHARADATA`: キャラごとの格納場所（`CharacterData.UserDefCVarDataList[ArrayIndex]`）に、配列ではなく`ResizableBuffer<T>`を置く。そこから配列として取り出す箇所は次のとおり対応した。
  - `GetArrayChara`（配列関数）: 今の長さに切り詰めた配列を返す。
  - `COPYCHARA`（`CharacterData.CopyTo`）: バッファを複製する（コピー元と共有しない）。
  - `SORTCHARA`のキー（`CharacterData.SetSortKey`）: `ResizableArray.AsArray`で切り詰めた配列から取る。
  - メモリ計測（`VariableData`）: バッファの容量込みの配列を数える。
  - セーブ・ロード（`CharacterData.SaveToStreamBinary`／`LoadFromStreamBinary`）: 今の長さの配列で書き、保存されていた長さに合わせて読む（ARRAY-03）。**キャラごとの格納場所を直接扱う処理を足すときは、この入れ物を考慮する必要がある。**

### 読み込み時の範囲確認

添字がすべて定数の変数参照は、読み込み時（`VariableTerm.Restructure`）に`CheckElement`で範囲確認される。`RESIZABLE`は長さが実行時に決まるため、`ARG`／`ARGS`と同様にこの確認を省き、実行時に確認する。

### 安全性の確認（2026-10-28）

- `GetArray()`／`GetArrayChara()`を使う箇所はすべて「引数を評価してから配列を取り出す」順になっており、取り出した後に同じ変数の長さが変わる経路はない（取り出した後に`GetIntValue(exm)`等を呼ぶ箇所を機械的に検索し、該当は`REF`後の別命令の誤検出1件のみ）。
- 配列そのものを保持し続けるのは`REF`（`Instraction.Child.cs`の`SetRef`）と参照渡しの引数（`Process.CalledFunction.cs`の`TransporterRef`）だけ。`REF`命令と普通の`REF`引数は`RESIZABLE`変数ならエラーにする。`REF RESIZABLE`引数は配列ではなく入れ物を保持する（ARRAY-03）。
- このforkの高速化（`FixedVariableTerm`の再利用、`CDFLAG`の遅延確保など）が保持するのはトークンであり、配列ではない。

### 追加の高速化（ARRAY-02、2026-10-28）

- 案1: `FINDELEMENT`／`FINDLASTELEMENT`／整数の`MATCH`は切り詰めずに内部配列を読む。追加と探索を交互に繰り返しても、探索のたびに配列を作り直さない。`SUMARRAY`／`MAXARRAY`／`INRANGEARRAY`／文字列の`MATCH`はもともと要素ごとに読んでおり、切り詰めていない。
- 案2: `ARRAYSIZE`／`RESIZE`は変数参照を確定する汎用の処理（`RentFixedVariableTerm`）を通さず、`CHARADATA`のキャラ番号（第1添字）だけを評価して範囲確認する。

ERBで100万回あたりのms（修正前は`5ec447e`の配布EXE、3回の平均。「追加だけ」は起動直後の準備時間を除くため2回目の計測）:

| 項目 | 前 | 後 |
|---|---|---|
| 追加と`FINDELEMENT`を交互（広域、長さ0〜100） | 273 | 118 |
| 追加と`MATCH`を交互（広域） | 255 | 112 |
| 追加と`FINDELEMENT`を交互（CHARADATA） | 301 | 139 |
| `ARRAYSIZE`だけ（広域） | 178 | 108 |
| `ARRAYSIZE`だけ（CHARADATA） | 213 | 140 |
| `RESIZE`だけ | 167 | 95 |
| 追加だけ（CHARADATA） | 81 | 70 |
| 追加だけ（広域） | 64 | 53 |
| 長さ50の`FINDELEMENT`だけ | 63 | 61 |

## 検証結果（2026-10-28、ブランチのsource、Release単一EXE）

### フィクスチャ

`Emuera以外(開発補助ツールなど)/ConfigRegressionTests/ResizableArray/Run-ResizableFixtures.ps1 -Exe <Emuera.exe>`で実行する。一時フォルダを1つ作り、終了時に削除する。結果 6/6 PASS。

| フィクスチャ | 内容 |
|---|---|
| main | 自動伸長（`=`・`+=`・複数代入）、範囲外読み取り以外の全動作、`RESIZE`の伸縮・空、`ARRAYSIZE`、`VARSIZE`、`FINDELEMENT`／`SUMARRAY`／`MATCH`／`STRJOIN`／`VARSET`／`ARRAYREMOVE`、`SPLIT`（伸びる・縮めない）、`HTML_TAGSPLIT`、関数内DYNAMICの再帰、関数内STATIC、CHARADATA（キャラごとの長さ・範囲省略・`COPYCHARA`の独立性・`DELCHARA`・`ADDVOIDCHARA`）、縮めてから伸ばしたときに古い値が見えないこと、`SORTCHARA`のキーにRESIZABLEのCHARADATAを使えること。約55項目 |
| decl-errors | ERHでの2次元／CONSTとの組み合わせエラー（SAVEDATA／GLOBALはARRAY-03で併用可能になった） |
| decl-errors-erb | 関数内のREFとの組み合わせエラー |
| resize-fixed | `RESIZE`に固定長配列を渡すと読み込み時エラー |
| read-oor | 範囲外読み取りが従来と同じ「配列の範囲外」エラー |
| refarg-error | 普通の`REF`引数にRESIZABLE変数を渡すとエラー |
| refarg-fixed-error | `REF RESIZABLE`引数に固定長の変数を渡すとエラー（ARRAY-03） |
| save-binary | バイナリ形式で、広域・`GLOBAL`・`CHARADATA`のRESIZABLEをセーブ→長さを変える→ロードして長さと中身が戻る。`RESETDATA`後も`REF RESIZABLE`が同じ変数を指す（ARRAY-03） |
| save-text | テキスト形式（既定）で、広域・`GLOBAL`の同じ確認（ARRAY-03） |

save-binary・save-textでは、末尾が0／空文字の配列と長さ0の配列も長さが戻ることを確かめる（テキスト形式で末尾が省かれて長さが失われる不具合を、修正前のEXEで再現してから直した）。

既存の回帰試験 92/92 PASS。現行ゲーム（fixture）の起動安定性テスト 2/2（Lv2警告0）。

### 方式の比較（ERB、各3回のうち最小値、ms）

比較用に4つのブランチを作って同じフィクスチャ（6/6）と同じベンチマークで比べた（2026-10-28）。修正前は正式配布EXE（main `8332563`）で、修正前でも書ける方法を測った。

既存の固定長配列（100万回）: 修正前・4方式とも誤差の範囲で同じ（読み取り31〜32／書き込み35〜41）。既存の変数は遅くなっていない。

| 処理 | 修正前のやり方 | 無し | **①** | ② | ①と② |
|---|---|---|---|---|---|
| 読み取り 広域／CHARADATA（100万回） | 固定長 31／32 | 38／34 | **32／35** | 35／37 | 31／38 |
| 書き込み 広域／CHARADATA（100万回） | 固定長 37／37 | 42／36 | **38／42** | 34／35 | 33／38 |
| 追加 広域（80件×12,500回） | 固定長＋件数変数 45、`/`連結 92 | 89 | **40** | 75（ARRAYPUSH） | 43（ARRAYPUSHは55） |
| 追加 添字指定（同） | — | 78 | **29** | 71 | 29 |
| 追加 CHARADATA（同） | `/`連結 97 | 114 | **71** | 103（ARRAYPUSH） | 72（ARRAYPUSHは65） |
| 追加ごとに`FINDELEMENT`（80件×2,500回） | — | 151 | **165** | 149 | 165 |
| 20件を番号順に全部見る（×5,000回） | `STRCOUNT`＋`SPLIT` 56 | 4 | **3〜4** | 4 | 3 |

- ①で、追加は修正前のどのやり方より速くなる（広域は固定長＋件数変数より速く、CHARADATAは`/`連結の97→71）。「無し」のままだとCHARADATAへの追加は現行の文字列方式より遅かった。
- ①の弱点: 1件追加するたびに配列関数を呼ぶ使い方は約10%遅い（切り詰めと再確保が毎回起きる）。
- 全件を見る処理は10倍以上速い。実際のゲームはERB関数の`ADD_SPLIT`／`AUTO_SPLIT`を使っているため、差はさらに大きい見込み。
- 比較用ブランチ（無し`9f431bd`、②`b8fa6c6`、①と②`8940692`）は採用決定後に削除した（コミットはreflogから復元可能な期間がある）。

### 実ゲームでの比較（eramegaten_p の TRYSKILL、2026-10-28）

現行ゲーム（ShinEraTenseiP 0.5.10）の作業用コピー1つで、戦闘効果の取り出しを`RESIZABLE`で書き換えて比べた。作業用コピーは比較後に削除した。

#### 方法

- セーブ（ユーザー提供、味方6人）をロードし、イベント67「テスト戦闘」→「サンドバッグ」で戦闘を開始する（入力は手動）。
- 作業用コピーだけ、`@EVENT_BATTLE_START_EVENT67`から計測用の関数を呼ぶ。戦闘のターンは進めない。
- 味方6人に、ダミーの戦闘効果を0／10／30件付ける（3種類に振り分け、呼び出し先のない名前）。
- 計測: `TRYSKILLNUM`＋`GET_TRYSKILL`での全件取り出し×300回、本物の`SYNC_STATUS`を味方全員に×20回。5周の中央値。
- 取り出した効果名・発動者・`TRYSKILL_STR`をつなげたハッシュを全実行で比べ、結果が同じことを確認した（全実行で一致）。
- EXEはブランチの試用版（`b59c857`）。無限ループ警告で処理が止まった1周は外れ値として中央値で除いた。以降は作業用コピーの設定で警告時間を延ばした。

#### 書き換え（B3、2ファイル）

効果の文字列（`C_BTL_EFFECT`など3種、1,022か所で使用）はそのまま残し、読み取り用の写しを`RESIZABLE`で持つ。追加・削除（15か所）は変更しない。

```
; SYSTEM_BATTLE.ERH に追加
#DIMS CHARADATA RESIZABLE C_BE_NAME, 0   ; 効果名
#DIM CHARADATA RESIZABLE C_BE_ACT, 0     ; 発動者
#DIM CHARADATA RESIZABLE C_BE_KIND, 0    ; 種類（0=通常 1=永続 2=チャージ）
#DIMS CHARADATA C_BE_SRC, 3              ; 写しを作ったときの元の文字列
#DIMS RESIZABLE BE_TMP, 0
#DIMS BE_PART, 10

; TRYSKILLNUM の戦闘効果部分
SIF C_BTL_EFFECT:ARG:0 == "" && C_BTL_EFFECT_PERMANENCE:ARG:0 == "" && C_BTL_EFFECT_CHARGE:ARG:0 == ""
	RETURNF L_RESULT
SIF C_BE_SRC:ARG:0 != C_BTL_EFFECT:ARG:0 || C_BE_SRC:ARG:1 != C_BTL_EFFECT_PERMANENCE:ARG:0 || C_BE_SRC:ARG:2 != C_BTL_EFFECT_CHARGE:ARG:0
	L_RESULT += BE_REBUILD(ARG)
L_RESULT += ARRAYSIZE(C_BE_NAME:ARG:0)
RETURNF L_RESULT

; GET_TRYSKILL の戦闘効果部分（AUTO_SPLITを使わず番号で取り出す）
IF C_BTL_EFFECT:ARG:0 == "" && C_BTL_EFFECT_PERMANENCE:ARG:0 == "" && C_BTL_EFFECT_CHARGE:ARG:0 == ""
	TRYSKILL_STR = 
	RETURNF "数値が範囲外です"
ENDIF
SIF （写しの確認、上と同じ）
	TRYSKILL_VAR = BE_REBUILD(ARG)
IF ARG:1 < ARRAYSIZE(C_BE_NAME:ARG:0)
	（C_BE_KINDからTRYSKILL_STRを決める）
	TRYSKILL_VAR = C_BE_ACT:ARG:(ARG:1)
	RETURNF "C_BTL_EFFECT_" + C_BE_NAME:ARG:(ARG:1)
ENDIF

; BE_REBUILD（#FUNCTION）: 3種の文字列を"/"でSPLITし、各要素を"_"でSPLITして名前・発動者・種類の配列へ写す。
```

- `TRYSKILLNUM`／`GET_TRYSKILL`は`#FUNCTION`なので、`CALL`は使えない。写しの作り直しも`#FUNCTION`にして式で呼ぶ。
- 文字列が空になった後は写しが古いまま残るが、空の間は写しを読まず、再び効果が付けば文字列が変わるので作り直される。

#### 結果（5周の中央値、ms）

| 戦闘効果 | 取り出し×300 A → B3 | `SYNC_STATUS`×20 A → B3 | ステータス計算 |
|---|---|---|---|
| 0件 | 72 → 72 | 233 → 232 | 同じ |
| 10件 | 136 → 94 | 307 → 263 | 約14%速い |
| 30件 | 298 → 137 | 491 → 319 | 約35%速い |

- A（元のまま）は2回起動して1〜2%以内で一致（233／230）。ERHに変数を足しただけの版（A+ERH）もAと同じ（232）。
- 途中の版（0件／10件／30件の`SYNC_STATUS`×20）:
  - B（効果の有無に関係なく写しを確認）: 246／271／328。
  - B2（`IF a != "" || b != "" || c != ""`〜`ENDIF`で囲んだ版）: 240／272／329。
  - B3（`SIF`＋`RETURNF`ですぐ返す版）: 232／263／319。
- 0件でB・B2がAより数%遅く見えたため`IF`〜`ENDIF`が重いと疑ったが、ゲームを使わない計測（同じ起動内で100万回ずつ交互に呼ぶ）では、`IF`〜`ENDIF`と`SIF`の差は1回あたり約5〜6ns、A・B2・B3の書き方の差も3〜5nsだった。0件での数%の差は書き方ではなく**起動ごとのばらつき**（3%程度は起こりうる）と考えられる。エンジン側に直すべき処理はなかった（`IF`は条件を評価して飛び先の行へ移るだけ）。
- 10件・30件での改善（約12〜35%）はばらつきより十分大きい。0件ではどの版もAと数%以内で、実用上の差はない。
- ダミーの効果名には呼び出し先がないため、本物の効果の補正計算は含まない。本物の効果なら、その処理時間がA・B3の両方に同じだけ加わる。
- 追加・削除も`RESIZABLE`に移せば、`ADD_SPLIT`や`REPLACE`の文字列処理もなくなる（今回は未計測）。

### 見つかった既存の不具合（今回の変更とは別）

- `REF`／`REFBYNAME`命令が、固定長の配列に対しても「書式が間違っています」になる（`ArgumentBuilder.cs`の`SP_REF_ArgumentBuilder`）。このため`REF`の参照先ガードはフィクスチャで確認できていない。
- `SPLIT`の出力先にキャラ変数を書くと、常にキャラ0へ書き込まれる。元のEmuera 1824+v24でも同じ動き・同じコード（出力先の添字を捨てて`[0, 0, 0]`へ書く）で、現行ゲームの`SPLIT`175か所に出力先がキャラ変数のものはない（2026-10-28確認）。
- （訂正）当初「キャラ変数への`MATCH`が一致を数えない」と記録したが、確認用ERBで文字列を`=`で代入していた（引用符まで中身に入っていた）ためで、`'=`で代入すると元のEmuera・現行とも正しく数える。不具合ではない。
