[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[string]$Version='1.8.0',[string]$SchemaFile)
$ErrorActionPreference='Stop'
$prefix="SuperPuTTY-CE-$Version"
$manifest=Get-Content (Join-Path $ReleaseDirectory "$prefix-build-manifest.json") -Raw|ConvertFrom-Json
if($manifest.version -ne $Version -or $manifest.packages.Count -ne 2){throw 'Unexpected release version or package count.'}
$names=@($manifest.packages.name)+@($manifest.portable,$manifest.sbom,"$prefix-build-manifest.json")
if(@($names|Select-Object -Unique).Count -ne 5){throw 'Expected five distinct assets plus checksums.'}
foreach($name in $names){if($name -ne [IO.Path]::GetFileName($name) -or $name -notlike "$prefix-*"){throw 'Invalid artifact filename.'}}
$checksums=@(Get-Content (Join-Path $ReleaseDirectory "$prefix-SHA256SUMS.txt"))
if($checksums.Count -ne 5){throw 'Checksum manifest must contain all five assets.'}
$seen=@()
foreach($line in $checksums){
 if($line -cnotmatch '^([0-9a-f]{64})  (.+)$'){throw 'Invalid checksum line.'}
 $hash=$Matches[1];$name=$Matches[2]
 if($name -notin $names -or $name -in $seen){throw 'Unexpected or duplicate checksum asset.'}
 if((Get-FileHash (Join-Path $ReleaseDirectory $name)).Hash -ne $hash){throw "Checksum mismatch: $name"}
 $seen+=$name
}
foreach($package in $manifest.packages){if((Get-FileHash (Join-Path $ReleaseDirectory $package.name)).Hash -ne $package.sha256){throw 'Build manifest MSI hash mismatch.'}}
$sbomPath=Join-Path $ReleaseDirectory $manifest.sbom
if($SchemaFile -and -not (Test-Json -Path $sbomPath -SchemaFile $SchemaFile)){throw 'CycloneDX schema validation failed.'}
$sbom=Get-Content $sbomPath -Raw|ConvertFrom-Json
if($sbom.bomFormat -ne 'CycloneDX' -or $sbom.specVersion -ne '1.6'){throw 'Unexpected SBOM format.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath((Join-Path $ReleaseDirectory $manifest.portable)))
try{
 $files=@($zip.Entries|Where-Object {$_.Name})
 if($files.Count -ne 57 -or $sbom.components.Count -ne $files.Count){throw 'Unexpected ZIP/SBOM file count.'}
 foreach($file in $files){
  $name=$file.FullName.Replace('\','/')
  $component=@($sbom.components|Where-Object name -CEQ $name)
  if($component.Count -ne 1){throw "Missing/duplicate SBOM file: $name"}
  $stream=$file.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
  if($component[0].hashes[0].alg -ne 'SHA-256' -or $component[0].hashes[0].content -ne $hash){throw "SBOM hash mismatch: $name"}
 }
}finally{$zip.Dispose()}
Write-Output 'Release bundle verified: five asset checksums, two MSI manifest hashes, 57 exact ZIP/SBOM files.'
