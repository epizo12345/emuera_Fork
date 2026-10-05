param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$data=Join-Path $PSScriptRoot 'fixture/data'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Refusing to overwrite a fixture package'}
New-Item -ItemType Directory -Path $out | Out-Null
$items=@();$total=0L
foreach($kind in @('CSV','ERB')){
 $zipName=$kind.ToLowerInvariant()+'-001.zip'
 $archive=[IO.Compression.ZipFile]::Open((Join-Path $out $zipName),[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($file in Get-ChildItem -LiteralPath (Join-Path $data $kind) -Recurse -File){
  $relative=[IO.Path]::GetRelativePath($data,$file.FullName).Replace('\','/')
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
  $items+=@{path=$relative;size=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash;pack=$zipName};$total+=$file.Length
 }}finally{$archive.Dispose()}
}
@{version=1;totalSize=$total;files=$items}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding utf8NoBOM
Write-Output ('Fixture package: '+$items.Count+' files / '+$total+' bytes')
