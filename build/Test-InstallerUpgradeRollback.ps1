[CmdletBinding()]
param([Parameter(Mandatory)][string]$StageDirectory,[switch]$ExpectRollbackDisabled)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Requires an authorized disposable VM.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if($identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$' -or ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Requires the dedicated interactive standard account.'}
$stage=[IO.Path]::GetFullPath($StageDirectory)
$fixture=Get-Content "$stage/fixture.json" -Raw|ConvertFrom-Json
if($fixture.guardOnly -and -not $ExpectRollbackDisabled){throw 'Guard-only fixture cannot exercise failure recovery.'}
if(Test-Path "$stage/summary.json"){throw 'Prior results exist; use a new stage.'}
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
foreach($pair in @(@('candidate.msi',$fixture.candidateSha256),@('failing-candidate.msi',$fixture.failingCandidateSha256))){Assert ((Get-FileHash "$stage/$($pair[0])").Hash -eq $pair[1]) 'Fixture hash changed.'}
$old='{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}'
$new=[string]$fixture.candidateProductCode
Assert ($new -match '^\{[0-9A-Fa-f-]{36}\}$' -and $new -ne $old) 'Invalid candidate identity.'
Assert ((Get-FileHash "$stage/previous.msi").Hash -eq '5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36') 'Previous MSI is not the published signed 1.8.3 user package.'
Assert ((Get-AuthenticodeSignature "$stage/previous.msi").Status -eq 'Valid') 'Previous MSI signature invalid.'
$msi=New-Object -ComObject WindowsInstaller.Installer
function State([string]$Code){$msi.GetType().InvokeMember('ProductState','GetProperty',$null,$msi,@($Code))}
function Assignment([string]$Code){$msi.GetType().InvokeMember('ProductInfo','GetProperty',$null,$msi,@($Code,'AssignmentType'))}
$exe=Join-Path $env:LOCALAPPDATA 'Apps/SuperPuTTY/SuperPutty.exe'
Assert ((State $old) -eq -1 -and (State $new) -eq -1 -and -not (Test-Path $exe)) 'Test must start without either installed or advertised product.'
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
Assert (Test-Path $preferences) 'Expected existing disposable test settings.'
[xml]$settings=Get-Content $preferences -Raw
$baseline=@(Get-Item $preferences;Get-ChildItem ([string]$settings.Settings.SettingsFolder) -Recurse -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}
$baseline|ConvertTo-Json|Set-Content "$stage/settings-baseline.json"
function Settings-Unchanged{foreach($entry in $baseline){Assert ((Test-Path $entry.path) -and (Get-FileHash $entry.path).Hash -eq $entry.hash) "Settings changed: $($entry.path)"}}
function Install([string]$File,[string]$Name){
 $msiPath=Join-Path $stage $File
 $logPath=Join-Path $stage "$Name.log"
 $process=Start-Process msiexec.exe -WindowStyle Hidden -PassThru -ArgumentList @('/i',"`"$msiPath`"",'/qn','/norestart','/l*v',"`"$logPath`"")
 if(-not $process.WaitForExit(180000)){throw 'MSI timed out; inspect guest.'}
 return $process.ExitCode
}
$success=$false
$restored=$false
$disabledRollbackRejected=$null
try{
 Assert ((Install 'previous.msi' 'install-previous') -eq 0) 'Previous install failed.'
 Assert ((State $old) -eq 5 -and [string](Assignment $old) -eq '0') 'Previous user package missing.'
 $oldHash=(Get-FileHash $exe).Hash
 if($ExpectRollbackDisabled){
  Assert ((Install 'candidate.msi' 'rollback-disabled') -eq 1603) 'Upgrade did not reject disabled rollback.'
  Assert ((Get-Content "$stage/rollback-disabled.log" -Raw) -match 'Upgrading SuperPuTTY requires Windows Installer rollback') 'Disabled rollback was not rejected by the safety condition.'
  Assert ((State $old) -eq 5 -and (State $new) -ne 5 -and (Get-FileHash $exe).Hash -eq $oldHash) 'Disabled rollback test changed the old installation.'
  Settings-Unchanged
  $disabledRollbackRejected=$true
  $success=$true
  return
 }
 Assert ((Install 'failing-candidate.msi' 'forced-failure') -eq 1603) 'Expected deliberate MSI failure 1603.'
 $log=Get-Content "$stage/forced-failure.log" -Raw
 Assert ($log -match 'Action ended[^\r\n]*RemoveExistingProducts\. Return value 1') 'Old removal did not complete before the forced failure.'
 Assert ($log -match 'Action ended[^\r\n]*InstallExecute\. Return value 1') 'Replacement script did not execute before the forced failure.'
 Assert ($log -match 'ForceUpgradeRollback') 'Forced failure action was not recorded.'
 Assert ((State $old) -eq 5 -and (State $new) -ne 5) 'Rollback did not restore the previous registration.'
 Assert ([string](Assignment $old) -eq '0') 'Restored package has wrong scope.'
 Assert ((Get-FileHash $exe).Hash -eq $oldHash -and (Get-AuthenticodeSignature $exe).Status -eq 'Valid') 'Rollback did not restore the signed old executable.'
 Settings-Unchanged
 $restored=$true
 Assert ((Install 'candidate.msi' 'successful-upgrade') -eq 0) 'Normal transactional upgrade failed.'
 Assert ((State $old) -ne 5 -and (State $new) -eq 5 -and [string](Assignment $new) -eq '0') 'Successful upgrade has wrong registrations/scope.'
 Settings-Unchanged
 $success=$true
}finally{
 foreach($code in @($new,$old)){if((State $code) -in @(1,5)){$cleanupLog=Join-Path $stage "cleanup-$($code.Trim('{}')).log";$p=Start-Process msiexec.exe -WindowStyle Hidden -PassThru -ArgumentList @('/x',$code,'/qn','/norestart','/l*v',"`"$cleanupLog`"");if(-not $p.WaitForExit(180000)){throw 'Cleanup timed out.'}}}
 # A failed per-user rollback can strand files after removing registration.
 # Recover ownership through the signed MSI, then uninstall; never edit MSI's
 # private registry or delete the payload to make a failed regression look clean.
 if(Test-Path $exe){
  Assert ((Install 'previous.msi' 'cleanup-recover-ownership') -eq 0) 'Recovery reinstall failed; inspect guest.'
  $cleanupLog=Join-Path $stage 'cleanup-recovered.log'
  $p=Start-Process msiexec.exe -WindowStyle Hidden -PassThru -ArgumentList @('/x',$old,'/qn','/norestart','/l*v',"`"$cleanupLog`"")
  if(-not $p.WaitForExit(180000)){throw 'Recovery cleanup timed out.'}
 }
 $cleaned=(State $old) -eq -1 -and (State $new) -eq -1 -and -not (Test-Path $exe)
 $preserved=$true;try{Settings-Unchanged}catch{$preserved=$false}
 [pscustomobject]@{success=($success -and $cleaned -and $preserved);scenario=$(if($ExpectRollbackDisabled){'DisabledRollback'}else{'FailureRecoveryAndSuccess'});disabledRollbackRejected=$disabledRollbackRejected;previousRestoredAfterFailure=$restored;previousVersion='1.8.3';candidateVersion=$fixture.candidateVersion;identity=$identity.Name;settingsPreserved=$preserved;cleanupSucceeded=$cleaned;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$stage/summary.json"
}
