# [Emuera改修:ARRAY-01] RESIZABLE配列のERBフィクスチャを、指定したEmuera.exeの起動試験モードで実行して判定する。
# 一時フォルダは1つだけ作り、終了時に削除する。
# REF/REFBYNAME命令は現行エンジンで書式エラーになる既存不具合があるため、REFの参照先ガードはフィクスチャで確認していない。
param([Parameter(Mandatory)][string]$Exe,[int]$TimeoutSeconds=60)
$ErrorActionPreference='Stop'
$Exe=(Resolve-Path -LiteralPath $Exe).Path
$fixtureRoot=Join-Path $PSScriptRoot 'fixtures'
$work=Join-Path ([IO.Path]::GetTempPath()) ('emuera-resizable-'+[Guid]::NewGuid().ToString('N'))
$expect=[ordered]@{
	'main'=@{must=@('RESIZABLE_ALL_PASS');mustNot=@('RESIZABLE_FAIL')}
	'decl-errors'=@{must=@('RESIZABLEとSAVEDATA','RESIZABLEとGLOBAL','RESIZABLEは1次元','RESIZABLEとCONST');mustNot=@('DECL_REACHED')}
	'decl-errors-erb'=@{must=@('RESIZABLEとREFキーワードは同時に指定できません');mustNot=@('DECL_REACHED')}
	'resize-fixed'=@{must=@('RESIZABLE変数ではありません');mustNot=@('FIXED_REACHED')}
	'read-oor'=@{must=@('配列変数"RG"の第1引数(5)は配列の範囲外です');mustNot=@('READ_SHOULD_NOT_REACH')}
	'refarg-error'=@{must=@('参照渡しの引数にできません');mustNot=@('REFARG_SHOULD_NOT_REACH')}
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
Write-Output "RESIZABLE_FIXTURES: $($expect.Count - $failed)/$($expect.Count) passed"
if($failed){exit 1}
