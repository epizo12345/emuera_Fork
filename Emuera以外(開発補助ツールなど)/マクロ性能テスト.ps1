# [Emuera改修:TOOLS-04]
# RichEdit class-nameだけに依存せず、画面遷移後も入力欄を再探索する。
# save219.savをロードし、(H\e\nd\e\n)*N をWindowsの入力欄へ送って所要時間を測る。
# N=10は画面確認、100は短い比較、1000は通常比較、5000は耐久試験に使う。
# InternalMetrics付きではEmuera内部のERB・描画・GC等もJSON Linesへ記録する。
# ゲームデータやセーブは書き換えない。使い方: プロジェクト資料/06_コード案内.md
# GameDirは実効DataDir（sav\save219.savとsetting.jsonの親）を指定する。
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\artifacts\publish\Emuera\release_win-x64\Emuera.exe'),
    [string]$GameDir = (Join-Path $PSScriptRoot '..\eramegaten_p\Data'),
    [ValidateSet(10, 100, 1000, 5000)]
    [int]$RepeatCount = 10,
    [ValidateRange(1, 20)]
    [int]$Iterations = 1,
    [ValidateRange(10, 1800)]
    [int]$TimeoutSeconds = 300,
    [switch]$InternalMetrics,
    [switch]$CaptureScreenshots,
    [switch]$CpuProfile,
    [ValidateRange(10, 300)]
    [int]$CpuProfileDurationSeconds = 20,
    [string]$TraceToolPath = (Join-Path $PSScriptRoot '..\artifacts\tools\dotnet-trace.exe'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\artifacts\macro-tests')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies UIAutomationClient,UIAutomationTypes -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

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

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern IntPtr SendText(IntPtr window, uint message, IntPtr wParam, string text);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendValue(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    private static string GetClass(IntPtr window)
    {
        StringBuilder className = new StringBuilder(128);
        GetClassName(window, className, className.Capacity);
        return className.ToString();
    }

    public static IntPtr FindMainWindow(int pid)
    {
        IntPtr hiddenResult = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            GetWindowThreadProcessId(window, out uint windowPid);
            if (windowPid == pid)
            {
                if (IsWindowVisible(window))
                {
                    hiddenResult = window;
                    return false;
                }
                hiddenResult = window;
            }
            return true;
        }, IntPtr.Zero);
        return hiddenResult;
    }

    public static IntPtr FindInput(IntPtr parent)
    {
        if (!GetWindowRect(parent, out Rect parentRect))
            return IntPtr.Zero;
        IntPtr richEdit = IntPtr.Zero;
        IntPtr fallback = IntPtr.Zero;
        int fallbackScore = int.MinValue;
        EnumChildWindows(parent, delegate(IntPtr window, IntPtr parameter)
        {
            string className = GetClass(window);
            bool visible = IsWindowVisible(window);
            bool enabled = IsWindowEnabled(window);
            if (!visible || !enabled || !GetWindowRect(window, out Rect rect))
                return true;
            if (className.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                richEdit = window;
                return false;
            }
            bool editClass = className.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int bottomDistance = parentRect.Bottom - rect.Bottom;
            if (editClass && width >= 200 && height >= 10 && height <= 80 && bottomDistance >= -10 && bottomDistance <= 160)
            {
                int score = width - Math.Abs(bottomDistance);
                if (score > fallbackScore)
                {
                    fallback = window;
                    fallbackScore = score;
                }
            }
            return true;
        }, IntPtr.Zero);
        return richEdit != IntPtr.Zero ? richEdit : fallback;
    }

    public static string DescribeChildren(IntPtr parent)
    {
        var result = new StringBuilder();
        EnumChildWindows(parent, delegate(IntPtr window, IntPtr parameter)
        {
            if (!GetWindowRect(window, out Rect rect))
                return true;
            if (result.Length > 0)
                result.Append("; ");
            result.Append(GetClass(window));
            result.Append(" visible=").Append(IsWindowVisible(window));
            result.Append(" enabled=").Append(IsWindowEnabled(window));
            result.Append(" rect=").Append(rect.Left).Append(',').Append(rect.Top).Append('-').Append(rect.Right).Append(',').Append(rect.Bottom);
            return result.Length < 4096;
        }, IntPtr.Zero);
        return result.ToString();
    }

    public static IntPtr FindAutomationInput(int pid)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            GetWindowThreadProcessId(window, out uint windowPid);
            if (windowPid != pid)
                return true;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                AutomationElementCollection elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                foreach (AutomationElement element in elements)
                {
                    if (string.Equals(element.Current.AutomationId, "richTextBox1", StringComparison.OrdinalIgnoreCase))
                    {
                        result = (IntPtr)element.Current.NativeWindowHandle;
                        return false;
                    }
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

$ExePath = [IO.Path]::GetFullPath($ExePath)
$GameDir = [IO.Path]::GetFullPath($GameDir) # DataDir; FixtureRootを渡した場合だけ既存補正でDataへ解決する。
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$TraceToolPath = [IO.Path]::GetFullPath($TraceToolPath)

if ((Test-Path -LiteralPath (Join-Path $GameDir 'Data\erb') -PathType Container) -and
    -not (Test-Path -LiteralPath (Join-Path $GameDir 'erb') -PathType Container)) {
    $GameDir = Join-Path $GameDir 'Data'
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Emuera.exeが見つかりません: $ExePath"
}
if ($CpuProfile -and -not (Test-Path -LiteralPath $TraceToolPath -PathType Leaf)) {
    throw "dotnet-traceが見つかりません: $TraceToolPath"
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

function Wait-ForInputHandle([Diagnostics.Process]$Process, [int]$TimeoutMilliseconds) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        if ($Process.HasExited) {
            throw "Emueraが入力欄の準備中に終了しました: ExitCode=$($Process.ExitCode)"
        }
        $Process.Refresh()
        $mainHandle = $Process.MainWindowHandle
        if ($mainHandle -eq [IntPtr]::Zero) {
            $mainHandle = [EmueraBenchmarkNative]::FindMainWindow($Process.Id)
        }
        $handle = [EmueraBenchmarkNative]::FindInput($mainHandle)
        if ($handle -eq [IntPtr]::Zero) {
            $handle = [EmueraBenchmarkNative]::FindAutomationInput($Process.Id)
        }
        if ($handle -ne [IntPtr]::Zero) {
            return $handle
        }
        Start-Sleep -Milliseconds 25
    }
    $Process.Refresh()
    $mainHandle = $Process.MainWindowHandle
    if ($mainHandle -eq [IntPtr]::Zero) {
        $mainHandle = [EmueraBenchmarkNative]::FindMainWindow($Process.Id)
    }
    $details = if ($mainHandle -ne [IntPtr]::Zero) {
        [EmueraBenchmarkNative]::DescribeChildren($mainHandle)
    } else { 'main window handle=0' }
    throw "Emueraの入力欄が見つかりません: PID=$($Process.Id); candidates=$details"
}

