param(
    [Parameter(Mandatory)][ValidateSet('Test', 'Publish')][string]$Mode,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')][string]$Name,
    [string]$OutputDirectory,
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repoRoot "artifacts/EmueraWebPackager/$Name" } else { [System.IO.Path]::GetFullPath($OutputDirectory) }
$buildArtifacts = if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) { Join-Path $repoRoot 'artifacts' } else { [System.IO.Path]::GetFullPath($ArtifactsPath) }
if (Test-Path -LiteralPath $outputRoot) { throw "出力先が既にあります。別のNameを指定してください: $outputRoot" }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$oldTemp = $env:TEMP
$oldTmp = $env:TMP
$oldTmpDir = $env:TMPDIR
$oldAppData = $env:APPDATA
$oldLocalAppData = $env:LOCALAPPDATA
$oldDotnetCliHome = $env:DOTNET_CLI_HOME
$processTemp = Join-Path $oldTemp "EmueraWebPackager-$Name"
if (Test-Path -LiteralPath $processTemp) { throw "一時出力先が既にあります。別のNameを指定してください: $processTemp" }
New-Item -ItemType Directory -Path $processTemp | Out-Null
$processAppData = Join-Path $processTemp 'AppData/Roaming'
$processLocalAppData = Join-Path $processTemp 'AppData/Local'
$processCliHome = Join-Path $processTemp 'dotnet-home'
New-Item -ItemType Directory -Path $processAppData,$processLocalAppData,$processCliHome | Out-Null

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet exited with code ${LASTEXITCODE}: dotnet $($Arguments -join ' ')" }
}

$gui = Join-Path $PSScriptRoot 'Gui/EmueraWebPackager.Gui.csproj'
try {
    $env:TEMP = $processTemp
    $env:TMP = $processTemp
    $env:TMPDIR = $processTemp
    $env:APPDATA = $processAppData
    $env:LOCALAPPDATA = $processLocalAppData
    $env:DOTNET_CLI_HOME = $processCliHome
    if ($Mode -eq 'Test') {
        $coreResult = Join-Path $outputRoot 'core-tests'
        $guiResult = Join-Path $outputRoot 'gui-tests'
        $artifactArgs = @('--artifacts-path', $buildArtifacts)
        Invoke-Dotnet (@('run', '--project', (Join-Path $PSScriptRoot 'Tests/EmueraWebPackager.Tests.csproj'), '-c', 'Release') + $artifactArgs + @('--', 'focused', $coreResult))
        Invoke-Dotnet (@('run', '--project', (Join-Path $PSScriptRoot 'Gui.Tests/EmueraWebPackager.Gui.Tests.csproj'), '-c', 'Release', '-p:TargetPlatformDisplayName=Windows') + $artifactArgs + @('--', $guiResult))
        return
    }

    $publishRoot = Join-Path $outputRoot 'publish'
    Invoke-Dotnet @('publish', $gui, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:TargetPlatformDisplayName=Windows', '-p:PublishSingleFile=true', '-p:RestoreIgnoreFailedSources=true', '-p:NuGetAudit=false', '--artifacts-path', $buildArtifacts, '-o', $publishRoot)
}
finally {
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
    $env:TMPDIR = $oldTmpDir
    $env:APPDATA = $oldAppData
    $env:LOCALAPPDATA = $oldLocalAppData
    $env:DOTNET_CLI_HOME = $oldDotnetCliHome
}
