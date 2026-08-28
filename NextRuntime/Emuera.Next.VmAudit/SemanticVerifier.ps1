param([Parameter(Mandatory)][string]$WorkRoot,[string]$ResultPath)
$ErrorActionPreference='Stop'
$errors=[Collections.Generic.List[string]]::new()
function Fail([string]$tag){if(-not $errors.Contains($tag)){$errors.Add($tag)}}
function Rows([string]$name){$p=Join-Path $WorkRoot "audit\$name"; if(-not(Test-Path $p)){Fail "Missing:$name";return @()}; @(Import-Csv -Delimiter "`t" -LiteralPath $p)}
function FirstValue([string]$path,[string]$key){$x=Get-Content -LiteralPath $path | Where-Object {$_ -like "$key=*"} | Select-Object -First 1; if($x){$x.Substring($key.Length+1)}}
$bindings=Rows 'semantic-bindings.tsv'; $sourceOnly=Rows 'source-only.tsv'; $runtimeOnly=Rows 'runtime-only.tsv'; $collisions=Rows 'binding-collisions.tsv'; $misbind=Rows 'ordinal-would-misbind.tsv'; $remap=Rows 'compiled-remap.tsv'; $links=Rows 'fixed-call-links.tsv'
if($bindings.Count -ne 134652){Fail 'IdentityBindingCount'}
if(@($bindings|Where-Object BindingKind -eq 'ExactBound').Count -ne 134649){Fail 'ExactBound'}
if($sourceOnly.Count -ne 3){Fail 'SourceOnly'}; if($runtimeOnly.Count -ne 3){Fail 'RuntimeOnly'}; if($collisions.Count -ne 0){Fail 'AmbiguousBinding'}; if($misbind.Count -ne 2){Fail 'Misbound'}
if($remap.Count -ne 59103){Fail 'MappingCount'}; if(@($remap|Group-Object RuntimeFunctionId|Where-Object Count -gt 1).Count -ne 0){Fail 'MappingDuplicate'}
if($links.Count -ne 12269){Fail 'FixedLinkCount'}; if(@($links|Where-Object Resolved -eq 'True').Count -ne 12269){Fail 'FixedLinkUnresolved'}; if(@($links|Where-Object Opcode -eq 'CALL').Count -ne 12254 -or @($links|Where-Object Opcode -eq 'JUMP').Count -ne 15){Fail 'FixedLinkOpcodeCount'}
if(@($runtimeOnly|Where-Object { $_.CodeAvailable -eq 'True' }).Count -ne 0){Fail 'RuntimeOnlyCodeAvailable'}
$ready=Join-Path $WorkRoot 'audit\real-execution-readiness.txt'; if((FirstValue $ready 'ExecutableReadyReal') -ne '0'){Fail 'ExecutableReadyReal'}
$summary=Join-Path $WorkRoot 'audit\control-link-summary.txt'; if((FirstValue $summary 'linkedFunctions') -ne '59103' -or (FirstValue $summary 'linkedInstructions') -ne '191273'){Fail 'ControlLinkSummary'}
$layout=Join-Path $WorkRoot 'audit\compact-layout.txt'; foreach($key in @('VmInstructionSize','VmFunctionDescriptorSize','VmFrameSize','FunctionCatalogEntrySize')){ $expected=@{'VmInstructionSize'='16';'VmFunctionDescriptorSize'='16';'VmFrameSize'='12';'FunctionCatalogEntrySize'='16'}[$key]; if((FirstValue $layout $key) -ne $expected){Fail "Layout:$key"} }
$manifest=Join-Path $WorkRoot 'runtime\runtime-case-manifest.tsv'; if(-not(Test-Path $manifest)){Fail 'RuntimeManifest'} else {$rr=@(Import-Csv -Delimiter "`t" $manifest); if($rr.Count -ne 24){Fail 'RuntimeCaseCount'}; if(@($rr|Where-Object OracleStatus -ne 'PASS').Count -ne 0){Fail 'RuntimeOracle'}; if(@($rr|Where-Object IndependentInitialState -ne '3/3').Count -ne 0){Fail 'InitialState'}}
$phase=Join-Path $WorkRoot 'phase1'; if((Get-Content (Join-Path $phase 'baseline-manifest.sha256') -Raw).Trim() -ne (Get-Content (Join-Path $phase 'current-manifest.sha256') -Raw).Trim()){Fail 'Phase1ManifestMismatch'}
$oracle=Join-Path $WorkRoot 'runtime\runtime-verifier-result.txt'; if(-not(Test-Path $oracle) -or (Get-Content $oracle -Raw) -notmatch 'RuntimeOracleVerifier=PASS'){Fail 'RuntimeOracleVerifier'}
$result=if($errors.Count -eq 0){'PASS'}else{'FAIL'}; $out=@("SemanticVerifier=$result","IdentityBindings=$($bindings.Count)","ExactBound=$(@($bindings|Where-Object BindingKind -eq 'ExactBound').Count)","SourceOnly=$($sourceOnly.Count)","RuntimeOnly=$($runtimeOnly.Count)","AmbiguousBinding=$($collisions.Count)","Misbound=$($misbind.Count)","CompiledMappings=$($remap.Count)","FixedLinks=$($links.Count)","Errors=$($errors.Count)","ErrorTags=$($errors -join ',')"); if($ResultPath){[IO.File]::WriteAllLines($ResultPath,$out,[Text.UTF8Encoding]::new($false))}; $out; if($errors.Count){exit 1}
