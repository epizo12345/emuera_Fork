# 起動・変数参照・文字列整理・整数1D読み捨ての回帰試験

既存ConfigRegressionTestsの85件とは別の試験host。通常Release・性能診断無効で本体を参照する。AssemblyNameは本体の既存のinternal参照許可を使うため、既存hostと同じ。出力はproject名で分離する。

```powershell
dotnet run --project ".\Emuera以外(開発補助ツールなど)\ConfigRegressionTests\StartupStudy\StartupStudy.csproj" -c Release -- A ".\artifacts\startup-test-fixture"
dotnet run --project ".\Emuera以外(開発補助ツールなど)\ConfigRegressionTests\StartupStudy\StartupStudy.csproj" -c Release -- C
dotnet run --project ".\Emuera以外(開発補助ツールなど)\ConfigRegressionTests\StartupStudy\StartupStudy.csproj" -c Release -- D
```

- A：81検査。内容・文字コード・BOM警告80条件と一時割当の構造検査1件。テスト内で作る入力には、BOMなし・破損UTF-8等の意図的な文字コード条件も含む。
- C：66検査。全Restructure後に定数getterを呼ぶ順序、動的要素、副作用、例外時の部分状態、文字列、残る要素、割当を確認。
- D：111検査。旧referenceと例外型・文言・stream位置・次の値を比較。巨大な正常配列は確保しない。構造上拒否される長さとRAM不足由来のOOMは区別する。

通常single-file EXEによるBの通常・逆順・並列解析経路・実Lazy、ARG/ARGS互換、C実Lazyは次で実行する。ゲームやセーブは使わず、小さな独自ERBだけを新しいOutputRootへコピーする。実行前にEXEも固定する。

```powershell
& ".\Emuera以外(開発補助ツールなど)\ConfigRegressionTests\StartupStudy\Run-Fixtures.ps1" -Exe "検証するsingle-fileの絶対パス" -OutputRoot ".\artifacts\startup-native-test-新しいrun名"
```

実行用ERBはUTF-8 BOM・タブ。実Lazyはfiles=1/fallback=0を検査する。「並列」は該当解析経路を使うことを意味し、特定worker数の同時競合を保証する名称ではない。

試験の期待値は2026-10-05の承認済みA81/C66/D111およびB/C fixtureを維持した。性能測定専用modeは移さず、旧reader/Preload referenceは試験だけに置く。本体に診断hook・旧アルゴリズムの重複を追加していない。
