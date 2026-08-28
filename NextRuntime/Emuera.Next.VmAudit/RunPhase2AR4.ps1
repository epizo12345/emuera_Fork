param(
    [Parameter(Mandatory)][string]$TemplateData,
    [Parameter(Mandatory)][string]$WorkRoot,
    [Parameter(Mandatory)][string]$ExePath,
    [string]$CaseName
)

$ErrorActionPreference = 'Stop'
$templateRoot = if ((Split-Path -Leaf $TemplateData) -ieq 'csv') { Split-Path -Parent $TemplateData } else { $TemplateData }
$csvSource = if ((Split-Path -Leaf $TemplateData) -ieq 'csv') { $TemplateData } else { Join-Path $TemplateData 'csv' }
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
CALL R3_24_SELF, 1, 2
PRINTL R3_24_CALLER_CONTINUATION
PRINTL ORACLE_END_24
WAIT
RETURN -1
@R3_24_SELF, R3_24_DEPTH, R3_24_STEP
#DIM R3_24_DEPTH
#DIM R3_24_STEP
FOR LOCAL, 0, 10, R3_24_STEP
PRINTFORML R3_24_ENTER_DEPTH_{R3_24_DEPTH}_LOCAL_{LOCAL}_STEP_{R3_24_STEP}
IF R3_24_DEPTH == 1
CALL R3_24_SELF, 2, 5
PRINTFORML R3_24_OUTER_AFTER_INNER_{LOCAL}
BREAK
ELSE
PRINTFORML R3_24_INNER_BEFORE_BREAK_{LOCAL}
BREAK
ENDIF
NEXT
'@ }
)

function Invoke([string]$DataRoot, [string]$OutputRoot) {
    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
    $startUtc = [DateTime]::UtcNow.ToString('o')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $log = Join-Path $OutputRoot 'startup-test.log'
    $command = "`"$ExePath`" --ExeDir `"$DataRoot`" --StartupTest"
    Set-Content -LiteralPath (Join-Path $OutputRoot 'command.txt') -Value $command -Encoding utf8
    $stdout = Join-Path $OutputRoot 'stdout.txt'
    $stderr = Join-Path $OutputRoot 'stderr.txt'
    $p = Start-Process -FilePath $ExePath -ArgumentList @('--ExeDir', $DataRoot, '--StartupTest') -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $timedOut = -not $p.WaitForExit(90000)
    if ($timedOut) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    $exit = if ($timedOut) { -1 } else { $p.ExitCode }
    $timer.Stop()
    $endUtc = [DateTime]::UtcNow.ToString('o')
    $timerElapsed = $timer.Elapsed.TotalMilliseconds.ToString('F3',[Globalization.CultureInfo]::InvariantCulture)
    $dataLog = Join-Path $DataRoot 'startup-test.log'
    if (Test-Path $dataLog) { Copy-Item -LiteralPath $dataLog -Destination $log -Force } else { Set-Content -LiteralPath $log -Value '' -Encoding utf8 }
    $text = [IO.File]::ReadAllText($log)
    $begin = ([regex]::Matches($text, 'ORACLE_BEGIN_[0-9]+')).Count
    $end = ([regex]::Matches($text, 'ORACLE_END_[0-9]+')).Count
    $fatal = ([regex]::Matches($text, '(?i)(fatal|exception|初期化に失敗|構文エラー|エラー)')).Count
    $status = "exit=$exit`ntimeout=$timedOut`nbegin=$begin`nend=$end`nfatal=$fatal`n"
    Set-Content -LiteralPath (Join-Path $OutputRoot 'status.txt') -Value $status -Encoding utf8
    Set-Content -LiteralPath (Join-Path $OutputRoot 'process-result.txt') -Value @("ExitCode=$exit", "TimedOut=$timedOut", "StartUtc=$startUtc", "EndUtc=$endUtc", "DurationMs=$timerElapsed") -Encoding utf8
    return [pscustomobject]@{ Exit=$exit; Timeout=$timedOut; Begin=$begin; End=$end; Fatal=$fatal; Text=$text }
}

