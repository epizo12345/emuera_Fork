param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not [Environment]::UserInteractive) {
	Write-Output 'UNAVAILABLE: Windowsの対話デスクトップが必要です。未実行をPASSには扱いません。'
	exit 2
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw '既存ログを保全するため、未使用の出力先を指定してください。' }
New-Item -ItemType Directory -Path $output | Out-Null
$hostDirectory = Join-Path $output 'host'
& dotnet build (Join-Path $PSScriptRoot 'ShutdownTests.csproj') -c Release -p:EnablePerformanceMetrics=false -o $hostDirectory -v:minimal *> (Join-Path $output 'build.log')
if ($LASTEXITCODE) { throw 'build失敗。build.logを参照してください。' }
$executable = Join-Path $hostDirectory 'Emuera.ConfigRegressionTests.exe'
$cases = @('lifetime','immediate-dispose','direct-dispose','cancel-close','queued-dispose','paint-close','slow-paint','unexpected-error','menu-cancel')
$results = @(foreach ($case in $cases) {
	# それぞれ別process。試験中はhostを再ビルドせず、生成物を正式checkoutへ出さない。
	$process = Start-Process -FilePath $executable -ArgumentList $case -WorkingDirectory $output -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output "$case.log") -RedirectStandardError (Join-Path $output "$case.error.log")
	$timedOut = -not $process.WaitForExit(30000)
	if ($timedOut) { $process.Kill(); $process.WaitForExit() }
	$process.Refresh()
	$log = Get-Content -LiteralPath (Join-Path $output "$case.log") -Raw
	$passed = -not $timedOut -and $process.ExitCode -eq 0 -and $log.Contains("PASS $case;")
	Write-Host "$case : $(if ($passed) {'PASS'} else {'FAIL'})"
	[ordered]@{ case=$case; passed=$passed; timedOut=$timedOut; exitCode=$process.ExitCode }
})
$record = [ordered]@{
	atUtc=[DateTime]::UtcNow.ToString('O'); tests=$results; skips=0;
	testSourceSha256=(Get-FileHash (Join-Path $PSScriptRoot 'Program.cs')).Hash;
	engineDllSha256=(Get-FileHash (Join-Path $hostDirectory 'Emuera.dll')).Hash;
	testDllSha256=(Get-FileHash (Join-Path $hostDirectory 'Emuera.ConfigRegressionTests.dll')).Hash
}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
$failures = @($results | Where-Object { -not $_.passed }).Count
Write-Output "RESULT: $($cases.Count-$failures)/$($cases.Count) passed; $failures failed; 0 skipped."
if ($failures) { exit 1 }
