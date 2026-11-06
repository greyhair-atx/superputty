[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[string]$Version='1.8.4',[switch]$RequireInstaller,[switch]$RequireCleanSource)
$ErrorActionPreference='Stop'
$root=(Resolve-Path $ReleaseDirectory).Path
$prefix="SuperPuTTY-CE-$Version"
$manifestName="$prefix-build-manifest.json"
$manifest=Get-Content "$root/$manifestName" -Raw|ConvertFrom-Json
if($manifest.format -ne 'superputty-inno-v1' -or $manifest.version -ne $Version -or $manifest.installerType -ne 'inno' -or $manifest.applicationVersion -ne "$Version.0"){throw 'Invalid Inno release identity/version.'}
if($manifest.sourceDirty -isnot [bool]){throw 'Source provenance requires an explicit Boolean sourceDirty value.'}
if($RequireCleanSource -and $manifest.sourceDirty -ne $false){throw 'Publication requires a frozen clean source commit.'}
if($manifest.sourceCommit -notmatch '^[0-9a-f]{40}$'){throw 'Missing source commit provenance.'}
if($RequireInstaller -and ($manifest.portableOnly -or -not $manifest.signaturesRequired)){throw 'A signed Inno installer is required.'}
$names=@($manifest.portable,$manifest.sbom,$manifest.guide,$manifestName)
if(-not $manifest.portableOnly){
 $installer=$manifest.installerBuild
 if(-not $manifest.signaturesRequired -or -not $installer -or $installer.testFixture -or $installer.testFault -ne 'none' -or $installer.version -ne $Version -or $installer.applicationVersion -ne "$Version.0"){throw 'Unsigned, mismatched or fault-enabled installer metadata.'}
 if($installer.setup -cne "$prefix-setup-win-x64.exe"){throw 'Production installer naming mismatch.'}
 $names+=@($installer.setup)
}elseif($manifest.installerBuild -or $manifest.signaturesRequired){throw 'Development portable metadata cannot claim a signed installer.'}
if(@($names|Select-Object -Unique).Count -ne $names.Count){throw 'Duplicate release filenames.'}
foreach($name in $names){if(-not $name -or $name -cne [IO.Path]::GetFileName($name) -or $name -notlike "$prefix-*"){throw 'Unsafe or unexpected release filename.'}}
$expectedZip=if($manifest.portableOnly){"$prefix-portable-dev-win-x64.zip"}else{"$prefix-portable-win-x64.zip"}
if($manifest.portable -cne $expectedZip){throw 'Development/signed archive naming mismatch.'}
$checksums=@(Get-Content "$root/$prefix-SHA256SUMS.txt")
if($checksums.Count -ne $names.Count){throw 'Incomplete checksum inventory.'}
$seen=@()
foreach($line in $checksums){
 if($line -cnotmatch '^([0-9a-f]{64})  (.+)$'){throw 'Invalid checksum line.'}
 $hash=$Matches[1];$name=$Matches[2]
 if($name -cnotin $names -or $name -cin $seen -or (Get-FileHash "$root/$name").Hash -ne $hash){throw 'Unexpected, duplicate or mismatched asset checksum.'}
 $seen+=@($name)
}
$children=@(Get-ChildItem -LiteralPath $root -Force)
$actual=@($children|Where-Object {-not $_.PSIsContainer}|ForEach-Object Name)
if($actual.Count -ne $names.Count+1 -or @($children|Where-Object {$_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)}).Count -ne 0 -or @($actual|Where-Object {$_ -cnotin ($names+@("$prefix-SHA256SUMS.txt"))}).Count){throw 'Private work files or unlisted assets in distributable directory.'}
function ProjectSignature([string]$Path){$sig=Get-AuthenticodeSignature $Path;if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.GetNameInfo('SimpleName',$false) -ne 'Christopher Thornton' -or -not $sig.TimeStamperCertificate){throw 'Missing project signature/timestamp.'}}
if(-not $manifest.portableOnly){
 if((Get-FileHash "$root/$($installer.setup)").Hash -ne $installer.sha256 -or (Get-FileHash "$root/$($manifest.portable)").Hash -ne $installer.archiveSha256 -or $installer.uninstallerSha256 -notmatch '^[0-9a-fA-F]{64}$'){throw 'Installer build hash pins differ from release assets.'}
 ProjectSignature "$root/$($installer.setup)"
}
$sbom=Get-Content "$root/$($manifest.sbom)" -Raw|ConvertFrom-Json
if($sbom.bomFormat -ne 'CycloneDX' -or $sbom.specVersion -ne '1.6' -or $sbom.metadata.component.version -ne $Version){throw 'SBOM format/version mismatch.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead("$root/$($manifest.portable)")
$scratch=Join-Path $env:TEMP ('spce-verify-'+[guid]::NewGuid().ToString('N')+'.exe')
try{
 $files=@($zip.Entries|Where-Object Name)
 $fileNames=@($files|ForEach-Object {$_.FullName.Replace('\','/')})
 $required=@('SuperPutty.exe','SuperPutty.exe.config','log4net.dll','WeifenLuo.WinFormsUI.Docking.dll','WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll','Interop.MSTSCLib.dll','AxInterop.MSTSCLib.dll','THIRD-PARTY-NOTICES.txt','License.txt','PORTABLE-README.txt')
 if($files.Count -ne 57 -or $sbom.components.Count -ne $files.Count -or @($fileNames|Sort-Object -Unique).Count -ne $files.Count -or @($required|Where-Object {$_ -cnotin $fileNames}).Count){throw 'Unexpected or duplicate ZIP payload.'}
 foreach($file in $files){
  $name=$file.FullName.Replace('\','/')
  if($name -cnotin $required -and $name -cnotmatch '^themes/default/icons/[a-zA-Z0-9_.-]+\.png$'){throw 'ZIP contains an unapproved runtime file.'}
  if($name -cnotin $manifest.payloadFiles){throw 'Payload provenance is incomplete.'}
  $component=@($sbom.components|Where-Object name -CEQ $name)
  if($component.Count -ne 1 -or $component[0].hashes.Count -ne 1 -or $component[0].hashes[0].alg -ne 'SHA-256'){throw 'SBOM inventory is missing/ambiguous.'}
  $stream=$file.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
  if($hash -ne $component[0].hashes[0].content){throw 'ZIP bytes differ from the SBOM.'}
  if($name -ceq 'SuperPutty.exe'){
   $stream=$file.Open();$dest=[IO.File]::Create($scratch)
   try{$stream.CopyTo($dest)}finally{$stream.Dispose();$dest.Dispose()}
   if([version][Diagnostics.FileVersionInfo]::GetVersionInfo($scratch).FileVersion -ne [version]$manifest.applicationVersion){throw 'Executable version differs from release metadata.'}
   if(-not $manifest.portableOnly){if($hash -ne $installer.applicationSha256){throw 'Installer/application payload pin mismatch.'};ProjectSignature $scratch}
  }
 }
 if(@($manifest.payloadFiles).Count -ne $files.Count){throw 'Payload provenance contains extra files.'}
}finally{$zip.Dispose();if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch}}
Write-Output "Verified $Version Inno bundle: $($names.Count) asset checksums; 57 exact runtime/SBOM files; portableOnly=$($manifest.portableOnly)."
