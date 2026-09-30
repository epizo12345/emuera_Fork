# Web版の概要

## 目的と対象

Emuera Webは、Emueraのゲームデータをブラウザ上で実行するWebAssembly版Runtimeです。Windows版の代替をすべてのゲームで保証するものではなく、ShinEraTenseiPを主要な互換性確認対象として段階的に検証しています。

Windows版はWindows上でEmuera.NETがゲームデータを読むデスクトップアプリです。Web版はHTML、JavaScript、WebAssemblyからなるRuntimeをブラウザが実行し、ゲームの処理をブラウザ内で進めます。

```text
Windows: ゲームデータ + Emuera.exe → Windowsアプリ
Web:     ゲームデータ → Emuera Web Packager → Web package + Web Runtime → ブラウザ
```

## ゲームデータ

通常、ERB / ERH / CSV / 画像をWeb専用に書き換える必要はありません。Packagerは利用者が選択したローカルゲームデータの現在のbytesを読み、検証済みWeb packageへまとめます。GitGud等から取得したデータも、利用者がローカルで修正したデータも入力にできます。特定の公式配布SHAとの一致は要求しません。SHA-256は、選択した入力と生成物の一致確認に使います。

## 現行版と確認状況

現在の採用候補はWeb Runtime UX16-07、Emuera Web Packager 1.0.1です。最新版は[検証済み機能](08_検証済み機能.md)と[既知の制限](09_既知の制限.md)を確認してください。「全Emueraゲーム完全互換」を意味しません。

詳細は[アーキテクチャ](02_Web版アーキテクチャ.md)、[パッケージ仕様](03_ゲームデータパッケージ仕様.md)、[保存仕様](05_保存・IndexedDB仕様.md)を参照してください。
