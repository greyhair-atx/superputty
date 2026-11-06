[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory,[string]$Installer,[switch]$VerifyOnly)
$ErrorActionPreference='Stop'
$pin=Import-PowerShellDataFile "$PSScriptRoot/InnoToolchain.psd1"
if(-not $Installer){
 $Installer=Join-Path $env:TEMP ('superputty-inno-'+[guid]::NewGuid().ToString('N')+'.exe')
 Invoke-WebRequest $pin.InstallerUrl -OutFile $Installer
}
if((Get-FileHash $Installer).Hash -ne $pin.InstallerSha256){throw 'Inno installer does not match the reviewed toolchain pin.'}
$signature=Get-AuthenticodeSignature $Installer
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName',$false) -ne $pin.Publisher){throw 'Inno installer publisher verification failed.'}
if($VerifyOnly){Write-Output "Verified Inno Setup $($pin.Version) installer.";return}
$target=[IO.Path]::GetFullPath($Directory)
if(Test-Path $target){throw 'Use a new compiler directory; do not overwrite an existing toolchain.'}
$process=Start-Process $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS',"/DIR=`"$target`"") -PassThru -Wait -WindowStyle Hidden
if($process.ExitCode -ne 0 -or (Get-FileHash "$target/ISCC.exe").Hash -ne $pin.CompilerSha256){throw 'Pinned Inno compiler installation failed.'}
Write-Output "Installed verified compiler: $target/ISCC.exe"
