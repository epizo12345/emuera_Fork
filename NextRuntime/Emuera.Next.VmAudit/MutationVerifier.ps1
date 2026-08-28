param([Parameter(Mandatory)][string]$Artifact)
$name = Split-Path -Leaf $Artifact
$text = Get-Content -LiteralPath $Artifact -Raw
$lines = Get-Content -LiteralPath $Artifact
if ($name -eq 'runtime-case-manifest.tsv') {
    $rows = @($lines | Select-Object -Skip 1 | Where-Object { $_ })
    if ($rows.Count -ne 24 -or @($rows | ForEach-Object { ($_ -split "`t")[2] } | Sort-Object -Unique).Count -ne 24 -or @($rows | Where-Object { $_ -notmatch "`t0`t0`t0`tTrue`tTrue`tPASS$" }).Count -ne 0) { exit 1 }
    exit 0
}
$checks = switch ($name) {
    'semantic-binding-summary.txt' { @('SourceDefinitions=134652', 'RuntimeDefinitions=134652', 'ExactBound=134649', 'PhysicalOnlyPreprocessorDisabled=3', 'RuntimeOnlyLineContinuation=3', 'UnexplainedSourceOnly=0', 'UnexplainedRuntimeOnly=0', 'AmbiguousBinding=0', 'Misbound=0', 'OrdinalWouldMisbind=2', 'EffectiveNameUnknownRuntime=0') }
    'source-runtime-remap-summary.txt' { @('compiledSource=59103', 'compiledRuntime=59103', 'mappingMissing=0', 'mappingDuplicate=0', 'ordinalJoin=REJECTED') }
    'oracle-result.txt' { @('OracleSource=LegacyRuntimeMeasured', 'Deterministic=True', 'BehaviorObserved=True', 'NextRuntimeBehaviorMatch=NOT_CLAIMED') }
    'runtime-oracle-summary.txt' { @('LegacyRuntimeStartupSeam=PASS', 'LegacyRuntimeBehaviorOracle=CAPTURED', 'LegacyRuntimeBehaviorCases=24', 'LegacyRuntimeBehaviorDeterministic=24/24', 'JumpPropagationCase=04-jump-return-propagation') }
    'runtime-case-design.md' { @('same function recursively') }
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
