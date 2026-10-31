[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$id='ChrisThornton.SuperPuTTYCommunityEdition'
function Scalar([string]$Text,[string]$Key,[string]$Prefix=''){
    $match=[regex]::Match($Text,'(?m)^'+[regex]::Escape($Prefix+$Key)+': (.+)\r?$')
    if(-not $match.Success){throw "Missing $Key in reviewed manifest"}
    return $match.Groups[1].Value.Trim().Trim("'")
}
$versions=@()
$searchVersions=@()
foreach($version in @('1.8.3','1.8.0')){
    $directory=Join-Path $root "packaging/winget/$id/$version"
    $text=Get-Content (Join-Path $directory "$id.installer.yaml") -Raw
    $locale=Get-Content (Join-Path $directory "$id.locale.en-US.yaml") -Raw
    if((Scalar $text PackageIdentifier) -ne $id -or (Scalar $text PackageVersion) -ne $version){throw 'Unexpected manifest identity'}
    $defaultLocale=@{}
    foreach($line in ($locale -split '\r?\n')){
        if($line -match '^([A-Za-z]+): (.+)$' -and $Matches[1] -notin @('PackageIdentifier','PackageVersion','ManifestType','ManifestVersion')){$defaultLocale[$Matches[1]]=$Matches[2]}
    }
    $defaultLocale.Tags=@([regex]::Matches($locale,'(?m)^- (.+)\r?$')|ForEach-Object {$_.Groups[1].Value.Trim()})
    $installers=@()
    $entries=[regex]::Split($text,'(?m)^- Architecture: ')
    if($entries.Count -ne 3){throw 'Expected exactly two installer scopes in the reviewed YAML'}
    $modes=[regex]::Match($text,'(?m)^InstallModes:\r?\n((?:- [^\r\n]+\r?\n)+)')
    if(-not $modes.Success){throw 'Unsupported InstallModes formatting'}
    $installModes=@([regex]::Matches($modes.Groups[1].Value,'(?m)^- (.+)\r?$')|ForEach-Object {$_.Groups[1].Value.Trim()})
    foreach($entry in $entries[1..2]){
        $features=@{}
        $features.DisplayName=Scalar $entry DisplayName '  - '
        foreach($field in @('Publisher','ProductCode','UpgradeCode','InstallerType')){$features[$field]=Scalar $entry $field '    '}
        $installer=@{
            Architecture=($entry -split '\r?\n')[0];Scope=(Scalar $entry Scope '  ')
            InstallerType=(Scalar $text InstallerType);InstallerLocale=(Scalar $text InstallerLocale)
            MinimumOSVersion=(Scalar $text MinimumOSVersion);UpgradeBehavior=(Scalar $text UpgradeBehavior)
            InstallModes=$installModes
            InstallerSwitches=@{Silent=(Scalar $text Silent '  ');SilentWithProgress=(Scalar $text SilentWithProgress '  ')}
            InstallerUrl=(Scalar $entry InstallerUrl '  ');InstallerSha256=(Scalar $entry InstallerSha256 '  ')
            ProductCode=(Scalar $entry ProductCode '  ');AppsAndFeaturesEntries=@($features)
        }
        if($entry -match '(?m)^  ElevationRequirement: (.+)\r?$'){$installer.ElevationRequirement=$Matches[1].Trim()}
        $installers+=$installer
    }
    if($installers.Count -ne 2){throw 'Expected exactly two installer scopes'}
    $versions+=@{PackageVersion=$version;DefaultLocale=$defaultLocale;Installers=$installers}
    $searchVersions+=@{PackageVersion=$version;ProductCodes=@($installers|ForEach-Object {$_.ProductCode});UpgradeCodes=@($installers|ForEach-Object {$_.AppsAndFeaturesEntries[0].UpgradeCode}|Select-Object -Unique)}
}
New-Item -ItemType Directory $OutputDirectory -Force|Out-Null
@{PackageIdentifier=$id;PackageName=$defaultLocale.PackageName;Publisher=$defaultLocale.Publisher;Versions=$versions;SearchVersions=$searchVersions}|ConvertTo-Json -Depth 30|Set-Content (Join-Path $OutputDirectory 'catalog.json') -Encoding UTF8
'["1.8.3","1.8.0"]'|Set-Content (Join-Path $OutputDirectory 'available-versions.json') -Encoding UTF8
