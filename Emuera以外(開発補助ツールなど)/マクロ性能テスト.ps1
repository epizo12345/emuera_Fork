param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\artifacts\publish\Emuera\release_win-x64\Emuera.exe'),
    [string]$GameDir = (Join-Path $PSScriptRoot '..\eramegaten_p\Data'),
    [ValidateSet(10, 100, 1000, 5000)]
    [int]$RepeatCount = 10,
    [ValidateRange(1, 20)]
    [int]$Iterations = 1,
    [ValidateRange(10, 1800)]
    [int]$TimeoutSeconds = 300,
    [switch]$CaptureScreenshots,
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\artifacts\macro-tests')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class EmueraBenchmarkNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern IntPtr SendText(IntPtr window, uint message, IntPtr wParam, string text);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendValue(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    public static IntPtr FindRichEdit(IntPtr parent)
    {
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr window, IntPtr parameter)
        {
            StringBuilder className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            if (className.ToString().IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

$ExePath = [IO.Path]::GetFullPath($ExePath)
$GameDir = [IO.Path]::GetFullPath($GameDir)
$OutputDir = [IO.Path]::GetFullPath($OutputDir)

if ((Test-Path -LiteralPath (Join-Path $GameDir 'Data\erb') -PathType Container) -and
    -not (Test-Path -LiteralPath (Join-Path $GameDir 'erb') -PathType Container)) {
    $GameDir = Join-Path $GameDir 'Data'
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Emuera.exeが見つかりません: $ExePath"
}
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'sav\save219.sav') -PathType Leaf)) {
    throw "save219.savが見つかりません: $GameDir"
}

function Wait-ForWindow([Diagnostics.Process]$Process, [int]$TimeoutMilliseconds) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        if ($Process.HasExited) {
            throw "Emueraが起動中に終了しました: ExitCode=$($Process.ExitCode)"
        }
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero -and $Process.Responding) {
            return
        }
        Start-Sleep -Milliseconds 50
    }
    throw "Emueraのウィンドウ待ちがタイムアウトしました"
}

function Wait-ForStartupComplete(
    [Diagnostics.Process]$Process,
    [string]$TimeLogPath,
    [datetime]$ProcessStartedAtUtc,
    [int]$TimeoutMilliseconds
) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        if ($Process.HasExited) {
            throw "Emueraが起動中に終了しました: ExitCode=$($Process.ExitCode)"
        }
        try {
            $logFile = Get-Item -LiteralPath $TimeLogPath -ErrorAction Stop
            if ($logFile.LastWriteTimeUtc -ge $ProcessStartedAtUtc.AddSeconds(-1)) {
                $timeLog = Get-Content -LiteralPath $TimeLogPath -Raw -ErrorAction Stop
                if ($timeLog -match '(?m)^Init:End\s+\d+ms\s*$') {
                    return $timeLog
                }
            }
        }
        catch [IO.IOException] {
            # Initializeがtime.logを書き込み中。ファイルが閉じられるまで待つ。
        }
        Start-Sleep -Milliseconds 50
    }
    throw "Emueraの操作受付開始（time.logのInit:End）待ちがタイムアウトしました"
}

function Get-InputHandle([Diagnostics.Process]$Process) {
    $Process.Refresh()
    $handle = [EmueraBenchmarkNative]::FindRichEdit($Process.MainWindowHandle)
    if ($handle -eq [IntPtr]::Zero) {
        throw "Emueraの入力欄が見つかりません: PID=$($Process.Id)"
    }
    return $handle
}

function Set-InputText([IntPtr]$InputHandle, [string]$Text) {
    $WM_SETTEXT = 0x000C
    [void][EmueraBenchmarkNative]::SendText($InputHandle, $WM_SETTEXT, [IntPtr]::Zero, $Text)
}

function Send-EnterSynchronously([IntPtr]$InputHandle) {
    $WM_KEYDOWN = 0x0100
    $WM_KEYUP = 0x0101
    $VK_RETURN = 0x0D
    [void][EmueraBenchmarkNative]::SendValue($InputHandle, $WM_KEYDOWN, [IntPtr]$VK_RETURN, [IntPtr]::Zero)
    [void][EmueraBenchmarkNative]::SendValue($InputHandle, $WM_KEYUP, [IntPtr]$VK_RETURN, [IntPtr]::Zero)
}

function Send-TextAndEnter([IntPtr]$InputHandle, [string]$Text) {
    Set-InputText $InputHandle $Text
    Send-EnterSynchronously $InputHandle
}

function Wait-ForMacroQuiescence(
    [Diagnostics.Process]$Process,
    [Diagnostics.Stopwatch]$MacroWatch,
    [double]$CpuBeforeMilliseconds,
    [int]$TimeoutMilliseconds
) {
    $lastCpuMilliseconds = $CpuBeforeMilliseconds
    $lastBusyWallMilliseconds = 0.0
    $quietSinceMilliseconds = $null
    $observedBusy = $false

    while ($MacroWatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        if ($Process.HasExited) {
            throw "Emueraがマクロ実行中に終了しました: ExitCode=$($Process.ExitCode)"
        }
        Start-Sleep -Milliseconds 25
        $Process.Refresh()
        $currentCpuMilliseconds = $Process.TotalProcessorTime.TotalMilliseconds
        $cpuDelta = $currentCpuMilliseconds - $lastCpuMilliseconds
        $lastCpuMilliseconds = $currentCpuMilliseconds

        if ($cpuDelta -ge 1.0) {
            $observedBusy = $true
            $lastBusyWallMilliseconds = $MacroWatch.Elapsed.TotalMilliseconds
            $quietSinceMilliseconds = $null
        }
        elseif ($observedBusy) {
            if ($null -eq $quietSinceMilliseconds) {
                $quietSinceMilliseconds = $MacroWatch.Elapsed.TotalMilliseconds
            }
            elseif ($MacroWatch.Elapsed.TotalMilliseconds - $quietSinceMilliseconds -ge 750.0) {
                return [pscustomobject]@{
                    EstimatedCompletionMilliseconds = [math]::Round($lastBusyWallMilliseconds, 1)
                    ObservedUntilMilliseconds = [math]::Round($MacroWatch.Elapsed.TotalMilliseconds, 1)
                }
            }
        }
    }
    throw "Emueraのマクロ完了（CPU静止）待ちがタイムアウトしました"
}

