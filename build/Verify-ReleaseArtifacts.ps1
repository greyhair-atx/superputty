[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Platform = 'x64',
    [string] $ExpectedVersion = '1.7.1.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appDirectory = Join-Path $repoRoot "bin\$Platform\$Configuration"
$appPath = Join-Path $appDirectory 'SuperPutty.exe'
$msiPath = Join-Path $repoRoot "SuperPuttyInstaller\bin\$Platform\$Configuration\SuperPuttySetup.msi"

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

$missingDlls = @($requiredDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $appDirectory $_)) })
Assert-Condition ($missingDlls.Count -eq 0) "Missing runtime DLLs: $($missingDlls -join ', ')"

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
$defaultScope = Get-MsiRecord $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='WixAppFolder'"
$scopeDialog = Get-MsiRecord $database "SELECT ``Dialog`` FROM ``Dialog`` WHERE ``Dialog``='InstallScopeDlg'"
$perUserFolder = Get-MsiRecord $database "SELECT ``Target`` FROM ``CustomAction`` WHERE ``Action``='WixSetDefaultPerUserFolder'"
$perMachineFolder = Get-MsiRecord $database "SELECT ``Target`` FROM ``CustomAction`` WHERE ``Action``='SetX64PerMachineFolder'"
$perMachineUiSequence = Get-MsiRecord $database "SELECT ``Action`` FROM ``InstallUISequence`` WHERE ``Action``='SetX64PerMachineFolder'"
$perMachineExecuteSequence = Get-MsiRecord $database "SELECT ``Action`` FROM ``InstallExecuteSequence`` WHERE ``Action``='SetX64PerMachineFolder'"
Assert-Condition ($allUsers -eq '2') 'The MSI is not authored as a dual-scope package.'
Assert-Condition ($installPerUser -eq '1') 'The MSI does not default to a current-user installation.'
Assert-Condition ($productVersion -eq ($ExpectedVersion -replace '\.0$', '')) "Unexpected MSI product version: $productVersion"
Assert-Condition ($defaultScope -eq 'WixPerUserFolder') 'The installer UI does not default to current user.'
Assert-Condition ($scopeDialog -eq 'InstallScopeDlg') 'The installer does not contain the install-scope selection dialog.'
Assert-Condition ($perUserFolder -eq '[LocalAppDataFolder]Apps\[ApplicationFolderName]') 'Unexpected current-user installation folder.'
Assert-Condition ($perMachineFolder -eq '[ProgramFiles6432Folder][ApplicationFolderName]') 'Unexpected all-users installation folder.'
Assert-Condition ($perMachineUiSequence -eq 'SetX64PerMachineFolder') 'The all-users x64 path is not configured in the installer UI sequence.'
Assert-Condition ($perMachineExecuteSequence -eq 'SetX64PerMachineFolder') 'The all-users x64 path is not configured in the installer execute sequence.'

$validationRoot = Join-Path ([IO.Path]::GetTempPath()) ('SuperPutty-MsiValidation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $validationRoot | Out-Null
try {
    $arguments = @('/a', ('"' + $msiPath + '"'), '/qn', ('TARGETDIR="' + $validationRoot + '"'))
    $process = Start-Process 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru
    Assert-Condition ($process.ExitCode -eq 0) "MSI administrative extraction failed with code $($process.ExitCode)."

    $installedApp = Get-ChildItem -LiteralPath $validationRoot -Recurse -Filter 'SuperPutty.exe' -File | Select-Object -First 1
    Assert-Condition ($null -ne $installedApp) 'The MSI does not contain SuperPutty.exe.'
    Assert-Condition ($installedApp.FullName -like '*\PFiles64\*') 'The MSI does not target 64-bit Program Files.'
    Assert-Condition ((Get-PeMachine $installedApp.FullName) -eq 0x8664) 'The MSI contains a non-x64 executable.'

    $installedDirectory = $installedApp.DirectoryName
    $installedMissingDlls = @($requiredDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $installedDirectory $_)) })
    Assert-Condition ($installedMissingDlls.Count -eq 0) "MSI is missing runtime DLLs: $($installedMissingDlls -join ', ')"
    $installedThemeCount = @(Get-ChildItem -LiteralPath (Join-Path $installedDirectory 'themes\default\icons') -Filter '*.png' -File).Count
    Assert-Condition ($installedThemeCount -eq 47) "MSI contains $installedThemeCount theme icons instead of 47."
}
finally {
    if (Test-Path -LiteralPath $validationRoot) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}

Write-Host "Release verification passed: version=$ExpectedVersion platform=$Platform DLLs=$($requiredDlls.Count) themes=$themeCount MSI=$template scope=current-user-or-machine"
