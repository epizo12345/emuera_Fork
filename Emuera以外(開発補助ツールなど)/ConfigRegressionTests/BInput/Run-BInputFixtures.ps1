# [Emuera改修:INPUT-01] BINPUT/BINPUTSのERBフィクスチャを、指定したEmuera.exeで実行して判定する。
# input: 入力欄へ値を書いてEnterを送り、ボタンに無い値が表示もされず弾かれることを確かめる。
# no-button: 起動試験モードで、押せるボタンが無いBINPUTがエラーになることを確かめる。
# 一時フォルダは1つだけ作り、終了時に削除する。
param([Parameter(Mandatory)][string]$Exe,[int]$TimeoutSeconds=60)
$ErrorActionPreference='Stop'
$Exe=(Resolve-Path -LiteralPath $Exe).Path
$fixtureRoot=Join-Path $PSScriptRoot 'fixtures'
$work=Join-Path ([IO.Path]::GetTempPath()) ('emuera-binput-'+[Guid]::NewGuid().ToString('N'))
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class BInputNative
{
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SendText(IntPtr window, uint message, IntPtr wParam, string text);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] public static extern IntPtr SendValue(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static IntPtr FindRichEdit(IntPtr parent)
    {
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr window, IntPtr parameter)
        {
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (name.ToString().IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0) { result = window; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

function Send-Input([IntPtr]$h,[string]$text){
	[void][BInputNative]::SendText($h,0x000C,[IntPtr]::Zero,$text)
	[void][BInputNative]::SendValue($h,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
	[void][BInputNative]::SendValue($h,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
	Start-Sleep -Milliseconds 300
}

function Read-Logs([string]$run){
	$text=''
	foreach($log in @('startup-test.log','emuera.log')){
		$f=Join-Path $run $log
		if(Test-Path -LiteralPath $f){$text+=[IO.File]::ReadAllText($f)}
	}
	$text
}

$failed=0
$total=0
$texts=@{}
# 1要素ずつ入力欄へ書いてEnterを送る。'0\n2'のような\n入りはマクロとして解釈される
$inputs=@{
	'input'=@(
		'5'      # ボタンに無い数値 → 弾く
		'x'      # 数値でない → 従来どおり弾く
		'1'      # 有効 → GOT_INT=1
		'c'      # ボタンに無い文字列 → 弾く
		'A'      # 大文字小文字は区別 → 弾く
		'b'      # 有効 → GOT_STR=b
		'0\n2'   # 0は前の画面の古いボタン → 弾いてマクロ中断、2は入らない
		'2'      # 有効 → GOT_MACRO=2
	)
	'edge'=@(
		'8'; '3'        # 改行していない行のボタン
		'1'; '4'; '4'   # 同じ行のBINPUTをループしてもボタンは有効なまま。前の[3]は無効
		'99'; '7'       # HTML_PRINTのbutton
		''              # 空Enterはデフォルト値9
		'06'            # 数値として6に一致
		''              # 空文字のボタン
	)
}
function Check([string]$name,[string]$text,[string[]]$must,[string[]]$mustNotLine){
	$script:total++
	$lines=$text -split "\r?\n"
	$miss=@($must | Where-Object { -not $text.Contains($_) })
	# 弾いた値は1行として表示されてはいけない
	$bad=@($mustNotLine | Where-Object { $lines -contains $_ })
	if($text.Length -eq 0 -or $miss.Count -or $bad.Count){
		$script:failed++
		Write-Output "FAIL: $name missing=[$($miss -join ', ')] unexpected=[$($bad -join ', ')]"
		Write-Output ($lines | Select-Object -Last 30 | Out-String)
	}else{
		Write-Output "PASS: $name"
	}
}

try{
	New-Item -ItemType Directory -Path $work | Out-Null

	foreach($name in @('input','edge')){
		$run=Join-Path $work $name
		Copy-Item -LiteralPath (Join-Path $fixtureRoot $name) -Destination $run -Recurse
		$p=Start-Process -FilePath $Exe -ArgumentList @('--ExeDir',('"'+$run+'"')) -WorkingDirectory $run -PassThru
		try{
			$h=[IntPtr]::Zero
			$sw=[Diagnostics.Stopwatch]::StartNew()
			while($h -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt $TimeoutSeconds){
				Start-Sleep -Milliseconds 200
				$p.Refresh()
				# time.logは起動中ずっと書き込み用に開かれているので、読めない間は待つ
				try{$ready=([IO.File]::ReadAllText((Join-Path $run 'time.log'))).Contains('Init:End')}catch{$ready=$false}
				if($ready -and $p.MainWindowHandle -ne [IntPtr]::Zero){$h=[BInputNative]::FindRichEdit($p.MainWindowHandle)}
			}
			if($h -eq [IntPtr]::Zero){throw 'input box not found'}
			foreach($v in $inputs[$name]){Send-Input $h $v}
			$sw.Restart()
			while(-not (Read-Logs $run).Contains('BINPUT_DONE') -and $sw.Elapsed.TotalSeconds -lt $TimeoutSeconds){Start-Sleep -Milliseconds 200}
		}finally{
			if(-not $p.HasExited){Stop-Process -Id $p.Id -Force}
			$p.WaitForExit()
		}
		$texts[$name]=Read-Logs $run
	}
	$text=$texts['input']
	Check 'input' $text @('GOT_INT=1','GOT_STR=b','GOT_MACRO=2','BINPUT_DONE') @('5','x','c','A','0')
	# GOT_MACROより前に2が入っていない（マクロの残りが流れていない）こと
	$total++
	$order=($text -split "\r?\n") | Where-Object { $_ -match '^(2|GOT_MACRO=.*)$' }
	if(($order -join ',') -ne '2,GOT_MACRO=2'){$failed++; Write-Output "FAIL: macro-stop order=[$($order -join ',')]"}else{Write-Output 'PASS: macro-stop'}
	Check 'edge' $texts['edge'] @('E_NOLF=3','E_LOOP=4','E_HTML=7','E_DEF=9','E_ZERO=6','E_EMPTY=[]','BINPUT_DONE') @('8','1','99')
	$total++
	if(([regex]::Matches($texts['edge'],'E_LOOP=4')).Count -ne 2){$failed++; Write-Output 'FAIL: edge-loop count'}else{Write-Output 'PASS: edge-loop'}

	# no-button
	$run=Join-Path $work 'no-button'
	Copy-Item -LiteralPath (Join-Path $fixtureRoot 'no-button') -Destination $run -Recurse
	$p=Start-Process -FilePath $Exe -ArgumentList @('--ExeDir',('"'+$run+'"'),'--StartupTest') -WorkingDirectory $run -WindowStyle Hidden -PassThru
	if(-not $p.WaitForExit($TimeoutSeconds*1000)){Stop-Process -Id $p.Id -Force}
	# 通常のエラーと同じくemuera.logにも残ること
	$f=Join-Path $run 'emuera.log'; $text=if(Test-Path -LiteralPath $f){[IO.File]::ReadAllText($f)}else{''}
	Check 'no-button' $text @('BINPUT:選択できるボタンがありません','NO_BUTTON_START') @('BINPUT_SHOULD_NOT_REACH')
}finally{
	if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force}
}
Write-Output "BINPUT_FIXTURES: $($total - $failed)/$total passed"
if($failed){exit 1}
