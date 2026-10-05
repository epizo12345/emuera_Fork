# 終了時の再描画寿命・追加9試験

既存ConfigRegressionTestsの85件とは別hostで、実MainWindow・EmueraConsoleの終了寿命を確認する。製品hookは追加しない。

## 実行条件

Windows 10/11 x64、.NET 10 SDK/Desktop Runtime、対話デスクトップとSKGLControlを初期化できるOpenGL環境が必要。Windows UIを表示するため、短い実行中は他の操作を避ける。非Windows・非対話環境ではrunnerはUNAVAILABLE/exit 2。OpenGLやUI条件を満たせず失敗した場合もPASSとは扱わない。未実行環境は未確認とする。

リポジトリrootからPowerShell 7で、未使用のartifact出力先を指定する。

```powershell
pwsh -NoProfile -File '.\Emuera以外(開発補助ツールなど)\ConfigRegressionTests\ShutdownStudy\Run-Tests.ps1' -OutputDirectory '.\artifacts\shutdown-regression-new-run'
```

build、各caseのstdout/stderr、結果JSON、ソース/実行DLLのSHAを出力する。同じ出力先の上書きは拒否する。各caseは別processで30秒上限。timeoutは失敗であり、当該試験processだけを終了する。

## 検査内容

1. lifetime: 実ループ稼働中は保持Task未完了、二重Dispose後の正常完了、破棄後再開なし。
2. immediate-dispose: 開始直後の直接Disposeを30回。
3. direct-dispose: MainWindow直接Disposeがループを止める。
4. cancel-close: 後続FormClosing handlerで終了をキャンセルしてもアニメ・時限timerを維持。
5. queued-dispose: UIを100ms処理中にして配送待ち→Disposeの順序を固定。
6. paint-close: 配送されたPaintSurface内で終了する合成順序。
7. slow-paint: 1ms周期・40ms描画で再入/要求蓄積がなく、停止・終了できる。
8. unexpected-error: 通常の意図的Paint例外がApplication.ThreadExceptionへ通知される。WinFormsのWM_PAINT例外処理では実TaskのFaultedは必須としない。
9. menu-cancel: 既存終了メニューhandlerの確認dialogへIDCANCELを配送し、アニメが継続する。

reflectionは状態観察と窓の初期化省略に限定する。queued-dispose等は診断用の順序固定であり自然発生頻度の測定ではない。実ERB、通常入力、アニメの実フレーム、再起動は通常EXEの別試験で確認する。このhostだけを実ゲーム全般の確認と扱わない。

基準4883333の同一focused試験ではlifetime/direct-dispose/cancel-closeが失敗。既存証拠はartifacts/shutdown-redraw-study-20261005に保全。今回の正式採用検証は別artifactへ保存する。
