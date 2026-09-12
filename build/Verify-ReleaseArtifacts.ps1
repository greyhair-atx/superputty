[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Platform = 'x64',
    [string] $ExpectedVersion = '1.8.0.0',
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
    $MsiName = "SuperPuTTY-CE-$installerVersion-$scopeName-test-x64.msi"
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
Assert-Condition ($MsiName -cmatch '^SuperPuTTY-CE-[0-9.]+-(current-user|all-users)-.+\.msi$') 'The MSI filename does not use CE branding.'
Assert-Condition ($version.FileDescription -eq 'SuperPuTTY Community Edition') 'Unexpected application file description.'
Assert-Condition ($version.ProductName -ceq 'SuperPuTTY') 'AssemblyProduct changed the legacy settings identity.'
Assert-Condition ($version.CompanyName -eq 'Chris Thornton') 'Unexpected application company.'
Assert-Condition ($version.LegalCopyright -like '*2009 - 2023 Jim Radford*') 'Missing original copyright.'
Assert-Condition ($version.LegalCopyright -like '*2026 Chris Thornton*') 'Missing community modifications copyright.'
Assert-Condition ($version.FileVersion -eq $ExpectedVersion) "Unexpected file version: $($version.FileVersion)"
Assert-Condition ($version.ProductVersion -eq $ExpectedVersion) "Unexpected product version: $($version.ProductVersion)"

$requiredDlls = @(
    'log4net.dll',
    'WeifenLuo.WinFormsUI.Docking.dll',
    'WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll',
    'Interop.MSTSCLib.dll',
    'AxInterop.MSTSCLib.dll'
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
$productName = Get-MsiRecord $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductName'"
$upgradeCode = Get-MsiRecord $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='UpgradeCode'"
$expectedUpgradeCode = if ($InstallerScope -eq 'PerUser') { '{3E4059D9-E5A5-48DE-B787-FC88268C1B95}' } else { '{98CB966B-60D4-46C3-AADA-1990464696B8}' }
Assert-Condition ($productName -eq 'SuperPuTTY Community Edition') 'Unexpected MSI/ARP product name.'
Assert-Condition ($upgradeCode -eq $expectedUpgradeCode) 'Unexpected edition/scope upgrade identity.'
$shortcutName = Get-MsiRecord $database "SELECT ``Name`` FROM ``Shortcut`` WHERE ``Shortcut``='ApplicationShortcut1'"
Assert-Condition ($shortcutName -like '*SuperPuTTY Community Edition') 'The Start menu shortcut is missing CE branding.'
$shortcutTarget = Get-MsiRecord $database "SELECT ``Target`` FROM ``Shortcut`` WHERE ``Shortcut``='ApplicationShortcut1'"
Assert-Condition ($shortcutTarget -eq '[APPLICATIONFOLDER]SuperPutty.exe') 'The shortcut executable changed.'
$legacyCode = '{42567F59-2F27-4E5B-A900-9141DC2DD929}'
$bridgeMinimum = Get-MsiRecord $database "SELECT ``VersionMin`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$legacyCode' AND ``ActionProperty``='LEGACYCOMMUNITYFOUND'"
$bridgeMaximum = Get-MsiRecord $database "SELECT ``VersionMax`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$legacyCode' AND ``ActionProperty``='LEGACYCOMMUNITYFOUND'"
$bridgeAttributes = Get-MsiRecord $database "SELECT ``Attributes`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$legacyCode' AND ``ActionProperty``='LEGACYCOMMUNITYFOUND'"
Assert-Condition ($bridgeMinimum -eq '1.6.0' -and $bridgeMaximum -eq '1.7.5') 'The legacy community bridge could target upstream or unknown future versions.'
Assert-Condition (([int]$bridgeAttributes -band 770) -eq 768) 'The community bridge must include both endpoints and remove the related product.'
$upstreamAttributes = Get-MsiRecord $database "SELECT ``Attributes`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$legacyCode' AND ``ActionProperty``='LEGACYUPSTREAMFOUND'"
Assert-Condition (([int]$upstreamAttributes -band 2) -eq 2) 'Upstream must be detected only, never removed.'
$upstreamBlock = Get-MsiRecord $database "SELECT ``Description`` FROM ``LaunchCondition`` WHERE ``Condition``='Installed OR NOT LEGACYUPSTREAMFOUND'"
Assert-Condition ($upstreamBlock -like '*uninstall*') 'The ambiguous upstream installation guard is missing.'
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
Assert-Condition ($licenseText -like '*2009 - 2023 Jim Radford*') 'The MSI license is missing the original copyright.'
Assert-Condition ($licenseText -like '*THE SOFTWARE IS PROVIDED*') 'The MSI license is missing the MIT disclaimer.'

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
    $process = Start-Process 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
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
    Assert-Condition ((Get-FileHash $installedApp.FullName).Hash -eq (Get-FileHash $appPath).Hash) 'MSI executable differs from the verified build (including its signature).'
    $installedLicense = Join-Path $installedDirectory 'License.txt'
    Assert-Condition (Test-Path -LiteralPath $installedLicense -PathType Leaf) 'MSI is missing the original MIT license.'
    Assert-Condition ((Get-FileHash $installedLicense).Hash -eq (Get-FileHash (Join-Path $repoRoot 'License.txt')).Hash) 'MSI license differs from the original.'
    $installedMissingDlls = @($requiredDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $installedDirectory $_)) })
    Assert-Condition ($installedMissingDlls.Count -eq 0) "MSI is missing runtime DLLs: $($installedMissingDlls -join ', ')"
    foreach ($file in @($requiredDlls) + @('SuperPutty.exe.config', $thirdPartyNoticesName)) {
        Assert-Condition ((Get-FileHash (Join-Path $installedDirectory $file)).Hash -eq (Get-FileHash (Join-Path $appDirectory $file)).Hash) "MSI payload differs from the build: $file"
    }
    Assert-Condition (Test-Path -LiteralPath (Join-Path $installedDirectory $thirdPartyNoticesName) -PathType Leaf) "MSI is missing $thirdPartyNoticesName."
    $installedThemeCount = @(Get-ChildItem -LiteralPath (Join-Path $installedDirectory 'themes\default\icons') -Filter '*.png' -File).Count
    Assert-Condition ($installedThemeCount -eq 47) "MSI contains $installedThemeCount theme icons instead of 47."
}
finally {
    if (Test-Path -LiteralPath $validationRoot) {
        $resolvedValidationRoot = (Resolve-Path -LiteralPath $validationRoot).Path
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        Assert-Condition ($resolvedValidationRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) 'Unsafe extraction cleanup path.'
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}

Write-Host "Release verification passed: version=$ExpectedVersion platform=$Platform DLLs=$($requiredDlls.Count) themes=$themeCount MSI=$template scope=$InstallerScope file=$MsiName"
