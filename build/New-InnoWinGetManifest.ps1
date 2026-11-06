[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[Parameter(Mandatory)][string]$ManifestDirectory,[string]$Version='1.8.4')
$ErrorActionPreference='Stop'
& "$PSScriptRoot/Verify-InnoReleaseBundle.ps1" -ReleaseDirectory $ReleaseDirectory -Version $Version -RequireInstaller
if(Test-Path $ManifestDirectory){throw 'Use a new manifest directory; preserve previous review files.'}
$manifest=Get-Content "$ReleaseDirectory/SuperPuTTY-CE-$Version-build-manifest.json" -Raw|ConvertFrom-Json
$id='ChrisThornton.SuperPuTTYCommunityEdition';$schema='1.28.0';$code='{EBCA29DD-46C1-4A93-9340-79DF5375711D}_is1'
$base="https://github.com/greyhair-atx/superputty"
New-Item -ItemType Directory $ManifestDirectory -Force|Out-Null
$lines=@("# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json","PackageIdentifier: $id","PackageVersion: $Version",'InstallerType: inno','InstallerLocale: en-US','MinimumOSVersion: 10.0.19041.0','InstallModes:','- interactive','- silent','- silentWithProgress','UpgradeBehavior: install','Installers:')
foreach($scope in @('user','machine')){
 $flag=if($scope -eq 'user'){'/CURRENTUSER'}else{'/ALLUSERS'}
 $lines+=@('- Architecture: x64',"  Scope: $scope")
 if($scope -eq 'machine'){$lines+='  ElevationRequirement: elevationRequired'}
 $lines+=@("  InstallerUrl: $base/releases/download/sp-$Version/$($manifest.installerBuild.setup)","  InstallerSha256: $($manifest.installerBuild.sha256)","  ProductCode: '$code'",'  InstallerSwitches:',"    Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- $flag","    SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART /SP- $flag",'  AppsAndFeaturesEntries:','  - DisplayName: SuperPuTTY Community Edition','    Publisher: Chris Thornton',"    ProductCode: '$code'",'    InstallerType: inno')
}
$lines+=@('ManifestType: installer',"ManifestVersion: $schema")
$lines|Set-Content "$ManifestDirectory/$id.installer.yaml" -Encoding utf8
@("# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json","PackageIdentifier: $id","PackageVersion: $Version",'PackageLocale: en-US','Publisher: Chris Thornton',"PublisherUrl: $base",'PackageName: SuperPuTTY Community Edition',"PackageUrl: $base",'License: MIT',"LicenseUrl: $base/blob/sp-$Version/License.txt",'ShortDescription: A tabbed workspace for PuTTY and other Windows connection clients.',"PrivacyUrl: $base/blob/sp-$Version/docs/manual/security.md","ReleaseNotesUrl: $base/releases/tag/sp-$Version",'ManifestType: defaultLocale',"ManifestVersion: $schema")|Set-Content "$ManifestDirectory/$id.locale.en-US.yaml" -Encoding utf8
@("# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json","PackageIdentifier: $id","PackageVersion: $Version",'DefaultLocale: en-US','ManifestType: version',"ManifestVersion: $schema")|Set-Content "$ManifestDirectory/$id.yaml" -Encoding utf8
Write-Output 'Staged review manifests only. Installer URLs become available after a separately approved release.'
