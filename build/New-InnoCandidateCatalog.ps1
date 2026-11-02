[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path $OutputDirectory){throw 'Retain prior catalog evidence; use a new directory.'}
$id='ChrisThornton.SuperPuTTYCommunityEdition'
$display='SuperPuTTY Community Edition'
$code='{EBCA29DD-46C1-4A93-9340-79DF5375711D}_is1'
$versions=@();$search=@();$pins=@()
foreach($version in @('1.8.4.100','1.8.4.101')){
 $name="SuperPuTTY-CE-Candidate-$version-none.exe"
 $file=Join-Path $InstallerDirectory $name
 $signature=Get-AuthenticodeSignature $file
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName',$false) -ne 'Christopher Thornton' -or -not $signature.TimeStamperCertificate){throw 'Signed, timestamped candidate fixtures required.'}
 $hash=(Get-FileHash $file).Hash;$pins+=@{name=$name;sha256=$hash}
 $installers=@(foreach($scope in @('user','machine')){
  $flag=if($scope -eq 'user'){'/CURRENTUSER'}else{'/ALLUSERS'}
  $entry=@{Architecture='x64';Scope=$scope;InstallerType='inno';InstallerLocale='en-US';MinimumOSVersion='10.0.19041.0';UpgradeBehavior='install';
   InstallerUrl="https://localhost:9443/installers/$name";InstallerSha256=$hash;ProductCode=$code;
   InstallModes=@('silent');InstallerSwitches=@{Silent="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- $flag"};
   AppsAndFeaturesEntries=@(@{DisplayName=$display;Publisher='Chris Thornton';ProductCode=$code;InstallerType='inno'})}
  if($scope -eq 'machine'){$entry.ElevationRequirement='elevationRequired'}
  $entry
 })
 $versions+=@{PackageVersion=$version;DefaultLocale=@{PackageLocale='en-US';PackageName=$display;Publisher='Chris Thornton';License='MIT';ShortDescription='Private packaging qualification fixture, not a release'};Installers=$installers}
 $search+=@{PackageVersion=$version;ProductCodes=@($code);UpgradeCodes=@()}
}
# Preserve the actual public MSI identity and type. Never claim MSI compatibility
# on the Inno entries merely to persuade the client to run a destructive migration.
$oldCodes=@('{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}','{A339D1A0-E398-46D4-A1A3-1CE0477E69BE}')
$oldUpgrade=@('{3E4059D9-E5A5-48DE-B787-FC88268C1B95}','{98CB966B-60D4-46C3-AADA-1990464696B8}')
$oldHash=@('5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36','CAC64BD522C4DFF97501290E62C69E55D638895C4FF5213C8412879A5512D60B')
$oldInstallers=@(for($i=0;$i -lt 2;$i++){
 $scope=@('user','machine')[$i];$suffix=@('current-user','all-users')[$i]
 $entry=@{Architecture='x64';Scope=$scope;InstallerType='wix';InstallerLocale='en-US';MinimumOSVersion='10.0.19041.0';UpgradeBehavior='uninstallPrevious';
  InstallerUrl="https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.3/SuperPuTTY-CE-1.8.3-$suffix-win-x64-signed.msi";
  InstallerSha256=$oldHash[$i];ProductCode=$oldCodes[$i];InstallerSwitches=@{Silent='/qn /norestart'};
  AppsAndFeaturesEntries=@(@{DisplayName=$display;Publisher='Chris Thornton';ProductCode=$oldCodes[$i];UpgradeCode=$oldUpgrade[$i];InstallerType='wix'})}
 if($scope -eq 'machine'){$entry.ElevationRequirement='elevationRequired'}
 $entry
})
$versions+=@{PackageVersion='1.8.3';DefaultLocale=@{PackageLocale='en-US';PackageName=$display;Publisher='Chris Thornton';License='MIT';ShortDescription='Existing published MSI'};Installers=$oldInstallers}
$search+=@{PackageVersion='1.8.3';ProductCodes=$oldCodes;UpgradeCodes=$oldUpgrade}
New-Item -ItemType Directory $OutputDirectory|Out-Null
@{PackageIdentifier=$id;PackageName=$display;Publisher='Chris Thornton';Versions=$versions;SearchVersions=$search}|ConvertTo-Json -Depth 25|Set-Content "$OutputDirectory/catalog.json"
'["1.8.3","1.8.4.100","1.8.4.101"]'|Set-Content "$OutputDirectory/available-versions.json"
ConvertTo-Json -InputObject $pins|Set-Content (Join-Path $InstallerDirectory 'candidate-hashes.json')
