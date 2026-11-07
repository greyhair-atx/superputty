[CmdletBinding()]
param([string]$Version='1.8.4',[string]$Configuration='Release',[string]$ApplicationDirectory,
 [Parameter(Mandatory)][string]$OutputDirectory,[string]$WorkDirectory,
 [switch]$PortableOnly,[string]$Compiler,[string]$SigningTool,[string]$SigningDlib,[string]$SigningMetadata)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Use a three-part numeric release version.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $output){throw 'Retain previous candidates; use a new output directory.'}
if(-not $WorkDirectory){$WorkDirectory=Join-Path (Split-Path $output -Parent) ('work-'+[guid]::NewGuid().ToString('N'))}
$work=[IO.Path]::GetFullPath($WorkDirectory)
if(Test-Path $work){throw 'Use a new working directory.'}
if($work.StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase) -or $work -eq $output){throw 'Private signing/work files must stay outside the distributable bundle.'}
$app=if($ApplicationDirectory){(Resolve-Path -LiteralPath $ApplicationDirectory).Path}else{Join-Path $repo "bin/x64/$Configuration"}
$appExe=Join-Path $app 'SuperPutty.exe'
if([version][Diagnostics.FileVersionInfo]::GetVersionInfo($appExe).FileVersion -ne [version]($Version+'.0')){throw 'Application/release versions differ.'}
if(-not $PortableOnly){
 foreach($path in @($Compiler,$SigningTool,$SigningDlib,$SigningMetadata)){if(-not $path -or -not (Test-Path $path -PathType Leaf)){throw 'Signed installer builds require compiler and signing inputs.'}}
 $pin=Import-PowerShellDataFile "$PSScriptRoot/InnoToolchain.psd1"
 if((Get-FileHash $Compiler).Hash -ne $pin.CompilerSha256){throw 'Compiler differs from the pinned Inno toolchain.'}
 $sig=Get-AuthenticodeSignature $appExe
 if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.GetNameInfo('SimpleName',$false) -ne 'Christopher Thornton' -or -not $sig.TimeStamperCertificate){throw 'Sign and timestamp the application before creating a signed bundle.'}
}
New-Item -ItemType Directory $output,$work|Out-Null
$payload=Join-Path $work 'payload';New-Item -ItemType Directory $payload|Out-Null
$required=@('SuperPutty.exe','SuperPutty.exe.config','log4net.dll','WeifenLuo.WinFormsUI.Docking.dll','WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll','Interop.MSTSCLib.dll','AxInterop.MSTSCLib.dll','THIRD-PARTY-NOTICES.txt')
foreach($name in $required){Copy-Item -LiteralPath (Join-Path $app $name) -Destination $payload -ErrorAction Stop}
Copy-Item -LiteralPath "$repo/License.txt" -Destination $payload
$icons=Join-Path $payload 'themes/default/icons';New-Item -ItemType Directory $icons -Force|Out-Null
$iconFiles=@(Get-ChildItem "$app/themes/default/icons" -File -Filter '*.png')
if($iconFiles.Count -ne 47){throw 'Unexpected runtime icon file set.'}
$iconFiles|Copy-Item -Destination $icons
@'
SuperPuTTY Community Edition — no-install ZIP
Extract all files into a new writable directory and launch SuperPutty.exe.
Requires Windows x64, Windows 10 build 19041 or later, .NET Framework 4.8,
and separately installed PuTTY/PSCP or other selected clients.
This ZIP does not isolate preferences: USERPROFILE/SuperPuTTY.settings has
priority; sessions/layouts use the configured settings folder. Back up first.
Existing MSI users: the ZIP does not uninstall or migrate MSI registration.
Use the INSTALLATION guide shipped alongside this archive for MSI migration,
installation scope, signature checks, interrupted recovery and uninstall.
This candidate is for testing. No public release or WinGet submission is made.
'@|Set-Content "$payload/PORTABLE-README.txt" -Encoding utf8
$prefix="SuperPuTTY-CE-$Version"
$zipName=if($PortableOnly){"$prefix-portable-dev-win-x64.zip"}else{"$prefix-portable-win-x64.zip"}
Compress-Archive -Path "$payload/*" -DestinationPath "$output/$zipName"
$guideName="$prefix-INSTALLATION.md"
Get-Content "$repo/docs/manual/installation.md" -Raw|Set-Content "$output/$guideName" -Encoding utf8
$installer=$null
if(-not $PortableOnly){
 & "$PSScriptRoot/Build-InnoCandidate.ps1" -Compiler $Compiler -PayloadDirectory $payload -PortableArchive "$output/$zipName" -ExpectedArchiveSha256 (Get-FileHash "$output/$zipName").Hash -OutputDirectory "$work/installer" -SignedUninstallerDirectory "$work/signed-uninstallers" -Version $Version -SigningTool $SigningTool -SigningDlib $SigningDlib -SigningMetadata $SigningMetadata
 $installer=Get-Content "$work/installer/candidate-build.json" -Raw|ConvertFrom-Json
 if($installer.testFixture -or $installer.testFault -ne 'none'){throw 'Fault fixture cannot enter a release bundle.'}
 Copy-Item -LiteralPath "$work/installer/$($installer.setup)" -Destination $output
}
$components=@(Get-ChildItem $payload -Recurse -File|Sort-Object FullName|ForEach-Object {
 $name=$_.FullName.Substring($payload.Length+1).Replace('\','/')
 $component=[ordered]@{type='file';'bom-ref'="file:$name";name=$name;hashes=@(@{alg='SHA-256';content=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()})}
 if($_.Extension -in @('.exe','.dll')){$component.version=[Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).FileVersion}
 $component
})
$sbomName="$prefix-sbom.cdx.json"
@{bomFormat='CycloneDX';specVersion='1.6';serialNumber=('urn:uuid:'+ [guid]::NewGuid());version=1;metadata=@{timestamp=[DateTime]::UtcNow.ToString('o');component=@{type='application';name='SuperPuTTY Community Edition';version=$Version;licenses=@(@{license=@{id='MIT'}})};properties=@(@{name='superputty:inventory-scope';value='Exact ZIP payload files. Windows/.NET and separately installed clients are prerequisites.'})};components=$components}|ConvertTo-Json -Depth 12|Set-Content "$output/$sbomName" -Encoding utf8
$manifestName="$prefix-build-manifest.json"
$manifest=[ordered]@{format='superputty-inno-v1';version=$Version;applicationVersion="$Version.0";candidate=$true;publicationApproved=$false;sourceCommit=(& git -C $repo rev-parse HEAD).Trim();sourceDirty=(@(& git -C $repo status --porcelain).Count -gt 0);signaturesRequired=(-not [bool]$PortableOnly);portableOnly=[bool]$PortableOnly;installerType='inno';installerBuild=$installer;portable=$zipName;sbom=$sbomName;guide=$guideName;payloadFiles=@($components.name);validation=@{automated='pending';vmLifecycle='pending';liveRdpReconnect='pending';realScpMfa='pending';windows10='pending'}}
$manifest|ConvertTo-Json -Depth 12|Set-Content "$output/$manifestName" -Encoding utf8
$names=@($zipName,$sbomName,$guideName,$manifestName)
if($installer){$names+=@($installer.setup)}
$names|ForEach-Object {"$((Get-FileHash "$output/$_").Hash.ToLowerInvariant())  $_"}|Set-Content "$output/$prefix-SHA256SUMS.txt" -Encoding ascii
& "$PSScriptRoot/Verify-InnoReleaseBundle.ps1" -ReleaseDirectory $output -Version $Version
Write-Output "Prepared unpublished candidate: $output"
