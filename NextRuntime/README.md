# Next Runtime

Next Runtimeは、既存ゲーム・セーブ・ERB互換を最優先にしつつ、Legacy Runtimeでは難しいstartup、ERB parse/compile、CALL-heavy execution、retained memory、warm startupの大幅な高速化・軽量化を実測で目指すための独立領域です。Legacyと同等の速度・メモリなら採用する意味はありません。

## Phase 0A

このPhaseでは新VMや既存Runtimeの置換を行わず、side-effect freeなERB Source Index、IndexAudit、SelfTestだけを実装します。Source IndexはUTF-8 BOM付きERBだけを受理し、offsetはBOM 3 byteを含むファイル先頭からの物理byte offsetです。本文全体、1行ごとのLegacy object、Legacy parser graphは保持しません。semanticを安全に推測できない構造はfallback理由として記録します。

`Emuera.Next.Core`はWindows UI、Graphics、Gamepad、save、game state、任意のfilesystem mutationへ直接依存しません。IndexAuditは指定ERB directoryをread-only recursive scanし、SelfTestは一時ディレクトリだけを書き換えます。

全体の目的・互換性境界・将来ロードマップは[Next Runtime全体設計書](NextRuntime_全体設計書.md)を参照してください。技術用語を避けた概要は[かんたん説明](NextRuntime_かんたん説明.md)です。

## Phase 0Bの結果

実ゲームfixtureを対象に、Legacyの実際の`ErbLoader` / `LogicalLineParser` / `LabelDictionary`をoracleとして、NextのSource IndexとJSONL manifestを照合しました。LegacyとNextはともに9458 ERB・134652関数で、safe 112854関数、fallback 21798関数でした。R2ではLegacy PPStateのdisabled rangeを診断し、BIT_SETTING.ERBの3件も含めてunexplained fallback differencesは0です。missing、extra、name、order、FileOrder、invalid/error mismatchもすべて0です。

fallbackは、DeclarationDirective 6844、FunctionMetadata 6817、LineContinuation 164、OtherSemanticFallback 1425、Preprocessor 234、Rename 12705（重複計上）です。実データの重複関数名は3名称・9定義、定義順差分0です。#PRI/#LATER/#ONLY/#SINGLEを含むevent dispatch semantics自体はNext VM未実装のため、priority完全一致ではなく`DEFERRED / LEGACY FALLBACK`です。全角space、vertical tab/form feed、BOM、invalid UTF-8、引用符付き`@`、PPState disabled rangeの境界はSelfTestと診断値で確認しています。

性能値は同じ処理の比較ではありません。Legacyは実ERB parse/load、Nextはread-only Source Index構築を各5回測定し、runtime speedupは主張しません。Phase 0Bは差分ゲートを通過しましたが、Phase 1はまだ開始していません。

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

0A Source Index foundation → 0B Legacy oracle differential + performance baseline → 1 function-level compiler prototype → 2 compact instruction VM → 3 compact expression/format IR → 4 bounded/evictable compiled cache → 5 disk compile cache/warm startup → 6 Next VariableStore prototype → 7 Host I/O/security sandbox → 8 full compatibility expansion

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
