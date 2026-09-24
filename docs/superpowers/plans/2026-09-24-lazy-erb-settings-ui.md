# LazyERB設定UIとユーザー設定分離 — Implementation Plan

> **進め方:** 各項目を順番に実施し、対象の実装を変える前に記載した回帰テストを追加・実行する。

**Goal:** ゲーム側推奨設定とプレイヤー上書き設定を分離し、安全なLazyERBフォルダを既存設定画面から編集できるようにする。

**Architecture:** `JSONConfig`がgame/userの元JSON nodeを保持し、user LazyERB blockの有無で実効値を選ぶ。`LazyErbPolicy`に実効設定を渡し、同じ安全検査を設定読込・選択・loader対象判定で使う。既存WinFormsタブへページを加え、フォルダ選択だけ別TreeView dialogに分ける。

**Tech Stack:** .NET 10 Windows Forms、`System.Text.Json.Nodes`、既存WinFormsリソース、依存なしのC# console regression harness。

**Spec:** `docs/superpowers/specs/2026-09-24-lazy-erb-settings-ui-design.md`

## Global Constraints

- `setting.json`のLazyERBキーがない場合だけ`有効=false`、`フォルダ=[]`を追加する。
- user LazyERB blockの存在で完全上書きを判定し、game/user間でLazy設定をマージしない。
- malformed existing JSON blockは既定値で置換せず、元ファイルを保持する。
- 通常編集・保存ではgame/user JSONの未知propertyを保持し、reset時だけuser LazyERB objectを全削除する。
- reparse pointを選択・Lazy対象にせず、既存path制約、Debug / Analysis eager、危険ERB eager fallbackを維持する。
- 設定変更は次回起動から適用し、無関係なruntimeやERBを変更しない。
- Git push、配布EXE更新、依存framework追加を行わない。

## Review Focus

1. user blockが`null`またはscalarならgameへfallbackせず、元ファイルを変えず設定エラーになる。
2. game blockと旧`LazyErb`が両方ある場合は現行migration規則の優先順位を維持する。
3. user block内に未知propertyがあっても、通常変更で残り、推奨設定resetでだけ削除される。
4. ERB内のjunction/reparse pointが既存でも新規作成されても、TreeViewとruntime policyの双方で拒否される。
5. folder dialogをキャンセルしてから設定画面を保存しても、以前の対象リストが変化しない。

---

### Task 1: JSON設定の回帰テストを先に追加

**Files:**
- Create: `Emuera以外(開発補助ツールなど)/ConfigRegressionTests/ConfigRegressionTests.csproj`
- Create: `Emuera以外(開発補助ツールなど)/ConfigRegressionTests/Program.cs`
- Modify: `Emuera.csproj`（テストソースを本体compile対象から除外）

**Interfaces:**
- Test invokes existing `JSONConfig.Load` / `Save` and sets their private config path fields through reflection, so test setup adds no production seam.
- Assertions use only BCL: throw `InvalidOperationException` when an expected value differs.

- [ ] **Step 1: Write the package-free test project and failing assertions** for: absent game block yields disabled+empty; new user file has no LazyERB key; user block false is distinct from missing; user/game unknown top-level and nested values survive save; malformed present blocks leave source bytes unchanged. Use reflection only to set existing private paths and reach not-yet-public behavior.

  Create a test project targeting `net10.0-windows10.0.19041.0` with `OutputType=Exe` and no package references. Add `InternalsVisibleTo Include="Emuera.ConfigRegressionTests"` to the main project; give the harness that exact `AssemblyName`. The harness uses the real `JSONConfig.Load()` and `Save()` against isolated temporary `setting.json` and `setting_user.json` files. It sets only `_gameConfigFilePath` and `_userConfigFilePath` using reflection, writes UTF-8-BOM inputs to avoid unrelated encoding migration, and restores the original static fields in `finally`.

  Add this first assertion through the real load path (the empty `{}` game object currently receives enabled/recommended defaults, so this must be RED). Also inspect the serialized `setting.json`: the disabled/empty block must actually be written there while an unrelated top-level property survives. A second case compares the original bytes for a complete valid block and requires that ordinary load leave them unchanged when no migration is needed.

  ```csharp
  LoadFixture("{}", "{}");
  Equal(false, JSONConfig.Game.LazyErb.Enabled, "missing game block is disabled");
  Equal(0, JSONConfig.Game.LazyErb.Directories.Length, "missing game block has no folders");
  True(!File.ReadAllText(userPath).Contains("起動時に読み込まないERBフォルダ"), "new user config has no override");
  ```

  Add separate cases that load user `{"起動時に読み込まないERBフォルダ":{"有効":false,"フォルダ":[]}}` and assert the effective value is false; save user/game objects containing unknown top-level and nested properties and assert those JSON nodes remain; then assert malformed `null`, scalar, and invalid-known-field blocks throw before the corresponding original file bytes change. Use a case runner that prints every case and returns nonzero if any assertion fails.

