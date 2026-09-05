[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Platform = 'x64',
    [string] $ExpectedVersion = '1.7.3.0',
    [ValidateSet('PerUser', 'PerMachine')]
    [string] $InstallerScope = 'PerMachine',
    [string] $MsiName
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appDirectory = Join-Path $repoRoot "bin\$Platform\$Configuration"
$appPath = Join-Path $appDirectory 'SuperPutty.exe'
$installerVersion = $ExpectedVersion -replace '\.0$', ''
if ([string]::IsNullOrWhiteSpace($MsiName)) {
    $scopeName = if ($InstallerScope -eq 'PerUser') { 'current-user' } else { 'all-users' }
    $MsiName = "SuperPutty-$installerVersion-$scopeName-test-x64.msi"
}
$msiPath = Join-Path $repoRoot "SuperPuttyInstaller\bin\$Platform\$Configuration\$MsiName"

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Get-PeMachine {
    param([string] $Path)
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        Assert-Condition ($reader.ReadUInt16() -eq 0x5A4D) "$Path is not a PE file."
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        Assert-Condition ($reader.ReadUInt32() -eq 0x00004550) "$Path has an invalid PE signature."
        return $reader.ReadUInt16()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-MsiSummaryProperty {
    param($Installer, [string] $Path, [int] $Property)
    $summary = $Installer.GetType().InvokeMember(
        'SummaryInformation', 'GetProperty', $null, $Installer, @($Path, 0))
    return $summary.GetType().InvokeMember(
        'Property', 'GetProperty', $null, $summary, $Property)
}

function Get-MsiRecord {
    param($Database, [string] $Query)
    $view = $Database.GetType().InvokeMember(
        'OpenView', 'InvokeMethod', $null, $Database, @($Query))
    $null = $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    if ($null -eq $record) { return $null }
    return $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
}

Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) "Missing application: $appPath"
Assert-Condition (Test-Path -LiteralPath $msiPath -PathType Leaf) "Missing installer: $msiPath"
Assert-Condition ((Get-PeMachine $appPath) -eq 0x8664) 'SuperPutty.exe is not an x64 PE image.'

$version = (Get-Item -LiteralPath $appPath).VersionInfo
Assert-Condition ($version.FileVersion -eq $ExpectedVersion) "Unexpected file version: $($version.FileVersion)"
Assert-Condition ($version.ProductVersion -eq $ExpectedVersion) "Unexpected product version: $($version.ProductVersion)"

$requiredDlls = @(
    'log4net.dll',
    'WeifenLuo.WinFormsUI.Docking.dll',
    'WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll'
)
$thirdPartyNoticesName = 'THIRD-PARTY-NOTICES.txt'
$thirdPartyNoticesPath = Join-Path $appDirectory $thirdPartyNoticesName

$missingDlls = @($requiredDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $appDirectory $_)) })
Assert-Condition ($missingDlls.Count -eq 0) "Missing runtime DLLs: $($missingDlls -join ', ')"
Assert-Condition (Test-Path -LiteralPath $thirdPartyNoticesPath -PathType Leaf) "Missing third-party notices: $thirdPartyNoticesPath"
$thirdPartyNoticesText = Get-Content -LiteralPath $thirdPartyNoticesPath -Raw
Assert-Condition ($thirdPartyNoticesText -like '*DockPanelSuite.ThemeVS2015*') 'The third-party notices are missing DockPanelSuite.ThemeVS2015.'
Assert-Condition ($thirdPartyNoticesText -like '*Apache log4net*') 'The third-party notices are missing Apache log4net.'

$themePath = Join-Path $appDirectory 'themes\default\icons'
$themeCount = @(Get-ChildItem -LiteralPath $themePath -Filter '*.png' -File).Count
Assert-Condition ($themeCount -eq 47) "Expected 47 theme icons, found $themeCount."

$installer = New-Object -ComObject WindowsInstaller.Installer
$msiPath = (Resolve-Path -LiteralPath $msiPath).Path
$template = Get-MsiSummaryProperty $installer $msiPath 7
Assert-Condition ($template -eq 'x64;1033') "Unexpected MSI template: $template"
$database = $installer.GetType().InvokeMember(
    'OpenDatabase', 'InvokeMethod', $null, $installer, @($msiPath, [int] 0))
$allUsers = Get-MsiRecord $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ALLUSERS'"
$installPerUser = Get-MsiRecord $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='MSIINSTALLPERUSER'"
$productVersion = Get-MsiRecord $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductVersion'"
$applicationFolderParent = Get-MsiRecord $database "SELECT ``Directory_Parent`` FROM ``Directory`` WHERE ``Directory``='APPLICATIONFOLDER'"
$userAppsFolderParent = Get-MsiRecord $database "SELECT ``Directory_Parent`` FROM ``Directory`` WHERE ``Directory``='UserAppsFolder'"
$scopeDialog = Get-MsiRecord $database "SELECT ``Dialog`` FROM ``Dialog`` WHERE ``Dialog``='InstallScopeDlg'"
$installDirDialog = Get-MsiRecord $database "SELECT ``Dialog`` FROM ``Dialog`` WHERE ``Dialog``='InstallDirDlg'"
$welcomeEulaDialog = Get-MsiRecord $database "SELECT ``Dialog`` FROM ``Dialog`` WHERE ``Dialog``='WelcomeEulaDlg'"
$licenseDialog = if ($InstallerScope -eq 'PerUser') { 'WelcomeEulaDlg' } else { 'LicenseAgreementDlg' }
$licenseText = Get-MsiRecord $database "SELECT ``Text`` FROM ``Control`` WHERE ``Dialog_``='$licenseDialog' AND ``Control``='LicenseText'"

