[CmdletBinding()]
param([Parameter(Mandatory)][string]$Compiler,[Parameter(Mandatory)][string]$PayloadDirectory,
 [Parameter(Mandatory)][string]$PortableArchive,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$VerifyOnly)
$ErrorActionPreference='Stop'
$payload=(Resolve-Path $PayloadDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if((Get-FileHash $PortableArchive).Hash -ne '653773EC9E3F126684CA9DAD8BCACDF833984047C3E4795902E66FCB2A4E6263'){throw 'Expected immutable signed 1.8.3 portable release archive.'}
if(-not (Test-Path (Join-Path $payload 'SuperPutty.exe'))){throw 'Application payload missing.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path $PortableArchive).Path)
try{
 $files=@($zip.Entries|Where-Object {$_.Name})
 if(@(Get-ChildItem $payload -Recurse -File).Count -ne $files.Count){throw 'Payload file set differs from portable archive.'}
 foreach($entry in $files){
  if([IO.Path]::IsPathRooted($entry.FullName) -or ($entry.FullName -split '[/\\]') -contains '..'){throw 'Invalid portable archive path.'}
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$expected=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}finally{$sha.Dispose();$stream.Dispose()}
  if((Get-FileHash (Join-Path $payload $entry.FullName)).Hash -ne $expected){throw 'Extracted payload differs from immutable archive.'}
 }
}finally{$zip.Dispose()}
if((Get-AuthenticodeSignature (Join-Path $payload 'SuperPutty.exe')).Status -ne 'Valid'){throw 'Published executable signature invalid.'}
if($VerifyOnly){Write-Output 'Prototype payload verification passed.';return}
if(Test-Path $output){throw 'Use a new output directory to preserve prototype evidence.'}
New-Item -ItemType Directory $output|Out-Null
$script=Join-Path (Split-Path $PSScriptRoot -Parent) 'packaging/inno/SuperPuttyPrototype.iss'
foreach($spec in @(@('1.8.4.100','none'),@('1.8.4.101','none'),@('1.8.4.101','payload'),@('1.8.4.101','registration'))){
 & $Compiler "/DPayloadDirectory=$payload" "/DPrototypeVersion=$($spec[0])" "/DFailurePoint=$($spec[1])" "/O$output" $script
 if($LASTEXITCODE -ne 0){throw "Prototype compilation failed: $($spec -join '/')"}
}
Get-ChildItem $output -Filter *.exe|ForEach-Object {[pscustomobject]@{name=$_.Name;sha256=(Get-FileHash $_.FullName).Hash}}|
 ConvertTo-Json|Set-Content (Join-Path $output 'prototype-hashes.json')