- [ ] **Step 2: Exclude the test source folder from the main SDK project's default compile glob.** Add `<Compile Remove="Emuera以外(開発補助ツールなど)\ConfigRegressionTests\**\*.cs" />` next to the existing helper-tool exclusion. Keep test helpers in the harness project and use only BCL APIs.
- [ ] **Step 3: Run each harness case and confirm RED.** Run `dotnet run --project "Emuera以外(開発補助ツールなど)/ConfigRegressionTests/ConfigRegressionTests.csproj" -c Release`. The absent-game-block assertion must fail against current active defaults; user unknown-property assertion must fail against current DTO rewrite. The harness must report all cases before returning nonzero so each expected failure is visible. Fix test setup errors until only behavior assertions fail.

### Task 2: Game/user JSON ownership, migration, and unknown retention

**Files:**
- Modify: `Runtime/Config/JSON/JSONConfig.cs`
- Modify: `Runtime/Config/JSON/JSONConfigData.cs`
- Test: `Emuera以外(開発補助ツールなど)/ConfigRegressionTests/Program.cs`

**Interfaces:**
- `JSONConfig.EffectiveLazyErb` returns the complete user object when the Japanese user key exists; otherwise it returns `Game.LazyErb`.
- `JSONConfig.SetUserLazyErbOverride(enabled, directories)` stages a complete user override; `ResetUserLazyErbOverride()` removes the user key on the next save.

- [ ] **Step 1: Add and run the next failing tests** for precedence: game `[A,B,C]` + user `[A]` resolves to `[A]`; user `false` stays false; absent user block returns game values; reset deletes the entire user LazyERB object.
- [ ] **Step 2: Make game default creation explicit.** Set the DTO's missing-block default to disabled/empty. In migration, absence of both the Japanese and legacy keys creates `{ "有効": false, "フォルダ": [] }`; legacy `LazyErb:null` retains the prior missing/default migration behavior and removes the old key. Preserve valid current-block precedence, existing legacy conversion, and unknown legacy/current child properties.
- [ ] **Step 3: Validate before migration writes.** Validate both raw JSON objects and LazyERB known fields before writing either file. Reject a current Japanese block `null`/non-object, a non-null legacy block that is non-object, explicit-null known field, wrong `有効` boolean or `フォルダ` string-array type. Treat only legacy block `null` as the prior compatibility default. Continue filling only omitted known child fields using the established compatibility defaults. If validation fails, `Load()` throws and the malformed source file remains byte-for-byte unchanged.
- [ ] **Step 4: Preserve raw game and user `JsonObject`s.** Keep `_gameJson` and add `_userJson`; save known DTO properties into cloned raw objects rather than replacing them. For both LazyERB objects, update only known child keys so nested unknown properties survive. `SetUserLazyErbOverride` stores a complete override in memory; `ResetUserLazyErbOverride` removes the key in memory. `Save()` writes the resulting user raw object; it does not serialize an absent override.
- [ ] **Step 5: Run the focused regression harness.** All cases for absent defaults, existing values, legacy conversion, precedence, `false`, reset, malformed-file byte preservation, and unknown fields must pass. Add cases for legacy `null`/bad fields and a current block plus legacy block to lock the existing new-key priority.

### Task 3: Apply reparse-safe policy and test path constraints

