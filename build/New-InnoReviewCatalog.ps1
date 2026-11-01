[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$id='ChrisThornton.SuperPuTTYCEPackagingPrototype'
$code='{3C8A31DE-74CA-48B7-889B-F44B19DE8714}_is1'
$hashes=Get-Content (Join-Path $InstallerDirectory 'prototype-hashes.json') -Raw|ConvertFrom-Json
$versions=@();$search=@()
foreach($version in @('1.8.4.100','1.8.4.101')){
 $name="SuperPuTTY-CE-Prototype-$version-none.exe"
 $hash=($hashes|Where-Object name -EQ $name).sha256
 if(-not $hash -or (Get-FileHash (Join-Path $InstallerDirectory $name)).Hash -ne $hash){throw 'Prototype catalog fixture verification failed.'}
 $installers=@(foreach($scope in @('user','machine')){
  $flag=if($scope -eq 'user'){'/CURRENTUSER'}else{'/ALLUSERS'}
  $entry=@{Architecture='x64';Scope=$scope;InstallerType='inno';InstallerLocale='en-US';MinimumOSVersion='10.0.19041.0';UpgradeBehavior='install';
   InstallerUrl="https://localhost:9443/installers/$name";InstallerSha256=$hash;ProductCode=$code;
   InstallModes=@('silent');InstallerSwitches=@{Silent="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- $flag"};
   AppsAndFeaturesEntries=@(@{DisplayName='SuperPuTTY CE Packaging Prototype';Publisher='Chris Thornton';ProductCode=$code;InstallerType='inno'})}
  if($scope -eq 'machine'){$entry.ElevationRequirement='elevationRequired'}
  $entry
 })
 $versions+=@{PackageVersion=$version;DefaultLocale=@{PackageLocale='en-US';PackageName='SuperPuTTY CE Packaging Prototype';Publisher='Chris Thornton';License='MIT';ShortDescription='Disposable packaging prototype only'};Installers=$installers}
 $search+=@{PackageVersion=$version;ProductCodes=@($code);UpgradeCodes=@()}
}
if(Test-Path $OutputDirectory){throw 'Use a new catalog output directory.'}
New-Item -ItemType Directory $OutputDirectory|Out-Null
@{PackageIdentifier=$id;PackageName='SuperPuTTY CE Packaging Prototype';Publisher='Chris Thornton';Versions=$versions;SearchVersions=$search}|
 ConvertTo-Json -Depth 25|Set-Content (Join-Path $OutputDirectory 'catalog.json')
'["1.8.4.100","1.8.4.101"]'|Set-Content (Join-Path $OutputDirectory 'available-versions.json')