Assert-Condition ($productVersion -eq $installerVersion) "Unexpected MSI product version: $productVersion"
Assert-Condition ([string]::IsNullOrEmpty($installPerUser)) 'The fixed-scope MSI unexpectedly contains MSIINSTALLPERUSER.'
Assert-Condition ([string]::IsNullOrEmpty($scopeDialog)) 'The fixed-scope MSI unexpectedly contains an install-scope selection dialog.'
Assert-Condition ($licenseText -like '*Permission is hereby granted*') 'The MSI license control does not contain readable MIT license text.'

if ($InstallerScope -eq 'PerUser') {
    Assert-Condition ([string]::IsNullOrEmpty($allUsers)) 'The current-user MSI unexpectedly sets ALLUSERS.'
    Assert-Condition ($applicationFolderParent -eq 'UserAppsFolder') 'The current-user MSI does not install under the user Apps directory.'
    Assert-Condition ($userAppsFolderParent -eq 'LocalAppDataFolder') 'The current-user MSI does not root its Apps directory in LocalAppData.'
    Assert-Condition ([string]::IsNullOrEmpty($installDirDialog)) 'The current-user MSI unexpectedly allows a machine-wide install path.'
    Assert-Condition ($welcomeEulaDialog -eq 'WelcomeEulaDlg') 'The current-user MSI is missing its installation UI.'
}
else {
    Assert-Condition ($allUsers -eq '1') 'The all-users MSI is not authored as a per-machine package.'
    Assert-Condition ($applicationFolderParent -eq 'ProgramFiles6432Folder') 'The all-users MSI does not target 64-bit Program Files.'
    Assert-Condition ([string]::IsNullOrEmpty($userAppsFolderParent)) 'The all-users MSI unexpectedly contains the current-user Apps directory.'
    Assert-Condition ($installDirDialog -eq 'InstallDirDlg') 'The all-users MSI is missing its installation-directory UI.'
    Assert-Condition ([string]::IsNullOrEmpty($welcomeEulaDialog)) 'The all-users MSI unexpectedly contains the current-user UI.'
}

$validationRoot = Join-Path ([IO.Path]::GetTempPath()) ('SuperPutty-MsiValidation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $validationRoot | Out-Null
try {
    $arguments = @('/a', ('"' + $msiPath + '"'), '/qn', ('TARGETDIR="' + $validationRoot + '"'))
    $process = Start-Process 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru
    Assert-Condition ($process.ExitCode -eq 0) "MSI administrative extraction failed with code $($process.ExitCode)."

    $installedApp = Get-ChildItem -LiteralPath $validationRoot -Recurse -Filter 'SuperPutty.exe' -File | Select-Object -First 1
    Assert-Condition ($null -ne $installedApp) 'The MSI does not contain SuperPutty.exe.'
    Assert-Condition ((Get-PeMachine $installedApp.FullName) -eq 0x8664) 'The MSI contains a non-x64 executable.'

    if ($InstallerScope -eq 'PerUser') {
        Assert-Condition ($installedApp.DirectoryName -like '*\Apps\SuperPuTTY') 'The current-user MSI payload is not under Apps\SuperPuTTY.'
    }
    else {
        Assert-Condition ($installedApp.FullName -like '*\PFiles64\*') 'The all-users MSI payload does not target 64-bit Program Files.'
    }

    $installedDirectory = $installedApp.DirectoryName
    $installedMissingDlls = @($requiredDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $installedDirectory $_)) })
    Assert-Condition ($installedMissingDlls.Count -eq 0) "MSI is missing runtime DLLs: $($installedMissingDlls -join ', ')"
    Assert-Condition (Test-Path -LiteralPath (Join-Path $installedDirectory $thirdPartyNoticesName) -PathType Leaf) "MSI is missing $thirdPartyNoticesName."
    $installedThemeCount = @(Get-ChildItem -LiteralPath (Join-Path $installedDirectory 'themes\default\icons') -Filter '*.png' -File).Count
    Assert-Condition ($installedThemeCount -eq 47) "MSI contains $installedThemeCount theme icons instead of 47."
}
finally {
    if (Test-Path -LiteralPath $validationRoot) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}

Write-Host "Release verification passed: version=$ExpectedVersion platform=$Platform DLLs=$($requiredDlls.Count) themes=$themeCount MSI=$template scope=$InstallerScope file=$MsiName"
