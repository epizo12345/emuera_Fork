# [Emuera改修:ARRAYSORTLOCALE] ARRAYSORTLOCALEのERBフィクスチャを、指定したEmuera.exeの起動試験モードで実行して判定する。
# 一時フォルダは1つだけ作り、終了時に削除する。
param([Parameter(Mandatory)][string]$Exe,[int]$TimeoutSeconds=60)
$ErrorActionPreference='Stop'
$Exe=(Resolve-Path -LiteralPath $Exe).Path
$fixtureRoot=Join-Path $PSScriptRoot 'fixtures'
$work=Join-Path ([IO.Path]::GetTempPath()) ('emuera-sortlocale-'+[Guid]::NewGuid().ToString('N'))
$expect=[ordered]@{
	'main'=@{must=@('SORTLOCALE_ALL_PASS');mustNot=@('SORTLOCALE_FAIL')}
	'range-error'=@{must=@('命令ARRAYSORTLOCALEの第4引数 1 と第5引数の和 5');mustNot=@('SORTLOCALE_SHOULD_NOT_REACH')}
	'bad-locale'=@{must=@('ARRAYSORTLOCALEの第2引数 "xx-NOPE" は使用できる言語名ではありません');mustNot=@('SORTLOCALE_SHOULD_NOT_REACH')}
	'missing-locale'=@{must=@('第２引数（言語名）が指定されていません');mustNot=@('SORTLOCALE_SHOULD_NOT_REACH')}
}
$failed=0
try{
	New-Item -ItemType Directory -Path $work | Out-Null
	foreach($name in $expect.Keys){
		$run=Join-Path $work $name
		Copy-Item -LiteralPath (Join-Path $fixtureRoot $name) -Destination $run -Recurse
		$p=Start-Process -FilePath $Exe -ArgumentList @('--ExeDir',('"'+$run+'"'),'--StartupTest') -WorkingDirectory $run -WindowStyle Hidden -PassThru
		if(-not $p.WaitForExit($TimeoutSeconds*1000)){Stop-Process -Id $p.Id -Force}
		$text=''
		foreach($log in @('startup-test.log','emuera.log')){
			$f=Join-Path $run $log
			if(Test-Path -LiteralPath $f){$text+=[IO.File]::ReadAllText($f)}
		}
		$miss=@($expect[$name].must | Where-Object { -not $text.Contains($_) })
		$bad=@($expect[$name].mustNot | Where-Object { $text.Contains($_) })
		if($text.Length -eq 0 -or $miss.Count -or $bad.Count){
			$failed++
			Write-Output "FAIL: $name missing=[$($miss -join ', ')] unexpected=[$($bad -join ', ')]"
			Write-Output ($text -split "`n" | Select-Object -Last 30 | Out-String)
		}else{
			Write-Output "PASS: $name"
		}
	}
}finally{
	if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force}
}
Write-Output "SORTLOCALE_FIXTURES: $($expect.Count - $failed)/$($expect.Count) passed"
if($failed){exit 1}
