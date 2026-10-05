param([string]$Exe,[string]$Fixture,[string]$RunName,[string]$Marker='B_ALL_PASS',[int]$Seconds=12,[Parameter(Mandatory)][string]$RunRoot)
$ErrorActionPreference='Stop'
$run=Join-Path $RunRoot $RunName
if(Test-Path -LiteralPath $run){throw 'Fresh run required'}
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item -LiteralPath $PSCommandPath -Destination "$run/runner.ps1"
Copy-Item -LiteralPath $Fixture -Destination "$run/Data" -Recurse
$process=$null
try{
	$start=[DateTime]::UtcNow
	$process=Start-Process -FilePath $Exe -ArgumentList @('--ExeDir',('"'+$run+'\Data"')) -WorkingDirectory $run -WindowStyle Hidden -PassThru
	$clock=[Diagnostics.Stopwatch]::StartNew();$found=$false
	while($clock.Elapsed.TotalSeconds -lt $Seconds){
		if($process.HasExited){break}
		if((Test-Path "$run/Data/emuera.log") -and (Select-String -LiteralPath "$run/Data/emuera.log" -Pattern $Marker -Quiet)){
			$found=$true
			if((Test-Path "$run/Data/time.log") -and (Select-String -LiteralPath "$run/Data/time.log" -Pattern 'Init:End' -Quiet)){break}
		}
		Start-Sleep -Milliseconds 50
	}
	$process.Refresh()
	$init=if(Test-Path "$run/Data/time.log"){Select-String -LiteralPath "$run/Data/time.log" -Pattern 'Init:End' | Select-Object -Last 1 -ExpandProperty Line}
	$result=[ordered]@{marker=$Marker;markerFound=$found;init=$init;alive=(-not $process.HasExited);responding=if(-not $process.HasExited){$process.Responding}else{$false};startUtc=$start.ToString('O');endUtc=[DateTime]::UtcNow.ToString('O');exe=$Exe;exeSha256=(Get-FileHash -LiteralPath $Exe).Hash;runnerSha256=(Get-FileHash "$run/runner.ps1").Hash;fixtureSha256=@(Get-ChildItem "$run/Data/ERB" -Recurse -File | ForEach-Object{[ordered]@{path=$_.FullName;sha256=(Get-FileHash $_.FullName).Hash}})}
	$result|ConvertTo-Json -Depth 8|Set-Content "$run/result.json" -Encoding utf8
}finally{
	if($process -and -not $process.HasExited){$process.CloseMainWindow()|Out-Null;if(-not $process.WaitForExit(1500)){Stop-Process -Id $process.Id}}
}
$result.markerFoundDuringWindow=$result.markerFound
$result.markerFoundFinalLog=if(Test-Path "$run/Data/emuera.log"){Select-String -LiteralPath "$run/Data/emuera.log" -Pattern $Marker -Quiet}else{$false}
$result.logSha256=if(Test-Path "$run/Data/emuera.log"){(Get-FileHash "$run/Data/emuera.log").Hash}else{$null}
$result|ConvertTo-Json -Depth 8|Set-Content "$run/result-final.json" -Encoding utf8
Get-Content "$run/result-final.json"