**Files:**
- Modify: `Runtime/Utils/LazyErbPolicy.cs`
- Test: `Emuera以外(開発補助ツールなど)/ConfigRegressionTests/Program.cs`

**Interfaces:**
- Add `LazyErbPolicy.IsSafeDirectoryPath(string dataRoot, string erbRoot, string configuredDirectory)` shared by config policy and the folder dialog. It accepts only a non-root DataDir-relative directory under the active ERB root and rejects traversal, rooted input, existing non-directory targets, and any reparse-point component.
- `IsEnabledForCurrentMode` retains all current runtime gates and changes only its settings source to `JSONConfig.EffectiveLazyErb`.

- [ ] **Step 1: Add failing path assertions** for accepted `ERB/RPG` and `ERB/RPG/依頼`; reject `ERB`, `../`, `ERB/../foo`, drive-rooted and slash-rooted paths, a file path, sibling-prefix escape, and a reparse-point directory under the ERB root. Create all paths under a unique temporary data root; try `Directory.CreateSymbolicLink`, then a Windows directory junction if symlink privilege is unavailable. If Windows permissions prevent both fixtures, report `SKIP/UNAVAILABLE` for that case; do not count the unavailable fixture as a product-code failure.
- [ ] **Step 2: Run the harness and verify each available rejection fails before implementation.** The reparse fixture may be skipped only when creation is unavailable; regardless, production code must unconditionally reject `FileAttributes.ReparsePoint` for the ERB root and every existing component through a selected/configured directory or candidate file.
- [ ] **Step 3: Implement lexical containment and component reparse checks** in the shared helper. Reject rooted input and any `..` path segment before calling `Path.GetFullPath`; require the normalized result to be a strict descendant of `erbRoot`; for each existing directory component from `erbRoot` (including the root) to the configured candidate, reject `FileAttributes.ReparsePoint` and non-directory components. A missing configured directory is safe but cannot match an actual file. Make `GetConfiguredDirectories` skip unsafe entries without rewriting JSON. In `IsConfiguredTargetPath`, separately reject reparse-point components from the ERB root through the actual candidate ERB file (the leaf may be a file, unlike `IsSafeDirectoryPath` input) before checking containment in an allowed configured directory.
- [ ] **Step 4: Run all path tests plus existing JSON tests.** Also assert Debug/Analysis and existing LazyERB prerequisite gates still return eager mode.

### Task 4: Add a dynamic folder picker dialog

**Files:**
- Create: `UI/Framework/Forms/LazyErbDirectoryDialog.cs`
- Test: `Emuera以外(開発補助ツールなど)/ConfigRegressionTests/Program.cs`

**Interfaces:**
- Constructor receives `Program.ExeDir`, the current `Program.ErbDir`, and selected `ERB/...` directory list.
- Dialog exposes the normalized selected relative paths only after `DialogResult.OK`; Cancel returns no changed list.

- [ ] **Step 1: Add a failing tree-model/path test** that confirms selecting `ERB/RPG` removes selected `ERB/RPG/依頼`, preserves unrelated selected folders, and output uses normalized forward-slash `ERB/...` paths. Expose the minimal normalization helper as `internal static` for the friend test assembly; do not add a general tree framework.
- [ ] **Step 2: Run the model test and confirm it fails before implementation.**
- [ ] **Step 3: Build the dialog with a checked `TreeView`, OK and Cancel buttons, and the short Japanese explanation.** Add only directories below `Program.ErbDir`; render the ERB root as an unchecked, nonselectable parent. Enumerate child directories dynamically, do not descend into reparse points, and revalidate every checked path at OK time with `LazyErbPolicy.IsSafeDirectoryPath`. Cancel leaves the caller's original list untouched.
- [ ] **Step 4: Run path/tree tests.** Manually open the dialog against a fresh fixture and verify files are absent, multiple folders can be checked, root and junction branches cannot be selected, and Cancel preserves the input list.

### Task 5: Integrate the 「起動」 settings tab

**Files:**
- Modify: `UI/Framework/Forms/ConfigDialog.cs`
- Modify: `UI/Framework/FormLocalization.resx`
- Modify: `UI/Framework/FormLocalization.ja-jp.resx`
- Modify: `UI/Framework/FormLocalization.zh-hans.resx`
- Modify: `UI/Framework/FormLocalization.Designer.cs`
- Test: `Emuera以外(開発補助ツールなど)/ConfigRegressionTests/Program.cs`

