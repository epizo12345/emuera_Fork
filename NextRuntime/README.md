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
