# GETKEYの保守試験と確認範囲

通常の文字入力欄でも押下状態を参照できるよう、Webはゲーム入力の配送とは別に状態を保持する。左右Alt/Shift/Ctrl、マウスボタンを区別し、GETKEYの照会では消費しない。ゲーム入力欄のDOM置換でBODYへフォーカスが移った場合は保持し、window blur、hidden、他編集欄、IME開始、detachでは解除する。古いrepeatイベントだけで押下状態を復活させない。

同じ共有ソースのNative GETKEYはGetKeyStateの符号から1/0を返す。下位bitは「前回照会以降に押された」という記録ではない。GETKEYTRIGGEREDはstatic keytoggleと下位bitに依存する別契約であり、Webのlowbit/toggle互換性は未対応・未確認。今回のGETKEY試験をその証拠に使わない。

## ユーザー受入

AOT02の61709環境でプラグイン・特殊弾のAlt＋クリックを確認済み。63475の専用fixtureで、押下中QID=1のNCREPEAT=1,1、解放後QID=2の0,0を画像で確認。日本語入力中の無変換も、普段どおりカタカナ／半角へ切り替わることを確認済み。

この環境での操作確認であり、画像に写っていないcode/key/keyCodeの実値は未記録。無変換の推測リマップは追加していない。左右修飾の全組合せやタブ復帰は自動fixtureの確認範囲であり、今回の実ゲームユーザー受入へ広げない。全ブラウザ・全IME・itchを保証しない。OS/ブラウザがページへ渡さない予約キー、サイドボタンのBack/Forward競合は残る。

## hostとJS

既存のSDK/NuGet・通常ビルド手順を使い、Release/profiler=falseで実行する。各host試験は別processで動かす。以下はrepoルートからの例。

```powershell
dotnet build Web/Emuera.Web.Runtime.Tests/Emuera.Web.Runtime.Tests.csproj -c Release -p:EnableErbExecutionProfiler=false
dotnet run --project Web/Emuera.Web.Runtime.Tests/Emuera.Web.Runtime.Tests.csproj -c Release --no-build -- getkey-state-contract
dotnet run --project Web/Emuera.Web.Runtime.Tests/Emuera.Web.Runtime.Tests.csproj -c Release --no-build -- p1c6-getkey
node Web/Emuera.Web.Runtime.Tests/getkey-state.test.mjs
node --test Web/Emuera.Web.Runtime.Tests/key-macros.test.mjs
node Web/Emuera.Web.Runtime.Tests/getkey-browser/verdict-test.mjs
```

getkey-state-contractは実ERBからGETKEYを照会し、highbit/lowbit、連続照会、解放、範囲外、従来host状態fallbackを検査する。JS試験は実module listenerと最小DOMによる26条件で、物理キーボードやOS IMEの試験ではない。

## 実browser-wasm

ゲーム・セーブを含まないUTF-8 BOMの専用ERBを使う。fixtureはRuntime.Tests配下に置き、Web wwwrootやruntime-templateへコピーしない。package/出力/profile/ログ/画像は指定した試験出力先にだけ生成する。既存出力は拒否する。

```powershell
$Site = '<最終publish/wwwrootの絶対パス>'
$TestOutput = '<新規の試験出力フォルダの絶対パス>'
& ./Web/Emuera.Web.Runtime.Tests/getkey-browser/build-fixture.ps1 -OutputDirectory "$TestOutput/fixture-package"
node Web/Emuera.Web.Runtime.Tests/getkey-browser/getkey-browser.mjs $Site "$TestOutput/normal" "$TestOutput/fixture-package"
node Web/Emuera.Web.Runtime.Tests/getkey-browser/getkey-browser.mjs $Site "$TestOutput/fault" "$TestOutput/fixture-package" fault-selftest
```

Node/Windows ChromeのCDP pipeを使用し、専用localhost origin・専用profile・headless=new・1920×1080/DPR1で実行する。CHROME_BINARY環境変数で実行ファイルを指定できるが、違うbinaryは違う試験条件として記録する。普段のChrome、保存領域、既存serverへ接続しない。sandbox無効化、CPU/network throttle、cache disableは行わない。

正常runは34条件を照合し、返値が一致しても予期しないConsole/Runtime例外、Network.loadingFailed、HTTP 4xx/5xx、assert失敗、途中終了はFAIL。失敗時もresult/errors/http/failureを保存する。仮想表示でDOM行数が減るため、完了はfixtureのQID連番で判定する。各呼出し・待ちには上限がある。

fault-selftestは正常34条件の後だけ、専用serverでConsole error、未処理Runtime例外、404、500、通信切断を意図的に起こす。result.status=FAILかつtestStatus=PASS_EXPECTED_REJECTIONが期待値。故障ログを正常runへ混ぜたり許容したりしない。HTTPのserver/CDP二重観測は別request数と同一視しない。

CDPのVK指定やisTrusted=trueは、物理キーの変換・OS IMEの証拠ではない。X1解放はブラウザBackを起こすため、実ERB runは押下中照会まで。解放/5ボタンmaskのJS試験と物理操作の範囲を分ける。

## AOT結果の再利用

採用時の製品4ファイルとProgram.csはAOT02のsource一覧とSHA一致。AOT02は基準HEAD b9c00a9と当時のdirty sourceから生成された候補であり、後の採用commitを埋め込んだバイナリではない。Wasm/配信JS一致を記録してその結果を再利用する。今回追加した保守用JS/ERB/資料は製品compile/配布対象外。製品挙動を変更した場合は対応するAOT試験を再実行する。
