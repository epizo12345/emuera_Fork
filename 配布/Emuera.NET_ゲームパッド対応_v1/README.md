# Emuera.NET ゲームパッド対応 v1

作成日: 2026-08-16

このフォルダの`Emuera.exe`は、ゲームパッド対応v1を含むWindows x64向けRelease単一EXEです。

## 動作条件

- Windows 10 Version 2004以降 / Windows 11
- x64
- .NET 10 Desktop Runtime

framework-dependent単一ファイル版のため、Emuera本体のDLLを別途コピーする必要はありません。.NET 10 Desktop Runtimeは必要です。

## 導入

使用中のゲームフォルダと既存`Emuera.exe`をバックアップしてから、この`Emuera.exe`をゲームフォルダへコピーしてください。ゲーム側のERB、CSV、セーブデータ、設定ファイルは変更しません。

## ゲームパッド対応

- XInput #0～#3
- WinMM joystick API
- Raw Input / HIDフォールバック
- 通常Console、HTML UI、HTML Island、INPUTMOUSEKEY
- ホットプラグ、Alt+Tab復帰
- Raw Inputでも十字キーと左スティックを別々に扱う
- 複数パッド接続時も、選択中デバイス単位でボタン配列を判定

### 標準操作

| 操作 | 機能 |
|---|---|
| × / A | 決定 |
| ○ / B | 戻る・キャンセル |
| △ / Y | 高速送り（ESC / マウス右クリック相当） |
| □ / X | 現在未使用 |
| D-pad | UI選択移動 |
| Left Stick | ゲーム操作 / UI移動 |
| LB / RB | ページ移動（対象ページがない場合は従来のログ操作） |
| Start / OPTIONS | Enter相当 |

△ / Yは、会話や戦闘中の表示・待機を高速で処理し、次の入力が必要な地点まで進みます。`INPUTMOUSEKEY`待機中も既存のマウス右クリック相当として扱います。

ゲームパッドのVirtual Focusにより、通常Consoleの選択肢、HTML、HTML Island、INPUTMOUSEKEY、確認ポップアップを操作できます。ON/OFF切替後や別画面から戻った場合のFocus復帰、複数行に分割された同一論理ボタンの統合にも対応しています。

PS4系の既定は×が決定、○がキャンセルです。XInputはXbox配列、WinMM / Raw Input(HID)は接続デバイスを確認してXbox系またはPS4系の配列を選択します。

## 診断

```powershell
.\Emuera.exe --GamepadDebug
```

EXEと同じフォルダへ`gamepad-debug.log`を出力します。

直接入力プロファイルは次のように指定できます。

```powershell
.\Emuera.exe --GamepadDirectInput Auto
```

`Auto`、`Disabled`、`Wasd`、`Numpad8462`、`ArrowKeys`を指定できます。

通常は自動判定です。非XInputパッドの配列を明示する必要がある場合だけ、次を指定できます。

```powershell
.\Emuera.exe --GamepadLayout Auto
.\Emuera.exe --GamepadLayout Xbox
.\Emuera.exe --GamepadLayout PlayStationWinMM
```

環境変数`EMUERA_GAMEPAD_LAYOUT`でも指定できます。XInputは常にXbox配列です。

## ビルド情報

- Configuration: Release
- Runtime: win-x64
- Self-contained: false
- Single-file: true
- Emuera.exe: 24,811,242 bytes
- SHA-256: `C319018050A07DD029661708B0636AE8527BB2E2D030699ECB7C2F432441E133`

詳細仕様はリポジトリの`プロジェクト資料/07_ゲームパッド対応_v1_仕様書.md`、変更説明は`今回の修正説明_2026-08-11_ゲームパッド対応_v1.md`を参照してください。
