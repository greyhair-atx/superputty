[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerDirectory,[Parameter(Mandatory)][string]$PreviousMsi,
 [Parameter(Mandatory)][string]$ResultDirectory,[Parameter(Mandatory)][string]$WinGetPath,
 [ValidateSet('user','machine')][string]$Scope='user')
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if([Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0 -or ($Scope -eq 'machine' -and -not $admin) -or ($Scope -eq 'user' -and ($admin -or $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'))){throw 'An interactive session with the reviewed test identity is required.'}
if(Test-Path $ResultDirectory){throw 'Retain prior results; use a new directory.'}
New-Item -ItemType Directory $ResultDirectory|Out-Null
$result=(Resolve-Path $ResultDirectory).Path
$package='ChrisThornton.SuperPuTTYCommunityEdition';$source='SPCEInnoCandidate'
$product=if($Scope -eq 'user'){'{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}'}else{'{A339D1A0-E398-46D4-A1A3-1CE0477E69BE}'}
$expectedMsiHash=if($Scope -eq 'user'){'5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36'}else{'CAC64BD522C4DFF97501290E62C69E55D638895C4FF5213C8412879A5512D60B'}
$hive=if($Scope -eq 'user'){'HKCU'}else{'HKLM'}
$key="Registry::$hive\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EBCA29DD-46C1-4A93-9340-79DF5375711D}_is1"
$app=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Programs\SuperPuTTY Community Edition'}else{Join-Path $env:ProgramFiles 'SuperPuTTY Community Edition'}
$oldExe=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Apps\SuperPuTTY\SuperPutty.exe'}else{Join-Path $env:ProgramFiles 'SuperPuTTY\SuperPutty.exe'}
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings';$baseline=@()
if(Test-Path $preferences){[xml]$settings=Get-Content $preferences -Raw;$baseline=@(Get-Item $preferences;Get-ChildItem ([string]$settings.Settings.SettingsFolder) -Recurse -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}}
$baseline|ConvertTo-Json|Set-Content "$result/settings-baseline.json"
$backupIndex=0
foreach($entry in $baseline){Copy-Item -LiteralPath $entry.path -Destination "$result/settings-backup-$backupIndex";$backupIndex++}
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Preserved{foreach($entry in $baseline){Assert ((Test-Path $entry.path) -and (Get-FileHash $entry.path).Hash -eq $entry.hash) 'Settings changed.'}}
function Run([string]$File,[string[]]$Arguments){$p=Start-Process $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru;return $p.ExitCode}
function WinGet([string[]]$Arguments,[string]$Label){& $WinGetPath @Arguments *> "$result/$Label.txt";return $LASTEXITCODE}
function Setup([string]$Version,[string]$Label){Run (Join-Path $InstallerDirectory "SuperPuTTY-CE-Candidate-$Version-none.exe") @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/$(if($Scope -eq 'user'){'CURRENTUSER'}else{'ALLUSERS'})","/LOG=`"$result\$Label.log`"")}
$msi=New-Object -ComObject WindowsInstaller.Installer
function MsiState{$msi.GetType().InvokeMember('ProductState','GetProperty',$null,$msi,@($product))}
function OldRegistration{
 @(foreach($root in @('HKCU','HKLM')){$oldKey="Registry::$root\Software\Microsoft\Windows\CurrentVersion\Uninstall\$product";if(Test-Path $oldKey){[pscustomobject]@{hive=$root;values=@((Get-ItemProperty $oldKey).PSObject.Properties|Where-Object Name -NotMatch '^PS'|Sort-Object Name|ForEach-Object {[pscustomobject]@{name=$_.Name;value=$_.Value}})}}})|ConvertTo-Json -Depth 8 -Compress
}
$checks=[Collections.Generic.List[string]]::new();$success=$false;$ownsMsi=$false
Assert (-not (Test-Path $key) -and (MsiState) -eq -1) 'Existing package must be reviewed before testing.'
Assert ((Get-FileHash $PreviousMsi).Hash -eq $expectedMsiHash -and (Get-AuthenticodeSignature $PreviousMsi).Status -eq 'Valid') 'Published recovery MSI verification failed.'
foreach($pin in (Get-Content "$InstallerDirectory/candidate-hashes.json" -Raw|ConvertFrom-Json)){Assert ((Get-FileHash (Join-Path $InstallerDirectory $pin.name)).Hash -eq $pin.sha256 -and (Get-AuthenticodeSignature (Join-Path $InstallerDirectory $pin.name)).Status -eq 'Valid') 'Candidate setup verification failed.'}
try{
 $ownsMsi=$true
 Assert ((Run msiexec.exe @('/i',"`"$PreviousMsi`"",'/qn','/norestart','/l*v',"`"$result\install-msi.log`"")) -eq 0) 'Published MSI install failed.'
 Assert ((MsiState) -eq 5 -and (Test-Path $oldExe)) 'Published MSI is not fully installed.'
 $oldHash=(Get-FileHash $oldExe).Hash;$oldRegistration=OldRegistration
 Assert ((WinGet @('list','--id',$package,'--exact','--source',$source,'--accept-source-agreements','--disable-interactivity') 'msi-correlation') -eq 0) 'Real catalog identity did not correlate to the MSI.'
 foreach($command in @('explicit','unscoped','all')){
  $arguments=if($command -eq 'all'){@('upgrade','--all')}else{@('upgrade','--id',$package,'--exact')}
  $arguments+=@('--source',$source,'--silent','--accept-source-agreements','--accept-package-agreements','--disable-interactivity')
  if($command -eq 'explicit'){$arguments+=@('--scope',$Scope)}
  $exit=WinGet $arguments "msi-upgrade-$command"
  Assert ((MsiState) -eq 5 -and (Get-FileHash $oldExe).Hash -eq $oldHash -and (OldRegistration) -ceq $oldRegistration -and -not (Test-Path $key)) 'Automatic cross-type upgrade modified the MSI.'
  Preserved;$checks.Add("MSI retained unchanged after $command upgrade (exit $exit)")
 }
 Assert ((Setup '1.8.4.100' 'existing-msi-guard') -ne 0 -and (MsiState) -eq 5 -and -not (Test-Path $key)) 'Candidate failed to reject an existing MSI.'
 # Explicit migration remains separate and nontransactional. The signed recovery
 # MSI and backed-up settings are available before removing the actual product.
 Assert ((WinGet @('uninstall','--product-code',$product,'--exact','--silent','--accept-source-agreements','--disable-interactivity') 'explicit-msi-removal') -eq 0) 'Explicit MSI removal failed.'
 Assert ((MsiState) -eq -1 -and -not (Test-Path $oldExe)) 'Explicit MSI removal was incomplete.'
 $ownsMsi=$false;Preserved
 Assert ((WinGet @('install','--id',$package,'--exact','--version','1.8.4.100','--scope',$Scope,'--source',$source,'--silent','--accept-source-agreements','--accept-package-agreements','--disable-interactivity') 'migration-install') -eq 0) 'Catalog fresh installation after explicit removal failed.'
 Assert ((Get-ItemProperty $key).DisplayVersion -eq '1.8.4.100') 'Migration registered the wrong version or scope.'
 Preserved;$checks.Add('Explicit MSI removal then catalog installation under the real package identity')
 foreach($command in @('explicit','unscoped','all')){
  if($command -ne 'explicit'){Assert ((Run "$app/unins000.exe" @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')) -eq 0) 'Scenario cleanup failed.';Assert ((Setup '1.8.4.100' "reset-$command") -eq 0) 'Scenario old candidate installation failed.'}
  $arguments=if($command -eq 'all'){@('upgrade','--all')}else{@('upgrade','--id',$package,'--exact')}
  $arguments+=@('--source',$source,'--silent','--accept-source-agreements','--accept-package-agreements','--disable-interactivity')
  if($command -eq 'explicit'){$arguments+=@('--scope',$Scope)}
  Assert ((WinGet $arguments "inno-upgrade-$command") -eq 0) "Candidate $command catalog upgrade failed."
  Assert ((Get-ItemProperty $key).DisplayVersion -eq '1.8.4.101' -and -not (Test-Path "$app/.upgrade-recovery")) 'Catalog upgrade did not commit cleanly.'
  Assert ((Get-AuthenticodeSignature "$app/unins000.exe").Status -eq 'Valid') 'Catalog upgrade lost the signed uninstaller.'
  Preserved;$checks.Add("Inno $command catalog upgrade passed under the real package identity")
 }
 $success=$true
}finally{
 if((Test-Path $key) -and -not (Test-Path "$app/.upgrade-recovery")){$null=Run "$app/unins000.exe" @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')}
 if($ownsMsi -and (MsiState) -eq 5){$null=Run msiexec.exe @('/x',$product,'/qn','/norestart')}
 $cleaned=(-not (Test-Path $key)) -and (MsiState) -eq -1 -and -not (Test-Path $oldExe) -and -not (Test-Path "$app/.upgrade-recovery")
 $preserved=$true;foreach($entry in $baseline){if(-not (Test-Path $entry.path) -or (Get-FileHash $entry.path).Hash -ne $entry.hash){$preserved=$false}}
 [pscustomobject]@{success=($success -and $cleaned -and $preserved);scope=$Scope;identity=$identity.Name;checks=@($checks.ToArray());settingsPreserved=$preserved;cleanupSucceeded=$cleaned}|ConvertTo-Json -Depth 5|Set-Content "$result/summary.json"
}
