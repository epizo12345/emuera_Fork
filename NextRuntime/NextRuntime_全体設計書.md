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

完了はPhase 0AのSource Index、IndexAudit、SelfTest、0A-R1のLegacy境界に沿ったflag分類、0A-R2の関数ヘッダー境界と計測分離、0BのLegacy oracle差分・性能baselineである。Phase 1はまだ開始していない。

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

Phase 0A、0A-R1、0A-R2、0A-R3、0Bを完了とする。0A-R3では、Legacyの`{`単独行から`}`単独行までの行連結を安全側fallbackとして検出し、連結内部の`@`を通常の関数境界として扱わない。nested・異常終了・未閉鎖も安全側へ倒す。識別子delimiterには`\\`を含め、先頭のvertical tab/form feedはLegacy互換のため空白として飛ばさない。全角spaceは`SystemAllowFullSpace`依存のためCoreへ設定を持ち込まずfallbackを付ける。保持メモリはindex構築前baselineとindexだけを保持したforced-GC後の差として診断する。0Bでは実際のLegacy `ErbLoader` / `LogicalLineParser` / `LabelDictionary`をoracleとして9458 ERB・134652関数を照合し、safe 112854、fallback 21798、unexpected missing/extra/name/order/invalid-error mismatch 0を確認した。Legacy設定は`IgnoreCase=True`、`OrdinalIgnoreCase`、`SystemAllowFullSpace=True`で、重複関数名は3名称・9定義だった。正式mainへの統合やGitGudへのpushは別の明示的な作業である。

## 14. ロードマップ

```text
0A Source Index
 → 0B Legacy oracle differential / performance baseline
 → 1 function-level compiler
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

LegacyとNextは9458ファイル・134652関数で一致した。Nextのsafe functionは112854、fallback functionは21798。unexpected missing、extra、name、order、invalid/error mismatchは0である。fallback reasonはDeclarationDirective 6844、FunctionMetadata 6817、LineContinuation 164、OtherSemanticFallback 1425、Preprocessor 234、Rename 12705（重複計上）。全角space、vertical tab/form feed、BOM、invalid UTF-8、引用符付き`@`、行継続のSelfTestを維持・追加した。

重複はcase-insensitive groupingで3名称・9定義、最大4定義。Legacyの比較設定は`IgnoreCase=True`、`StringComparison=OrdinalIgnoreCase`、`SystemAllowFullSpace=True`である。これはNextがLegacy設定を採用したという意味ではなく、oracleの実設定を記録したものである。

性能は各5回の診断baselineで、Legacy actual ERB parse/loadの中央値は8790.342 ms、allocation中央値は6280117840 bytes。Next Source Index構築の中央値は1796.173 ms、allocation中央値は36947992 bytes。処理境界が異なるため、速度比・runtime高速化・起動高速化は主張しない。p95、最大、標準偏差とメモリ境界はレビューartifactに記録する。

0Bの差分ゲートは通過した。これはPhase 1開始の承認ではなく、Chatレビュー可能な基準点である。Phase 1は別の明示的依頼まで開始しない。

## 18. Phase 0B-R1 差分更新とcache無効化

R1では差分更新を実装せず、root相対path・length・last-write time・content hashをfile identityとする。変更ファイルは関数spanと行連結blockを再計算し、関数content hashの変更を起点に、確定したcall/reference dependencyの逆向き到達範囲を再評価する。ERH、Rename、Preprocessor、宣言directiveは安全側にfile-level invalidationとする。

画像・CSVなどの非ERB assetは独立namespaceでcacheし、ERB変更では無効化しない。cache headerにはengine version、index schema version、parser rule version、設定値、root identityを含め、変更時はfull rebuildする。一時manifestをLegacy oracle互換のFileOrder・function order・span・fallback理由で検証してからswapし、失敗時は前回の完全なindexを維持する。R1はこの設計と検証契約までで、差分cache、dependency graph、runtime統合、Phase 1は未着手である。

## 19. 用語集

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