function Copy-PristineData([string]$DataRoot) {
    if (Test-Path $DataRoot) { Remove-Item -LiteralPath $DataRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
    Copy-Item -LiteralPath $csvSource -Destination (Join-Path $DataRoot 'csv') -Recurse -Force
    foreach ($name in @('emuera.config','setting.json','setting_user.json','_default.config','_fixed.config')) {
        $source = Join-Path $templateRoot $name
        if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $DataRoot $name) -Force }
    }
}

function Extract-SentinelRegion([string]$Text) {
    $lines = ($Text -replace "`r`n", "`n" -replace "`r", "`n") -split "`n"
    $begin = [Array]::FindIndex($lines, [Predicate[string]]{ param($line) $line -match 'ORACLE_BEGIN_[0-9]+' })
    if ($begin -lt 0) { return @() }
    $end = -1
    for ($i = $begin; $i -lt $lines.Length; $i++) { if ($lines[$i] -match 'ORACLE_END_[0-9]+') { $end = $i; break } }
    if ($end -lt $begin) { return @() }
    return $lines[$begin..$end]
}

function Normalize-Behavior([string]$Text, [switch]$WholeText) {
    $out = [System.Collections.Generic.List[string]]::new()
    $lines = if ($WholeText) { ($Text -replace "`r`n", "`n" -replace "`r", "`n") -split "`n" } else { Extract-SentinelRegion $Text }
    foreach ($raw in $lines) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith(';')) { continue }
        $line = [regex]::Replace($line, 'R[0-9]+_[0-9]{2}_', 'CASE_')
        $line = [regex]::Replace($line, 'ORACLE_BEGIN_[0-9]+', 'ORACLE_BEGIN_CASE')
        $line = [regex]::Replace($line, 'ORACLE_END_[0-9]+', 'ORACLE_END_CASE')
        if ($line -match '^([A-Za-z][A-Za-z0-9_]*)\s+(ORACLE_(?:BEGIN|END)_CASE)\s*$') { $line = "$($Matches[1]) $($Matches[2])" }
        elseif ($line -match '^([A-Za-z][A-Za-z0-9_]*)\b(?:\s+.*)?$' -and $Matches[1] -like 'PRINT*') { $line = "$($Matches[1]) <OUTPUT>" }
        elseif ($line -match '^CALL\s+[^,;]+(.*)$') { $line = 'CALL FUNC' + $Matches[1] }
        elseif ($line -match '^JUMP\s+[^,;]+(.*)$') { $line = 'JUMP FUNC' + $Matches[1] }
        elseif ($line -match '^@[^,\s]+(.*)$') { $line = '@FUNC' + $Matches[1] }
        elseif ($line -match '^#DIM\s+\S+') { $line = '#DIM <VAR>' }
        $line = [regex]::Replace($line, 'CASE_[A-Za-z0-9_]+', 'VAR')
        $line = [regex]::Replace($line, '[\t ]+', ' ').Trim()
        $out.Add($line)
    }
    return ($out -join "`n") + "`n"
}

function Write-Utf8NoBom([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }

function Get-TreeState([string]$Root) {
    $rows = Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object { $_.Name -ne 'startup-test.log' } | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/')
        "$relative`t$($_.Length)`t$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
    } | Sort-Object
    $body = ($rows -join "`n") + "`n"
    $hash = [Convert]::ToHexString(([Security.Cryptography.SHA256]::Create()).ComputeHash([Text.Encoding]::UTF8.GetBytes($body))).ToLowerInvariant()
    return [pscustomobject]@{ Hash=$hash; FileCount=@($rows).Count; Rows=$rows }
}

