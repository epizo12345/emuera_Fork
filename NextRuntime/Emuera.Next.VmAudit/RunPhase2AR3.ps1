param(
    [Parameter(Mandatory)][string]$TemplateData,
    [Parameter(Mandatory)][string]$WorkRoot,
    [Parameter(Mandatory)][string]$ExePath,
    [string]$CaseName
)

$ErrorActionPreference = 'Stop'
$casesRoot = Join-Path $WorkRoot 'runtime\cases'
New-Item -ItemType Directory -Force -Path $casesRoot | Out-Null

$cases = @(
    @{ Name='01-empty-callee-fallthrough'; Category='implicit-call'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_01
CALL R3_01_CALLEE
PRINTL R3_01_AFTER_CALL
PRINTL ORACLE_END_01
WAIT
RETURN -1
@R3_01_CALLEE
PRINTL R3_01_CALLEE
'@ },
    @{ Name='02-print-callee-fallthrough'; Category='implicit-call'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_02
CALL R3_02_CALLEE
PRINTL R3_02_CALLER_CONTINUATION
PRINTL ORACLE_END_02
WAIT
RETURN -1
@R3_02_CALLEE
PRINTL R3_02_CALLEE_PRINT
'@ },
    @{ Name='03-nested-fallthrough-call'; Category='implicit-call'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_03
CALL R3_03_A
PRINTL R3_03_AFTER_A
PRINTL ORACLE_END_03
WAIT
RETURN -1
@R3_03_A
PRINTL R3_03_A
CALL R3_03_B
PRINTL R3_03_A_AFTER_B
@R3_03_B
PRINTL R3_03_B
CALL R3_03_C
PRINTL R3_03_B_AFTER_C
@R3_03_C
PRINTL R3_03_C
'@ },
    @{ Name='04-jump-return-propagation'; Category='jump-return'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_04
CALL R3_04_A
PRINTL R3_04_CALLER_AFTER
PRINTL ORACLE_END_04
WAIT
RETURN -1
@R3_04_A
PRINTL R3_04_A_BEFORE_JUMP
JUMP R3_04_B
PRINTL R3_04_A_AFTER_JUMP
@R3_04_B
PRINTL R3_04_B_FALLTHROUGH
'@ },
    @{ Name='05-for-positive-step'; Category='for'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_05
FOR LOCAL, 0, 3
PRINTFORML R3_05_VALUE_{LOCAL}
NEXT
PRINTFORML R3_05_FINAL_{LOCAL}
PRINTL ORACLE_END_05
WAIT
RETURN -1
'@ },
    @{ Name='06-for-negative-step'; Category='for'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_06
FOR LOCAL, 3, 0, -1
PRINTFORML R3_06_VALUE_{LOCAL}
NEXT
PRINTFORML R3_06_FINAL_{LOCAL}
PRINTL ORACLE_END_06
WAIT
RETURN -1
'@ },
    @{ Name='07-for-zero-step'; Category='for'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_07
FOR LOCAL, 0, 2, 0
PRINTL R3_07_BODY
NEXT
PRINTFORML R3_07_FINAL_{LOCAL}
PRINTL ORACLE_END_07
WAIT
RETURN -1
'@ },
    @{ Name='08-for-break'; Category='for-break'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_08
FOR LOCAL, 0, 3
PRINTFORML R3_08_BEFORE_BREAK_{LOCAL}
BREAK
PRINTL R3_08_UNREACHED
NEXT
PRINTFORML R3_08_AFTER_BREAK_{LOCAL}
PRINTL ORACLE_END_08
WAIT
RETURN -1
'@ },
    @{ Name='09-for-continue'; Category='for-continue'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_09
FOR LOCAL, 0, 2
PRINTFORML R3_09_BEFORE_{LOCAL}
CONTINUE
PRINTL R3_09_AFTER_UNREACHED
NEXT
PRINTFORML R3_09_FINAL_{LOCAL}
PRINTL ORACLE_END_09
WAIT
RETURN -1
'@ },
    @{ Name='10-for-end-capture-reentrant'; Category='for-reentrant'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_10
CALL R3_10_SELF, 1
PRINTL R3_10_OUTER_DONE
PRINTL ORACLE_END_10
WAIT
RETURN -1
@R3_10_SELF, R3_10_DEPTH
#DIM R3_10_DEPTH
FOR LOCAL, 0, 3 - R3_10_DEPTH
PRINTFORML R3_10_DEPTH_{R3_10_DEPTH}_VALUE_{LOCAL}
IF R3_10_DEPTH == 1
CALL R3_10_SELF, 2
ENDIF
NEXT
'@ },
    @{ Name='11-for-step-capture-reentrant'; Category='for-reentrant'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_11
CALL R3_11_SELF, 1
PRINTL R3_11_OUTER_DONE
PRINTL ORACLE_END_11
WAIT
RETURN -1
@R3_11_SELF, R3_11_DEPTH
#DIM R3_11_DEPTH
FOR LOCAL, 0, 4, 3 - R3_11_DEPTH
PRINTFORML R3_11_DEPTH_{R3_11_DEPTH}_VALUE_{LOCAL}
IF R3_11_DEPTH == 1
CALL R3_11_SELF, 2
ENDIF
NEXT
'@ },
    @{ Name='12-repeat-normal'; Category='repeat'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_12
REPEAT 3
PRINTFORML R3_12_COUNT_{COUNT}
REND
PRINTFORML R3_12_FINAL_{COUNT}
PRINTL ORACLE_END_12
WAIT
RETURN -1
'@ },
    @{ Name='13-repeat-break'; Category='repeat-break'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_13
REPEAT 4
PRINTFORML R3_13_BEFORE_BREAK_{COUNT}
BREAK
PRINTL R3_13_UNREACHED
REND
PRINTFORML R3_13_AFTER_BREAK_{COUNT}
PRINTL ORACLE_END_13
WAIT
RETURN -1
'@ },
    @{ Name='14-repeat-continue'; Category='repeat-continue'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_14
REPEAT 3
PRINTFORML R3_14_BEFORE_{COUNT}
CONTINUE
PRINTL R3_14_AFTER_UNREACHED
REND
PRINTFORML R3_14_FINAL_{COUNT}
PRINTL ORACLE_END_14
WAIT
RETURN -1
'@ },
    @{ Name='15-repeat-reentrant'; Category='repeat-reentrant'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_15
CALL R3_15_SELF, 1
PRINTL R3_15_OUTER_DONE
PRINTL ORACLE_END_15
WAIT
RETURN -1
@R3_15_SELF, R3_15_DEPTH
#DIM R3_15_DEPTH
REPEAT 4 - R3_15_DEPTH
PRINTFORML R3_15_DEPTH_{R3_15_DEPTH}_COUNT_{COUNT}
IF R3_15_DEPTH == 1
CALL R3_15_SELF, 2
ENDIF
REND
'@ },
    @{ Name='16-while-normal'; Category='while'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_16
LOCAL:0 = 0
WHILE LOCAL:0 < 3
PRINTFORML R3_16_VALUE_{LOCAL:0}
LOCAL:0 += 1
WEND
PRINTFORML R3_16_FINAL_{LOCAL:0}
PRINTL ORACLE_END_16
WAIT
RETURN -1
'@ },
    @{ Name='17-while-break'; Category='while-break'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_17
LOCAL:0 = 0
WHILE LOCAL:0 < 3
PRINTFORML R3_17_BEFORE_BREAK_{LOCAL:0}
BREAK
WEND
PRINTFORML R3_17_AFTER_BREAK_{LOCAL:0}
PRINTL ORACLE_END_17
WAIT
RETURN -1
'@ },
    @{ Name='18-while-continue'; Category='while-continue'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_18
LOCAL:0 = 0
WHILE LOCAL:0 < 3
LOCAL:0 += 1
PRINTFORML R3_18_BEFORE_{LOCAL:0}
CONTINUE
PRINTL R3_18_AFTER_UNREACHED
WEND
PRINTFORML R3_18_FINAL_{LOCAL:0}
PRINTL ORACLE_END_18
WAIT
RETURN -1
'@ },
    @{ Name='19-do-loop-normal'; Category='do-loop'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_19
LOCAL:0 = 0
DO
PRINTFORML R3_19_VALUE_{LOCAL:0}
LOCAL:0 += 1
LOOP LOCAL:0 < 3
PRINTFORML R3_19_FINAL_{LOCAL:0}
PRINTL ORACLE_END_19
WAIT
RETURN -1
'@ },
    @{ Name='20-do-loop-break'; Category='do-loop-break'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_20
LOCAL:0 = 0
DO
PRINTFORML R3_20_BEFORE_BREAK_{LOCAL:0}
BREAK
LOCAL:0 += 1
LOOP LOCAL:0 < 3
PRINTFORML R3_20_AFTER_BREAK_{LOCAL:0}
PRINTL ORACLE_END_20
WAIT
RETURN -1
'@ },
    @{ Name='21-do-loop-continue'; Category='do-loop-continue'; Body=@'
@SYSTEM_TITLE
LOCAL:0 = 0
PRINTL ORACLE_BEGIN_21
DO
LOCAL:0 += 1
PRINTFORML R3_21_BEFORE_{LOCAL:0}
CONTINUE
PRINTL R3_21_AFTER_UNREACHED
LOOP LOCAL:0 < 3
PRINTFORML R3_21_FINAL_{LOCAL:0}
PRINTL ORACLE_END_21
WAIT
RETURN -1
'@ },
    @{ Name='22-nested-for-nearest-owner'; Category='nested-for'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_22
FOR LOCAL, 0, 1
FOR LOCAL:1, 0, 1
PRINTFORML R3_22_INNER_{LOCAL}_{LOCAL:1}
BREAK
NEXT
PRINTFORML R3_22_OUTER_{LOCAL}
NEXT
PRINTL ORACLE_END_22
WAIT
RETURN -1
'@ },
    @{ Name='23-nested-different-loop-owner'; Category='nested-loop-owner'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_23
LOCAL:0 = 0
WHILE LOCAL:0 < 2
FOR LOCAL:1, 0, 2
PRINTFORML R3_23_INNER_{LOCAL:0}_{LOCAL:1}
BREAK
NEXT
LOCAL:0 += 1
WEND
PRINTFORML R3_23_FINAL_{LOCAL:0}
PRINTL ORACLE_END_23
WAIT
RETURN -1
'@ },
    @{ Name='24-same-function-reentrant-control'; Category='reentrant-control'; Body=@'
@SYSTEM_TITLE
PRINTL ORACLE_BEGIN_24
CALL R3_24_SELF, 1
PRINTL R3_24_CALLER_CONTINUATION
PRINTL ORACLE_END_24
WAIT
RETURN -1
@R3_24_SELF, R3_24_DEPTH
#DIM R3_24_DEPTH
FOR LOCAL, 0, 3 - R3_24_DEPTH
PRINTFORML R3_24_DEPTH_{R3_24_DEPTH}_VALUE_{LOCAL}
IF R3_24_DEPTH == 1
CALL R3_24_SELF, 2
ENDIF
NEXT
'@ }
)

function Invoke([string]$DataRoot, [string]$OutputRoot) {
    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
    Remove-Item -LiteralPath (Join-Path $DataRoot 'startup-test.log') -Force -ErrorAction SilentlyContinue
    $log = Join-Path $OutputRoot 'startup-test.log'
    $command = "`"$ExePath`" --ExeDir `"$DataRoot`" --StartupTest"
    Set-Content -LiteralPath (Join-Path $OutputRoot 'command.txt') -Value $command -Encoding utf8
    $p = Start-Process -FilePath $ExePath -ArgumentList @('--ExeDir', $DataRoot, '--StartupTest') -WindowStyle Hidden -PassThru
    $timedOut = -not $p.WaitForExit(90000)
    if ($timedOut) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    $exit = if ($timedOut) { -1 } else { $p.ExitCode }
    $dataLog = Join-Path $DataRoot 'startup-test.log'
    if (Test-Path $dataLog) { Copy-Item -LiteralPath $dataLog -Destination $log -Force } else { Set-Content -LiteralPath $log -Value '' -Encoding utf8 }
    Set-Content -LiteralPath (Join-Path $OutputRoot 'stdout.txt') -Value '' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $OutputRoot 'stderr.txt') -Value '' -Encoding utf8
    $text = Get-Content -LiteralPath $log -Raw
    $begin = ([regex]::Matches($text, 'ORACLE_BEGIN_[0-9]+')).Count
    $end = ([regex]::Matches($text, 'ORACLE_END_[0-9]+')).Count
    $fatal = ([regex]::Matches($text, '(?i)(fatal|exception|初期化に失敗|構文エラー|エラー)')).Count
    $status = "exit=$exit`ntimeout=$timedOut`nbegin=$begin`nend=$end`nfatal=$fatal`n"
    Set-Content -LiteralPath (Join-Path $OutputRoot 'status.txt') -Value $status -Encoding utf8
    return [pscustomobject]@{ Exit=$exit; Timeout=$timedOut; Begin=$begin; End=$end; Fatal=$fatal; Text=$text }
}

$manifest = @("Case`tCategory`tBodySha256`tDiscoveryExit`tVerify1Exit`tVerify2Exit`tDeterministic`tBehaviorObserved`tOracleStatus")
if ($CaseName) { $cases = @($cases | Where-Object Name -eq $CaseName) }
foreach ($case in $cases) {
    $caseRoot = Join-Path $casesRoot $case.Name
    $dataRoot = Join-Path $caseRoot 'Data'
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
    if (-not (Test-Path (Join-Path $dataRoot 'csv'))) { Copy-Item -LiteralPath $TemplateData -Destination $dataRoot -Recurse -Force }
    $erbRoot = Join-Path $dataRoot 'ERB'
    New-Item -ItemType Directory -Force -Path $erbRoot | Out-Null
    $body = ($case.Body.Trim() + "`r`n")
    $bodyPath = Join-Path $erbRoot 'SYSTEM_TITLE.erb'
    [IO.File]::WriteAllText($bodyPath, $body, [Text.UTF8Encoding]::new($true))
    $sha = (Get-FileHash -LiteralPath $bodyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $caseRoot 'source-sha256.txt') -Value $sha -Encoding utf8
    $d = Invoke $dataRoot (Join-Path $caseRoot 'discovery')
    $v1 = Invoke $dataRoot (Join-Path $caseRoot 'verify-1')
    $v2 = Invoke $dataRoot (Join-Path $caseRoot 'verify-2')
    $normalized = @($d.Text, $v1.Text, $v2.Text) | ForEach-Object {
        $lines = $_ -split "`r?`n" | Where-Object { $_ -match 'ORACLE_|R3_' }
        ($lines -join "`n").Trim() + "`n"
    }
    Set-Content -LiteralPath (Join-Path $caseRoot 'normalized-discovery.txt') -Value $normalized[0] -Encoding utf8
    Set-Content -LiteralPath (Join-Path $caseRoot 'normalized-verify-1.txt') -Value $normalized[1] -Encoding utf8
    Set-Content -LiteralPath (Join-Path $caseRoot 'normalized-verify-2.txt') -Value $normalized[2] -Encoding utf8
    $deterministic = ($normalized[0] -eq $normalized[1] -and $normalized[0] -eq $normalized[2])
    $observed = ($d.Exit -eq 0 -and $v1.Exit -eq 0 -and $v2.Exit -eq 0 -and !$d.Timeout -and !$v1.Timeout -and !$v2.Timeout -and $d.Begin -eq 1 -and $v1.Begin -eq 1 -and $v2.Begin -eq 1 -and $d.End -eq 1 -and $v1.End -eq 1 -and $v2.End -eq 1 -and $d.Fatal -eq 0 -and $v1.Fatal -eq 0 -and $v2.Fatal -eq 0)
    $oracle = "OracleSource=LegacyRuntimeMeasured`nDeterministic=$deterministic`nBehaviorObserved=$observed`nNextRuntimeBehaviorMatch=NOT_CLAIMED`n"
    Set-Content -LiteralPath (Join-Path $caseRoot 'oracle-result.txt') -Value $oracle -Encoding utf8
    $status = if ($observed -and $deterministic) { 'PASS' } else { 'HOLD' }
    $manifest += "$($case.Name)`t$($case.Category)`t$sha`t$($d.Exit)`t$($v1.Exit)`t$($v2.Exit)`t$deterministic`t$observed`t$status"
}
if (-not $CaseName) { Set-Content -LiteralPath (Join-Path $WorkRoot 'runtime\runtime-case-manifest.tsv') -Value $manifest -Encoding utf8 }
Write-Host "Phase2A-R3 cases: $($cases.Count)"