function Refresh-InputHandle([Diagnostics.Process]$Process, [IntPtr]$CurrentHandle, [int]$TimeoutMilliseconds) {
    $mainHandle = $Process.MainWindowHandle
    if ($mainHandle -eq [IntPtr]::Zero) {
        $mainHandle = [EmueraBenchmarkNative]::FindMainWindow($Process.Id)
    }
    if ($CurrentHandle -ne [IntPtr]::Zero -and [EmueraBenchmarkNative]::FindInput($mainHandle) -eq $CurrentHandle)
    {
        return $CurrentHandle
    }
    return Wait-ForInputHandle $Process $TimeoutMilliseconds
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

function Wait-ForBenchmarkRecord(
    [string]$Path,
    [string]$Type,
    [int]$TimeoutMilliseconds
) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try {
                foreach ($line in Get-Content -LiteralPath $Path -ErrorAction Stop) {
                    if ([string]::IsNullOrWhiteSpace($line)) {
                        continue
                    }
                    $record = $line | ConvertFrom-Json
                    if ($record.type -eq $Type) {
                        return $record
                    }
                }
            }
            catch [IO.IOException] {
                # EmueraがJSON Linesを書き込み中。
            }
        }
        Start-Sleep -Milliseconds 25
    }
    throw "Emueraの計測レコード待ちがタイムアウトしました: type=$Type"
}