**Interfaces:**
- The page edits local draft fields only. Existing Save and Save+Restart commit them through `JSONConfig`; Cancel discards them.
- Selecting local settings starts from `EffectiveLazyErb`. Selecting recommended settings or pressing reset stages removal of the user key.

- [ ] **Step 1: Add failing persistence assertions** for three transitions: no setter/no save preserves absent override (Cancel equivalent); staging then `Save()` persists a complete local override; staging reset then `Save()` removes the full LazyERB object including its nested unknown fields.
- [ ] Add an assertion that a saved override/reset updates the persisted UI selection but leaves the current process's `EffectiveLazyErb` unchanged until `Load()` on the next startup.
- [ ] **Step 2: Run the tests and confirm the expected persistence assertions fail.**
- [ ] **Step 3: Add the tab, radio options, lazy-enable checkbox, folder-count/selection button, reset button, explanatory text, and restart note.** Reuse existing WinForms controls/layout conventions in `ConfigDialog`; add stable keys to the base, Japanese, and Simplified Chinese `FormLocalization` resources and corresponding generated accessors.
- [ ] **Step 4: Read and stage UI state only from the Save path.** On local selection, copy the currently effective enabled/list values into draft controls. On reset, display the game values and mark the user key for removal. Do not change current loader state.
- [ ] **Step 5: Wire draft commit at the existing `SaveConfig()` call used by both Save buttons.** No event handler writes JSON or mutates loader state; closing with Cancel must leave staged state unused. Verify selecting local initializes from the effective value once, recommended mode clears the override only on save, and folder selection cancellation leaves the draft paths unchanged.
- [ ] **Step 6: Run the regression harness and Release build.** Manually verify Save, Save+Restart, Cancel, recommended/local switching, folder count, and the displayed restart note.

### Task 6: Final regression and scope review

**Files:**
- Review all changed files; no additional files unless a failing test requires a scoped fix.

- [ ] Run `dotnet run --project "Emuera以外(開発補助ツールなど)/ConfigRegressionTests/ConfigRegressionTests.csproj" -c Release`.
- [ ] Run `dotnet build .\Emuera.csproj -c Release -v:minimal`.
- [ ] Run `git diff --check`, inspect `git status --short` (including untracked test files), and review the final diff.
- [ ] Confirm only configuration, path-policy, UI/resource, and regression-test files changed; confirm no ERB/data, publication EXE, or unrelated runtime paths changed.
- [ ] Report build/test results and any manual UI check that could not be completed. Do not push.

### TreeView実機確認後の確定仕様

- 親をONにすると、すべての子孫をON表示する。親自身をOFFにすると、子孫もすべてOFFにする。
- 親ON状態で子だけをOFFにした場合は、親をOFFにして他の選択済み兄弟は維持する。
- 子を個別にすべてONにしても、親を自動ONへ昇格させない。
- 親がONなら保存するのは親パスだけ。親がOFFなら選択された子パスだけを保存し、子パスの重複は除く。
- checkbox上のnative `WM_LBUTTONDBLCLK`だけを抑止する。ノード名上のdouble-clickと展開・折りたたみは維持する。

## Execution Result

- JSON/path/UI regression harness: `37/37` passed, failures 0 and skips 0. The reparse fixture was created and rejection passed. Coverage includes persisted `setting.json` defaults, unknown-property retention, unchanged valid-block load, startup-tab construction, all three resource cultures, multiple folder selections, canonical `ERB/...` output, parent/child check synchronization, checkbox double-click hit testing, folder-tree reparse exclusion, and candidate-file reparse rejection.
- Normal Release build: passed with `0` warnings and `0` errors.
- `git diff --check`: passed after the final documentation and source-comment edits. ERB/game data and the official distribution EXE were unchanged; the manual-test publish remains a temporary artifact.
- User manually confirmed the settings and folder-selection behavior in the real game, including checkbox rapid double-click handling. The test EXE was not treated as a formal distribution.
- Git stage, commit, and push were not performed.
