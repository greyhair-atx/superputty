[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory,[Parameter(Mandatory)][string]$SbomPath,[switch]$Installed)
$ErrorActionPreference='Stop'
$root=(Get-Item -LiteralPath $PayloadDirectory -Force).FullName.TrimEnd('\','/')
if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Payload contains a reparse point.'}
$sbom=Get-Content -LiteralPath $SbomPath -Raw|ConvertFrom-Json
if($sbom.bomFormat -ne 'CycloneDX' -or @($sbom.components).Count -eq 0){throw 'Invalid payload SBOM.'}
$expected=@{};$directories=@{}
foreach($component in $sbom.components){
 $name=[string]$component.name
 if($name -notmatch '^[a-zA-Z0-9_.-]+(/[a-zA-Z0-9_.-]+)*$' -or ($name -split '/') -contains '..' -or ($name -split '/') -contains '.' -or $name -eq '.payload.manifest' -or $expected.ContainsKey($name)){throw 'Invalid or duplicate SBOM payload path.'}
 if(@($component.hashes).Count -ne 1 -or $component.hashes[0].alg -ne 'SHA-256' -or $component.hashes[0].content -notmatch '^[a-fA-F0-9]{64}$'){throw 'Missing or invalid payload hash.'}
 $expected[$name]=[string]$component.hashes[0].content
 $parent=$name
 while($parent.Contains('/')){$parent=$parent.Substring(0,$parent.LastIndexOf('/'));$directories[$parent]=$true}
}
# Walk explicitly so hidden entries are counted and links are rejected before descent.
$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($root)
$seen=@{};$manifestFound=$false
while($pending.Count){
 foreach($entry in @(Get-ChildItem -LiteralPath $pending.Pop() -Force)){
  if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Payload contains a reparse point.'}
  $name=$entry.FullName.Substring($root.Length+1).Replace('\','/')
  if($entry.PSIsContainer){
   if(-not $directories.ContainsKey($name)){throw "Unexpected payload directory: $name"}
   $pending.Push($entry.FullName);continue
  }
  if($Installed -and $name -ceq '.payload.manifest'){$manifestFound=$true;continue}
  if(-not $expected.ContainsKey($name)){throw "Unexpected payload file: $name"}
  if((Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash -ne $expected[$name]){throw "Payload hash mismatch: $name"}
  $seen[$name]=$true
 }
}
if($seen.Count -ne $expected.Count){throw 'Payload files are missing.'}
if($Installed){
 if(-not $manifestFound){throw 'Installed payload manifest is missing.'}
 $recorded=@{}
 foreach($line in @(Get-Content -LiteralPath (Join-Path $root '.payload.manifest'))){
  if($line -notmatch '^([a-fA-F0-9]{64}) \*(.+)$'){throw 'Installed payload manifest is invalid.'}
  $hash=$Matches[1];$name=$Matches[2].Replace('\','/')
  if(-not $expected.ContainsKey($name) -or $recorded.ContainsKey($name) -or $hash -ne $expected[$name]){throw 'Installed payload manifest differs from the SBOM.'}
  $recorded[$name]=$true
 }
 if($recorded.Count -ne $expected.Count){throw 'Installed payload manifest is incomplete.'}
}
Write-Output "Verified $($seen.Count) exact payload files against the SBOM; installed=$([bool]$Installed)."
