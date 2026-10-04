param(
    [Parameter(Mandatory)][string]$PackagerExePath,
    [Parameter(Mandatory)][string]$RuntimeTemplatePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ArtifactsPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$SourceTreeSha256
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packagerExe = (Resolve-Path -LiteralPath $PackagerExePath).Path
$runtimeSource = (Resolve-Path -LiteralPath $RuntimeTemplatePath).Path
$licenseSource = Join-Path $repoRoot 'license.md'
$notesSource = Join-Path $PSScriptRoot 'RELEASE_NOTES.md'
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$buildArtifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
$zipName = 'EmueraWebPackager-1.0.7-win-x64.zip'
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
foreach ($requiredLicense in @('licenses/DOTNET-LICENSE.txt', 'licenses/DOTNET-THIRD-PARTY-NOTICES.txt', 'licenses/DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt', 'licenses/DOTNET-WINX64-RUNTIME-THIRD-PARTY-NOTICES.txt', 'licenses/DOTNET-WINDOWSDESKTOP-LICENSE.txt', 'licenses/AngleSharp-MIT.txt', 'licenses/System.IO.Hashing-MIT.txt', 'licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $requiredLicense) -PathType Leaf)) { throw "Packager配布に必要なライセンスがありません: $requiredLicense" }
}
if (!(Test-Path -LiteralPath (Join-Path $runtimeSource 'fonts/DotGothic16-OFL.txt') -PathType Leaf)) { throw 'runtime-templateにDotGothic16 OFL noticeがありません。' }

# P1 query-only test assets are not part of the user's production runtime template.
$runtimeFixtureRoots = @('p1a-fixture', 'p1a-r1-guards', 'p1a-r2-fixture', 'p1c1-fixture', 'p1c2-host-fixture')
$templateFiles = @(Get-ChildItem -LiteralPath $runtimeSource -File -Recurse -Force | Where-Object {
    $relative = [System.IO.Path]::GetRelativePath($runtimeSource, $_.FullName).Replace('\', '/')
    $runtimeFixtureRoots -notcontains $relative.Split('/')[0]
})
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
if ($version -ne 'Emuera Web Packager 1.0.7 / Runtime UX16-08-RC3-Compat107') { throw "Packager/Runtime versionが想定外です: $version" }

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

    # ゲーム入りWeb出力にも通知を引き継ぐため、seal前にtemplateへ同梱する。
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $runtimeTarget 'LICENSE.md')
    $runtimeLicenseTarget = Join-Path $runtimeTarget 'licenses'
    New-Item -ItemType Directory -Force -Path $runtimeLicenseTarget | Out-Null
    foreach ($notice in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -File) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $runtimeLicenseTarget $notice.Name) -Force
    }

    # Record the committed build revision and the exact source file table.
    $provenance = [ordered]@{
        packagerVersion = '1.0.7'
        runtimeTemplateVersion = 'UX16-08-RC3-Compat107'
        sourceCommit = $SourceCommit.ToLowerInvariant()
        sourceTreeSha256 = $SourceTreeSha256.ToUpperInvariant()
        sourceState = 'committed'
        sourceIdentity = 'committed source revision and source file table SHA-256'
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $bundle 'build-provenance.json') -Value $provenance -Encoding utf8
    Set-Content -LiteralPath (Join-Path $runtimeTarget 'build-provenance.json') -Value $provenance -Encoding utf8
    $notes = (Get-Content -LiteralPath $notesSource -Raw).Replace('{{SOURCE_COMMIT}}', $SourceCommit.ToLowerInvariant()).Replace('{{SOURCE_TREE_SHA256}}', $SourceTreeSha256.ToUpperInvariant())
    Set-Content -LiteralPath (Join-Path $bundle 'RELEASE_NOTES.md') -Value $notes -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination (Join-Path $bundle 'CHANGELOG.md')

    # 既存Packager CoreのSealTemplateを利用し、形式を二重実装しない。
    $sealProject = Join-Path $PSScriptRoot 'Tests/EmueraWebPackager.Tests.csproj'
    $sealAssembly = Join-Path $buildArtifacts 'bin/EmueraWebPackager.Tests/release/EmueraWebPackager.Tests.dll'
    if (!(Test-Path -LiteralPath $sealAssembly -PathType Leaf)) { throw 'Release bundle作成前にbuild.ps1 -Mode Testを実行してください。' }
    & dotnet run --project $sealProject -c Release --artifacts-path $buildArtifacts --no-restore --no-build -- seal $runtimeTarget
    if ($LASTEXITCODE -ne 0) { throw "Packager Coreのtemplate sealに失敗しました (exit $LASTEXITCODE)" }
    $templateManifest = Join-Path $runtimeTarget '.template-manifest.json'
    if (!(Test-Path -LiteralPath $templateManifest -PathType Leaf)) { throw 'Packager Coreがtemplate manifestを生成しませんでした。' }
    $sealedTemplateFiles = @(Get-Content -LiteralPath $templateManifest -Raw | ConvertFrom-Json)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $outputStage $zipName
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tempRoot, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
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
        version = '1.0.7'
        runtime = 'UX16-08-RC3-Compat107'
        sourceCommit = $SourceCommit.ToLowerInvariant()
        sourceTreeSha256 = $SourceTreeSha256.ToUpperInvariant()
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