function Save-WindowScreenshot([Diagnostics.Process]$Process, [string]$Path) {
    $mainHandle = $Process.MainWindowHandle
    if ($mainHandle -eq [IntPtr]::Zero) {
        $mainHandle = [EmueraBenchmarkNative]::FindMainWindow($Process.Id)
    }
    $rectangle = [EmueraBenchmarkNative+Rect]::new()
    if (-not [EmueraBenchmarkNative]::GetWindowRect($mainHandle, [ref]$rectangle)) {
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
    $traceProcess = $null
    try {
        Write-Host ("[{0}/{1}] Emueraを起動しています" -f $iteration, $Iterations)
        $startupWatch = [Diagnostics.Stopwatch]::StartNew()
        $processStartedAtUtc = [datetime]::UtcNow
        $benchmarkLogPath = Join-Path $runDir ("metrics-{0:D3}.jsonl" -f $iteration)
        # Program.ExeDirはDataDirを受け取る。FixtureRootを直接渡さない。
        $processArguments = @('--ExeDir', ('"{0}"' -f $GameDir))
        if ($InternalMetrics) {
            $processArguments += @('--BenchmarkLog', ('"{0}"' -f $benchmarkLogPath))
        }
        $process = Start-Process -FilePath $ExePath `
            -ArgumentList $processArguments `
            -WorkingDirectory ([IO.Path]::GetDirectoryName($ExePath)) `
            -PassThru
        Write-Host ("[{0}/{1}] PID={2} のウィンドウを待っています" -f $iteration, $Iterations, $process.Id)
        Wait-ForWindow $process ($TimeoutSeconds * 1000)
        [void]$process.WaitForInputIdle($TimeoutSeconds * 1000)
        $timeLog = Wait-ForStartupComplete $process (Join-Path $GameDir 'time.log') `
            $processStartedAtUtc ($TimeoutSeconds * 1000)
        $startupWatch.Stop()
        Write-Host ("[{0}/{1}] 起動完了、save219を読み込みます" -f $iteration, $Iterations)

        $inputHandle = Wait-ForInputHandle $process ($TimeoutSeconds * 1000)
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-title.png" -f $iteration))
        }
        $inputHandle = Refresh-InputHandle $process $inputHandle ($TimeoutSeconds * 1000)
        Send-TextAndEnter $inputHandle '1'
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-load-list.png" -f $iteration))
        }
        $inputHandle = Refresh-InputHandle $process $inputHandle ($TimeoutSeconds * 1000)
        Send-TextAndEnter $inputHandle '219'
        Start-Sleep -Milliseconds 200
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-loaded.png" -f $iteration))
        }

        if ($CpuProfile) {
            $tracePath = Join-Path $runDir ("{0:D3}-macro-cpu.nettrace" -f $iteration)
            $traceOutputPath = Join-Path $runDir ("{0:D3}-macro-cpu.stdout.log" -f $iteration)
            $traceErrorPath = Join-Path $runDir ("{0:D3}-macro-cpu.stderr.log" -f $iteration)
            $traceDuration = [TimeSpan]::FromSeconds($CpuProfileDurationSeconds).ToString('dd\:hh\:mm\:ss')
            $traceProcess = Start-Process -FilePath $TraceToolPath `
                -ArgumentList @('collect', '--providers',
                    'Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1:5',
                    '--process-id', $process.Id,
                    '--duration', $traceDuration, '--output', $tracePath) `
                -RedirectStandardOutput $traceOutputPath `
                -RedirectStandardError $traceErrorPath `
                -WindowStyle Hidden `
                -PassThru
            Start-Sleep -Milliseconds 1000
        }

        $inputHandle = Refresh-InputHandle $process $inputHandle ($TimeoutSeconds * 1000)
        Set-InputText $inputHandle $macroText

        $process.Refresh()
        $cpuBefore = $process.TotalProcessorTime.TotalMilliseconds
        $workingSetBefore = $process.WorkingSet64
        $macroWatch = [Diagnostics.Stopwatch]::StartNew()
        Send-EnterSynchronously $inputHandle
        $metric = if ($InternalMetrics) {
            Wait-ForBenchmarkRecord $benchmarkLogPath 'macro' ($TimeoutSeconds * 1000)
        } else { $null }
        $completion = if ($InternalMetrics) {
            [pscustomobject]@{
                EstimatedCompletionMilliseconds = [double]$metric.totalMilliseconds
                ObservedUntilMilliseconds = $macroWatch.Elapsed.TotalMilliseconds
            }
        } else {
            Wait-ForMacroQuiescence $process $macroWatch $cpuBefore ($TimeoutSeconds * 1000)
        }
        $macroWatch.Stop()
        $process.Refresh()
        if ($CaptureScreenshots) {
            Save-WindowScreenshot $process (Join-Path $runDir ("{0:D3}-macro-end.png" -f $iteration))
        }
        if ($null -ne $traceProcess -and -not $traceProcess.WaitForExit(($CpuProfileDurationSeconds + 15) * 1000)) {
            throw "dotnet-traceの終了待ちがタイムアウトしました: PID=$($traceProcess.Id)"
        }
        if ($null -ne $traceProcess -and ($traceProcess.ExitCode -ne 0 -or
                -not (Test-Path -LiteralPath $tracePath -PathType Leaf))) {
            $traceError = if (Test-Path -LiteralPath $traceErrorPath) {
                Get-Content -LiteralPath $traceErrorPath -Raw
            } else { '' }
            throw "dotnet-traceに失敗しました: ExitCode=$($traceProcess.ExitCode) $traceError"
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
            ExpansionMilliseconds = if ($InternalMetrics) { $metric.expansionMilliseconds } else { $null }
            InputHandoffMilliseconds = if ($InternalMetrics) { $metric.inputHandoffMilliseconds } else { $null }
            InputLoopMilliseconds = if ($InternalMetrics) { $metric.inputLoopMilliseconds } else { $null }
            ErbMilliseconds = if ($InternalMetrics) { $metric.erbMilliseconds } else { $null }
            StringGenerationMilliseconds = if ($InternalMetrics) { $metric.stringGenerationMilliseconds } else { $null }
            DisplayBuildMilliseconds = if ($InternalMetrics) { $metric.displayBuildMilliseconds } else { $null }
            DisplayAddMilliseconds = if ($InternalMetrics) { $metric.displayAddMilliseconds } else { $null }
            MeasureTextMilliseconds = if ($InternalMetrics) { $metric.measureTextMilliseconds } else { $null }
            RefreshMilliseconds = if ($InternalMetrics) { $metric.refreshMilliseconds } else { $null }
            PaintMilliseconds = if ($InternalMetrics) { $metric.paintMilliseconds } else { $null }
            ScrollMilliseconds = if ($InternalMetrics) { $metric.scrollMilliseconds } else { $null }
            UiEventMilliseconds = if ($InternalMetrics) { $metric.uiEventMilliseconds } else { $null }
            ExpandedInputCount = if ($InternalMetrics) { $metric.expandedInputCount } else { $null }
            InputDispatchCount = if ($InternalMetrics) { $metric.inputDispatchCount } else { $null }
            ErbRunCount = if ($InternalMetrics) { $metric.erbRunCount } else { $null }
            RefreshCount = if ($InternalMetrics) { $metric.refreshCount } else { $null }
            PaintCount = if ($InternalMetrics) { $metric.paintCount } else { $null }
            ScrollUpdateCount = if ($InternalMetrics) { $metric.scrollUpdateCount } else { $null }
            UiEventPumpCount = if ($InternalMetrics) { $metric.uiEventPumpCount } else { $null }
            StringGenerationCount = if ($InternalMetrics) { $metric.stringGenerationCount } else { $null }
            DisplayBuildCount = if ($InternalMetrics) { $metric.displayBuildCount } else { $null }
            MeasureTextCount = if ($InternalMetrics) { $metric.measureTextCount } else { $null }
            AllocatedBytes = if ($InternalMetrics) { $metric.allocatedBytes } else { $null }
            ManagedBytesBefore = if ($InternalMetrics) { $metric.managedBytesBefore } else { $null }
            ManagedBytesAfter = if ($InternalMetrics) { $metric.managedBytesAfter } else { $null }
            Gen0Collections = if ($InternalMetrics) { $metric.gen0Collections } else { $null }
            Gen1Collections = if ($InternalMetrics) { $metric.gen1Collections } else { $null }
            Gen2Collections = if ($InternalMetrics) { $metric.gen2Collections } else { $null }
            StateSha256 = if ($InternalMetrics) { $metric.stateSha256 } else { $null }
            DisplaySha256 = if ($InternalMetrics) { $metric.displaySha256 } else { $null }
            RandomCallCount = if ($InternalMetrics) { $metric.randomCallCount } else { $null }
            RandomTraceHash = if ($InternalMetrics) { $metric.randomTraceHash } else { $null }
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
        if ($null -ne $traceProcess -and -not $traceProcess.HasExited) {
            Stop-Process -Id $traceProcess.Id -Force -ErrorAction SilentlyContinue
            $traceProcess.WaitForExit()
        }
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
