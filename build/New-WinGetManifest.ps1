[CmdletBinding()]
param([string]$Version='1.8.0',[string]$ReleaseDirectory,[string]$ManifestDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $ReleaseDirectory){$ReleaseDirectory=Join-Path $repo "artifacts/release-$Version/win-x64-signed"}
if(-not $ManifestDirectory){$ManifestDirectory=Join-Path $repo "packaging/winget/ChrisThornton.SuperPuTTYCommunityEdition/$Version"}
$build=Get-Content (Join-Path $ReleaseDirectory "SuperPuTTY-CE-$Version-build-manifest.json") -Raw|ConvertFrom-Json
if(-not $build.signaturesRequired -or $build.version -ne $Version -or $build.packages.Count -ne 2){throw 'Expected a verified, signed dual-scope release bundle.'}
$id='ChrisThornton.SuperPuTTYCommunityEdition'
$schema='1.12.0'
New-Item -ItemType Directory -Force -Path $ManifestDirectory|Out-Null
$lines=@("# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json","PackageIdentifier: $id","PackageVersion: $Version",'InstallerType: wix','InstallerLocale: en-US','MinimumOSVersion: 10.0.0.0','InstallModes:','- interactive','- silent','- silentWithProgress','InstallerSwitches:','  Silent: /qn /norestart','  SilentWithProgress: /passive /norestart','UpgradeBehavior: install','Installers:')
foreach($package in $build.packages){
 $file=Join-Path $ReleaseDirectory $package.name
 if((Get-FileHash $file).Hash -ne $package.sha256 -or (Get-AuthenticodeSignature $file).Status -ne 'Valid'){throw 'Package bytes/signature differ from the verified release.'}
 $scope=if($package.scope -eq 'PerUser'){'user'}else{'machine'}
 $lines+=@('- Architecture: x64',"  Scope: $scope","  InstallerUrl: https://github.com/greyhair-atx/superputty/releases/download/sp-$Version/$($package.name)","  InstallerSha256: $($package.sha256)","  ProductCode: '$($package.productCode)'",'  AppsAndFeaturesEntries:','  - DisplayName: SuperPuTTY Community Edition','    Publisher: Chris Thornton',"    DisplayVersion: $Version","    ProductCode: '$($package.productCode)'","    UpgradeCode: '$($package.upgradeCode)'",'    InstallerType: wix')
}
$lines+=@('ManifestType: installer',"ManifestVersion: $schema")
$lines|Set-Content (Join-Path $ManifestDirectory "$id.installer.yaml") -Encoding utf8
@("# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json","PackageIdentifier: $id","PackageVersion: $Version",'DefaultLocale: en-US','ManifestType: version',"ManifestVersion: $schema")|Set-Content (Join-Path $ManifestDirectory "$id.yaml") -Encoding utf8
@("# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json","PackageIdentifier: $id","PackageVersion: $Version",'PackageLocale: en-US','Publisher: Chris Thornton','PublisherUrl: https://github.com/greyhair-atx','PackageName: SuperPuTTY Community Edition','PackageUrl: https://github.com/greyhair-atx/superputty','License: MIT',"LicenseUrl: https://github.com/greyhair-atx/superputty/blob/sp-$Version/License.txt",'Copyright: Copyright (c) 2009-2023 Jim Radford; Copyright (c) 2026 Chris Thornton (community modifications).','ShortDescription: Independently maintained Windows x64 workspace for PuTTY and related session tools.','Description: Independent community fork of Jim Radford''s SuperPuTTY. No official upstream or PuTTY endorsement is implied. Requires .NET Framework 4.8 and separately installed connection clients.',"ReleaseNotesUrl: https://github.com/greyhair-atx/superputty/releases/tag/sp-$Version",'Tags:','- superputty','- putty','- ssh','- terminal','- rdp','- vnc','- scp','ManifestType: defaultLocale',"ManifestVersion: $schema")|Set-Content (Join-Path $ManifestDirectory "$id.locale.en-US.yaml") -Encoding utf8
Write-Output "Prepared WinGet manifests from exact signed MSI metadata: $ManifestDirectory"
