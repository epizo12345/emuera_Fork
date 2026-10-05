param([Parameter(Mandatory)][string]$Exe,[Parameter(Mandatory)][string]$OutputRoot)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputRoot){throw '新しいOutputRootを指定してください。旧runは上書きしません。'}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path (Join-Path $OutputRoot 'build') -Force|Out-Null
# 実行前にsingle-fileを固定し、試験中の再build・差替えから分離する。
$frozen=Join-Path $OutputRoot 'build/Emuera.exe'
Copy-Item -LiteralPath $Exe -Destination $frozen
$exeSha=(Get-FileHash -LiteralPath $frozen).Hash
$cases=@(
	@{name='B-normal';marker='B_ALL_PASS';required=@('B_VALUE=99','B_TARGET_PASS','B_ARG_PASS')},
	@{name='B-reverse';marker='B_ALL_PASS';required=@('B_VALUE=99','B_TARGET_PASS','B_ARG_PASS')},
	@{name='B-parallel';marker='B_ALL_PASS';required=@('B_VALUE=99','B_TARGET_PASS','B_ARG_PASS')},
	@{name='B-lazy';marker='B_ALL_PASS';required=@('B_VALUE=99','B_TARGET_PASS','B_ARG_PASS');lazy=$true},
	@{name='B-arg-compatibility';marker='B_ARG_COMPAT_ALL_PASS';required=@('B_ARG_COMPAT_VALUE_PASS')},
	@{name='B-args-compatibility';marker='B_ARG_ARGS_COMPAT_ALL_PASS';required=@('B_NO_DECLARATION_ARG_ARGS_PASS','B_DECLARED_ARG_ARGS_PASS')},
	@{name='C-lazy';marker='C_ALL_PASS';required=@('C_ORDER=A定数B1CD2E');lazy=$true}
)
$results=@()
foreach($case in $cases){
	& (Join-Path $PSScriptRoot 'Run-Fixture.ps1') -Exe $frozen -Fixture (Join-Path $PSScriptRoot "fixtures/$($case.name)/Data") -RunName $case.name -Marker $case.marker -Seconds 5 -RunRoot (Join-Path $OutputRoot 'runs') *> (Join-Path $OutputRoot "$($case.name)-runner.log")
	$run=Join-Path $OutputRoot "runs/$($case.name)"
	$result=Get-Content -LiteralPath (Join-Path $run 'result-final.json') -Raw|ConvertFrom-Json
	$raw=Get-Content -LiteralPath (Join-Path $run 'Data/emuera.log') -Raw
	if(-not $result.markerFoundFinalLog -or -not $result.responding -or -not $result.init -or $result.exeSha256 -ne $exeSha -or $raw -match 'B_BAD_VALUE|B_COMPAT_BAD|エラー内容'){throw "失敗: $($case.name)"}
	foreach($marker in $case.required){if(-not $raw.Contains($marker)){throw "未到達: $marker"}}
	$lazy=$null
	if($case.lazy){
		$lazy=(Get-Content -LiteralPath (Join-Path $run 'Data/time.log'))|Where-Object {$_ -match 'LazyErb.*files=1.*fallback=0'}
		if(-not $lazy){throw '実Lazyの証拠がありません'}
	}
	$results += [ordered]@{name=$case.name;passed=$true;required=$case.required;lazyEvidence=$lazy;exeSha256=$exeSha;logSha256=$result.logSha256;runResultSha256=(Get-FileHash -LiteralPath (Join-Path $run 'result-final.json')).Hash}
	Write-Output "PASS: $($case.name)"
}
$results|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputRoot 'native-results.json') -Encoding utf8
