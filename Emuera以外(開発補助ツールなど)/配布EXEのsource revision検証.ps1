[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedSourceRevision
)

$ErrorActionPreference = 'Stop'
$exe = [IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "EXEが見つかりません: $exe"
}

$actualProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
$separator = $actualProductVersion.LastIndexOf('+')
$actualSourceRevision = if ($separator -ge 0) { $actualProductVersion.Substring($separator + 1) } else { '' }
$expected = $ExpectedSourceRevision.ToLowerInvariant()

Write-Output "期待source revision: $expected"
Write-Output "実EXE ProductVersion: $actualProductVersion"
Write-Output "実EXE source revision: $($actualSourceRevision.ToLowerInvariant())"

if ($actualSourceRevision.ToLowerInvariant() -ne $expected) {
    Write-Output '結果: FAIL（ProductVersion suffixがpublish元source commitと一致しません）'
    exit 1
}

Write-Output '結果: PASS'
