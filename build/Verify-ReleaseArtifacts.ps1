[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Platform = 'x64',
    [string] $ExpectedVersion = '1.6.1.0'
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

Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) "Missing application: $appPath"
Assert-Condition (Test-Path -LiteralPath $msiPath -PathType Leaf) "Missing installer: $msiPath"
Assert-Condition ((Get-PeMachine $appPath) -eq 0x8664) 'SuperPutty.exe is not an x64 PE image.'

$version = (Get-Item -LiteralPath $appPath).VersionInfo
Assert-Condition ($version.FileVersion -eq $ExpectedVersion) "Unexpected file version: $($version.FileVersion)"
Assert-Condition ($version.ProductVersion -eq $ExpectedVersion) "Unexpected product version: $($version.ProductVersion)"

$requiredDlls = @(
    'BouncyCastle.Cryptography.dll',
    'log4net.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'Microsoft.Bcl.Cryptography.dll',
    'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
    'Microsoft.Extensions.Logging.Abstractions.dll',
    'Renci.SshNet.dll',
    'System.Buffers.dll',
    'System.Formats.Asn1.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Threading.Tasks.Extensions.dll',
    'WeifenLuo.WinFormsUI.Docking.dll',
    'WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll'
)

$missingDlls = @($requiredDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $appDirectory $_)) })
Assert-Condition ($missingDlls.Count -eq 0) "Missing runtime DLLs: $($missingDlls -join ', ')"

$themePath = Join-Path $appDirectory 'themes\default\icons'
$themeCount = @(Get-ChildItem -LiteralPath $themePath -Filter '*.png' -File).Count
Assert-Condition ($themeCount -eq 47) "Expected 47 theme icons, found $themeCount."

$installer = New-Object -ComObject WindowsInstaller.Installer
$template = Get-MsiSummaryProperty $installer $msiPath 7
Assert-Condition ($template -eq 'x64;1033') "Unexpected MSI template: $template"

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

Write-Host "Release verification passed: version=$ExpectedVersion platform=$Platform DLLs=$($requiredDlls.Count) themes=$themeCount MSI=$template"
