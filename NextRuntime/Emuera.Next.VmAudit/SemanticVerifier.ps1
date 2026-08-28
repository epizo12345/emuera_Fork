param(
    [Parameter(Mandatory)][string]$WorkRoot,
    [string]$ResultPath
)
$ErrorActionPreference='Stop'
$errors=[Collections.Generic.List[string]]::new()
function Fail([string]$tag){if(-not $errors.Contains($tag)){$errors.Add($tag)}}
function Rows([string]$n){$p=Join-Path $WorkRoot "audit\$n";if(!(Test-Path -LiteralPath $p)){Fail "Missing:$n";return @()};@(Import-Csv -Delimiter "`t" -LiteralPath $p)}
function V([string]$p,[string]$k){if(!(Test-Path -LiteralPath $p)){Fail "Missing:$([IO.Path]::GetFileName($p))";return $null};$x=Get-Content -LiteralPath $p|Where-Object{$_ -like "$k=*"}|Select-Object -First 1;if($x){$x.Substring($k.Length+1)}}

$b=Rows 'semantic-bindings.tsv'
$so=Rows 'source-only.tsv'
$ro=Rows 'runtime-only.tsv'
$co=Rows 'binding-collisions.tsv'
$ord=Rows 'ordinal-would-misbind.tsv'
$rm=Rows 'compiled-remap.tsv'
$ln=Rows 'fixed-call-links.tsv'
$ex=@($b|Where-Object BindingKind -eq 'ExactBound')
$runtimeOnlyBindings=@($b|Where-Object BindingKind -eq 'RuntimeOnlyLineContinuation')

if($b.Count-ne 134652){Fail 'IdentityBindingCount'}
if($ex.Count-ne 134649){Fail 'ExactBound'}
if($so.Count-ne 3){Fail 'SourceOnly'}
if($ro.Count-ne 3){Fail 'RuntimeOnly'}
if($co.Count-ne 0){Fail 'AmbiguousBinding'}
if($ord.Count-ne 2){Fail 'OrdinalWouldMisbind'}

$bad=0
foreach($x in $ex){
    if([string]::IsNullOrWhiteSpace($x.SourceFunctionId)-or$x.SourceFunctionId-eq'-1'-or$x.SourceStartLine-ne$x.RuntimeStartLine){$bad++}
}
if(@($ex|Group-Object SourceFunctionId|Where-Object{$_.Count -gt 1}).Count-or@($ex|Group-Object RuntimeFunctionId|Where-Object{$_.Count -gt 1}).Count){$bad++}
if($bad){Fail 'Misbound'}

if($rm.Count -lt 59103){Fail 'MappingMissing'}
if($rm.Count -gt 59103){Fail 'MappingCount'}
if(@($rm|Group-Object RuntimeFunctionId|Where-Object{$_.Count -gt 1}).Count-or@($rm|Group-Object SourceFunctionId|Where-Object{$_.Count -gt 1}).Count){Fail 'MappingDuplicate'}

$linkCountOk=($ln.Count -eq 12269)
if(-not $linkCountOk){Fail 'FixedLinks'}
$missingCount=@($ln|Where-Object{$_.Resolved -ne 'True'}).Count
$callCount=@($ln|Where-Object{$_.Opcode -eq 'CALL'}).Count
$jumpCount=@($ln|Where-Object{$_.Opcode -eq 'JUMP'}).Count
if($linkCountOk){
    if($missingCount){Fail 'Missing'}
    if($callCount-ne 12254){Fail 'Calls'}
    if($jumpCount-ne 15){Fail 'Jumps'}
}

$runtimeOnlyCodeAvailable=@($runtimeOnlyBindings|Where-Object{$_.CodeAvailable -eq 'True'}).Count
if($runtimeOnlyCodeAvailable){Fail 'RuntimeOnlyCodeAvailable'}

$fixedSummary=Join-Path $WorkRoot 'audit\fixed-call-link-summary.txt'
$wrongKind=V $fixedSummary 'wrongKind'
if($null -eq $wrongKind){$wrongKind=''}
if($wrongKind-ne'0'){Fail 'WrongKind'}

$ready=Join-Path $WorkRoot 'audit\real-execution-readiness.txt'
$readyValue=V $ready 'ExecutableReadyReal'
if($readyValue-ne'0'){Fail 'ExecutableReadyReal'}
$s=Join-Path $WorkRoot 'audit\control-link-summary.txt'
if((V $s 'linkedFunctions')-ne'59103'-or(V $s 'linkedInstructions')-ne'191273'){Fail 'ControlLinkSummary'}
$fatal=V $s 'fatalDiagnostics'
if($fatal-ne'0'){Fail 'FatalDiagnostics'}

$layoutExpected=[ordered]@{VmInstructionSize='16';VmFunctionDescriptorSize='16';VmFrameSize='12';FunctionCatalogEntrySize='16'}
foreach($k in $layoutExpected.Keys){if((V (Join-Path $WorkRoot 'audit\compact-layout.txt') $k)-ne$layoutExpected[$k]){Fail "Layout:$k"}}

$p=Join-Path $WorkRoot 'phase1'
if((Get-Content -LiteralPath (Join-Path $p 'baseline-manifest.sha256') -Raw).Trim() -ne (Get-Content -LiteralPath (Join-Path $p 'current-manifest.sha256') -Raw).Trim()){Fail 'Phase1ManifestMismatch'}
$o=Join-Path $WorkRoot 'runtime\runtime-verifier-result.txt'
if(!(Test-Path -LiteralPath $o)-or(Get-Content -LiteralPath $o -Raw)-notmatch '(?m)^RuntimeOracleVerifier=PASS\r?$'){Fail 'RuntimeOracleVerifier'}

$result=if($errors.Count){'FAIL'}else{'PASS'}
$out=@(
    'Verifier=SemanticVerifier',
    "Result=$result",
    "SemanticVerifier=$result",
    "SourceDefinitions=$($ex.Count+$so.Count)",
    "RuntimeDefinitions=$($ex.Count+$ro.Count)",
    "ExactBound=$($ex.Count)",
    "SourceOnly=$($so.Count)",
    "RuntimeOnly=$($ro.Count)",
    "AmbiguousBinding=$($co.Count)",
    "Misbound=$bad",
    "OrdinalWouldMisbind=$($ord.Count)",
    "CompiledMappings=$($rm.Count)",
    "MappingMissing=$([int]($rm.Count -lt 59103))",
    "MappingDuplicate=$([int](@($rm|Group-Object RuntimeFunctionId|Where-Object{$_.Count -gt 1}).Count -gt 0))",
    "FixedLinks=$($ln.Count)",
    "Calls=$callCount",
    "Jumps=$jumpCount",
    "Resolved=$(@($ln|Where-Object{$_.Resolved -eq 'True'}).Count)",
    "Missing=$missingCount",
    "WrongKind=$wrongKind",
    "RuntimeOnlyCodeAvailable=$runtimeOnlyCodeAvailable",
    "ExecutableReadyReal=$readyValue",
    "FatalDiagnostics=$fatal",
    "Errors=$($errors.Count)",
    "ErrorTags=$($errors -join ',')"
)
if($ResultPath){[IO.File]::WriteAllLines($ResultPath,$out,[Text.UTF8Encoding]::new($false))}
$out
if($errors.Count){exit 1}else{exit 0}
