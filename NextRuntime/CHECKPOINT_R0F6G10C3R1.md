# R0-F6G10C3R1 checkpoint

- C3R1 automated diagnostic gate: PASS
- Source-driven post-H path through HTML island clear: PASS
- Initial manual GUI round-trip was Legacy mode because the validation launcher omitted `--Runtime CompactStrict`.
- Actual CompactStrict manual GUI validation: `FAIL_WITH_KNOWN_COMPATIBILITY_GAPS`
- Correct CompactStrict launch: `Emuera.exe --Runtime CompactStrict`

Known gaps:

- Return to title: black screen; title UI was not restored.
- Battle entry: `BATTLE_MAIN` admission stops at a `SemanticBarrier` on `BATTLE.ERB` line 465 (grouped string-array assignment).
- ITEM/SKILL path: `INPUT_CHARA_LIST` compilation does not support runtime/private `#DIM` and `#DIMS` metadata.
- Severe first-demand latency remains on the H to encounter/battle path.

- Macro performance: not validated
- Long-play compatibility: not validated
- Whole-ERB compatibility: not validated
- Production or whole-product superiority: not claimed

The CompactStrict GUI result is a user manual validation, not Codex GUI automation.
