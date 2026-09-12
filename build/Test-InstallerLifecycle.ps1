[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[string]$Version='1.8.0')
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Run only in a disposable Windows VM/hosted agent with SUPERPUTTY_DISPOSABLE_TEST=1.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Both-scope lifecycle testing requires an elevated disposable session.'}
$paths=@('HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/*','HKLM:/Software/Microsoft/Windows/CurrentVersion/Uninstall/*','HKLM:/Software/WOW6432Node/Microsoft/Windows/CurrentVersion/Uninstall/*')
if(@(Get-ItemProperty $paths -ErrorAction SilentlyContinue | Where-Object DisplayName -Match 'SuperPuTTY').Count){throw 'Existing SuperPuTTY installation detected; refusing lifecycle tests.'}
if((Test-Path "$env:LOCALAPPDATA/Apps/SuperPuTTY") -or (Test-Path "$env:ProgramFiles/SuperPuTTY")){throw 'Existing installation files detected.'}
$manifest=Get-Content (Join-Path $ReleaseDirectory "SuperPuTTY-CE-$Version-build-manifest.json") -Raw|ConvertFrom-Json
if($manifest.version -ne $Version -or $manifest.packages.Count -ne 2){throw 'Invalid release manifest.'}
$preference=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
if(Test-Path $preference){throw 'Existing preferences detected; refusing lifecycle tests.'}
# Installers must not rewrite or remove legacy user preferences.
'<?xml version="1.0"?><Settings><LaunchTest>preserve-me</LaunchTest></Settings>'|Set-Content $preference
$preferenceHash=(Get-FileHash $preference).Hash
try {
 foreach($package in $manifest.packages){
  $msi=[IO.Path]::GetFullPath((Join-Path $ReleaseDirectory $package.name))
  if((Get-FileHash $msi).Hash -ne $package.sha256){throw 'MSI hash mismatch.'}
  $code=$package.productCode
  if($code -notmatch '^\{[0-9A-Fa-f-]{36}\}$'){throw 'Invalid ProductCode.'}
  $root=if($package.scope -eq 'PerUser'){'HKCU:'}else{'HKLM:'}
  $key="$root/Software/Microsoft/Windows/CurrentVersion/Uninstall/$code"
  $log=Join-Path $ReleaseDirectory "$($package.scope)-install.log"
  $installed=$false
  try {
   $process=Start-Process msiexec.exe -ArgumentList @('/i',"`"$msi`"",'/qn','/norestart','/l*v',"`"$log`"") -WindowStyle Hidden -Wait -PassThru
   if($process.ExitCode -notin 0,3010){throw "Silent install failed: $($process.ExitCode). See $log"}
   $installed=$true
   if((Get-ItemProperty $key).DisplayName -ne 'SuperPuTTY Community Edition'){throw 'Installed product name mismatch.'}
   if((Get-FileHash $preference).Hash -ne $preferenceHash){throw 'Installer changed legacy preferences.'}
  } finally {
   if($installed){
    $process=Start-Process msiexec.exe -ArgumentList @('/x',$code,'/qn','/norestart') -WindowStyle Hidden -Wait -PassThru
    if($process.ExitCode -notin 0,3010){throw "Silent uninstall failed: $($process.ExitCode)"}
   }
  }
  if(Test-Path $key){throw 'Uninstall registration remains.'}
  if((Get-FileHash $preference).Hash -ne $preferenceHash){throw 'Uninstall changed legacy preferences.'}
  Write-Output "$($package.scope): unattended install/uninstall and preference preservation passed."
 }
} finally { Remove-Item -LiteralPath $preference -Force }
