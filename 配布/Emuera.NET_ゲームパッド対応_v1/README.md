# Emuera.NET ゲームパッド対応 v1

作成日: 2026-08-11

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
- 十字キー・左スティック移動
- ×決定、○semantic CANCEL
- L1/R1は、`PAGE.0`型または「前のページ」「次のページ」型のPage UIでは前/次ページ、それ以外では従来のログ操作。Directional型のページ端では反対方向を消費して何もしません。OPTIONS/Start
- ホットプラグ、Alt+Tab復帰
- Raw Inputでも十字キーと左スティックを別々に扱う
- 複数パッド接続時も、選択中デバイス単位でボタン配列を判定
- ON/OFF切替後の同一画面再描画で、決定直前のフォーカスを安全に復元
- 通常Consoleの説明文・装飾文をFocus Targetから除外し、Input=0の実ボタンは保持
- 複数行に分割された同一論理ボタンのクリック可能断片を1Targetへ統合し、遠い同番号ボタンは分離保持
- HTML確認ポップアップの背面Cancel overlayをDirectional Focusから除外し、前面の0/1選択肢を初期Focus・上下移動対象にする
- modalでない巨大HTML Buttonと独立したHTML Islandは自動除外しない

PS4系の既定は×が決定、○がキャンセルです。

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
- Emuera.exe: 24,741,610 bytes
- SHA-256: `65C0AD524F627655CE8E3CFDAE99BE6AEA2EC3067916890F525E72B42F13970C`

詳細仕様はリポジトリの`プロジェクト資料/07_ゲームパッド対応_v1_仕様書.md`、変更説明は`今回の修正説明_2026-08-11_ゲームパッド対応_v1.md`を参照してください。
