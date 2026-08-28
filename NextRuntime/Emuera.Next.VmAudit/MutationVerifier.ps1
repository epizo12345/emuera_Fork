param([Parameter(Mandatory)][string]$Artifact)
$name = Split-Path -Leaf $Artifact
$text = Get-Content -LiteralPath $Artifact -Raw
$lines = Get-Content -LiteralPath $Artifact
if ($name -eq 'runtime-case-manifest.tsv') {
    $rows = @($lines | Select-Object -Skip 1 | Where-Object { $_ })
    $hashes=@($rows|ForEach-Object{($_ -split "`t")[3]}); $behaviors=@($rows|ForEach-Object{($_ -split "`t")[4]}); if($rows.Count -ne 24){exit 1}; if(@($rows|Where-Object{($_-split "`t").Count -ne 13}).Count -gt 0){exit 1}; if(@($rows|ForEach-Object{($_-split "`t")[0]}|Sort-Object -Unique).Count -ne 24){exit 1}; if(@($hashes|Sort-Object -Unique).Count -ne 24){exit 1}; if(@($behaviors|Sort-Object -Unique).Count -ne 24){exit 1}; if(@($hashes|Where-Object{$_ -match '^0{64}$'}).Count -gt 0){exit 1}; if(@($behaviors|Where-Object{$_ -match '^0{64}$'}).Count -gt 0){exit 1}; exit 0
    exit 0
}
if($name -eq 'semantic-bindings.tsv'){if(@($lines|Select-Object -Skip 1).Count -ne 134652 -or $lines[1] -notmatch "^0`tCHECK_ENDING`tExactBound`t0`tENDING\.ERB`t40`t40`tNormal`tFalse$"){exit 1};exit 0}
if($name -eq 'runtime-only.tsv'){if(@($lines|Select-Object -Skip 1).Count -ne 3 -or ($lines[0] -match 'CodeAvailable' -and $text -match "(?m)`tTrue`r?$")){exit 1};exit 0}
if($name -eq 'compiled-remap.tsv'){$ids=@($lines|Select-Object -Skip 1|ForEach-Object{($_-split "`t")[1]});if(@($ids|Sort-Object -Unique).Count -ne 59103){exit 1};exit 0}
if($name -eq 'binding-collisions.tsv'){if(@($lines|Select-Object -Skip 1).Count -ne 0){exit 1};exit 0}
$checks = switch ($name) {
    'semantic-binding-summary.txt' { @('SourceDefinitions=134652', 'RuntimeDefinitions=134652', 'ExactBound=134649', 'PhysicalOnlyPreprocessorDisabled=3', 'RuntimeOnlyLineContinuation=3', 'UnexplainedSourceOnly=0', 'UnexplainedRuntimeOnly=0', 'AmbiguousBinding=0', 'Misbound=0', 'OrdinalWouldMisbind=2', 'EffectiveNameUnknownRuntime=0') }
    'source-runtime-remap-summary.txt' { @('compiledSource=59103', 'compiledRuntime=59103', 'mappingMissing=0', 'mappingDuplicate=0', 'ordinalJoin=REJECTED') }
    'oracle-result.txt' { @('OracleSource=LegacyRuntimeMeasured', 'Deterministic=True', 'BehaviorObserved=True', 'NextRuntimeBehaviorMatch=NOT_CLAIMED') }
    'runtime-oracle-summary.txt' { @('LegacyRuntimeStartupSeam=PASS', 'LegacyRuntimeBehaviorOracle=CAPTURED', 'LegacyRuntimeBehaviorCases=24', 'LegacyRuntimeBehaviorDeterministic=24/24', 'JumpPropagationCase=04-jump-return-propagation') }
    'runtime-case-design.md' { @('CASE01_EMPTY_CALLEE','CASE24_SELF_RECURSION','same function recursively') }
    'compact-layout.txt' { @('VmInstructionSize=16', 'VmFunctionDescriptorSize=16', 'VmFrameSize=12', 'FunctionCatalogEntrySize=16', 'PermanentSourceLookup=False', 'PerFunctionClassObjects=0', 'PerNameIntArrays=0', 'PermanentCompositePathStrings=0') }
    'function-id-catalog-summary.txt' { @('definitions=134652', 'uniqueFunctionIds=134652', 'compactEntrySize=16', 'duplicateFunctionIds=0', 'compiledFunctions=59103', 'codeAvailableTrue=59103', 'codeAvailableFalse=75549') }
    'fixed-call-link-summary.txt' { @('calls=12254', 'jumps=15', 'staticTargetScanFailures=0', 'targetResolved=12269', 'targetMissing=0', 'wrongKind=0', 'codeAvailableTrue=1527', 'codeAvailableFalse=10742', 'resolverFirstDefinition=True') }
    'operand-span-differential.txt' { @('prototypeInstructions=191273', 'linkedInstructions=191273', 'operandSpanMismatch=0', 'result=PASS') }
    'local-pc-verification.txt' { @('errors=0', 'allTargetPcFieldsAreLocal=True') }
    'real-execution-readiness.txt' { @('ExecutableReadyReal=0', 'SemanticNotAvailableBeforeFetch=True') }
    'control-link-summary.txt' { @('linkedFunctions=59103', 'linkedInstructions=191273', 'structuralLinks=56912', 'sifLinks=4274', 'ifGroups=11051', 'ifClauses=29189', 'selectGroups=2825', 'selectCases=6415', 'loopDescriptors=128', 'invalidStructure=0', 'unsupportedControl=0', 'executableRealReady=0', 'fatalDiagnostics=0') }
    'memory.txt' { @('retainedRuns=3', 'negativeRetainedRuns=0') }
    default { Write-Error "unsupported artifact: $name"; exit 2 }
}
if (($checks | Where-Object { -not $text.Contains($_) }).Count -gt 0) { exit 1 }
exit 0
