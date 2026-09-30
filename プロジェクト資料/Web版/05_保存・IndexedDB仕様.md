# 保存・IndexedDB仕様

## Namespaceとデータ

現在のWeb Runtimeは`gameId=shin-era-tensei-p`を前提とします。IndexedDB database `emuera-web-saves-v1`の`files` storeは`[gameId, profileId, logicalFilename]`をkeyとし、revisionとbytesを保存します。同じoriginでもprofileまたはgame namespaceを混ぜません。

通常save、`global.sav`、SAVEGLOBAL/LOADGLOBALは元Runtimeのcodecを通ります。Web上のexportはcommit済みbytesをダウンロードし、importは一時データ取得・codec検証後に対象をcommitします。

## 排他・commit・ACK

```text
Runtime mutation queue → JavaScript Web Lock → IndexedDB transaction/revision check
→ transaction complete → operation-specific ACK → Runtime再開
```

同一game/profileはWeb Lockで排他します。commitは最新revisionと比較し、競合を検出します。成功ACKはtransaction完了後にoperation IDを指定して返し、Runtimeは一致するACKだけを受理します。queue drain、失敗通知、重複・古いACK拒否を省略しません。

ブラウザ再起動後も同じorigin/profileのIndexedDBデータを読みます。ブラウザデータ消去、origin変更、保存領域不足、別profileでは同じ保存を保証できません。

## タイトル復帰とQUIT

タイトル復帰は入力待ちかつ未完了の保存がない安全境界でのみ行います。macro、message-skip、timer、入力状態、画面と画像を片付けた後、同じ`BrowserRuntimeSession`/`Process`をタイトル開始位置へ戻します。AOTで旧Processの大きな実行領域を残したまま作り直すことを避けるためです。

NativeのQUITはウィンドウを閉じます。ブラウザには閉じる権限がないため、WebではRuntime sessionの正常終了へ対応付けます。
