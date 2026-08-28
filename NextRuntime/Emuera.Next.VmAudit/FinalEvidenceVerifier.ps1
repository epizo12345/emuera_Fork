param(
    [Parameter(Mandatory)][string]$WorkRoot,
    [string]$ResultPath,
    [ValidateSet('HOLD','COMPLETE')][string]$ExpectedDecision='HOLD'
)
$ErrorActionPreference = 'Stop'
$errors = [Collections.Generic.List[string]]::new()
$details = [Collections.Generic.List[string]]::new()
function Add-Detail([string]$text) { if (-not [string]::IsNullOrWhiteSpace($text)) { $details.Add($text) } }
function Fail([string]$tag,[string]$detail='') { if (-not $errors.Contains($tag)) { $errors.Add($tag) }; if ($detail) { Add-Detail ('{0}:{1}' -f $tag,$detail) } }
function Read-Lines([string]$path) { [IO.File]::ReadAllLines($path) }
function Parse-KeyValues([string[]]$lines) {
    $m = @{}
    foreach ($line in $lines) {
        if ($line -match '^([^=]+)=(.*)$') { $m[$Matches[1].Trim()] = $Matches[2].Trim() }
    }
    return $m
}
function Normalize-Rel([string]$p) {
    if ($null -eq $p) { return '' }
    $x = $p.Trim().Replace('\','/')
    while ($x.StartsWith('./')) { $x = $x.Substring(2) }
    return $x.ToLowerInvariant()
}
function Test-Hex64([string]$s) { return $null -ne $s -and $s -match '^[0-9A-Fa-f]{64}$' }
function Test-ExactTsvHeader([string]$path,[string[]]$columns) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $lines = Read-Lines $path
    if ($lines.Length -eq 0) { Fail 'TsvHeaderInvalid' ('path={0};reason=empty' -f (Normalize-Rel $path)); return $false }
    $first = $lines[0]
    $expected = $columns -join "`t"
    if ($first -ne $expected -or $first.Contains('`t')) {
        $actualDisplay = $first.Replace("`t",'<TAB>').Replace('`t','<LITERAL_BACKTICK_T>')
        $expectedDisplay = $expected.Replace("`t",'<TAB>')
        Fail 'TsvHeaderInvalid' ('path={0};expected={1};actual={2}' -f (Normalize-Rel $path),$expectedDisplay,$actualDisplay)
        return $false
    }
    return $true
}
function Read-CanonicalRaw([string]$path,[string]$kind) {
    if (-not (Test-Path -LiteralPath $path)) { Fail $kind; return @{} }
    $lines = Read-Lines $path
    $map = Parse-KeyValues $lines
    $pass = $false
    if ($kind -eq 'SemanticVerifier') {
        if ($map.ContainsKey('Verifier') -and $map['Verifier'] -eq 'SemanticVerifier' -and $map.ContainsKey('Result')) { $pass = $map['Result'] -eq 'PASS' }
        elseif ($map.ContainsKey('SemanticVerifier')) { $pass = $map['SemanticVerifier'] -eq 'PASS' }
    } else {
        if ($map.ContainsKey('Verifier') -and $map['Verifier'] -eq 'RuntimeOracleVerifier' -and $map.ContainsKey('Result')) { $pass = $map['Result'] -eq 'PASS' }
        elseif ($map.ContainsKey('RuntimeOracleVerifier')) { $pass = $map['RuntimeOracleVerifier'] -eq 'PASS' }
    }
    if (-not $pass) { Fail $kind }
    return $map
}
function Get-StatusMap([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { Fail 'FinalStatusMismatch'; return @{} }
    return Parse-KeyValues (Read-Lines $path)
}
function Compare-Status([hashtable]$status,[string]$key,[string]$expected,[string]$authority) {
    $actual = if ($status.ContainsKey($key)) { [string]$status[$key] } else { '<MISSING>' }
    if ($actual -ne $expected) { Fail 'FinalStatusMismatch' "key=$key;expected=$expected;actual=$actual;authority=$authority" }
}
function Get-FileSha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant() }

# Raw Semantic/Runtime verifier outputs are status authority. Do not rerun them against a mutated FinalEvidence root.
$semPath = Join-Path $WorkRoot 'results\semantic-verifier.txt'
$runPath = Join-Path $WorkRoot 'runtime\runtime-verifier-result.txt'
if (-not (Test-Path -LiteralPath $runPath)) { $runPath = Join-Path $WorkRoot 'runtime\runtime-verifier.stdout.txt' }
$sem = Read-CanonicalRaw $semPath 'SemanticVerifier'
$run = Read-CanonicalRaw $runPath 'RuntimeOracleVerifier'

