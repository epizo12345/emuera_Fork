param([Parameter(Mandatory)][string]$Artifact)
$ErrorActionPreference='Stop';$n=Split-Path $Artifact -Leaf;$t=Get-Content $Artifact -Raw;$bad=$null
switch($n){
 'compact-layout.txt'{foreach($x in 'VmInstructionSize=16','VmFunctionDescriptorSize=16','VmFrameSize=12','FunctionCatalogEntrySize=16'){if(!$t.Contains($x)){$bad=$x;break}}
 }
 'runtime-case-manifest.tsv'{ $r=@((Get-Content $Artifact|select -Skip 1)|?{$_});if($r.Count-ne 24){$bad='CaseCountMismatch'}elseif(@($r|%{($_-split "`t")[3]}|sort -Unique).Count-ne 24){$bad='DuplicateBodySha'}elseif(@($r|%{($_-split "`t")[4]}|sort -Unique).Count-ne 24){$bad='DuplicateBehaviorSignature'} }
 'semantic-bindings.tsv'{if(@(Get-Content $Artifact|select -Skip 1).Count-ne 134652){$bad='IdentityBindingCount'}elseif((Get-Content $Artifact)[1] -notmatch 'ExactBound.*`t40`t40'){$bad='Misbound'} }
 'runtime-only.tsv'{if($t-match '(?m)`tTrue\r?$'){$bad='RuntimeOnlyCodeAvailable'}}
 'compiled-remap.tsv'{if(@((Get-Content $Artifact|select -Skip 1)|%{($_-split "`t")[1]}|sort -Unique).Count-ne 59103){$bad='MappingDuplicate'}}
 'binding-collisions.tsv'{if(@(Get-Content $Artifact|select -Skip 1).Count-ne 0){$bad='AmbiguousBinding'}}
 default {$checks=@{'semantic-binding-summary.txt'=@('OrdinalWouldMisbind=2','Misbound=0');'source-runtime-remap-summary.txt'=@('mappingMissing=0','mappingDuplicate=0');'oracle-result.txt'=@('Deterministic=True','BehaviorObserved=True');'real-execution-readiness.txt'=@('ExecutableReadyReal=0');'control-link-summary.txt'=@('fatalDiagnostics=0');'runtime-oracle-summary.txt'=@('JumpPropagationCase=04-jump-return-propagation') }[$n];if($checks){foreach($x in $checks){if(!$t.Contains($x)){$bad=$x;break}}}else{$bad='UnsupportedArtifact'}}
}
if($bad){Write-Output "MutationVerifier=FAIL`nFailureTag=$($bad -replace '=.*$','')";exit 1};Write-Output 'MutationVerifier=PASS';exit 0
