[CmdletBinding()]
param(
    [string]$Version = '1.8.0',
    [string]$Configuration = 'Release',
    [ValidateSet('test-x64','win-x64-signed')][string]$InstallerArtifactSuffix = 'test-x64',
    [switch]$RequireSignatures,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "artifacts/release-$Version/$InstallerArtifactSuffix" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$app = Join-Path $repo "bin/x64/$Configuration"
$prefix = "SuperPuTTY-CE-$Version"
if ($InstallerArtifactSuffix -eq 'win-x64-signed' -and -not $RequireSignatures) { throw 'Signed artifact names require signature verification.' }
if ($RequireSignatures) {
    & "$PSScriptRoot/Verify-CodeSignatures.ps1" -Configuration $Configuration -InstallerVersion $Version -InstallerArtifactSuffix $InstallerArtifactSuffix
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
function Read-MsiValue($database, [string]$query) {
    $view = $database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@($query))
    $null = $view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
    $record = $view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)
    if (-not $record) { throw "Required MSI row missing: $query" }
    return $record.GetType().InvokeMember('StringData','GetProperty',$null,$record,1)
}
$installer = New-Object -ComObject WindowsInstaller.Installer
$packages = @()
foreach ($scope in @('PerUser','PerMachine')) {
    $scopeName = if ($scope -eq 'PerUser') { 'current-user' } else { 'all-users' }
    $name = "$prefix-$scopeName-$InstallerArtifactSuffix.msi"
    & "$PSScriptRoot/Verify-ReleaseArtifacts.ps1" -Configuration $Configuration -ExpectedVersion "$Version.0" -InstallerScope $scope -MsiName $name
    $source = [IO.Path]::GetFullPath((Join-Path $repo "SuperPuttyInstaller/bin/x64/$Configuration/$name"))
    $database = $installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@($source,[int]0))
    $productCode = Read-MsiValue $database 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode'''
    $upgradeCode = Read-MsiValue $database 'SELECT `Value` FROM `Property` WHERE `Property`=''UpgradeCode'''
    $productVersion = Read-MsiValue $database 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductVersion'''
    if ($productVersion -ne $Version) { throw 'MSI version mismatch.' }
    Copy-Item -LiteralPath $source -Destination (Join-Path $output $name) -Force
    $packages += [ordered]@{name=$name;scope=$scope;productCode=$productCode;upgradeCode=$upgradeCode;sha256=(Get-FileHash $source).Hash;version=$productVersion}
}

# Explicit allowlist: never include PDBs, user settings, local keys, test output,
# or arbitrary files that happen to exist in a developer's bin directory.
$payload = Join-Path (Split-Path -Parent $output) ('payload-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payload | Out-Null
$required = @('SuperPutty.exe','SuperPutty.exe.config','log4net.dll','WeifenLuo.WinFormsUI.Docking.dll','WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll','Interop.MSTSCLib.dll','AxInterop.MSTSCLib.dll','THIRD-PARTY-NOTICES.txt')
foreach ($name in $required) {
    $file = Join-Path $app $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required runtime artifact missing: $name" }
    Copy-Item -LiteralPath $file -Destination $payload
}
Copy-Item -LiteralPath (Join-Path $repo 'License.txt') -Destination $payload
$icons = Join-Path $payload 'themes/default/icons'
New-Item -ItemType Directory -Path $icons -Force | Out-Null
$iconFiles = @(Get-ChildItem (Join-Path $app 'themes/default/icons') -Filter '*.png' -File)
if ($iconFiles.Count -ne 47) { throw 'Unexpected runtime icon count.' }
$iconFiles | Copy-Item -Destination $icons
@'
SuperPuTTY Community Edition — no-install ZIP
Extract all files to a writable directory and launch SuperPutty.exe.
Requires Windows x64, .NET Framework 4.8, and separately installed PuTTY/PSCP.
Settings behavior is unchanged: USERPROFILE/SuperPuTTY.settings takes precedence;
an existing file beside the EXE is used only if no profile file exists. Sessions
and layouts use the configured settings folder. This ZIP does not provide full
profile isolation or carry a user's private settings. Read the installation guide.
Verify the ZIP checksum, then the Authenticode signature on SuperPutty.exe.
https://github.com/greyhair-atx/superputty/blob/master/docs/manual/installation.md
'@ | Set-Content (Join-Path $payload 'PORTABLE-README.txt') -Encoding utf8
$zipName = if ($RequireSignatures) { "$prefix-portable-win-x64.zip" } else { "$prefix-portable-test-x64.zip" }
Compress-Archive -Path "$payload/*" -DestinationPath (Join-Path $output $zipName) -Force

# CycloneDX runtime-file inventory derived from the exact ZIP payload. External
# clients and Windows/.NET are prerequisites, not files redistributed by this ZIP.
$components = @()
foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
    $relative = $file.FullName.Substring($payload.Length + 1).Replace('\','/')
    $component = [ordered]@{type='file';'bom-ref'="file:$relative";name=$relative;hashes=@(@{alg='SHA-256';content=(Get-FileHash $file.FullName).Hash.ToLowerInvariant()})}
    if ($file.Extension -in '.exe','.dll') {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
        if ($info.FileVersion) { $component.version = $info.FileVersion }
    }
    $components += $component
}
$sbom = [ordered]@{
    bomFormat='CycloneDX';specVersion='1.6';serialNumber=('urn:uuid:' + [Guid]::NewGuid());version=1
    metadata=@{timestamp=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');component=@{type='application';'bom-ref'='superputty-ce';name='SuperPuTTY Community Edition';version=$Version;licenses=@(@{license=@{id='MIT'}})};properties=@(@{name='superputty:inventory-scope';value='Exact runtime payload files including icons, config, and license notices. External PuTTY/viewers, Windows and .NET Framework are prerequisites. NuGet build/test inventory is separate.'})}
    components=$components
}
$sbomName = "$prefix-sbom.cdx.json"
$sbom | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $output $sbomName) -Encoding utf8
$manifest = [ordered]@{version=$Version;sourceCommit=(git -C $repo rev-parse HEAD).Trim();sourceDirty=(@(git -C $repo status --porcelain).Count -gt 0);signaturesRequired=[bool]$RequireSignatures;packages=$packages;portable=$zipName;sbom=$sbomName}
$manifestName = "$prefix-build-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output $manifestName) -Encoding utf8
$assetNames = @($packages.name) + @($zipName,$sbomName,$manifestName)
$checksumName = "$prefix-SHA256SUMS.txt"
$assetNames | ForEach-Object { "$((Get-FileHash (Join-Path $output $_)).Hash.ToLowerInvariant())  $_" } | Set-Content (Join-Path $output $checksumName) -Encoding ascii
foreach ($name in $assetNames) { if (-not (Test-Path (Join-Path $output $name) -PathType Leaf)) { throw "Missing release asset: $name" } }
& "$PSScriptRoot/Verify-ReleaseBundle.ps1" -ReleaseDirectory $output -Version $Version
Write-Output "Prepared local release bundle: $output"
Write-Output "Source dirty: $($manifest.sourceDirty). A dirty-tree candidate must be rebuilt from the approved tagged commit before publication."