# Phase1 byte/SHA authority.
$phase = Join-Path $WorkRoot 'phase1'
$bp = Join-Path $phase 'baseline-manifest.jsonl'
$cp = Join-Path $phase 'current-manifest.jsonl'
if (-not (Test-Path -LiteralPath $bp) -or -not (Test-Path -LiteralPath $cp)) {
    Fail 'Phase1ManifestMismatch'
} else {
    $bh = Get-FileSha $bp
    $ch = Get-FileSha $cp
    if ($bh -ne $ch) { Fail 'Phase1ManifestMismatch' }
    $bs = Join-Path $phase 'baseline-manifest.sha256'
    $cs = Join-Path $phase 'current-manifest.sha256'
    if (Test-Path -LiteralPath $bs) {
        $decl = (Get-Content -LiteralPath $bs -Raw).Trim().ToUpperInvariant()
        if (-not (Test-Hex64 $decl) -or $bh -ne $decl) { Fail 'Phase1BaselineMismatch' }
    }
    if (Test-Path -LiteralPath $cs) {
        $decl = (Get-Content -LiteralPath $cs -Raw).Trim().ToUpperInvariant()
        if (-not (Test-Hex64 $decl) -or $ch -ne $decl) { Fail 'Phase1ManifestMismatch' }
    }
}

# Exact header gates. Semantic comparison is skipped for the malformed authority to keep header mutations single-invariant.
$fixtureBefore = Join-Path $WorkRoot 'fixture\before-manifest.tsv'
$fixtureAfter  = Join-Path $WorkRoot 'fixture\after-manifest.tsv'
$sourceManifest = Join-Path $WorkRoot 'source\source-snapshot-manifest.tsv'
$reviewManifest = Join-Path $WorkRoot 'review-file-manifest.tsv'
$fixtureBeforeHeaderOk = Test-ExactTsvHeader $fixtureBefore @('RelativePath','Length','SHA256')
$fixtureAfterHeaderOk  = Test-ExactTsvHeader $fixtureAfter  @('RelativePath','Length','SHA256')
$sourceHeaderOk = Test-ExactTsvHeader $sourceManifest @('RepoPath','SHA256','SnapshotSHA256','ChangedSinceStart')
$reviewHeaderOk = Test-ExactTsvHeader $reviewManifest @('Path','Length','SHA256')
foreach ($extra in @('runtime\legacy-behavior-contract.tsv','mutation\mutation-cases.tsv')) {
    $q = Join-Path $WorkRoot $extra
    if (Test-Path -LiteralPath $q) {
        $ls = Read-Lines $q
        if ($ls.Length -eq 0) {
            Fail 'TsvHeaderInvalid' ('path={0};reason=empty' -f (Normalize-Rel $q))
        } elseif ([Array]::IndexOf([Text.Encoding]::UTF8.GetBytes($ls[0]),[byte]9) -lt 0 -or $ls[0].Contains('`t')) {
            $actualDisplay = $ls[0].Replace("`t",'<TAB>').Replace('`t','<LITERAL_BACKTICK_T>')
            Fail 'TsvHeaderInvalid' ('path={0};reason=no-actual-tab-or-literal-backtick-t;actual={1}' -f (Normalize-Rel $q),$actualDisplay)
        }
    }
}

# Fixture authority: before/after manifests only; do not infer fixture member files.
if ($fixtureBeforeHeaderOk -and $fixtureAfterHeaderOk) {
    $a = @(Import-Csv -Delimiter "`t" -LiteralPath $fixtureAfter)
    $b = @(Import-Csv -Delimiter "`t" -LiteralPath $fixtureBefore)
    $am = @{}; $bm = @{}; $adup = $false; $bdup = $false
    foreach ($x in $a) {
        $k = Normalize-Rel $x.RelativePath
        if ($am.ContainsKey($k)) { $adup = $true } else { $am[$k] = "$($x.Length):$($x.SHA256.ToUpperInvariant())" }
    }
    foreach ($x in $b) {
        $k = Normalize-Rel $x.RelativePath
        if ($bm.ContainsKey($k)) { $bdup = $true } else { $bm[$k] = "$($x.Length):$($x.SHA256.ToUpperInvariant())" }
    }
    if ($adup -or $bdup) { Fail 'FixtureChanged' }
    if (@($am.Keys | Where-Object { -not $bm.ContainsKey($_) }).Count -gt 0) { Fail 'FixtureNew' }
    if (@($bm.Keys | Where-Object { -not $am.ContainsKey($_) }).Count -gt 0) { Fail 'FixtureDeleted' }
    if (@($am.Keys | Where-Object { $bm.ContainsKey($_) -and $am[$_] -ne $bm[$_] }).Count -gt 0) { Fail 'FixtureChanged' }
}

