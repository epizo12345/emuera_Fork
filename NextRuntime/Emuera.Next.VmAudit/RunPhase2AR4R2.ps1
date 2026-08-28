param(
    [Parameter(Mandatory)][string]$FormalZip,
    [Parameter(Mandatory)][string]$WorkRoot,
    [string]$RepoRoot
)
$ErrorActionPreference='Stop'
if(-not $RepoRoot){$RepoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))}
$RepoRoot=[IO.Path]::GetFullPath($RepoRoot)
$FormalZip=[IO.Path]::GetFullPath($FormalZip)
$WorkRoot=[IO.Path]::GetFullPath($WorkRoot)
if(-not(Test-Path -LiteralPath $FormalZip -PathType Leaf)){throw "FormalZip missing: $FormalZip"}
if(Test-Path -LiteralPath $WorkRoot){throw "WorkRoot must not already exist: $WorkRoot"}
New-Item -ItemType Directory -Force -Path $WorkRoot|Out-Null

$SemanticVerifier=Join-Path $PSScriptRoot 'SemanticVerifier.ps1'
$RuntimeVerifier=Join-Path $PSScriptRoot 'RuntimeOracleVerifier.ps1'
$FinalVerifier=Join-Path $PSScriptRoot 'FinalEvidenceVerifier.ps1'
foreach($p in @($SemanticVerifier,$RuntimeVerifier,$FinalVerifier)){if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Verifier missing: $p"}}

function Read-Lines([string]$p) {
    [string[]]$lines = [IO.File]::ReadAllLines($p)
    Write-Output -NoEnumerate $lines
}
function Write-Lines([string]$p,[string[]]$lines){[IO.File]::WriteAllLines($p,$lines,[Text.UTF8Encoding]::new($false))}
function Sha([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToUpperInvariant()}
function Different-Hex64([string]$current) {
    [string]$zero = -join ('0' * 64)
    [string]$one = -join ('1' * 64)
    if ($current -and $current.ToLowerInvariant() -eq $zero) {
        return $one
    }
    return $zero
}
function Tsv-Safe([object]$value) {
    if ($null -eq $value) { return '' }
    return ([string]$value).Replace("`t",'\t').Replace("`r",'\r').Replace("`n",'\n')
}
function Assert-UnderRoot([string]$root,[string]$path){$r=[IO.Path]::GetFullPath($root).TrimEnd([char[]]@('\','/'))+[IO.Path]::DirectorySeparatorChar;$p=[IO.Path]::GetFullPath($path);if(-not$p.StartsWith($r,[StringComparison]::OrdinalIgnoreCase)){throw "Path escape: $path"}}
function Read-CanonicalValue([string]$path,[string]$key){$prefix=$key+'=';$hits=@([IO.File]::ReadAllLines($path)|Where-Object{$_.StartsWith($prefix,[StringComparison]::Ordinal)});if($hits.Count-ne1){throw "Canonical cardinality: $key count=$($hits.Count) path=$path"};$hits[0].Substring($prefix.Length).Trim()}
function Run-Script([string]$script,[string]$root,[string]$dir,[switch]$Final){
    New-Item -ItemType Directory -Force -Path $dir|Out-Null
    $stdout=Join-Path $dir 'stdout.txt';$stderr=Join-Path $dir 'stderr.txt';$exitp=Join-Path $dir 'exit.txt';$command=Join-Path $dir 'command.txt'
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName='pwsh';$psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    $args=@('-NoProfile','-File',$script,'-WorkRoot',$root);if($Final){$args+=@('-ExpectedDecision','HOLD')}
    foreach($arg in $args){[void]$psi.ArgumentList.Add($arg)}
    Write-Lines $command @($args -join ' ')
    $proc=[Diagnostics.Process]::Start($psi);$out=$proc.StandardOutput.ReadToEnd();$err=$proc.StandardError.ReadToEnd();$proc.WaitForExit();[int]$ec=$proc.ExitCode
    [IO.File]::WriteAllText($stdout,$out,[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText($stderr,$err,[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText($exitp,[string]$ec,[Text.UTF8Encoding]::new($false))
    [pscustomobject]@{ExitCode=$ec;Stdout=$stdout;Stderr=$stderr}
}
function Run-FreshAuthority([string]$script,[string]$root,[string]$name,[string]$dest){
    $d=Join-Path $WorkRoot "prepare\$name";$r=Run-Script $script $root $d
    $result=Read-CanonicalValue $r.Stdout 'Result';$tags=Read-CanonicalValue $r.Stdout 'ErrorTags'
    if($r.ExitCode-ne0-or$result -ne'PASS'-or$tags -ne''){throw "Fresh authority failed: $name exit=$($r.ExitCode) result=$result tags=$tags"}
    Copy-Item -LiteralPath $r.Stdout -Destination $dest -Force
}
function Normalize-Header([string]$root,[string]$rel,[string[]]$cols){
    $p=Join-Path $root $rel;if(-not(Test-Path -LiteralPath $p)){throw "Header target missing: $rel"}
    $lines=Read-Lines $p;if($lines.Length-eq0){throw "Empty TSV: $rel"}
    $expected=$cols-join"`t";$literal=$cols-join'`t'
    if($lines[0] -eq$literal){$lines[0]=$expected;Write-Lines $p $lines}
    elseif($lines[0] -ne$expected){throw "Unexpected TSV header: $rel :: $($lines[0])"}
}
function Reseal-ReviewManifest([string]$root){
    $manifest=Join-Path $root 'review-file-manifest.tsv';$lines=Read-Lines $manifest
    if($lines.Length-lt2-or$lines[0] -ne"Path`tLength`tSHA256"){throw 'Review manifest header invalid'}
    for($i=1;$i -lt$lines.Length;$i++){
        $c=$lines[$i]-split"`t",3;if($c.Length-ne3){throw "Review manifest row invalid: $i"}
        $rel=$c[0].Replace('\','/');if($rel-ieq'review-file-manifest.tsv'){throw 'Review manifest self-reference forbidden'}
        if([IO.Path]::IsPathRooted($rel)-or$rel.Contains('..')){throw "Unsafe review path: $rel"}
        $p=Join-Path $root $rel;Assert-UnderRoot $root $p;if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Review covered file missing: $rel"}
        $c[1]=[string](Get-Item -LiteralPath $p).Length;$c[2]=Sha $p;$lines[$i]=$c-join"`t"
    }
    Write-Lines $manifest $lines
}
function Update-ReviewEntry([string]$root,[string]$relativePath){
    $manifest=Join-Path $root 'review-file-manifest.tsv';if(-not(Test-Path -LiteralPath $manifest)){return}
    $target=Join-Path $root $relativePath;Assert-UnderRoot $root $target;if(-not(Test-Path -LiteralPath $target -PathType Leaf)){throw "Reseal target missing: $relativePath"}
    $lines=Read-Lines $manifest;if($lines[0] -ne"Path`tLength`tSHA256"){throw 'Review header invalid before entry reseal'};$found=0
    for($i=1;$i -lt$lines.Length;$i++){$c=$lines[$i]-split"`t",3;if($c.Length-eq3-and$c[0].Replace('\','/')-ieq$relativePath.Replace('\','/')){$c[1]=[string](Get-Item -LiteralPath $target).Length;$c[2]=Sha $target;$lines[$i]=$c-join"`t";$found++}}
    if($found-gt1){throw "Duplicate review entry: $relativePath"};if($found-eq1){Write-Lines $manifest $lines}
}
function Sync-SourceSnapshots([string]$root){
    $manifest=Join-Path $root 'source\source-snapshot-manifest.tsv';$lines=Read-Lines $manifest
    if($lines[0] -ne"RepoPath`tSHA256`tSnapshotSHA256`tChangedSinceStart"){throw 'Source manifest header invalid'}
    $aliasMap=[ordered]@{
        'VmRuntime.cs'='NextRuntime/Emuera.Next.Vm/VmRuntime.cs';'LegacyLogicalLineParser.cs'='Runtime/Script/Parser/LogicalLineParser.cs';'LegacyLogicalLine.cs'='Runtime/Script/Statements/LogicalLine.cs';'LegacyProcess.State.cs'='Runtime/Script/Process.State.cs';'MutationVerifier.ps1'='NextRuntime/Emuera.Next.VmAudit/MutationVerifier.ps1';'LegacyInstraction.Child.cs'='Runtime/Script/Statements/Instraction.Child.cs';'RunMutationMatrix.ps1'='NextRuntime/Emuera.Next.VmAudit/RunMutationMatrix.ps1';'VmAuditProgram.cs'='NextRuntime/Emuera.Next.VmAudit/Program.cs';'RuntimeOracleVerifier.ps1'='NextRuntime/Emuera.Next.VmAudit/RuntimeOracleVerifier.ps1';'Program.cs'='Program.cs';'PrototypeCompiler.cs'='NextRuntime/Emuera.Next.Compiler/PrototypeCompiler.cs';'CoreSourceIndex.cs'='NextRuntime/Emuera.Next.Core/SourceIndex.cs';'CompilerAuditProgram.cs'='NextRuntime/Emuera.Next.CompilerAudit/Program.cs';'VmSelfTestProgram.cs'='NextRuntime/Emuera.Next.VmSelfTest/Program.cs';'SemanticVerifier.ps1'='NextRuntime/Emuera.Next.VmAudit/SemanticVerifier.ps1';'RunPhase2AR4.ps1'='NextRuntime/Emuera.Next.VmAudit/RunPhase2AR4.ps1'
    }
    $byRepo=@{};for($i=1;$i -lt$lines.Length;$i++){$c=$lines[$i]-split"`t",4;if($c.Length-ne4){throw "Source row invalid: $i"};$key=$c[0].Replace('\','/').ToLowerInvariant();if($byRepo.ContainsKey($key)){throw "Duplicate source row: $key"};$byRepo[$key]=[pscustomobject]@{Index=$i;Cols=$c}}
    foreach($alias in $aliasMap.Keys){$repoRel=$aliasMap[$alias];$key=$repoRel.ToLowerInvariant();if(-not$byRepo.ContainsKey($key)){throw "Source row missing: $repoRel"};$repoFile=Join-Path $RepoRoot $repoRel;if(-not(Test-Path -LiteralPath $repoFile -PathType Leaf)){throw "Repo snapshot source missing: $repoRel"};$dest=Join-Path (Join-Path $root 'source') $alias;Copy-Item -LiteralPath $repoFile -Destination $dest -Force;$fresh=Sha $dest;$row=$byRepo[$key];$old=$row.Cols[1].ToUpperInvariant();$row.Cols[1]=$fresh;$row.Cols[2]=$fresh;if($row.Cols[3] -eq'True'-or$old -ne$fresh){$row.Cols[3]='True'}else{$row.Cols[3]='False'};$lines[$row.Index]=$row.Cols-join"`t"}
    Write-Lines $manifest $lines
}
function Set-StatusDifferent([string]$path,[string]$key,[scriptblock]$makeDifferent){$lines=Read-Lines $path;$hits=@();for($i=0;$i -lt$lines.Length;$i++){if($lines[$i]-match('^'+[regex]::Escape($key)+'=(.*)$')){$hits+=$i}};if($hits.Count-ne1){throw "Status key cardinality: $key count=$($hits.Count)"};$i=$hits[0];$old=($lines[$i]-split'=',2)[1];$new=&$makeDifferent $old;if([string]::IsNullOrWhiteSpace($new)-or$new -eq$old){throw "Status mutation no-op: $key"};$lines[$i]="$key=$new";Write-Lines $path $lines;"$key=$old->$key=$new"}
function Read-Utf8BomText([string]$path){$bytes=[IO.File]::ReadAllBytes($path);if($bytes.Length-lt3-or$bytes[0]-ne0xEF-or$bytes[1]-ne0xBB-or$bytes[2]-ne0xBF){throw "Expected UTF-8 BOM: $path"};[Text.UTF8Encoding]::new($false,$true).GetString($bytes,3,$bytes.Length-3)}
function Write-Utf8BomText([string]$path,[string]$text){$enc=[Text.UTF8Encoding]::new($false);$body=$enc.GetBytes($text);$all=New-Object byte[] ($body.Length+3);$all[0]=0xEF;$all[1]=0xBB;$all[2]=0xBF;[Array]::Copy($body,0,$all,3,$body.Length);[IO.File]::WriteAllBytes($path,$all)}
function Replace-Once([string]$text,[string]$old,[string]$new){$count=([regex]::Matches($text,[regex]::Escape($old))).Count;if($count-ne1){throw "Replace cardinality old=[$old] count=$count"};$text.Replace($old,$new)}
function Change-ProcessField([string]$root,[string]$case,[string]$run,[string]$key,[string]$new){$p=Join-Path $root "runtime\cases\$case\$run\process-result.txt";$lines=Read-Lines $p;$hits=@();for($i=0;$i -lt$lines.Length;$i++){if($lines[$i] -like "$key=*"){$hits+=$i}};if($hits.Count-ne1){throw "process-result key cardinality: $case/$run/$key"};$old=($lines[$hits[0]]-split'=',2)[1];if($old -eq$new){throw 'process mutation no-op'};$lines[$hits[0]]="$key=$new";Write-Lines $p $lines;"$case/$run $key=$old->$new"}

function Read-ReviewManifestRows([string]$manifest){
    $lines = [IO.File]::ReadAllLines($manifest)
    if($lines.Length -lt 2 -or $lines[0] -ne "Path`tLength`tSHA256"){throw 'Review manifest header/shape invalid'}
    $rows = [Collections.Generic.List[object]]::new()
    for($i=1;$i -lt $lines.Length;$i++){
        $c = $lines[$i] -split "`t",3
        if($c.Length -ne 3){throw "Review manifest row invalid: $i"}
        $rows.Add([pscustomobject]@{Path=$c[0];Length=$c[1];SHA256=$c[2]})
    }
    return $rows.ToArray()
}

function Test-RunnerHelpers {
    $selfRoot = Join-Path $WorkRoot '_runner-selftest'
    New-Item -ItemType Directory -Force -Path $selfRoot | Out-Null
    try {
        [string]$zero = -join ('0' * 64)
        [string]$one = -join ('1' * 64)
        $d0 = Different-Hex64 ''
        $d1 = Different-Hex64 $zero
        $d2 = Different-Hex64 $one
        if ($d0 -isnot [string] -or $d0.Length -ne 64 -or $d0 -ne $zero) { throw 'SelfTest Different-Hex64 empty failed' }
        if ($d1 -isnot [string] -or $d1.Length -ne 64 -or $d1 -ne $one) { throw 'SelfTest Different-Hex64 zero failed' }
        if ($d2 -isnot [string] -or $d2.Length -ne 64 -or $d2 -ne $zero) { throw 'SelfTest Different-Hex64 one failed' }

        $oneLine = Join-Path $selfRoot 'one-line.txt'
        Write-Lines $oneLine @('alpha')
        $oneRead = Read-Lines $oneLine
        if ($oneRead -isnot [array] -or $oneRead.Length -ne 1 -or $oneRead[0] -ne 'alpha') { throw 'SelfTest Read-Lines one-line failed' }

        if ((Tsv-Safe $null) -ne '') { throw 'SelfTest Tsv-Safe null failed' }
        if ((Tsv-Safe "a`tB`r`nC") -ne 'a\tB\r\nC') { throw 'SelfTest Tsv-Safe escaping failed' }

        $canon = Join-Path $selfRoot 'canonical.txt'
        Write-Lines $canon @('Result=PASS','ErrorTags=')
        if ((Read-CanonicalValue $canon 'Result') -ne 'PASS') { throw 'SelfTest canonical Result failed' }
        if ((Read-CanonicalValue $canon 'ErrorTags') -ne '') { throw 'SelfTest canonical ErrorTags failed' }

        $status = Join-Path $selfRoot 'status.txt'
        Write-Lines $status @('A=0')
        $statusDetail = Set-StatusDifferent $status 'A' { param($v) if ($v -eq '0') { '1' } else { '0' } }
        if (([IO.File]::ReadAllLines($status)[0]) -ne 'A=1' -or $statusDetail -ne 'A=0->A=1') { throw 'SelfTest Set-StatusDifferent failed' }

        $bom = Join-Path $selfRoot 'bom.txt'
        Write-Utf8BomText $bom "X`n"
        if ((Read-Utf8BomText $bom) -ne "X`n") { throw 'SelfTest UTF8 BOM roundtrip failed' }
        if ((Replace-Once 'abc' 'b' 'x') -ne 'axc') { throw 'SelfTest Replace-Once failed' }

        $processDir = Join-Path $selfRoot 'runtime\cases\selfcase\selfrun'
        New-Item -ItemType Directory -Force -Path $processDir | Out-Null
        $processFile = Join-Path $processDir 'process-result.txt'
        Write-Lines $processFile @('ExitCode=0','TimedOut=False')
        $processDetail = Change-ProcessField $selfRoot 'selfcase' 'selfrun' 'ExitCode' '1'
        $processLines = [IO.File]::ReadAllLines($processFile)
        if ($processLines[0] -ne 'ExitCode=1' -or $processDetail -notlike '*ExitCode=0->1') { throw 'SelfTest Change-ProcessField failed' }

        $review = Join-Path $selfRoot 'review-file-manifest.tsv'
        Write-Lines $review @("Path`tLength`tSHA256","audit/a.txt`t1`t$($zero.ToUpperInvariant())","results/final-status.txt`t2`t$($zero.ToUpperInvariant())")
        $reviewRows = @(Read-ReviewManifestRows $review)
        if ($reviewRows.Count -ne 2 -or $reviewRows[0].Path -ne 'audit/a.txt' -or $reviewRows[1].Length -ne '2') { throw 'SelfTest Read-ReviewManifestRows failed' }
    }
    finally {
        if (Test-Path -LiteralPath $selfRoot) { Remove-Item -LiteralPath $selfRoot -Recurse -Force }
    }
    Write-Output 'RunnerSelfTest=PASS'
}

Test-RunnerHelpers

# Immutable source is extracted read-only; all normalization happens in PreparedBase.
$EvidenceBase=Join-Path $WorkRoot 'EvidenceBase';$PreparedBase=Join-Path $WorkRoot 'PreparedBase'
New-Item -ItemType Directory -Force -Path $EvidenceBase|Out-Null;Expand-Archive -LiteralPath $FormalZip -DestinationPath $EvidenceBase -Force
Copy-Item -LiteralPath $EvidenceBase -Destination $PreparedBase -Recurse -Force
Normalize-Header $PreparedBase 'source\source-snapshot-manifest.tsv' @('RepoPath','SHA256','SnapshotSHA256','ChangedSinceStart')
Normalize-Header $PreparedBase 'review-file-manifest.tsv' @('Path','Length','SHA256')
Normalize-Header $PreparedBase 'runtime\legacy-behavior-contract.tsv' @('Case','Category','ObservedValue','NextRuntimeBehaviorMatch')
# mutation-cases has changed schema historically; normalize literal separators without imposing column names.
$mutationHeader=Join-Path $PreparedBase 'mutation\mutation-cases.tsv';if(Test-Path -LiteralPath $mutationHeader){$ml=Read-Lines $mutationHeader;if($ml.Length){$ml[0]=$ml[0].Replace('`t',"`t");Write-Lines $mutationHeader $ml}}
Normalize-Header $PreparedBase 'fixture\before-manifest.tsv' @('RelativePath','Length','SHA256')
Normalize-Header $PreparedBase 'fixture\after-manifest.tsv' @('RelativePath','Length','SHA256')
Sync-SourceSnapshots $PreparedBase
Run-FreshAuthority $RuntimeVerifier $PreparedBase 'runtime-authority' (Join-Path $PreparedBase 'runtime\runtime-verifier-result.txt')
Run-FreshAuthority $SemanticVerifier $PreparedBase 'semantic-authority' (Join-Path $PreparedBase 'results\semantic-verifier.txt')
if((Read-CanonicalValue (Join-Path $PreparedBase 'results\semantic-verifier.txt') 'Misbound')-ne'0'){throw 'Fresh Semantic Misbound mismatch'}
if((Read-CanonicalValue (Join-Path $PreparedBase 'results\semantic-verifier.txt') 'OrdinalWouldMisbind')-ne'2'){throw 'Fresh Semantic OrdinalWouldMisbind mismatch'}
if((Read-CanonicalValue (Join-Path $PreparedBase 'results\semantic-verifier.txt') 'ExecutableReadyReal')-ne'0'){throw 'Fresh Semantic ExecutableReadyReal mismatch'}
Reseal-ReviewManifest $PreparedBase
$pre=Run-Script $FinalVerifier $PreparedBase (Join-Path $WorkRoot 'prepare\finalevidence') -Final
if($pre.ExitCode-ne0-or(Read-CanonicalValue $pre.Stdout 'Result')-ne'PASS'-or(Read-CanonicalValue $pre.Stdout 'ErrorTags')-ne''){throw 'PreparedBase FinalEvidence baseline failed'}

function Mutate-Case([string]$id,[string]$root){
    switch($id){
        '01'{$p=Join-Path $root 'audit\semantic-bindings.tsv';$l=Read-Lines $p;$c=$l[1]-split"`t",9;$old=$c[6];$c[6]=([int]$c[6]+1).ToString();$l[1]=$c-join"`t";Write-Lines $p $l;return @('audit/semantic-bindings.tsv',"SourceStartLine $old->$($c[6])")}
        '02'{$p=Join-Path $root 'audit\binding-collisions.tsv';$l=[IO.File]::ReadAllLines($p);if($l.Length-ne1-or$l[0] -ne"Domain`tRelativeFile`tStartLine`tCount`tNamesOrIds"){throw "Case02 baseline shape/header unexpected: lines=$($l.Length) header=[$($l[0])]"};$l+= "Mutation`t__mutation__.ERB`t1`t2`tA,B";Write-Lines $p $l;return @('audit/binding-collisions.tsv','append one schema-valid collision row')}
        '03'{$p=Join-Path $root 'audit\ordinal-would-misbind.tsv';$l=Read-Lines $p;if($l.Length-ne3){throw "Case03 expected 2 data rows, got $($l.Length-1)"};Write-Lines $p @($l[0],$l[1]);return @('audit/ordinal-would-misbind.tsv','delete one ordinal-would-misbind row')}
        '04'{$p=Join-Path $root 'audit\compiled-remap.tsv';$l=Read-Lines $p;$a=$l[1]-split"`t",5;$b=$l[2]-split"`t",5;$old=$b[1];$b[1]=$a[1];$l[2]=$b-join"`t";Write-Lines $p $l;return @('audit/compiled-remap.tsv',"RuntimeFunctionId $old->$($b[1]) duplicate")}
        '05'{$p=Join-Path $root 'audit\compiled-remap.tsv';$l=Read-Lines $p;if($l.Length-lt3){throw 'Case05 no data'};Write-Lines $p $l[0..($l.Length-2)];return @('audit/compiled-remap.tsv','delete one mapping row')}
        '06'{$p=Join-Path $root 'audit\semantic-bindings.tsv';$l=Read-Lines $p;$hit=-1;for($i=1;$i -lt$l.Length;$i++){$c=$l[$i]-split"`t",9;if($c[2] -eq'RuntimeOnlyLineContinuation'-and$c[8] -eq'False'){$hit=$i;$c[8]='True';$l[$i]=$c-join"`t";break}};if($hit-lt0){throw 'Case06 runtime-only binding not found'};Write-Lines $p $l;return @('audit/semantic-bindings.tsv','RuntimeOnlyLineContinuation CodeAvailable False->True')}
        '07'{$p=Join-Path $root 'audit\fixed-call-links.tsv';$l=Read-Lines $p;if($l.Length-lt3){throw 'Case07 no data'};$kept=@($l[0])+@($l[2..($l.Length-1)]);Write-Lines $p $kept;return @('audit/fixed-call-links.tsv','delete one fixed-link row')}
        '08'{$p=Join-Path $root 'audit\fixed-call-links.tsv';$l=Read-Lines $p;$hit=-1;for($i=1;$i -lt$l.Length;$i++){$c=$l[$i]-split"`t",8;if($c[2] -eq'CALL'){$hit=$i;$c[2]='CALL_MUTATED';$l[$i]=$c-join"`t";break}};if($hit-lt0){throw 'Case08 CALL row not found'};Write-Lines $p $l;return @('audit/fixed-call-links.tsv','one CALL opcode -> CALL_MUTATED')}
        '09'{$p=Join-Path $root 'audit\fixed-call-links.tsv';$l=Read-Lines $p;$hit=-1;for($i=1;$i -lt$l.Length;$i++){$c=$l[$i]-split"`t",8;if($c[4] -eq'True'){$hit=$i;$c[4]='False';$l[$i]=$c-join"`t";break}};if($hit-lt0){throw 'Case09 resolved row not found'};Write-Lines $p $l;return @('audit/fixed-call-links.tsv','one Resolved True->False')}
        '10'{$p=Join-Path $root 'audit\fixed-call-link-summary.txt';$l=Read-Lines $p;$hit=@(0..($l.Length-1)|Where-Object{$l[$_] -eq'wrongKind=0'});if($hit.Count-ne1){throw 'Case10 wrongKind cardinality'};$l[$hit[0]]='wrongKind=1';Write-Lines $p $l;return @('audit/fixed-call-link-summary.txt','wrongKind=0->1')}
        '11'{$p=Join-Path $root 'audit\compact-layout.txt';$t=[IO.File]::ReadAllText($p);$t=Replace-Once $t 'VmInstructionSize=16' 'VmInstructionSize=15';[IO.File]::WriteAllText($p,$t,[Text.UTF8Encoding]::new($false));return @('audit/compact-layout.txt','VmInstructionSize=16->15')}
        '12'{$p=Join-Path $root 'audit\compact-layout.txt';$t=[IO.File]::ReadAllText($p);$t=Replace-Once $t 'VmFunctionDescriptorSize=16' 'VmFunctionDescriptorSize=15';[IO.File]::WriteAllText($p,$t,[Text.UTF8Encoding]::new($false));return @('audit/compact-layout.txt','VmFunctionDescriptorSize=16->15')}
        '13'{$p=Join-Path $root 'audit\compact-layout.txt';$t=[IO.File]::ReadAllText($p);$t=Replace-Once $t 'VmFrameSize=12' 'VmFrameSize=11';[IO.File]::WriteAllText($p,$t,[Text.UTF8Encoding]::new($false));return @('audit/compact-layout.txt','VmFrameSize=12->11')}
        '14'{$p=Join-Path $root 'audit\compact-layout.txt';$t=[IO.File]::ReadAllText($p);$t=Replace-Once $t 'FunctionCatalogEntrySize=16' 'FunctionCatalogEntrySize=15';[IO.File]::WriteAllText($p,$t,[Text.UTF8Encoding]::new($false));return @('audit/compact-layout.txt','FunctionCatalogEntrySize=16->15')}
        '15'{$p=Join-Path $root 'audit\real-execution-readiness.txt';$t=[IO.File]::ReadAllText($p);$t=Replace-Once $t 'ExecutableReadyReal=0' 'ExecutableReadyReal=1';[IO.File]::WriteAllText($p,$t,[Text.UTF8Encoding]::new($false));return @('audit/real-execution-readiness.txt','ExecutableReadyReal=0->1')}
        '16'{$p=Join-Path $root 'audit\control-link-summary.txt';$t=[IO.File]::ReadAllText($p);$t=Replace-Once $t 'fatalDiagnostics=0' 'fatalDiagnostics=1';[IO.File]::WriteAllText($p,$t,[Text.UTF8Encoding]::new($false));return @('audit/control-link-summary.txt','fatalDiagnostics=0->1')}
        '17'{$p=Join-Path $root 'runtime\runtime-case-manifest.tsv';$l=Read-Lines $p;$c=$l[1]-split"`t",13;$old=$c[3];$c[3]=Different-Hex64 $old;$l[1]=$c-join"`t";Write-Lines $p $l;return @('runtime/runtime-case-manifest.tsv',"manifest BodySha256 $old->$($c[3])")}
        '18'{$p=Join-Path $root 'runtime\runtime-case-manifest.tsv';$l=Read-Lines $p;$c=$l[1]-split"`t",13;$old=$c[4];$c[4]=Different-Hex64 $old;$l[1]=$c-join"`t";Write-Lines $p $l;return @('runtime/runtime-case-manifest.tsv',"manifest BehaviorSignatureSha256 $old->$($c[4])")}
        '19'{$d=Change-ProcessField $root '01-empty-callee-fallthrough' 'discovery' 'ExitCode' '1';return @('runtime/cases/01-empty-callee-fallthrough/discovery/process-result.txt',$d)}
        '20'{$p=Join-Path $root 'runtime\cases\01-empty-callee-fallthrough\discovery\state-before.txt';$l=Read-Lines $p;$old=($l[0]-replace'^Hash=','');$l[0]='Hash='+(Different-Hex64 $old);Write-Lines $p $l;return @('runtime/cases/01-empty-callee-fallthrough/discovery/state-before.txt','state-before hash changed')}
        '21'{$p=Join-Path $root 'runtime\cases\03-nested-fallthrough-call\case-body.erb';$t=Read-Utf8BomText $p;$t=Replace-Once $t 'CALL R3_03_B' 'PRINTL R3_03_B';Write-Utf8BomText $p $t;return @('runtime/cases/03-nested-fallthrough-call/case-body.erb','CALL R3_03_B -> PRINTL R3_03_B; BOM preserved')}
        '22'{$p=Join-Path $root 'runtime\cases\01-empty-callee-fallthrough\oracle-result.txt';$t=[IO.File]::ReadAllText($p);$t=Replace-Once $t 'Deterministic=True' 'Deterministic=False';[IO.File]::WriteAllText($p,$t,[Text.UTF8Encoding]::new($false));return @('runtime/cases/01-empty-callee-fallthrough/oracle-result.txt','Deterministic=True->False')}
        '23'{$src=Join-Path $root 'runtime\cases\05-for-positive-step\case-body.erb';$dst=Join-Path $root 'runtime\cases\06-for-negative-step\case-body.erb';Copy-Item -LiteralPath $src -Destination $dst -Force;return @('runtime/cases/06-for-negative-step/case-body.erb','exact body copy from case05 to case06')}
        '24'{$p=Join-Path $root 'runtime\cases\02-print-callee-fallthrough\source-sha256.txt';$old=(Get-Content -LiteralPath $p -Raw).Trim();[IO.File]::WriteAllText($p,(Different-Hex64 $old)+"`n",[Text.UTF8Encoding]::new($false));return @('runtime/cases/02-print-callee-fallthrough/source-sha256.txt','source hash sidecar changed')}
        '25'{$p=Join-Path $root 'runtime\cases\02-print-callee-fallthrough\behavior-signature.txt';$old=(Get-Content -LiteralPath $p -Raw).Trim();[IO.File]::WriteAllText($p,(Different-Hex64 $old)+"`n",[Text.UTF8Encoding]::new($false));return @('runtime/cases/02-print-callee-fallthrough/behavior-signature.txt','behavior signature sidecar changed')}
        '26'{$d=Change-ProcessField $root '01-empty-callee-fallthrough' 'verify-1' 'ExitCode' '1';return @('runtime/cases/01-empty-callee-fallthrough/verify-1/process-result.txt',$d)}
        '27'{$d=Change-ProcessField $root '01-empty-callee-fallthrough' 'verify-2' 'ExitCode' '1';return @('runtime/cases/01-empty-callee-fallthrough/verify-2/process-result.txt',$d)}
        '28'{$d=Change-ProcessField $root '24-same-function-reentrant-control' 'discovery' 'ExitCode' '1';return @('runtime/cases/24-same-function-reentrant-control/discovery/process-result.txt',$d)}
        '29'{$p=Join-Path $root 'runtime\cases\02-print-callee-fallthrough\case-body.erb';$t=Read-Utf8BomText $p;$t+="`n; CASE29_BODY_SHA_ONLY`n";Write-Utf8BomText $p $t;return @('runtime/cases/02-print-callee-fallthrough/case-body.erb','append comment; behavior normalization unchanged')}
        '30'{$case='05-for-positive-step';$p=Join-Path $root "runtime\cases\$case\case-body.erb";$t=Read-Utf8BomText $p;$t+="`nLOCAL = 987654321`n";Write-Utf8BomText $p $t;$fresh=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant();[IO.File]::WriteAllText((Join-Path $root "runtime\cases\$case\source-sha256.txt"),$fresh+"`n",[Text.UTF8Encoding]::new($false));$mp=Join-Path $root 'runtime\runtime-case-manifest.tsv';$l=Read-Lines $mp;$hit=-1;for($i=1;$i -lt$l.Length;$i++){$c=$l[$i]-split"`t",13;if($c[0] -eq$case){$c[3]=$fresh;$l[$i]=$c-join"`t";$hit=$i;break}};if($hit-lt0){throw 'Case30 manifest row missing'};Write-Lines $mp $l;return @("runtime/cases/$case/case-body.erb",'behavior body changed; BodySha manifest+sidecar resealed')}
        '31'{$d=Change-ProcessField $root '01-empty-callee-fallthrough' 'discovery' 'TimedOut' 'True';return @('runtime/cases/01-empty-callee-fallthrough/discovery/process-result.txt',$d)}
        '32'{$d=Change-ProcessField $root '01-empty-callee-fallthrough' 'verify-1' 'TimedOut' 'True';return @('runtime/cases/01-empty-callee-fallthrough/verify-1/process-result.txt',$d)}
        '33'{$rel='phase1/current-manifest.jsonl';$p=Join-Path $root $rel;$lines=Read-Lines $p;$idx=-1;for($i=0;$i -lt$lines.Length;$i++){if($lines[$i].Trim().Length){$idx=$i;break}};if($idx-lt0){throw 'Empty current-manifest'};$lines[$idx]=$lines[$idx]+' ';Write-Lines $p $lines;$side=Join-Path $root 'phase1/current-manifest.sha256';if(Test-Path -LiteralPath $side){[IO.File]::WriteAllText($side,(Sha $p)+"`n",[Text.UTF8Encoding]::new($false));Update-ReviewEntry $root 'phase1/current-manifest.sha256'};Update-ReviewEntry $root $rel;return @($rel,'valid JSONL whitespace + current sidecar reseal')}
        '34'{$rel='phase1/baseline-manifest.sha256';$p=Join-Path $root $rel;$old=(Get-Content -LiteralPath $p -Raw).Trim();$new=Different-Hex64 $old;[IO.File]::WriteAllText($p,$new+"`n",[Text.UTF8Encoding]::new($false));Update-ReviewEntry $root $rel;return @($rel,'baseline sidecar changed')}
        '35'{$rel='fixture/after-manifest.tsv';$p=Join-Path $root $rel;$lines=Read-Lines $p;$c=$lines[1]-split"`t",3;$old=$c[2];$c[2]=Different-Hex64 $old;$lines[1]=$c-join"`t";Write-Lines $p $lines;Update-ReviewEntry $root $rel;return @($rel,'same fixture path hash changed')}
        '36'{$rel='fixture/after-manifest.tsv';$p=Join-Path $root $rel;$lines=[Collections.Generic.List[string]]::new();$lines.AddRange([string[]](Read-Lines $p));$newPath='__mutation__/case36-new.bin';$lines.Add("$newPath`t1`t$(Different-Hex64 '')");Write-Lines $p $lines.ToArray();Update-ReviewEntry $root $rel;return @($rel,"after-only row $newPath")}
        '37'{$rel='fixture/after-manifest.tsv';$p=Join-Path $root $rel;$lines=Read-Lines $p;$removed=$lines[1];$kept=[Collections.Generic.List[string]]::new();$kept.Add($lines[0]);for($i=2;$i -lt$lines.Length;$i++){$kept.Add($lines[$i])};Write-Lines $p $kept.ToArray();Update-ReviewEntry $root $rel;return @($rel,"remove after row: $removed")}
        '38'{$rel='source/source-snapshot-manifest.tsv';$p=Join-Path $root $rel;$lines=Read-Lines $p;$c=$lines[1]-split"`t",4;$old=$c[2];$c[2]=Different-Hex64 $old;$lines[1]=$c-join"`t";Write-Lines $p $lines;Update-ReviewEntry $root $rel;return @($rel,'SnapshotSHA256 changed')}
        '39'{$rel='results/final-status.txt';$p=Join-Path $root $rel;$d=Set-StatusDifferent $p 'Misbound' {param($v)if($v -eq'0'){'1'}else{'0'}};Update-ReviewEntry $root $rel;return @($rel,$d)}
        '40'{$rel='results/final-status.txt';$p=Join-Path $root $rel;$d=Set-StatusDifferent $p 'OrdinalWouldMisbind' {param($v)if($v -eq'0'){'1'}else{'0'}};Update-ReviewEntry $root $rel;return @($rel,$d)}
        '41'{$rel='results/final-status.txt';$p=Join-Path $root $rel;$d=Set-StatusDifferent $p 'Phase2ADecision' {param($v)if($v -eq'COMPLETE'){'HOLD'}else{'COMPLETE'}};Update-ReviewEntry $root $rel;return @($rel,$d)}
        '42'{$rel='results/final-status.txt';$p=Join-Path $root $rel;$d=Set-StatusDifferent $p 'ExecutableReadyReal' {param($v)if($v -eq'0'){'1'}else{'0'}};Update-ReviewEntry $root $rel;return @($rel,$d)}
        '43'{$rel='fixture/before-manifest.tsv';$p=Join-Path $root $rel;$lines=Read-Lines $p;$lines[0]='RelativePath`tLength`tSHA256';Write-Lines $p $lines;Update-ReviewEntry $root $rel;return @($rel,'actual TAB header -> literal backtick-t')}
        '44'{$rel='source/source-snapshot-manifest.tsv';$p=Join-Path $root $rel;$lines=Read-Lines $p;$lines[0]="RepoPath`t`tSHA256`tSnapshotSHA256`tChangedSinceStart";Write-Lines $p $lines;Update-ReviewEntry $root $rel;return @($rel,'insert extra actual TAB in header')}
        '45'{$p=Join-Path $root 'review-file-manifest.tsv';$lines=Read-Lines $p;$c=$lines[1]-split"`t",3;$c[2]=Different-Hex64 $c[2];$lines[1]=$c-join"`t";Write-Lines $p $lines;return @('review-file-manifest.tsv','corrupt one review SHA; no reseal')}
        '46'{$manifest=Join-Path $root 'review-file-manifest.tsv';$rows=@(Read-ReviewManifestRows $manifest);$protected=@('review-file-manifest.tsv','results/final-status.txt','results/semantic-verifier.txt','runtime/runtime-verifier-result.txt','runtime/runtime-verifier.stdout.txt','source/source-snapshot-manifest.tsv','fixture/before-manifest.tsv','fixture/after-manifest.tsv','phase1/baseline-manifest.jsonl','phase1/current-manifest.jsonl','phase1/baseline-manifest.sha256','phase1/current-manifest.sha256');$pick=$rows|Where-Object{$protected -notcontains$_.Path.Replace('\','/')}|Where-Object{$_.Path -like'audit/*'}|Select-Object -First 1;if(-not$pick){throw 'No safe covered file for Case46'};$rel=$pick.Path.Replace('\','/');$p=Join-Path $root $rel;[IO.File]::AppendAllText($p,"`n",[Text.UTF8Encoding]::new($false));return @($rel,'covered file changed without review reseal')}
        default{throw "Unknown case $id"}
    }
}

$cases=@(
    @('01','Semantic','Misbound'),@('02','Semantic','AmbiguousBinding'),@('03','Semantic','OrdinalWouldMisbind'),@('04','Semantic','MappingDuplicate'),@('05','Semantic','MappingMissing'),@('06','Semantic','RuntimeOnlyCodeAvailable'),@('07','Semantic','FixedLinks'),@('08','Semantic','Calls'),@('09','Semantic','Missing'),@('10','Semantic','WrongKind'),@('11','Semantic','Layout:VmInstructionSize'),@('12','Semantic','Layout:VmFunctionDescriptorSize'),@('13','Semantic','Layout:VmFrameSize'),@('14','Semantic','Layout:FunctionCatalogEntrySize'),@('15','Semantic','ExecutableReadyReal'),@('16','Semantic','FatalDiagnostics'),
    @('17','Runtime','BodyShaMismatch'),@('18','Runtime','BehaviorSignatureMismatch'),@('19','Runtime','ProcessExit'),@('20','Runtime','InitialStateMismatch'),@('21','Runtime','StaticContract'),@('22','Runtime','OracleResultMismatch'),@('23','Runtime','DuplicateBodySha'),@('24','Runtime','BodyShaMismatch'),@('25','Runtime','BehaviorSignatureMismatch'),@('26','Runtime','ProcessExit'),@('27','Runtime','ProcessExit'),@('28','Runtime','ProcessExit'),@('29','Runtime','BodyShaMismatch'),@('30','Runtime','BehaviorSignatureMismatch'),@('31','Runtime','ProcessExit'),@('32','Runtime','ProcessExit'),
    @('33','Final','Phase1ManifestMismatch'),@('34','Final','Phase1BaselineMismatch'),@('35','Final','FixtureChanged'),@('36','Final','FixtureNew'),@('37','Final','FixtureDeleted'),@('38','Final','SourceSnapshotMismatch'),@('39','Final','FinalStatusMismatch'),@('40','Final','FinalStatusMismatch'),@('41','Final','DecisionMismatch'),@('42','Final','FinalStatusMismatch'),@('43','Final','TsvHeaderInvalid'),@('44','Final','TsvHeaderInvalid'),@('45','Final','ReviewManifestMismatch'),@('46','Final','ReviewManifestMismatch')
)
$mutationRoot=Join-Path $WorkRoot 'mutation';New-Item -ItemType Directory -Force -Path $mutationRoot|Out-Null
$records=[Collections.Generic.List[object]]::new()
foreach($case in $cases){
    $id=$case[0];$kind=$case[1];$tag=$case[2];$dir=Join-Path (Join-Path $mutationRoot 'cases') $id;$baseline=Join-Path $dir 'BaselineRoot';$mut=Join-Path $dir 'MutationRoot';New-Item -ItemType Directory -Force -Path $dir|Out-Null;Copy-Item -LiteralPath $PreparedBase -Destination $baseline -Recurse -Force;Copy-Item -LiteralPath $PreparedBase -Destination $mut -Recurse -Force
    if($kind -eq'Semantic'){$script=$SemanticVerifier;$isFinal=$false}elseif($kind -eq'Runtime'){$script=$RuntimeVerifier;$isFinal=$false}else{$script=$FinalVerifier;$isFinal=$true}
    $br=Run-Script $script $baseline (Join-Path $dir 'baseline') -Final:$isFinal;$mutationInfo=@(Mutate-Case $id $mut);if($mutationInfo.Count -ne 2){throw "Mutation info cardinality: Case$id count=$($mutationInfo.Count)"};$mr=Run-Script $script $mut (Join-Path $dir 'mutation') -Final:$isFinal
    $baselineResult=Read-CanonicalValue $br.Stdout 'Result';$mutationResult=Read-CanonicalValue $mr.Stdout 'Result';$actual=Read-CanonicalValue $mr.Stdout 'ErrorTags';$tags=@($actual-split','|Where-Object{$_.Trim().Length}|ForEach-Object{$_.Trim()});$unexpected=@($tags|Where-Object{$_ -ne$tag});$det=($br.ExitCode-eq0-and$baselineResult -eq'PASS'-and$mr.ExitCode-ne0-and$mutationResult -eq'FAIL'-and$tags -contains$tag -and$unexpected.Count-eq0)
    $rec=[pscustomobject]@{CaseId=$id;Verifier=$kind;TargetArtifact=$mutationInfo[0];Mutation=$mutationInfo[1];BaselineExit=$br.ExitCode;BaselineResult=$baselineResult;MutationExit=$mr.ExitCode;MutationResult=$mutationResult;ExpectedFailureTag=$tag;ActualErrorTags=$actual;UnexpectedErrorTags=($unexpected-join',');Detected=$det};$records.Add($rec)
    $resultLines=@("CaseId=$id","Verifier=$kind","BaselineExit=$($br.ExitCode)","BaselineResult=$baselineResult","MutationExit=$($mr.ExitCode)","MutationResult=$mutationResult","ExpectedFailureTag=$tag","ActualErrorTags=$actual","UnexpectedErrorTags=$($unexpected-join ',')","Detected=$det","ChangedFiles=$($mutationInfo[0])","Mutation=$(Tsv-Safe $mutationInfo[1])");Write-Lines (Join-Path $dir 'result.txt') $resultLines
}
$outTsv=Join-Path $mutationRoot 'mutation-cases.tsv';$tsv=[Collections.Generic.List[string]]::new();$tsv.Add("CaseId`tVerifier`tTargetArtifact`tMutation`tBaselineExit`tBaselineResult`tMutationExit`tMutationResult`tExpectedFailureTag`tActualErrorTags`tUnexpectedErrorTags`tDetected")
foreach($r in $records){$fields=@($r.CaseId,$r.Verifier,$r.TargetArtifact,$r.Mutation,[string]$r.BaselineExit,$r.BaselineResult,[string]$r.MutationExit,$r.MutationResult,$r.ExpectedFailureTag,$r.ActualErrorTags,$r.UnexpectedErrorTags,[string]$r.Detected)|ForEach-Object{Tsv-Safe $_};$tsv.Add($fields-join"`t")};Write-Lines $outTsv $tsv.ToArray()
$baselineFailures=@($records|Where-Object{$_.BaselineExit-ne0-or$_.BaselineResult -ne'PASS'}).Count
$falsePass=@($records|Where-Object{$_.MutationExit-eq0-or$_.MutationResult -ne'FAIL'}).Count
$tagMismatch=@($records|Where-Object{($_.ActualErrorTags-split',')-notcontains$_.ExpectedFailureTag -or-not[string]::IsNullOrWhiteSpace($_.UnexpectedErrorTags)}).Count
$detected=@($records|Where-Object{$_.Detected}).Count
$summary=@("Total=46","UniqueCaseIds=$(@($records.CaseId|Sort-Object -Unique).Count)","Detected=$detected","BaselineFailures=$baselineFailures","FalsePass=$falsePass","ExpectedTagMismatch=$tagMismatch");Write-Lines (Join-Path $mutationRoot 'mutation-summary.txt') $summary
Write-Output "MutationDetected=$detected/46";Write-Output "BaselineFailures=$baselineFailures";Write-Output "FalsePass=$falsePass";Write-Output "ExpectedTagMismatch=$tagMismatch"
if($detected-ne46-or$baselineFailures-ne0-or$falsePass-ne0-or$tagMismatch-ne0){exit 1}else{exit 0}
