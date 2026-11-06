[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[Parameter(Mandatory)][string]$ResultDirectory,[string]$Version='1.8.4')
$ErrorActionPreference='Stop'
$root=(Resolve-Path $ReleaseDirectory).Path
if(Test-Path $ResultDirectory){throw 'Use a new test result directory.'}
$null=New-Item -ItemType Directory $ResultDirectory
$results=(Resolve-Path $ResultDirectory).Path
$prefix="SuperPuTTY-CE-$Version"
$manifestName="$prefix-build-manifest.json"
$manifest=Get-Content "$root/$manifestName" -Raw|ConvertFrom-Json
& "$PSScriptRoot/Verify-InnoReleaseBundle.ps1" -ReleaseDirectory $root -Version $Version
$checks=[Collections.Generic.List[string]]::new()
$checks.Add('Valid bundle accepted')
function Rehash([string]$Directory){
 Get-Content "$root/$prefix-SHA256SUMS.txt"|ForEach-Object {
  $name=$_.Substring(66)
  "$((Get-FileHash "$Directory/$name").Hash.ToLowerInvariant())  $name"
 }|Set-Content "$Directory/$prefix-SHA256SUMS.txt" -Encoding ascii
}
function Reject([string]$Name,[scriptblock]$Mutate,[string]$Expected,[switch]$RequireCleanSource){
 $case=Join-Path $results $Name
 $null=New-Item -ItemType Directory $case
 Get-ChildItem $root -File -Force|Copy-Item -Destination $case
 & $Mutate $case
 $rejected=$false
 try{& "$PSScriptRoot/Verify-InnoReleaseBundle.ps1" -ReleaseDirectory $case -Version $Version -RequireCleanSource:$RequireCleanSource|Out-Null}
 catch{if($_.Exception.Message -notmatch $Expected){throw};$rejected=$true}
 if(-not $rejected){throw "Verifier accepted invalid case: $Name"}
 $checks.Add("Rejected: $Name")
}
Reject 'private-file' {param($d) Set-Content "$d/private-signing.txt" 'must never ship'} 'unlisted assets'
Reject 'hidden-private-file' {param($d)
 $path=Join-Path $d 'private-signing.txt';Set-Content $path 'test sentinel; no credentials'
 (Get-Item -LiteralPath $path).Attributes=[IO.FileAttributes]::Hidden
} 'unlisted assets'
Reject 'hidden-directory' {param($d)
 $entry=New-Item -ItemType Directory (Join-Path $d 'private-work');$entry.Attributes=[IO.FileAttributes]::Hidden
} 'unlisted assets'
Reject 'missing-dirty-status' {param($d)
 $m=Get-Content "$d/$manifestName" -Raw|ConvertFrom-Json;$m.PSObject.Properties.Remove('sourceDirty')
 $m|ConvertTo-Json -Depth 12|Set-Content "$d/$manifestName" -Encoding utf8;Rehash $d
} 'explicit Boolean' -RequireCleanSource
Reject 'null-dirty-status' {param($d)
 $m=Get-Content "$d/$manifestName" -Raw|ConvertFrom-Json;$m.sourceDirty=$null
 $m|ConvertTo-Json -Depth 12|Set-Content "$d/$manifestName" -Encoding utf8;Rehash $d
} 'explicit Boolean' -RequireCleanSource
Reject 'string-dirty-status' {param($d)
 $m=Get-Content "$d/$manifestName" -Raw|ConvertFrom-Json;$m.sourceDirty='false'
 $m|ConvertTo-Json -Depth 12|Set-Content "$d/$manifestName" -Encoding utf8;Rehash $d
} 'explicit Boolean' -RequireCleanSource
Reject 'dirty-source' {param($d)
 $m=Get-Content "$d/$manifestName" -Raw|ConvertFrom-Json;$m.sourceDirty=$true
 $m|ConvertTo-Json -Depth 12|Set-Content "$d/$manifestName" -Encoding utf8;Rehash $d
} 'frozen clean source' -RequireCleanSource
$clean=Join-Path $results 'clean-source-fixture';$null=New-Item -ItemType Directory $clean
Get-ChildItem $root -File -Force|Copy-Item -Destination $clean
$cleanManifest=Get-Content "$clean/$manifestName" -Raw|ConvertFrom-Json;$cleanManifest.sourceDirty=$false
$cleanManifest|ConvertTo-Json -Depth 12|Set-Content "$clean/$manifestName" -Encoding utf8;Rehash $clean
& "$PSScriptRoot/Verify-InnoReleaseBundle.ps1" -ReleaseDirectory $clean -Version $Version -RequireCleanSource|Out-Null
$checks.Add('Explicit Boolean false accepted by clean-source gate (synthetic fixture)')
Reject 'changed-archive' {param($d) Add-Content "$d/$($manifest.portable)" 'tampered'} 'checksum'
Reject 'wrong-app-version' {param($d)
 $m=Get-Content "$d/$manifestName" -Raw|ConvertFrom-Json;$m.applicationVersion='1.0.0.0'
 $m|ConvertTo-Json -Depth 12|Set-Content "$d/$manifestName" -Encoding utf8;Rehash $d
} 'identity/version'
Reject 'changed-inventory' {param($d)
 $s=Get-Content "$d/$($manifest.sbom)" -Raw|ConvertFrom-Json
 $s.components[0].hashes[0].content=('0'*64)
 $s|ConvertTo-Json -Depth 12|Set-Content "$d/$($manifest.sbom)" -Encoding utf8;Rehash $d
} 'differ from the SBOM'
Reject 'changed-payload-provenance' {param($d)
 $m=Get-Content "$d/$manifestName" -Raw|ConvertFrom-Json;$m.payloadFiles+=@('extra.exe')
 $m|ConvertTo-Json -Depth 12|Set-Content "$d/$manifestName" -Encoding utf8;Rehash $d
} 'provenance contains extra'
if($manifest.portableOnly){
 $rejected=$false
 try{& "$PSScriptRoot/New-InnoWinGetManifest.ps1" -ReleaseDirectory $root -Version $Version -ManifestDirectory "$results/invalid-winget"|Out-Null}catch{if($_.Exception.Message -notmatch 'signed Inno installer is required'){throw};$rejected=$true}
 if(-not $rejected){throw 'Unsigned portable bundle accepted for WinGet.'}
 $checks.Add('Unsigned portable bundle rejected for WinGet')
}else{
 Reject 'fault-fixture' {param($d)
  $m=Get-Content "$d/$manifestName" -Raw|ConvertFrom-Json;$m.installerBuild.testFixture=$true
  $m|ConvertTo-Json -Depth 12|Set-Content "$d/$manifestName" -Encoding utf8;Rehash $d
 } 'fault-enabled'
 & "$PSScriptRoot/New-InnoWinGetManifest.ps1" -ReleaseDirectory $root -Version $Version -ManifestDirectory "$results/winget"|Out-Null
 $yaml=Get-Content "$results/winget/ChrisThornton.SuperPuTTYCommunityEdition.installer.yaml" -Raw
 if(([regex]::Matches($yaml,'(?m)^  Scope: (user|machine)\r?$')).Count -ne 2 -or $yaml -notmatch '/CURRENTUSER' -or $yaml -notmatch '/ALLUSERS' -or $yaml -match '(?i)InstallerType: (msi|wix)' -or ([regex]::Matches($yaml,[regex]::Escape($manifest.installerBuild.sha256))).Count -ne 2){throw 'WinGet manifests do not represent the signed two-scope Inno installer.'}
 $checks.Add('WinGet manifests use exact signed bytes and honest Inno scope entries')
}
# Extract a disposable installation-shaped copy; never install during tooling tests.
$payloadCases=Join-Path $results 'payload-cases';$null=New-Item -ItemType Directory $payloadCases
$good=Join-Path $payloadCases 'valid'
Expand-Archive -LiteralPath (Join-Path $root $manifest.portable) -DestinationPath $good
$sbomPath=Join-Path $root $manifest.sbom
$sbom=Get-Content $sbomPath -Raw|ConvertFrom-Json
$sbom.components|ForEach-Object {"$($_.hashes[0].content) *$($_.name.Replace('/','\'))"}|Set-Content "$good/.payload.manifest" -Encoding utf8
& "$PSScriptRoot/Verify-InnoPayload.ps1" -PayloadDirectory $good -SbomPath $sbomPath -Installed|Out-Null
$checks.Add('Complete installed payload and manifest accepted')
function RejectPayload([string]$Name,[scriptblock]$Mutate,[string]$Expected){
 $case=Join-Path $payloadCases $Name;$null=New-Item -ItemType Directory $case
 Get-ChildItem -LiteralPath $good -Force|Copy-Item -Destination $case -Recurse -Force
 & $Mutate $case
 $rejected=$false
 try{& "$PSScriptRoot/Verify-InnoPayload.ps1" -PayloadDirectory $case -SbomPath $sbomPath -Installed|Out-Null}
 catch{if($_.Exception.Message -notmatch $Expected){throw};$rejected=$true}
 if(-not $rejected){throw "Invalid installed payload accepted: $Name"}
 $checks.Add("Rejected installed payload: $Name")
}
RejectPayload 'missing-dll' {param($d) Remove-Item -LiteralPath "$d/log4net.dll"} 'files are missing'
RejectPayload 'changed-config' {param($d) Add-Content "$d/SuperPutty.exe.config" 'changed'} 'hash mismatch'
RejectPayload 'extra-hidden-file' {param($d)
 $path=Join-Path $d extra.dll;Set-Content $path 'test';(Get-Item $path).Attributes=[IO.FileAttributes]::Hidden
} 'Unexpected payload file'
RejectPayload 'extra-directory' {param($d) $null=New-Item -ItemType Directory "$d/extra"} 'Unexpected payload directory'
RejectPayload 'missing-installed-manifest' {param($d) Remove-Item -LiteralPath "$d/.payload.manifest"} 'manifest is missing'
RejectPayload 'changed-installed-manifest' {param($d) Add-Content "$d/.payload.manifest" ((('0'*64)+' *extra.dll'))} 'differs from the SBOM'
[pscustomobject]@{success=$true;version=$Version;checks=@($checks.ToArray())}|ConvertTo-Json -Depth 5|Set-Content "$results/summary.json" -Encoding utf8
Write-Output "Release tooling checks passed: $($checks.Count)."