# Source authority. R4 uses stable evidence aliases for the 16 snapshotted repo files.
# Verify the manifest internally and verify every physical snapshot through the explicit alias mapping.
if ($sourceHeaderOk) {
    $rows = @(Import-Csv -Delimiter "`t" -LiteralPath $sourceManifest)
    $rowByRepo = @{}
    foreach ($x in $rows) {
        $repoPath = Normalize-Rel ([string]$x.RepoPath)
        $sha = ([string]$x.SHA256).ToUpperInvariant()
        $snap = ([string]$x.SnapshotSHA256).ToUpperInvariant()
        $changed = [string]$x.ChangedSinceStart
        if ([string]::IsNullOrWhiteSpace($repoPath) -or -not (Test-Hex64 $sha) -or -not (Test-Hex64 $snap) -or $changed -notin @('True','False')) {
            Fail 'SourceSnapshotMismatch' "invalid-row=$repoPath"
            continue
        }
        if ($rowByRepo.ContainsKey($repoPath)) { Fail 'SourceSnapshotMismatch' "duplicate-repo-path=$repoPath"; continue }
        $rowByRepo[$repoPath] = $x
        if ($sha -ne $snap) { Fail 'SourceSnapshotMismatch' "manifest-hash-disagree=$repoPath;SHA256=$sha;SnapshotSHA256=$snap" }
    }
    $aliasMap = [ordered]@{
        'VmRuntime.cs'='NextRuntime/Emuera.Next.Vm/VmRuntime.cs'
        'LegacyLogicalLineParser.cs'='Runtime/Script/Parser/LogicalLineParser.cs'
        'LegacyLogicalLine.cs'='Runtime/Script/Statements/LogicalLine.cs'
        'LegacyProcess.State.cs'='Runtime/Script/Process.State.cs'
        'MutationVerifier.ps1'='NextRuntime/Emuera.Next.VmAudit/MutationVerifier.ps1'
        'LegacyInstraction.Child.cs'='Runtime/Script/Statements/Instraction.Child.cs'
        'RunMutationMatrix.ps1'='NextRuntime/Emuera.Next.VmAudit/RunMutationMatrix.ps1'
        'VmAuditProgram.cs'='NextRuntime/Emuera.Next.VmAudit/Program.cs'
        'RuntimeOracleVerifier.ps1'='NextRuntime/Emuera.Next.VmAudit/RuntimeOracleVerifier.ps1'
        'Program.cs'='Program.cs'
        'PrototypeCompiler.cs'='NextRuntime/Emuera.Next.Compiler/PrototypeCompiler.cs'
        'CoreSourceIndex.cs'='NextRuntime/Emuera.Next.Core/SourceIndex.cs'
        'CompilerAuditProgram.cs'='NextRuntime/Emuera.Next.CompilerAudit/Program.cs'
        'VmSelfTestProgram.cs'='NextRuntime/Emuera.Next.VmSelfTest/Program.cs'
        'SemanticVerifier.ps1'='NextRuntime/Emuera.Next.VmAudit/SemanticVerifier.ps1'
        'RunPhase2AR4.ps1'='NextRuntime/Emuera.Next.VmAudit/RunPhase2AR4.ps1'
    }
    foreach ($alias in $aliasMap.Keys) {
        $repoPath = Normalize-Rel $aliasMap[$alias]
        $physical = Join-Path (Join-Path $WorkRoot 'source') $alias
        if (-not $rowByRepo.ContainsKey($repoPath)) { Fail 'SourceSnapshotMismatch' "manifest-row-missing=$repoPath"; continue }
        if (-not (Test-Path -LiteralPath $physical -PathType Leaf)) { Fail 'SourceSnapshotMismatch' "physical-missing=$alias;repoPath=$repoPath"; continue }
        $expected = ([string]$rowByRepo[$repoPath].SnapshotSHA256).ToUpperInvariant()
        $actual = Get-FileSha $physical
        if ($actual -ne $expected) { Fail 'SourceSnapshotMismatch' "physical=$alias;repoPath=$repoPath;expected=$expected;actual=$actual" }
    }
}