function Save-WindowScreenshot([Diagnostics.Process]$Process, [string]$Path) {
    $rectangle = [EmueraBenchmarkNative+Rect]::new()
    if (-not [EmueraBenchmarkNative]::GetWindowRect($Process.MainWindowHandle, [ref]$rectangle)) {
        throw "Emueraウィンドウの領域を取得できません"
    }
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($rectangle.Left, $rectangle.Top, 0, 0, $bitmap.Size)
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$runDir = Join-Path $OutputDir (Get-Date -Format 'yyyyMMdd-HHmmss')
[void](New-Item -ItemType Directory -Path $runDir -Force)
$results = [Collections.Generic.List[object]]::new()
$macroText = "(H\e\nd\e\n)*$RepeatCount"
$savePath = Join-Path $GameDir 'sav\save219.sav'
$saveFile = Get-Item -LiteralPath $savePath
$saveHash = (Get-FileHash -LiteralPath $savePath -Algorithm SHA256).Hash

for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $process = $null
    try {
        $startupWatch = [Diagnostics.Stopwatch]::StartNew()
        $processStartedAtUtc = [datetime]::UtcNow
        $process = Start-Process -FilePath $ExePath `
            -ArgumentList @('--ExeDir', ('"{0}"' -f $GameDir)) `
            -WorkingDirectory ([IO.Path]::GetDirectoryName($ExePath)) `
            -PassThru
        Wait-ForWindow $process ($TimeoutSeconds * 1000)
        [void]$process.WaitForInputIdle($TimeoutSeconds * 1000)
        $timeLog = Wait-ForStartupComplete $process (Join-Path $GameDir 'time.log') `
            $processStartedAtUtc ($TimeoutSeconds * 1000)
        $startupWatch.Stop()

        $inputHandle = Get-InputHandle $process
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-title.png" -f $iteration))
        }
        Send-TextAndEnter $inputHandle '1'
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-load-list.png" -f $iteration))
        }
        Send-TextAndEnter $inputHandle '219'
        Start-Sleep -Milliseconds 200
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-loaded.png" -f $iteration))
        }

        Set-InputText $inputHandle $macroText

        $process.Refresh()
        $cpuBefore = $process.TotalProcessorTime.TotalMilliseconds
        $workingSetBefore = $process.WorkingSet64
        $macroWatch = [Diagnostics.Stopwatch]::StartNew()
        Send-EnterSynchronously $inputHandle
        $completion = Wait-ForMacroQuiescence $process $macroWatch $cpuBefore ($TimeoutSeconds * 1000)
        $macroWatch.Stop()
        $process.Refresh()
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-macro-end.png" -f $iteration))
        }

        $result = [pscustomobject]@{
            Iteration = $iteration
            RepeatCount = $RepeatCount
            InputStepsApprox = 4 * $RepeatCount
            MacroText = $macroText
            Save219Sha256 = $saveHash
            Save219LastWriteTimeUtc = $saveFile.LastWriteTimeUtc.ToString('O')
            StartupWindowMilliseconds = [math]::Round($startupWatch.Elapsed.TotalMilliseconds, 1)
            MacroMilliseconds = $completion.EstimatedCompletionMilliseconds
            MacroObservedUntilMilliseconds = $completion.ObservedUntilMilliseconds
            CpuMilliseconds = [math]::Round($process.TotalProcessorTime.TotalMilliseconds - $cpuBefore, 1)
            WorkingSetBeforeBytes = $workingSetBefore
            WorkingSetAfterBytes = $process.WorkingSet64
            PeakWorkingSetBytes = $process.PeakWorkingSet64
            Responding = $process.Responding
            WindowTitle = $process.MainWindowTitle
        }
        $results.Add($result)
        Write-Host ("[{0}/{1}] *{2}: {3}ms / CPU {4}ms / 応答={5}" -f `
            $iteration, $Iterations, $RepeatCount, $result.MacroMilliseconds,
            $result.CpuMilliseconds, $result.Responding)
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(3000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                $process.WaitForExit()
            }
        }
    }
}

$csvPath = Join-Path $runDir ("macro-{0}.csv" -f $RepeatCount)
$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$average = [math]::Round(($results | Measure-Object MacroMilliseconds -Average).Average, 1)
$minimum = [math]::Round(($results | Measure-Object MacroMilliseconds -Minimum).Minimum, 1)
$maximum = [math]::Round(($results | Measure-Object MacroMilliseconds -Maximum).Maximum, 1)
Write-Host ''
Write-Host ("完了: *{0} x {1}回 / 平均={2}ms 最短={3}ms 最長={4}ms" -f `
    $RepeatCount, $Iterations, $average, $minimum, $maximum)
Write-Host ("結果: {0}" -f $csvPath)
