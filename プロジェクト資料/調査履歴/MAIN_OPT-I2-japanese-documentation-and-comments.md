# MAIN_OPT-I2 — M3A / M4A 日本語資料・コードコメント最終整備

## Initial Git State

- branch: `main`
- 開始HEAD: `7fbac4be214cd5ae61a5c4d653033ee79028c489`
- 開始origin/main: `a7e3962795161b53596e8daaed9bbadab671b909`
- 既存untracked: `MAIN_OPT-I1-main-integration.md`

## Updated Code Comments

- `Runtime/Script/Parser/WordCollection.cs` と `Runtime/Script/Statements/LogicalLine.cs` に `[Emuera改修:MEM-M3A]` を追加した。
  - SET左辺を初回parseまでexact-size `Word[]` snapshotで保持すること
  - `Word`の参照同一性・順序を保つこと
  - 一時復元後にone-shot slotを解放すること
- `Runtime/Script/Statements/Variable/VariableTerm.cs` と `Runtime/Script/Statements/Variable/VariableToken.cs` に `[Emuera改修:MEM-M4A]` を追加した。
  - 定数添字の長寿命`FixedVariableTerm`がexact-size transporterを共有すること
  - 欠けた添字の読取りは論理0であること
  - 欠けた添字への書込み時だけcopy-on-writeすること

## Runtime Code Semantic Diff Audit

C#差分はコメント行だけであることを確認した。式、条件、配列処理、constructor、method body、field、visibility、型、API、performance metrics、build propertyの変更はない。

## Updated Documentation

- `更新履歴.md`へ2026-09-15の採用記録を追加した。
- `プロジェクト資料/修正履歴/#今回の修正説明_2026-09-15.md`を新設した。
- `01_仕様書.md`、`05_採用済み変更ファイル一覧.md`、`06_コード案内.md`、`02_引き継ぎ書.md`を更新した。
- optimization worktreeで確定済みのM3A/M3A1/I0調査記録を内容を変えずmain側へ複写した。
- I1 main integration記録を追跡対象にした。

`README.md`は配布利用者向けの一般説明であり、M3A/M4Aの内部実装を追加する必要がないため変更していない。

## Imported Authority Reports

- `MAIN_OPT-M3A-frozen-set-snapshot-prototype.md`: 原文のまま複写
- `MAIN_OPT-M3A1-adoption-and-diagnostic-cleanup.md`: 原文のまま複写
- `MAIN_OPT-I0-adopted-changes-cleanup.md`: cleanup authorityとして原文のまま複写
- `MAIN_OPT-I1-main-integration.md`: 既存untracked reportを追跡対象へ追加

M3B/M3C/S1/R1のartifact、raw trace、benchmark log、cache file、fixture_450は取り込んでいない。

## 数値の扱い

- M3Aの23.90 MiBは直接対象型のshallow保持量であり、ゲーム全体のメモリ削減量ではない。
- M4Aの約6.225 MiB retained見積りと約18.806 MiB startup allocation見積りは対象が異なるため合算しない。
- `System.Int64[]`約7.69 MiBはM4A単独の効果ではなく、補助的なheap観測値として扱う。

## Build

- normal Release / `PERFORMANCE_METRICS` Release: 30 warnings / 0 errors

## Focused Tests

- M3A focused snapshot tests: PASS
- M4A shared exact transporter focused tests: PASS

## N100

- `ExpandedInput / InputDispatch = 201 / 201`
- `ErbRunCount = 1000`
- `RandomCallCount = 5550`
- `RandomTraceHash = F1A61293AEF35FF7`
- `StateSha256 = 6357F729EBEEC133F94E48B1BBCF38BFF3C28D59CC0EEA697AAD222879DD07B8`
- `DisplaySha256 = 83D1AC4E6EB630CBFC27B3453CCAE59B51FFE07B38ACB0457AFC8B9547E852B7`
- `SaveTo / save401 / failure = 49 / 49 / 0`

## Canonical Save

`fixture/Data/sav/save219.sav` SHA-256は`6C26E53CE95D3C5388E9E7A1EEFF512E57645368E8744EE14D9298D7F3E4814B`を維持した。

## I2 Commit

対象ファイルだけを明示的にstageし、commit messageは`M3A・M4Aの日本語資料とコードコメントを整備`とする。

## Final Git State

I2 commit後、local `main`はM4A、M3A、I2の3 commit分だけ`origin/main`より先行する。既存M3A/M4A commitは書き換えない。

## Push State

push、正式配布EXE更新、publish、tag、version bumpは行わない。