# Review authority: manifest entries versus actual files under the same root.
if ($reviewHeaderOk) {
    $rootFull = [IO.Path]::GetFullPath($WorkRoot).TrimEnd([char[]]@('\','/')) + [IO.Path]::DirectorySeparatorChar
    foreach ($x in @(Import-Csv -Delimiter "`t" -LiteralPath $reviewManifest)) {
        $rel = [string]$x.Path
        if ([string]::IsNullOrWhiteSpace($rel) -or [IO.Path]::IsPathRooted($rel)) { Fail 'ReviewManifestMismatch'; continue }
        try { $full = [IO.Path]::GetFullPath((Join-Path $WorkRoot $rel)) } catch { Fail 'ReviewManifestMismatch'; continue }
        if (-not $full.StartsWith($rootFull,[StringComparison]::OrdinalIgnoreCase)) { Fail 'ReviewManifestMismatch'; continue }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Fail 'ReviewManifestMismatch'; continue }
        [int64]$len = 0
        if (-not [int64]::TryParse([string]$x.Length,[ref]$len) -or $len -ne (Get-Item -LiteralPath $full).Length) { Fail 'ReviewManifestMismatch'; continue }
        $decl = ([string]$x.SHA256).ToUpperInvariant()
        if (-not (Test-Hex64 $decl) -or (Get-FileSha $full) -ne $decl) { Fail 'ReviewManifestMismatch' }
    }
}

# Final status is compared to raw verifier output, not fixed constants copied into this verifier.
$statusPath = Join-Path $WorkRoot 'results\final-status.txt'
$status = Get-StatusMap $statusPath
if ($sem.ContainsKey('Misbound')) { Compare-Status $status 'Misbound' ([string]$sem['Misbound']) 'SemanticVerifier' } else { Fail 'FinalStatusMismatch' 'raw-authority-missing=Semantic.Misbound' }
if ($sem.ContainsKey('OrdinalWouldMisbind')) { Compare-Status $status 'OrdinalWouldMisbind' ([string]$sem['OrdinalWouldMisbind']) 'SemanticVerifier' } else { Fail 'FinalStatusMismatch' 'raw-authority-missing=Semantic.OrdinalWouldMisbind' }
$readyAuthority = $null; $readyValue = $null
if ($sem.ContainsKey('ExecutableReadyReal')) { $readyAuthority='SemanticVerifier'; $readyValue=[string]$sem['ExecutableReadyReal'] }
elseif ($run.ContainsKey('ExecutableReadyReal')) { $readyAuthority='RuntimeOracleVerifier'; $readyValue=[string]$run['ExecutableReadyReal'] }
if ($null -ne $readyValue) { Compare-Status $status 'ExecutableReadyReal' $readyValue $readyAuthority } else { Fail 'FinalStatusMismatch' 'raw-authority-missing=ExecutableReadyReal' }
# RuntimeOracle is an additional consistency check when the raw Runtime verifier exposes a full-case value.
$runtimeObserved = $null
if ($run.ContainsKey('RuntimeOracle')) { $runtimeObserved = [string]$run['RuntimeOracle'] }
elseif ($run.ContainsKey('Cases')) {
    [int]$n = 0
    if ([int]::TryParse([string]$run['Cases'],[ref]$n) -and $n -gt 0) { $runtimeObserved = "$n/$n" }
}
if ($runtimeObserved -and $status.ContainsKey('RuntimeOracle')) { Compare-Status $status 'RuntimeOracle' $runtimeObserved 'RuntimeOracleVerifier' }
# Decision is a closure-state input, not derivable from Semantic/Runtime PASS alone.
# During focused/pre-closure verification use -ExpectedDecision HOLD.
# Only after all formal closure gates (fresh full 46/46 etc.) may the caller use -ExpectedDecision COMPLETE.
$actualDecision = if ($status.ContainsKey('Phase2ADecision')) { [string]$status['Phase2ADecision'] } else { '<MISSING>' }
if ($actualDecision -ne $ExpectedDecision) { Fail 'DecisionMismatch' ('expected={0};actual={1};authority=InvocationExpectedDecision' -f $ExpectedDecision,$actualDecision) }

$result = if ($errors.Count -eq 0) { 'PASS' } else { 'FAIL' }
$out = [Collections.Generic.List[string]]::new()
$out.Add('Verifier=FinalEvidenceVerifier')
$out.Add("Result=$result")
$out.Add("FinalEvidenceVerifier=$result")
$out.Add("Errors=$($errors.Count)")
$out.Add("ErrorTags=$($errors -join ',')")
$out.Add("DetailCount=$($details.Count)")
for ($i=0; $i -lt $details.Count; $i++) { $out.Add("Detail$($i+1)=$($details[$i])") }
$out = $out.ToArray()
if ($ResultPath) { [IO.File]::WriteAllLines($ResultPath,$out,[Text.UTF8Encoding]::new($false)) }
$out
if ($errors.Count) { exit 1 }
