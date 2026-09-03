[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$InstallerVersion = '1.7.2',
    [string]$InstallerArtifactSuffix = 'win-x64-signed',
    [string]$ExpectedSigner = 'Christopher Thornton'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifacts = @(
    (Join-Path $repositoryRoot "bin\$Platform\$Configuration\SuperPutty.exe"),
    (Join-Path $repositoryRoot "SuperPuttyInstaller\bin\$Platform\$Configuration\SuperPutty-$InstallerVersion-current-user-$InstallerArtifactSuffix.msi"),
    (Join-Path $repositoryRoot "SuperPuttyInstaller\bin\$Platform\$Configuration\SuperPutty-$InstallerVersion-all-users-$InstallerArtifactSuffix.msi")
)

foreach ($artifact in $artifacts) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Signing verification artifact not found: $artifact"
    }

    $signature = Get-AuthenticodeSignature -FilePath $artifact
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Invalid Authenticode signature for '$artifact': $($signature.Status) - $($signature.StatusMessage)"
    }

    $signerName = $signature.SignerCertificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false
    )
    if ($signerName -ne $ExpectedSigner) {
        throw "Artifact signer '$signerName' did not match expected signer '$ExpectedSigner': $artifact"
    }

    if ($null -eq $signature.TimeStamperCertificate) {
        throw "The Authenticode signature is not timestamped: $artifact"
    }

    Write-Host "Verified signed artifact: $artifact"
    Write-Host "  Signer: $($signature.SignerCertificate.Subject)"
    Write-Host "  Timestamp authority: $($signature.TimeStamperCertificate.Subject)"
}
