# Next Runtime

Next Runtimeは、既存ゲーム・セーブ・ERB互換を最優先にしつつ、Legacy Runtimeでは難しいstartup、ERB parse/compile、CALL-heavy execution、retained memory、warm startupの大幅な高速化・軽量化を実測で目指すための独立領域です。Legacyと同等の速度・メモリなら採用する意味はありません。

## Phase 0A

このPhaseでは新VMや既存Runtimeの置換を行わず、side-effect freeなERB Source Index、IndexAudit、SelfTestだけを実装します。Source IndexはUTF-8 BOM付きERBだけを受理し、offsetはBOM 3 byteを含むファイル先頭からの物理byte offsetです。本文全体、1行ごとのLegacy object、Legacy parser graphは保持しません。semanticを安全に推測できない構造はfallback理由として記録します。

`Emuera.Next.Core`はWindows UI、Graphics、Gamepad、save、game state、任意のfilesystem mutationへ直接依存しません。IndexAuditは指定ERB directoryをread-only recursive scanし、SelfTestは一時ディレクトリだけを書き換えます。

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

分類はLegacy `ErbLoader` / parserの境界に合わせます。`[IF_DEBUG]`、`[IF_NDEBUG]`、`[IF ...]`、`[ELSEIF]`、`[ELSE]`、`[ENDIF]`、`[SKIPSTART]`、`[SKIPEND]`はPreprocessorです。`[[...]]`はRenameでありPreprocessorではありません。`#DIM` / `#DIMS`はDeclarationDirective、`#FUNCTION` / `#FUNCTIONS` / `#LOCALSIZE` / `#LOCALSSIZE` / `#PRI` / `#LATER` / `#ONLY` / `#SINGLE`はFunctionMetadataとして別に記録します。未知のSharp directiveや特殊labelはOtherSemanticFallbackです。

Preprocessorは後半の有効sourceを変えるLegacy PPStateを持つためfile-level fallbackです。Auditのfallback file countはsemantic fallback flagを1つ以上持つファイル、fallback function countはそのflagを持つ関数です。Preprocessorを含むファイルでは全関数へPreprocessor flagを伝播させます。DeclarationDirective、FunctionMetadata、Rename、LineContinuationなどは検出位置の関数へ記録します。

ERBはEF BB BFのUTF-8 BOM付きだけを受理し、strict UTF-8 validationを全物理行へ行います。構文markerはraw UTF-8 bytesで判定し、UTF-16 stringを作るのはfunction header名だけです。`SourceSpan`はBOM 3 byteを含むphysical byte offsetと1-based line rangeを保持し、source本文は保持しません。`FunctionIndex`はreadonly record structで、file identityはSourceFileIndexに1回だけ保持し、fallback reasonはflagsからAudit時に生成します。

IndexAuditの`indexBuild*`は`ErbSourceIndexer.IndexDirectory`の直前から直後までだけを測ります。後続のflatten、sort、集計、JSON生成、diagnostic full GCは`auditPostProcess*`へ分離します。`retainedIndexManagedBytesEstimate`はindexを保持したままaudit用full GCを行った後のmanaged memoryであり、production runtimeのGC操作ではありません。旧Phase 0Aのallocation値は後処理を含むため、新値との比較はNOT DIRECTLY COMPARABLEです。

## Windows distribution invariant

最終Windows版の正式配布は、現行Emueraと同じく `PublishSingleFile=true`、`SelfContained=false` のframework-dependent single-file publishを基本とします。内部をWindows Host、Next Core、Compiler、VMなどへ分割しても、ユーザーがゲームフォルダへ手動配置するRuntime本体は原則 `Emuera.exe` 1ファイルです。publish時に必要なruntime componentをsingle EXEへまとめ、compile cacheなどの再生成可能cacheを手動配置Runtime componentとは扱いません。
