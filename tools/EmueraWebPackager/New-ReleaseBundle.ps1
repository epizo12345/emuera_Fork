param(
    [Parameter(Mandatory)][string]$PackagerExePath,
    [Parameter(Mandatory)][string]$RuntimeTemplatePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ArtifactsPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packagerExe = (Resolve-Path -LiteralPath $PackagerExePath).Path
$runtimeSource = (Resolve-Path -LiteralPath $RuntimeTemplatePath).Path
$licenseSource = Join-Path $repoRoot 'license.md'
$notesSource = Join-Path $PSScriptRoot 'RELEASE_NOTES.md'
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$buildArtifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
$zipName = 'EmueraWebPackager-1.0.1-win-x64.zip'
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase "EmueraWebPackager-Release-$([guid]::NewGuid().ToString('N'))"))
$outputParent = Split-Path -Parent $output
$outputStage = Join-Path $outputParent ".EmueraWebPackager-Release-$([guid]::NewGuid().ToString('N'))"

function Test-PathWithin([string]$Path, [string]$Parent) {
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    return $fullPath.Equals($fullParent, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullParent + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

if (Test-Path -LiteralPath $output) { throw "出力先は既にあります。既存物を上書きしません: $output" }
if ((Test-PathWithin $output $repoRoot) -or (Test-PathWithin $output $runtimeSource) -or (Test-PathWithin $runtimeSource $output)) { throw "出力先はrepoやRuntime templateの外を指定してください: $output" }
if (Test-PathWithin $buildArtifacts $repoRoot) { throw "MSBuild成果物はsource exportの外を指定してください: $buildArtifacts" }
if ((Test-PathWithin $output $buildArtifacts) -or (Test-PathWithin $buildArtifacts $output)) { throw 'Release成果物とMSBuild成果物は別directoryへ分離してください。' }
if (Test-PathWithin $output (Split-Path -Parent $packagerExe)) { throw "出力先はPackager実行ファイルのbuild output外を指定してください: $output" }
if (!(Test-Path -LiteralPath (Join-Path $runtimeSource 'index.html')) -or !(Test-Path -LiteralPath (Join-Path $runtimeSource '_framework') -PathType Container)) { throw 'Runtime publishにはindex.htmlと_frameworkが必要です。' }
if (!(Test-Path -LiteralPath $licenseSource -PathType Leaf) -or !(Test-Path -LiteralPath $notesSource -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path $PSScriptRoot '使い方.md') -PathType Leaf)) { throw 'Packager license、使い方、またはRELEASE_NOTES.mdがありません。' }
foreach ($requiredLicense in @('licenses/DOTNET-LICENSE.txt', 'licenses/DOTNET-THIRD-PARTY-NOTICES.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $requiredLicense) -PathType Leaf)) { throw "Packager配布に必要なライセンスがありません: $requiredLicense" }
}
if (!(Test-Path -LiteralPath (Join-Path $runtimeSource 'fonts/DotGothic16-OFL.txt') -PathType Leaf)) { throw 'runtime-templateにDotGothic16 OFL noticeがありません。' }

$templateFiles = @(Get-ChildItem -LiteralPath $runtimeSource -File -Recurse -Force)
foreach ($file in $templateFiles) {
    $relative = [System.IO.Path]::GetRelativePath($runtimeSource, $file.FullName).Replace('\', '/')
    if ($relative -match '(^|/)(p1b-data|reports|artifacts|browser-profiles|user-data-dir)(/|$)' -or $relative -match '\.sav$') {
        throw "runtime-templateにゲームデータ、セーブ、または診断物があります: $relative"
    }
}

$versionStart = [System.Diagnostics.ProcessStartInfo]::new($packagerExe)
$versionStart.ArgumentList.Add('--version')
$versionStart.UseShellExecute = $false
$versionStart.RedirectStandardOutput = $true
$versionStart.RedirectStandardError = $true
$versionProcess = [System.Diagnostics.Process]::Start($versionStart)
try {
    $version = $versionProcess.StandardOutput.ReadToEnd().Trim()
    $versionError = $versionProcess.StandardError.ReadToEnd().Trim()
    $versionProcess.WaitForExit()
    if ($versionProcess.ExitCode -ne 0) { throw "Packager --versionに失敗しました (exit $($versionProcess.ExitCode)): $versionError" }
}
finally { $versionProcess.Dispose() }
if ($version -ne 'Emuera Web Packager 1.0.1 / Runtime UX16-07') { throw "Packager/Runtime versionが想定外です: $version" }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
try {
    New-Item -ItemType Directory -Path $outputStage | Out-Null
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $bundle = Join-Path $tempRoot 'EmueraWebPackager'
    $runtimeTarget = Join-Path $bundle 'runtime-template'
    New-Item -ItemType Directory -Path $runtimeTarget | Out-Null
    Copy-Item -LiteralPath $packagerExe -Destination (Join-Path $bundle 'EmueraWebPackager.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使い方.md') -Destination (Join-Path $bundle '使い方.md')
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $bundle 'LICENSE.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination (Join-Path $bundle 'licenses') -Recurse

    foreach ($file in $templateFiles) {
        $relative = [System.IO.Path]::GetRelativePath($runtimeSource, $file.FullName)
        $target = Join-Path $runtimeTarget $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }

    # 既存Packager CoreのSealTemplateを利用し、形式を二重実装しない。
    $sealProject = Join-Path $PSScriptRoot 'Tests/EmueraWebPackager.Tests.csproj'
    & dotnet run --project $sealProject -c Release --artifacts-path $buildArtifacts --no-restore -- seal $runtimeTarget
    if ($LASTEXITCODE -ne 0) { throw "Packager Coreのtemplate sealに失敗しました (exit $LASTEXITCODE)" }
    $templateManifest = Join-Path $runtimeTarget '.template-manifest.json'
    if (!(Test-Path -LiteralPath $templateManifest -PathType Leaf)) { throw 'Packager Coreがtemplate manifestを生成しませんでした。' }
    $sealedTemplateFiles = @(Get-Content -LiteralPath $templateManifest -Raw | ConvertFrom-Json)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $outputStage $zipName
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tempRoot, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $notes = (Get-Content -LiteralPath $notesSource -Raw).Replace('{{SOURCE_COMMIT}}', $SourceCommit.ToLowerInvariant())
    Set-Content -LiteralPath (Join-Path $outputStage 'RELEASE_NOTES.md') -Value $notes -Encoding utf8
    $sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
    Set-Content -LiteralPath (Join-Path $outputStage 'SHA256SUMS.txt') -Value "$sha  $zipName" -Encoding ascii

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($required in @('EmueraWebPackager/EmueraWebPackager.exe', 'EmueraWebPackager/使い方.md', 'EmueraWebPackager/LICENSE.md', 'EmueraWebPackager/licenses/DOTNET-LICENSE.txt', 'EmueraWebPackager/licenses/DOTNET-THIRD-PARTY-NOTICES.txt', 'EmueraWebPackager/runtime-template/.template-manifest.json', 'EmueraWebPackager/runtime-template/index.html')) {
            if ($names -notcontains $required) { throw "ZIPに必要なファイルがありません: $required" }
        }
        if (!($names | Where-Object { $_ -like 'EmueraWebPackager/runtime-template/_framework/*' })) { throw 'ZIP内のruntime-templateに_frameworkがありません。' }
        $forbidden = @($names | Where-Object { $_ -match '(^|/)(p1b-data|reports|artifacts|browser-profiles|user-data-dir)(/|$)' -or $_ -match '\.sav$' -or $_ -match '^[A-Za-z]:' -or $_.StartsWith('/') -or $_.StartsWith('\') })
        if ($forbidden.Count) { throw "ZIPに禁止pathがあります: $($forbidden -join ', ')" }
    }
    finally { $archive.Dispose() }

    [pscustomobject]@{
        version = '1.0.1'
        runtime = 'UX16-07'
        sourceCommit = $SourceCommit.ToLowerInvariant()
        zip = $zipName
        zipBytes = (Get-Item -LiteralPath $zipPath).Length
        zipSha256 = $sha
        templateFileCount = $sealedTemplateFiles.Count
        templateManifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $templateManifest).Hash
        containsGameData = $false
        containsSaves = $false
        uploadPerformed = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputStage 'release-bundle.json') -Encoding utf8
    Move-Item -LiteralPath $outputStage -Destination $output
    Write-Output "PASS Release ZIP: $(Join-Path $output $zipName)"
    Write-Output "SHA-256: $sha"
}
finally {
    $tempPrefix = $tempBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ((Test-Path -LiteralPath $tempRoot) -and $tempRoot.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $tempRoot) -match '^EmueraWebPackager-Release-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
    $outputParentPrefix = [System.IO.Path]::GetFullPath($outputParent).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ((Test-Path -LiteralPath $outputStage) -and $outputStage.StartsWith($outputParentPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $outputStage) -match '^\.EmueraWebPackager-Release-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $outputStage -Recurse -Force
    }
}
