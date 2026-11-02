[CmdletBinding()]
param([Parameter(Mandatory)][string]$Compiler,[Parameter(Mandatory)][string]$PayloadDirectory,
 [Parameter(Mandatory)][string]$PortableArchive,[Parameter(Mandatory)][string]$ExpectedArchiveSha256,
 [Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$SignedUninstallerDirectory,
 [string]$Version='1.8.4',[switch]$TestFixture,
 [ValidateSet('none','payload','registry','pause-files','pause-activation','recovery-only')][string]$TestFault='none',
 [string]$SigningTool,[string]$SigningDlib,[string]$SigningMetadata)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Numeric candidate version required.'}
if($TestFault -ne 'none' -and -not $TestFixture){throw 'Fault hooks are restricted to explicit test fixtures.'}
if(-not $TestFixture -and (-not $SigningTool -or -not $SigningDlib -or -not $SigningMetadata)){throw 'Production candidates require compiler-integrated signing tool, dlib and metadata paths.'}
if($SigningTool){foreach($path in @($SigningTool,$SigningDlib,$SigningMetadata)){if(-not $path -or -not (Test-Path $path -PathType Leaf)){throw 'Signing input is missing.'}}}
if((Get-FileHash $PortableArchive).Hash -ne $ExpectedArchiveSha256){throw 'Portable archive hash differs from the reviewed input.'}
$payload=(Resolve-Path $PayloadDirectory).Path
$exe=Join-Path $payload 'SuperPutty.exe'
$signature=Get-AuthenticodeSignature $exe
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName',$false) -ne 'Christopher Thornton' -or -not $signature.TimeStamperCertificate){throw 'Expected the signed, timestamped application payload.'}
$appVersion=[version][Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$requested=[version]$Version
$normalized=if($requested.Revision -eq -1){[version]($Version+'.0')}else{$requested}
if(-not $TestFixture -and $normalized -ne $appVersion){throw 'Production package/application versions must match.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path $PortableArchive).Path)
try{
 $entries=@($zip.Entries|Where-Object Name)
 if(@(Get-ChildItem $payload -Recurse -File).Count -ne $entries.Count){throw 'Extracted payload file set differs from archive.'}
 foreach($entry in $entries){
  if([IO.Path]::IsPathRooted($entry.FullName) -or ($entry.FullName -split '[/\\]') -contains '..'){throw 'Invalid archive path.'}
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
  if((Get-FileHash (Join-Path $payload $entry.FullName)).Hash -ne $hash){throw 'Extracted payload bytes differ from archive.'}
 }
}finally{$zip.Dispose()}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $output){throw 'Output directory already exists; retain previous evidence.'}
New-Item -ItemType Directory $output|Out-Null
New-Item -ItemType Directory -Force $SignedUninstallerDirectory|Out-Null
$SignedUninstallerDirectory=(Resolve-Path $SignedUninstallerDirectory).Path
$manifest=Join-Path $output 'payload.manifest'
$lines=Get-ChildItem $payload -Recurse -File|Sort-Object FullName|ForEach-Object {((Get-FileHash $_.FullName).Hash.ToLowerInvariant())+' *'+$_.FullName.Substring($payload.Length+1)}
[IO.File]::WriteAllLines($manifest,[string[]]$lines,[Text.UTF8Encoding]::new($false))
$script=Join-Path (Split-Path $PSScriptRoot -Parent) 'packaging/inno/SuperPuttyCandidate.iss'
$arguments=@("/DPayloadDirectory=$payload","/DPayloadManifest=$manifest","/DCandidateVersion=$Version","/DTestFault=$TestFault","/DSignedUninstallerDirectory=$SignedUninstallerDirectory","/O$output")
& $Compiler @arguments $script
if($LASTEXITCODE -ne 0 -and -not $SigningTool){throw 'Candidate compilation failed. If this is the unsigned uninstaller seed pass, sign the persistent cache before retrying into a new output directory.'}
# Compile once to populate/sign the persistent uninstaller cache, then pin the
# exact signed replacement bytes in the final setup's durable recovery journal.
$cached=@(Get-ChildItem $SignedUninstallerDirectory -File|Where-Object {
 try{[version]([Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).ProductVersion.Trim()) -eq $requested}catch{$false}
})
if($cached.Count -ne 1){throw 'Expected exactly one uninstaller cache entry for this version; review ambiguous or missing resources.'}
if($SigningTool -and (Get-AuthenticodeSignature $cached[0].FullName).Status -eq 'NotSigned'){
 & $SigningTool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $SigningDlib /dmdf $SigningMetadata $cached[0].FullName
 if($LASTEXITCODE -ne 0){throw 'Uninstaller cache signing failed.'}
}
$cacheSignature=Get-AuthenticodeSignature $cached[0].FullName
if($cacheSignature.Status -ne 'Valid' -or $cacheSignature.SignerCertificate.GetNameInfo('SimpleName',$false) -ne 'Christopher Thornton' -or -not $cacheSignature.TimeStamperCertificate){throw 'Expected a valid project-signed, timestamped uninstaller cache entry.'}
if($SigningTool){
 $config=Join-Path $output 'signing-config.json'
 @{tool=(Resolve-Path $SigningTool).Path;dlib=(Resolve-Path $SigningDlib).Path;metadata=(Resolve-Path $SigningMetadata).Path;cache=$cached[0].FullName;sha256=(Get-FileHash $cached[0].FullName).Hash;version=$Version}|ConvertTo-Json|Set-Content $config
 $signer=Join-Path $PSScriptRoot 'Sign-InnoCandidateArtifact.ps1'
 $shell=(Get-Process -Id $PID).Path
 if($shell.StartsWith("$env:WINDIR\System32\",[StringComparison]::OrdinalIgnoreCase)){$shell=$shell.Replace('\System32\','\Sysnative\')}
 $command='$q'+$shell+'$q -NoProfile -ExecutionPolicy Bypass -File $q'+$signer+'$q -File $f -Configuration $q'+$config+'$q'
 $arguments+="/Sproject=$command";$arguments+='/DSigningConfigured=1'
}
$arguments+="/DCandidateUninstallerSha256=$((Get-FileHash $cached[0].FullName).Hash)"
& $Compiler @arguments $script
if($LASTEXITCODE -ne 0){throw 'Hash-pinned candidate compilation failed.'}
$setup=Get-ChildItem $output -Filter *.exe
if($setup.Count -ne 1){throw 'Expected exactly one candidate setup executable.'}
if(-not $TestFixture -and (Get-AuthenticodeSignature $setup.FullName).Status -ne 'Valid'){throw 'Production setup signature invalid.'}
$sourceCommit=$null
if(Get-Command git -ErrorAction SilentlyContinue){$sourceCommit=& git -C (Split-Path $PSScriptRoot -Parent) rev-parse HEAD 2>$null}
$sourceHashes=@(Get-FileHash $script,(Join-Path (Split-Path $script -Parent) 'Recovery.iss')|ForEach-Object {@{name=(Split-Path $_.Path -Leaf);sha256=$_.Hash}})
[pscustomobject]@{version=$Version;applicationVersion=$appVersion.ToString();testFixture=[bool]$TestFixture;testFault=$TestFault;archiveSha256=$ExpectedArchiveSha256;uninstallerSha256=(Get-FileHash $cached[0].FullName).Hash;setup=$setup.Name;sha256=(Get-FileHash $setup.FullName).Hash;sourceCommit=$sourceCommit;sourceHashes=$sourceHashes}|
 ConvertTo-Json -Depth 5|Set-Content (Join-Path $output 'candidate-build.json')
