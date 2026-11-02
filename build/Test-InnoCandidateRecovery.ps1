[CmdletBinding()]
param([Parameter(Mandatory)][string]$FixtureDirectory,[Parameter(Mandatory)][string]$ResultDirectory,
 [ValidateSet('user','machine')][string]$Scope='machine',[switch]$LaunchTest)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
$id=[Security.Principal.WindowsIdentity]::GetCurrent()
$admin=([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Scope -eq 'machine' -and -not $admin) -or ($Scope -eq 'user' -and ($admin -or $id.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'))){throw 'Unexpected test identity.'}
if(Test-Path $ResultDirectory){throw 'Use a new result directory.'}
New-Item -ItemType Directory $ResultDirectory|Out-Null
$result=(Resolve-Path $ResultDirectory).Path
$app=if($Scope -eq 'machine'){Join-Path $env:ProgramFiles 'SuperPuTTY Community Edition'}else{Join-Path $env:LOCALAPPDATA 'Programs\SuperPuTTY Community Edition'}
$hive=if($Scope -eq 'machine'){'HKLM'}else{'HKCU'}
$key="Registry::$hive\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EBCA29DD-46C1-4A93-9340-79DF5375711D}_is1"
if(Test-Path $key){throw 'Candidate already installed.'}
$programs=if($Scope -eq 'machine'){Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'}else{[Environment]::GetFolderPath('Programs')}
$shortcut=Join-Path $programs 'SuperPuTTY Community Edition.lnk'
$mode=if($Scope -eq 'machine'){'/ALLUSERS'}else{'/CURRENTUSER'}
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Fixture([string]$Version,[string]$Fault){Join-Path $FixtureDirectory "$Version-$Fault/SuperPuTTY-CE-Candidate-$Version-$Fault.exe"}
function StartSetup([string]$Version,[string]$Fault,[string]$Label,[switch]$Wait){
 $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',$mode,"/LOG=`"$result\$Label.log`"")
 Start-Process (Fixture $Version $Fault) -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait:$Wait -RedirectStandardOutput "$result/$Label.stdout.txt" -RedirectStandardError "$result/$Label.stderr.txt"
}
function Install([string]$Version,[string]$Fault,[string]$Label){(StartSetup $Version $Fault $Label -Wait).ExitCode}
function ReadSetupLog([string]$Path){
 if(-not (Test-Path $Path)){return ''}
 $stream=[IO.File]::Open($Path,'Open','Read',([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 $reader=[IO.StreamReader]::new($stream)
 try{$reader.ReadToEnd()}finally{$reader.Dispose()}
}
function Registration{@((Get-ItemProperty $key).PSObject.Properties|Where-Object {$_.Name -notmatch '^PS'}|Sort-Object Name|ForEach-Object {[pscustomobject]@{name=$_.Name;value=$_.Value}})|ConvertTo-Json -Depth 5 -Compress}
function Snapshot{
 [pscustomobject]@{registration=(Registration);files=@(Get-ChildItem $app -File -Recurse|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}});shortcut=(Get-FileHash $shortcut).Hash}
}
function CheckSnapshot($Before){
 Assert ((Registration) -ceq $Before.registration) 'Previous registration changed.'
 foreach($file in $Before.files){Assert ((Test-Path $file.path) -and (Get-FileHash $file.path).Hash -eq $file.hash) "Previous file changed: $($file.path)"}
 Assert (@(Get-ChildItem $app -Recurse -File).Count -eq $Before.files.Count) 'Recovery left unexpected installation files.'
 Assert ((Get-FileHash $shortcut).Hash -eq $Before.shortcut) 'Previous shortcut changed.'
 Assert (-not (Test-Path "$app/.upgrade-recovery")) 'Recovery journal remains active.'
}
function StopOwnedTree($Process){
 $all=Get-CimInstance Win32_Process
 $owned=[Collections.Generic.List[int]]::new();$owned.Add($Process.Id)
 for($index=0;$index -lt $owned.Count;$index++){foreach($child in @($all|Where-Object ParentProcessId -EQ $owned[$index])){if(-not $owned.Contains([int]$child.ProcessId)){$owned.Add([int]$child.ProcessId)}}}
 $ids=$owned.ToArray();[array]::Reverse($ids)
 foreach($processId in $ids){Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue}
 $null=$Process.WaitForExit(10000)
}
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
$settingsBaseline=@()
if(Test-Path $preferences){[xml]$settings=Get-Content $preferences -Raw;$settingsBaseline=@(Get-Item $preferences;Get-ChildItem ([string]$settings.Settings.SettingsFolder) -File -Recurse)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}}
$settingsBaseline|ConvertTo-Json|Set-Content "$result/settings-baseline.json"
Assert ($settingsBaseline.Count -ge 2) 'Expected the prepared test profile and its existing settings files.'
$checks=[Collections.Generic.List[string]]::new();$success=$false;$runtime=$null;$process=$null
try{
 Assert ((Install '1.8.4.100' 'none' 'install-old') -eq 0) 'Old candidate installation failed.'
 Assert ((Get-ItemProperty $key).DisplayVersion -eq '1.8.4.100') 'Incorrect initial version.'
 $signature=Get-AuthenticodeSignature "$app/unins000.exe"
 Assert ($signature.Status -eq 'Valid' -and $null -ne $signature.TimeStamperCertificate) 'Installed uninstaller is not validly signed/timestamped.'
 $checks.Add('Signed uninstaller installed')
 $before=Snapshot;$before|ConvertTo-Json -Depth 6|Set-Content "$result/old-installation.json"
 foreach($fault in @('payload','registry')){
  Assert ((Install '1.8.4.101' $fault "engine-$fault") -ne 0) "Real engine $fault failure unexpectedly succeeded."
  Assert (([IO.File]::ReadAllText("$result/engine-$fault.log")) -match 'SPCE_TEST_ENGINE_') 'Engine failure fixture did not reach its checkpoint.'
  Assert (([IO.File]::ReadAllText("$result/engine-$fault.log")) -match 'Rolling back changes') 'Inno engine did not perform rollback.'
  CheckSnapshot $before
  Assert (-not (Test-Path "$app/versions/1.8.4.101")) 'Failed engine candidate remains.'
  $checks.Add("Actual engine $fault write failure restored the complete previous installation")
 }
 Assert ((Install '1.8.4.100' 'none' 'repeat-version') -ne 0) 'Repeated version was not rejected.';CheckSnapshot $before
 $checks.Add('Repeated version rejected without changes')
 $lock=[IO.File]::Open("$app/versions/1.8.4.100/log4net.dll",'Open','Read','Read')
 try{Assert ((Install '1.8.4.101' 'none' 'locked-file') -ne 0) 'Locked payload was not rejected.'}finally{$lock.Dispose()}
 CheckSnapshot $before;$checks.Add('Locked file rejected without changes')
 if($LaunchTest){
  Assert ((Get-Process -Id $PID).SessionId -gt 0) 'Launch test requires an interactive session.'
  $profileRoot=Join-Path $result 'runtime-profile';$dataRoot=Join-Path $result 'runtime-data'
  New-Item -ItemType Directory $profileRoot,$dataRoot|Out-Null
  [xml]$prefs=Get-Content $preferences -Raw
  foreach($setting in @{SettingsFolder=[string]$dataRoot;DefaultLayoutName='';SingleInstanceMode='False'}.GetEnumerator()){
   $node=$prefs.SelectSingleNode('/Settings/'+$setting.Key)
   if(-not $node){$node=$prefs.CreateElement($setting.Key);$null=$prefs.DocumentElement.AppendChild($node)}
   $node.InnerText=[string]$setting.Value
  }
  $prefs.Save((Join-Path $profileRoot 'SuperPuTTY.settings'))
  $info=[Diagnostics.ProcessStartInfo]::new("$app\versions\1.8.4.100\SuperPutty.exe")
  $info.WorkingDirectory="$app\versions\1.8.4.100";$info.UseShellExecute=$false;$info.WindowStyle='Hidden';$info.EnvironmentVariables['USERPROFILE']=$profileRoot
  $runtime=[Diagnostics.Process]::Start($info)
  $deadline=(Get-Date).AddSeconds(30)
  do{Start-Sleep -Milliseconds 250;$runtime.Refresh();if($runtime.HasExited){throw 'Application exited before main-window initialization.'}}while($runtime.MainWindowTitle -notmatch 'SuperPuTTY' -and (Get-Date) -lt $deadline)
  Assert ($runtime.MainWindowTitle -match 'SuperPuTTY') 'Main window did not initialize from versioned directory.'
  Assert ((Install '1.8.4.101' 'none' 'running-application') -ne 0) 'Running application was not rejected.'
  $null=$runtime.CloseMainWindow();Assert ($runtime.WaitForExit(15000)) 'Application did not close normally.';$runtime=$null
  CheckSnapshot $before;$checks.Add('Main window launch and running-application rejection')
 }
 $shortcutLock=[IO.File]::Open($shortcut,'Open','Read','Read')
 try{Assert ((Install '1.8.4.101' 'none' 'locked-shortcut') -ne 0) 'Locked shortcut activation unexpectedly succeeded.'}finally{$shortcutLock.Dispose()}
 Assert (([IO.File]::ReadAllText("$result/locked-shortcut.log")) -match 'Cannot activate new application shortcut') 'Shortcut failure did not reach final activation.'
 Assert (Test-Path "$app/.upgrade-recovery/ready") 'Unfinished finalization did not retain recovery state.'
 Assert ((Install '1.8.4.101' 'recovery-only' 'locked-shortcut-recover') -ne 0) 'Recovery-only fixture unexpectedly installed.'
 CheckSnapshot $before;$checks.Add('Failed final shortcut activation recovered after releasing the lock')
 foreach($fault in @('pause-files','pause-activation')){
  $process=StartSetup '1.8.4.101' $fault $fault
  $deadline=(Get-Date).AddSeconds(30)
  do{
   Start-Sleep -Milliseconds 250
   $logText=ReadSetupLog "$result/$fault.log"
   $process.Refresh();if($process.HasExited){throw "Pause fixture exited before checkpoint: $fault"}
  }while($logText -notmatch 'SPCE_TEST_PAUSE_' -and (Get-Date) -lt $deadline)
  Assert ([regex]::IsMatch($logText,'SPCE_TEST_PAUSE_')) 'Interruption checkpoint not reached.'
  Assert (Test-Path "$app/.upgrade-recovery/ready") 'Durable recovery marker missing before interruption.'
  if($fault -eq 'pause-activation'){Assert ((Get-ItemProperty $key).DisplayVersion -eq '1.8.4.101') 'Activation pause did not follow registration commit.'}
  $uninstall=Start-Process "$app/unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$result\$fault-concurrent-uninstall.log`"") -PassThru -Wait -WindowStyle Hidden
  Assert ($uninstall.ExitCode -ne 0 -and ([IO.File]::ReadAllText("$result/$fault-concurrent-uninstall.log")) -match 'SPCE_OPERATION_BUSY') 'Concurrent uninstall was not excluded by the operation mutex.'
  StopOwnedTree $process
  $process=$null
  if($fault -eq 'pause-files'){
   $backupPath="$app/.upgrade-recovery/old.reg"
   $backupBytes=[IO.File]::ReadAllBytes($backupPath)
   $corrupted=[byte[]]$backupBytes.Clone();$corrupted[0]=$corrupted[0] -bxor 1
   [IO.File]::WriteAllBytes($backupPath,$corrupted)
   try{
    Assert ((Install '1.8.4.101' 'recovery-only' 'corrupt-backup') -ne 0) 'Corrupt recovery backup was accepted.'
    Assert (([IO.File]::ReadAllText("$result/corrupt-backup.log")) -match 'Recovery backup integrity check failed') 'Backup corruption was not detected.'
    Assert ((Registration) -ceq $before.registration) 'Corrupt recovery changed registration.'
    foreach($file in $before.files){Assert ((Get-FileHash $file.path).Hash -eq $file.hash) 'Corrupt recovery changed old installation bytes.'}
    Assert (Test-Path "$app/.upgrade-recovery/ready") 'Rejected recovery discarded its journal.'
   }finally{[IO.File]::WriteAllBytes($backupPath,$backupBytes)}
   $checks.Add('Corrupt recovery backup rejected without modifying old installation')
  }
  Assert ((Install '1.8.4.101' 'recovery-only' "$fault-recover") -ne 0) 'Recovery-only fixture unexpectedly installed.'
  Assert ((Get-Content "$result/$fault-recover.log" -Raw) -match 'SPCE_RECOVERY: previous installation restored') 'Recovery did not run.'
  CheckSnapshot $before
  Assert (-not (Test-Path "$app/versions/1.8.4.101")) 'Interrupted candidate payload remains.'
  $checks.Add("Interrupted $fault restored complete previous installation")
 }
 Assert ((Install '1.8.4.101' 'none' 'upgrade') -eq 0) 'Normal candidate upgrade failed.'
 Assert ((Get-ItemProperty $key).DisplayVersion -eq '1.8.4.101' -and -not (Test-Path "$app/.upgrade-recovery")) 'Upgrade did not commit cleanly.'
 Assert ((Get-AuthenticodeSignature "$app/unins000.exe").Status -eq 'Valid') 'Upgraded uninstaller signature invalid.'
 $shell=New-Object -ComObject WScript.Shell
 Assert ($shell.CreateShortcut($shortcut).TargetPath -eq "$app\versions\1.8.4.101\SuperPutty.exe") 'Shortcut did not activate new version.'
 $checks.Add('Successful upgrade activates candidate and clears journal')
 $upgraded=Snapshot
 Assert ((Install '1.8.4.100' 'none' 'downgrade') -ne 0) 'Downgrade was accepted.'
 CheckSnapshot $upgraded;$checks.Add('Downgrade rejected without changes')
 $success=$true
}finally{
 if($process -and -not $process.HasExited){StopOwnedTree $process}
 if($runtime -and -not $runtime.HasExited){Stop-Process -Id $runtime.Id -Force}
 # An active journal is retained for investigation; never fake successful cleanup.
 if((Test-Path $key) -and -not (Test-Path "$app/.upgrade-recovery")){$p=Start-Process "$app/unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$result\uninstall.log`"") -WindowStyle Hidden -PassThru -Wait}
 $cleaned=(-not (Test-Path $key)) -and (@(Get-ChildItem "$app/versions" -Filter SuperPutty.exe -Recurse -File -ErrorAction SilentlyContinue).Count -eq 0)
 $preserved=$true;foreach($entry in $settingsBaseline){if(-not (Test-Path $entry.path) -or (Get-FileHash $entry.path).Hash -ne $entry.hash){$preserved=$false}}
 [pscustomobject]@{success=($success -and $cleaned -and $preserved);scope=$Scope;identity=$id.Name;checks=@($checks.ToArray());settingsPreserved=$preserved;cleanupSucceeded=$cleaned}|ConvertTo-Json -Depth 5|Set-Content "$result/summary.json"
}