$manifest = @("Case`tCategory`tMeasurementRunId`tBodySha256`tBehaviorSignatureSha256`tDiscoveryExit`tVerify1Exit`tVerify2Exit`tIndependentInitialState`tDeterministic`tBehaviorObserved`tStaticContract`tOracleStatus")
if ($CaseName) { $cases = @($cases | Where-Object Name -eq $CaseName) }
foreach ($case in $cases) {
    $caseRoot = Join-Path $casesRoot $case.Name
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
    $body = ($case.Body.Trim() + "`r`n")
    $bodyPath = Join-Path $caseRoot 'case-body.erb'
    [IO.File]::WriteAllText($bodyPath, $body, [Text.UTF8Encoding]::new($true))
    $sha = (Get-FileHash -LiteralPath $bodyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $caseRoot 'source-sha256.txt') -Value $sha -Encoding utf8
    $pristineState = $null
    $runs = foreach ($runName in @('discovery','verify-1','verify-2')) {
        $outputRoot = Join-Path $caseRoot $runName
        $dataRoot = Join-Path $outputRoot 'Data'
        Copy-PristineData $dataRoot
        New-Item -ItemType Directory -Force -Path (Join-Path $dataRoot 'ERB') | Out-Null
        Copy-Item -LiteralPath $bodyPath -Destination (Join-Path $dataRoot 'ERB\SYSTEM_TITLE.erb') -Force
        $before = Get-TreeState $dataRoot
        if (-not $pristineState) { $pristineState = $before }
        Set-Content -LiteralPath (Join-Path $outputRoot 'state-before.txt') -Value @("Hash=$($before.Hash)","FileCount=$($before.FileCount)") -Encoding utf8
        $result = Invoke $dataRoot $outputRoot
        $after = Get-TreeState $dataRoot
        Set-Content -LiteralPath (Join-Path $outputRoot 'state-after.txt') -Value @("Hash=$($after.Hash)","FileCount=$($after.FileCount)") -Encoding utf8
        $result | Add-Member -NotePropertyName StateBefore -NotePropertyValue $before.Hash
        $result | Add-Member -NotePropertyName StateAfter -NotePropertyValue $after.Hash
        $result | Add-Member -NotePropertyName InitialState -NotePropertyValue ($before.Hash -eq $pristineState.Hash)
        $result
    }
    Set-Content -LiteralPath (Join-Path $caseRoot 'pristine-state.txt') -Value @("Hash=$($pristineState.Hash)","FileCount=$($pristineState.FileCount)") -Encoding utf8
    $normalized = @($runs | ForEach-Object { Normalize-Behavior $_.Text })
    Write-Utf8NoBom (Join-Path $caseRoot 'normalized-discovery.txt') $normalized[0]
    Write-Utf8NoBom (Join-Path $caseRoot 'normalized-verify-1.txt') $normalized[1]
    Write-Utf8NoBom (Join-Path $caseRoot 'normalized-verify-2.txt') $normalized[2]
    $behaviorNormalized = Normalize-Behavior $body -WholeText
    Write-Utf8NoBom (Join-Path $caseRoot 'behavior-normalized.txt') $behaviorNormalized
    $hash = [Security.Cryptography.SHA256]::Create()
    $behaviorSha = [Convert]::ToHexString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($behaviorNormalized))).ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $caseRoot 'behavior-signature.txt') -Value $behaviorSha -Encoding utf8
    $d, $v1, $v2 = $runs
    $deterministic = ($normalized[0] -eq $normalized[1] -and $normalized[0] -eq $normalized[2])
    $observed = ($d.Exit -eq 0 -and $v1.Exit -eq 0 -and $v2.Exit -eq 0 -and !$d.Timeout -and !$v1.Timeout -and !$v2.Timeout -and $d.Begin -eq 1 -and $v1.Begin -eq 1 -and $v2.Begin -eq 1 -and $d.End -eq 1 -and $v1.End -eq 1 -and $v2.End -eq 1 -and $d.Fatal -eq 0 -and $v1.Fatal -eq 0 -and $v2.Fatal -eq 0)
    $oracle = "OracleSource=LegacyRuntimeMeasured`nDeterministic=$deterministic`nBehaviorObserved=$observed`nNextRuntimeBehaviorMatch=NOT_CLAIMED`n"
    Set-Content -LiteralPath (Join-Path $caseRoot 'oracle-result.txt') -Value $oracle -Encoding utf8
    $status = if ($observed -and $deterministic) { 'PASS' } else { 'HOLD' }
    $independent = $runs | Where-Object InitialState | Measure-Object | Select-Object -ExpandProperty Count
    $manifest += "$($case.Name)`t$($case.Category)`t20260828_Phase2A_R4_Final`t$sha`t$behaviorSha`t$($d.Exit)`t$($v1.Exit)`t$($v2.Exit)`t$independent/3`t$deterministic`t$observed`tPASS`t$status"
}
if (-not $CaseName) { Write-Utf8NoBom (Join-Path $WorkRoot 'runtime\runtime-case-manifest.tsv') (($manifest -join "`n") + "`n") }
Write-Host "Phase2A-R4 cases: $($cases.Count)"
