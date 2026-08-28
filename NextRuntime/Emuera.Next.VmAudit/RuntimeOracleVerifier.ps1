param(
    [Parameter(Mandatory)][string]$WorkRoot,
    [string]$ResultPath,
    [string]$ReplayRoot,
    [string]$ReplayOutput
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path $WorkRoot 'runtime'
$manifestPath = Join-Path $runtime 'runtime-case-manifest.tsv'
$errors = [System.Collections.Generic.List[string]]::new()
function ErrorTag([string]$tag) { if (-not $errors.Contains($tag)) { $errors.Add($tag) } }
function Utf8Body([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) { ErrorTag 'BodyNotUtf8Bom' }
    try { [Text.UTF8Encoding]::new($true,$true).GetString($bytes,3,$bytes.Length-3) } catch { ErrorTag 'BodyDecodeFailure'; '' }
}
function Lines([string]$text) { ($text -replace "`r`n", "`n" -replace "`r", "`n") -split "`n" }
function Normalize([string]$text, [switch]$SentinelOnly) {
    $all = Lines $text
    if ($SentinelOnly) {
        $begin = [Array]::FindIndex($all, [Predicate[string]]{ param($line) $line -match '^ORACLE_BEGIN_[0-9]+$' })
        $end = -1; for ($i=$begin; $i -ge 0 -and $i -lt $all.Length; $i++) { if ($all[$i] -match '^ORACLE_END_[0-9]+$') { $end=$i; break } }
        if ($begin -lt 0 -or $end -lt $begin) { ErrorTag 'SentinelCountMismatch'; return '' }
        $all = $all[$begin..$end]
    }
    $out = [Collections.Generic.List[string]]::new()
    foreach ($raw in $all) {
        $line = $raw.Trim(); if ($line.Length -eq 0 -or $line.StartsWith(';')) { continue }
        $line = [regex]::Replace($line,'R[0-9]+_[0-9]{2}_','CASE_')
        $line = [regex]::Replace($line,'ORACLE_BEGIN_[0-9]+','ORACLE_BEGIN_CASE')
        $line = [regex]::Replace($line,'ORACLE_END_[0-9]+','ORACLE_END_CASE')
        if ($line -match '^([A-Za-z][A-Za-z0-9_]*)\s+(ORACLE_(?:BEGIN|END)_CASE)\s*$') { $line="$($Matches[1]) $($Matches[2])" }
        elseif ($line -match '^([A-Za-z][A-Za-z0-9_]*)\b(?:\s+.*)?$' -and $Matches[1] -like 'PRINT*') { $line="$($Matches[1]) <OUTPUT>" }
        elseif ($line -match '^CALL\s+[^,;]+(.*)$') { $line='CALL FUNC'+$Matches[1] }
        elseif ($line -match '^JUMP\s+[^,;]+(.*)$') { $line='JUMP FUNC'+$Matches[1] }
        elseif ($line -match '^@[^,\s]+(.*)$') { $line='@FUNC'+$Matches[1] }
        elseif ($line -match '^#DIM\s+\S+') { $line='#DIM <VAR>' }
        $line=[regex]::Replace($line,'CASE_[A-Za-z0-9_]+','VAR')
        $line=[regex]::Replace($line,'[\t ]+',' ').Trim(); $out.Add($line)
    }
    ($out -join "`n") + "`n"
}
function Sha([string]$text) { [Convert]::ToHexString(([Security.Cryptography.SHA256]::Create()).ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant() }
function BodyLines([string]$body) { @((Lines $body) | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') }) }
function Has([string]$body,[string]$pattern) { [regex]::IsMatch($body,$pattern,[Text.RegularExpressions.RegexOptions]::Multiline) }

if ($ReplayRoot) {
    $replayRows = @("Case`tBodySha256`tBehaviorSignatureSha256"); $replayRecords = @()
    foreach ($dir in Get-ChildItem $ReplayRoot -Directory | Sort-Object Name) {
        $path = Join-Path $dir.FullName 'SYSTEM_TITLE.erb'
        if (-not (Test-Path $path)) { continue }
        $body=Utf8Body $path; $bodySha=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant(); $signature=Sha (Normalize $body -WholeText)
        $replayRows += "$($dir.Name)`t$bodySha`t$signature"; $replayRecords += [pscustomobject]@{Case=$dir.Name; BodySha256=$bodySha; BehaviorSignatureSha256=$signature}
    }
    $bodyDup=@($replayRecords | Group-Object BodySha256 | Where-Object Count -gt 1).Count
    $signatureGroups=@($replayRecords | Group-Object BehaviorSignatureSha256 | Where-Object Count -gt 1)
    $proof=@("Metric`tValue","Replay`tPhase2A-R3 immutable ZIP extracted read-only","RawBodyShaDuplicateGroups`t$bodyDup","BehaviorSignatureDuplicateGroups`t$($signatureGroups.Count)")
    foreach ($group in $signatureGroups) { $proof += ("DuplicateSignatureCases`t"+($group.Group.Case -join ',')) }
    $proof += "Case`tBodySha256`tBehaviorSignatureSha256"; $proof += $replayRows | Select-Object -Skip 1
    if ($ReplayOutput) { [IO.File]::WriteAllLines($ReplayOutput,$proof,[Text.UTF8Encoding]::new($false)) }
    $proof | ForEach-Object { Write-Output $_ }
    if ($bodyDup -ne 0 -or $signatureGroups.Count -ne 2 -or -not ($signatureGroups | Where-Object { $_.Group.Case -contains '01-empty-callee-fallthrough' -and $_.Group.Case -contains '02-print-callee-fallthrough' }) -or -not ($signatureGroups | Where-Object { $_.Group.Case -contains '10-for-end-capture-reentrant' -and $_.Group.Case -contains '24-same-function-reentrant-control' })) { exit 1 }
    exit 0
}
function StaticContract([string]$case,[string]$body) {
    $ok = switch ($case) {
        '01-empty-callee-fallthrough' { $h=$body.IndexOf('@R3_01_CALLEE'); $h -ge 0 -and [string]::IsNullOrWhiteSpace($body.Substring($h + '@R3_01_CALLEE'.Length)) }
        '02-print-callee-fallthrough' { $tail=($body -split '(?m)^@R3_02_CALLEE\s*$')[1]; (Has $body '(?m)^@R3_02_CALLEE\s*$') -and (Has $body '(?m)^PRINTL R3_02_CALLEE_PRINT\s*$') -and -not (Has $tail '(?m)^RETURN\b') }
        '03-nested-fallthrough-call' { (Has $body '@R3_03_A') -and (Has $body '@R3_03_B') -and (Has $body '@R3_03_C') -and ([regex]::Matches($body,'(?m)^CALL R3_03_').Count -eq 3) }
        '04-jump-return-propagation' { (Has $body '(?m)^JUMP R3_04_B\s*$') -and (Has $body 'R3_04_A_AFTER_JUMP') -and -not (Has ($body -split '(?m)^@R3_04_B')[1] '(^|\n)RETURN\b') }
        '08-for-break' { (Has $body '(?m)^FOR\b') -and (Has $body '(?m)^BREAK\s*$') -and (Has $body '(?m)^NEXT\s*$') }
        '10-for-end-capture-reentrant' { (Has $body '(?m)^@R3_10_SELF,') -and ([regex]::Matches($body,'(?m)^FOR\b')).Count -eq 1 -and (Has $body '3 - R3_10_DEPTH') -and (Has $body 'CALL R3_10_SELF, 2') }
        '11-for-step-capture-reentrant' { (Has $body '(?m)^@R3_11_SELF,') -and ([regex]::Matches($body,'(?m)^FOR\b')).Count -eq 1 -and (Has $body '3 - R3_11_DEPTH') -and (Has $body 'CALL R3_11_SELF, 2') }
        '13-repeat-break' { (Has $body '(?m)^REPEAT\b') -and (Has $body '(?m)^BREAK\s*$') -and (Has $body '(?m)^REND\s*$') }
        '15-repeat-reentrant' { (Has $body '(?m)^@R3_15_SELF,') -and ([regex]::Matches($body,'(?m)^REPEAT\b')).Count -eq 1 -and (Has $body 'CALL R3_15_SELF, 2') }
        '22-nested-for-nearest-owner' { ([regex]::Matches($body,'(?m)^FOR\b')).Count -eq 2 -and (Has $body '(?m)^BREAK\s*$') }
        '23-nested-different-loop-owner' { (Has $body '(?m)^WHILE\b') -and (Has $body '(?m)^FOR\b') -and (Has $body '(?m)^BREAK\s*$') }
        '24-same-function-reentrant-control' { (Has $body '(?m)^@R3_24_SELF, R3_24_DEPTH, R3_24_STEP\s*$') -and ([regex]::Matches($body,'(?m)^FOR\b')).Count -eq 1 -and (Has $body 'FOR LOCAL, 0, 10, R3_24_STEP') -and (Has $body 'CALL R3_24_SELF, 2, 5') -and (Has $body '(?m)^BREAK\s*$') }
        default { $true }
    }
    if (-not $ok) { ErrorTag $(if ($case -eq '01-empty-callee-fallthrough') {'Case01NotEmpty'} elseif ($case -eq '24-same-function-reentrant-control') {'Case24NotSameFunctionRecursive'} else {"StaticContractFailure:$case"}) }
    return $ok
}

if (-not (Test-Path $manifestPath)) { ErrorTag 'ManifestMissing' }
else {
    $rows = @(Import-Csv -Delimiter "`t" $manifestPath)
    if ($rows.Count -ne 24) { ErrorTag 'CaseCountMismatch' }
    $bodyHashes = @(); $behaviorHashes=@()
    foreach ($row in $rows) {
        $caseRoot=Join-Path (Join-Path $runtime 'cases') $row.Case; $bodyPath=Join-Path $caseRoot 'case-body.erb'
        if (-not (Test-Path $bodyPath)) { ErrorTag 'CaseBodyMissing'; continue }
        $body=Utf8Body $bodyPath; $bodySha=(Get-FileHash $bodyPath -Algorithm SHA256).Hash.ToLowerInvariant(); $behavior=Sha (Normalize $body -WholeText)
        $bodyHashes += $bodySha; $behaviorHashes += $behavior
        if ($bodySha -ne $row.BodySha256 -or $bodySha -ne (Get-Content (Join-Path $caseRoot 'source-sha256.txt') -Raw).Trim()) { ErrorTag 'BodyShaMismatch' }
        if ($behavior -ne $row.BehaviorSignatureSha256 -or $behavior -ne (Get-Content (Join-Path $caseRoot 'behavior-signature.txt') -Raw).Trim()) { ErrorTag 'BehaviorSignatureMismatch' }
        if (-not (StaticContract $row.Case $body)) { }
        $pristine=(Get-Content (Join-Path $caseRoot 'pristine-state.txt') | Select-Object -First 1) -replace '^Hash=',''
        $normalized=@()
        foreach ($runName in @('discovery','verify-1','verify-2')) {
            $run=Join-Path $caseRoot $runName; $log=Join-Path $run 'startup-test.log'; if (-not (Test-Path $log)) { ErrorTag 'ProcessEvidenceMissing'; continue }
            $text=[IO.File]::ReadAllText($log); $n=Normalize $text -SentinelOnly; $normalized += $n
            if (([regex]::Matches($text,"(?m)^ORACLE_BEGIN_$($row.Case.Substring(0,2))\s*$")).Count -ne 1 -or ([regex]::Matches($text,"(?m)^ORACLE_END_$($row.Case.Substring(0,2))\s*$")).Count -ne 1) { ErrorTag 'SentinelCountMismatch' }
            $saved=Get-Content (Join-Path $caseRoot ("normalized-$runName.txt")) -Raw -ErrorAction SilentlyContinue
            if ($null -eq $saved -or $saved -ne $n) { ErrorTag "NormalizedMismatch:$($row.Case):$runName" }
            $before=(Get-Content (Join-Path $run 'state-before.txt') | Select-Object -First 1) -replace '^Hash=',''; if ($before -ne $pristine) { ErrorTag 'InitialStateMismatch' }
            if (-not (Test-Path (Join-Path $run 'stdout.txt')) -or -not (Test-Path (Join-Path $run 'stderr.txt')) -or -not (Test-Path (Join-Path $run 'process-result.txt'))) { ErrorTag 'ProcessEvidenceMissing' }
        }
        if ($normalized.Count -eq 3 -and ($normalized[0] -ne $normalized[1] -or $normalized[0] -ne $normalized[2])) { ErrorTag 'NonDeterministic' }
    }
    if (@($bodyHashes | Sort-Object -Unique).Count -ne 24) { ErrorTag 'DuplicateBodySha' }
    if (@($behaviorHashes | Sort-Object -Unique).Count -ne 24) { ErrorTag 'DuplicateBehaviorSignature' }
}
$status = if ($errors.Count -eq 0) { 'PASS' } else { 'FAIL' }
$lines=@("RuntimeOracleVerifier=$status", "Cases=24", "Errors=$($errors.Count)", "ErrorTags=$($errors -join ',')")
if ($ResultPath) { [IO.File]::WriteAllLines($ResultPath,$lines,[Text.UTF8Encoding]::new($false)) }
$lines | ForEach-Object { Write-Output $_ }
if ($errors.Count -gt 0) { exit 1 } else { exit 0 }
