# [Emuera改修:TOOLS-02]
# Emueraを指定回数起動し、「操作可能まで到達したか」「警告が出たか」「何ms掛かったか」を
# CSVへまとめる自動試験。ゲームのERB・CSV・セーブは変更しない。
# --StartupTestで起動したEmueraは、必要なログを出したあと自動終了する。
# 使い方と結果の見方: プロジェクト資料/06_コード案内.md
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\artifacts\publish\Emuera\release_win-x64\Emuera.exe'),
    [string]$GameDir = (Join-Path $PSScriptRoot '..\eramegaten_p\Data'),
    [ValidateRange(1, 1000)]
    [int]$Iterations = 20,
    [ValidateRange(5, 600)]
    [int]$TimeoutSeconds = 60,
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\artifacts\startup-tests')
)

$ErrorActionPreference = 'Stop'

$ExePath = [IO.Path]::GetFullPath($ExePath)
$GameDir = [IO.Path]::GetFullPath($GameDir)
$OutputDir = [IO.Path]::GetFullPath($OutputDir)

if ((Test-Path -LiteralPath (Join-Path $GameDir 'Data\erb') -PathType Container) -and
    -not (Test-Path -LiteralPath (Join-Path $GameDir 'erb') -PathType Container)) {
    $GameDir = Join-Path $GameDir 'Data'
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Emuera.exeが見つかりません: $ExePath"
}
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'erb') -PathType Container)) {
    throw "ゲームのerbフォルダが見つかりません: $GameDir"
}
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'csv') -PathType Container)) {
    throw "ゲームのcsvフォルダが見つかりません: $GameDir"
}

$runDir = Join-Path $OutputDir (Get-Date -Format 'yyyyMMdd-HHmmss')
[void](New-Item -ItemType Directory -Path $runDir -Force)

$timeLogPath = Join-Path $GameDir 'time.log'
$startupLogPath = Join-Path $GameDir 'startup-test.log'
$results = [Collections.Generic.List[object]]::new()

for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    Remove-Item -LiteralPath $timeLogPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $startupLogPath -Force -ErrorAction SilentlyContinue

    $startedAt = Get-Date
    $process = Start-Process -FilePath $ExePath `
        -ArgumentList @('--ExeDir', ('"{0}"' -f $GameDir), '--StartupTest') `
        -PassThru -WindowStyle Hidden

    $exited = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $process.WaitForExit()
    }

    $wallMilliseconds = [int]((Get-Date) - $startedAt).TotalMilliseconds
    $timeText = if (Test-Path -LiteralPath $timeLogPath) {
        Get-Content -LiteralPath $timeLogPath -Raw
    } else { '' }
    $startupText = if (Test-Path -LiteralPath $startupLogPath) {
        Get-Content -LiteralPath $startupLogPath -Raw
    } else { '' }

    $initMatch = [regex]::Match($timeText, '(?m)^Init:End\s+(\d+)ms')
    $erbStartMatch = [regex]::Match($timeText, '(?m)^Proc:Init:ERB:Start\s+(\d+)ms')
    $erbEndMatch = [regex]::Match($timeText, '(?m)^Proc:Init:ERB:End\s+(\d+)ms')
    $warningMatches = [regex]::Matches($startupText, '(?m)^警告Lv(\d+):')
    $level2Count = @($warningMatches | Where-Object { $_.Groups[1].Value -eq '2' }).Count

    $initMilliseconds = if ($initMatch.Success) { [int]$initMatch.Groups[1].Value } else { $null }
    $erbMilliseconds = if ($erbStartMatch.Success -and $erbEndMatch.Success) {
        [int]$erbEndMatch.Groups[1].Value - [int]$erbStartMatch.Groups[1].Value
    } else { $null }
    $exitCode = if ($exited) { $process.ExitCode } else { $null }
    $status = if (-not $exited) {
        'Timeout'
    } elseif (-not $initMatch.Success) {
        'StartupFailed'
    } elseif ($level2Count -gt 0) {
        'WarningLv2'
    } else {
        'OK'
    }

    if ($timeText) {
        [IO.File]::WriteAllText((Join-Path $runDir ('time-{0:D3}.log' -f $iteration)), $timeText)
    }
    if ($startupText) {
        [IO.File]::WriteAllText((Join-Path $runDir ('startup-{0:D3}.log' -f $iteration)), $startupText)
    }

    $result = [pscustomobject]@{
        Iteration = $iteration
        Status = $status
        ExitCode = $exitCode
        InitMilliseconds = $initMilliseconds
        ErbMilliseconds = $erbMilliseconds
        WallMilliseconds = $wallMilliseconds
        WarningCount = $warningMatches.Count
        WarningLv2Count = $level2Count
    }
    $results.Add($result)
    Write-Host ("[{0}/{1}] {2}  Init={3}ms ERB={4}ms Lv2={5}" -f `
        $iteration, $Iterations, $status, $initMilliseconds, $erbMilliseconds, $level2Count)
}

$csvPath = Join-Path $runDir 'results.csv'
$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$successful = @($results | Where-Object Status -eq 'OK')
$failed = @($results | Where-Object Status -ne 'OK')
$averageInit = if ($successful.Count -gt 0) {
    [math]::Round(($successful | Measure-Object InitMilliseconds -Average).Average, 1)
} else { $null }
$averageErb = if ($successful.Count -gt 0) {
    [math]::Round(($successful | Measure-Object ErbMilliseconds -Average).Average, 1)
} else { $null }

Write-Host ''
Write-Host ("完了: {0}回 / 失敗・Lv2警告: {1}回" -f $successful.Count, $failed.Count)
Write-Host ("平均起動時間: {0}ms / 平均ERB読込: {1}ms" -f $averageInit, $averageErb)
Write-Host ("結果: {0}" -f $csvPath)

if ($failed.Count -gt 0) {
    exit 1
}
