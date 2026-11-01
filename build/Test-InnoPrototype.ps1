[CmdletBinding()]
param([Parameter(Mandatory)][string]$StageDirectory,[ValidateSet('user','machine')][string]$Scope='user',
 [switch]$TestMsiMigration,[string]$WinGetPath,
 [ValidateSet('none','explicit','unscoped','all')][string]$CatalogCommand='none')
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Scope -eq 'user' -and ($admin -or $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$')) -or ($Scope -eq 'machine' -and -not $admin)){throw 'Wrong test identity/elevation.'}
$stage=(Resolve-Path $StageDirectory).Path
$result=Join-Path $stage "results-$Scope"
if($CatalogCommand -ne 'none' -and -not $WinGetPath){throw 'Catalog tests require an explicit WinGet binary.'}
if(Test-Path $result){throw 'Prior evidence exists; use a new stage.'}
New-Item -ItemType Directory $result|Out-Null
$key='Software\Microsoft\Windows\CurrentVersion\Uninstall\{3C8A31DE-74CA-48B7-889B-F44B19DE8714}_is1'
$hive=if($Scope -eq 'user'){'HKCU'}else{'HKLM'}
$arp="Registry::$hive\$key"
$other=if($Scope -eq 'user'){'HKLM'}else{'HKCU'}
if((Test-Path $arp) -or (Test-Path "Registry::$other\$key")){throw 'Prototype already registered.'}
foreach($entry in (Get-Content "$stage/prototype-hashes.json" -Raw|ConvertFrom-Json)){
 if((Get-FileHash (Join-Path $stage $entry.name)).Hash -ne $entry.sha256){throw 'Prototype fixture hash mismatch.'}
}
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
$baseline=@()
if(Test-Path $preferences){
 [xml]$settings=Get-Content $preferences -Raw
 $baseline=@(Get-Item $preferences;Get-ChildItem ([string]$settings.Settings.SettingsFolder) -Recurse -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}
}
$baseline|ConvertTo-Json|Set-Content "$result/settings-baseline.json"
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Preserved{foreach($entry in $baseline){Assert ((Test-Path $entry.path) -and (Get-FileHash $entry.path).Hash -eq $entry.hash) 'Settings changed.'}}
function Run([string]$File,[string[]]$Arguments){$p=Start-Process $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru;return $p.ExitCode}
function Setup([string]$Version,[string]$Failure,[string]$Name){
 Run (Join-Path $stage "SuperPuTTY-CE-Prototype-$Version-$Failure.exe") @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/$(if($Scope -eq 'user'){'CURRENTUSER'}else{'ALLUSERS'})","/LOG=`"$result\$Name.log`"")
}
function Version{(Get-ItemProperty $arp).DisplayVersion}
function RegistrationSnapshot{
 @((Get-ItemProperty $arp).PSObject.Properties|Where-Object {$_.Name -notmatch '^PS'}|Sort-Object Name|ForEach-Object {[pscustomobject]@{name=$_.Name;value=$_.Value}})|ConvertTo-Json -Depth 6 -Compress
}
function WinGet([string[]]$Arguments,[string]$Name){
 & $WinGetPath @Arguments *> "$result/$Name.txt"
 return $LASTEXITCODE
}
$checks=[Collections.Generic.List[string]]::new()
$success=$false
$oldMsi='{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}'
$msi=New-Object -ComObject WindowsInstaller.Installer
function MsiState{$msi.GetType().InvokeMember('ProductState','GetProperty',$null,$msi,@($oldMsi))}
$ownsMsi=$false
try{
 if($TestMsiMigration){
  Assert ($Scope -eq 'user' -and (MsiState) -eq -1) 'Migration requires a clean user MSI state.'
  $previous=Join-Path $stage 'previous.msi'
  Assert ((Get-FileHash $previous).Hash -eq '5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36' -and (Get-AuthenticodeSignature $previous).Status -eq 'Valid') 'Published MSI verification failed.'
  $ownsMsi=$true
  Assert ((Run msiexec.exe @('/i',"`"$previous`"",'/qn','/norestart', '/l*v',"`"$result\migration-install-msi.log`"")) -eq 0) 'Previous MSI install failed.'
  Assert ((MsiState) -eq 5) 'Previous MSI is not installed.'
  $oldExe=Join-Path $env:LOCALAPPDATA 'Apps\SuperPuTTY\SuperPutty.exe'
  $oldHash=(Get-FileHash $oldExe).Hash
  Assert ((Setup '1.8.4.100' 'none' 'blocked-msi') -ne 0) 'Prototype accepted an existing MSI.'
  Assert ((Get-Content "$result/blocked-msi.log" -Raw) -match 'Prototype blocked:') 'MSI rejection not recorded.'
  Assert ((MsiState) -eq 5 -and (Get-FileHash $oldExe).Hash -eq $oldHash -and -not (Test-Path $arp)) 'MSI rejection changed the old installation.'
  Preserved
  $checks.Add('Existing MSI rejected without removal')
  Assert ((Run msiexec.exe @('/x',$oldMsi,'/qn','/norestart','/l*v',"`"$result\migration-remove-msi.log`"")) -eq 0) 'Explicit MSI migration removal failed.'
  Assert ((MsiState) -eq -1 -and -not (Test-Path $oldExe)) 'Old MSI migration incomplete.'
  $ownsMsi=$false
  $checks.Add('Explicit signed-MSI removal before prototype installation')
 }
 Assert ((Setup '1.8.4.100' 'none' 'install-old') -eq 0) 'Old prototype install failed.'
 Assert ((Version) -eq '1.8.4.100') 'Old registration incorrect.'
 $app=(Get-ItemProperty $arp).InstallLocation.TrimEnd('\')
 Assert ($app.EndsWith('\SuperPuTTY-CE-Prototype',[StringComparison]::OrdinalIgnoreCase)) 'Unexpected prototype location.'
 $oldDir=Join-Path $app 'versions\1.8.4.100'
 $files=@(Get-ChildItem $oldDir -Recurse -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}
 $programs=if($Scope -eq 'user'){[Environment]::GetFolderPath('Programs')}else{Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'}
 $shortcut=Join-Path $programs 'SuperPuTTY CE Packaging Prototype.lnk'
 $shortcutHash=(Get-FileHash $shortcut).Hash
 $uninstaller=Join-Path $app 'unins000.exe'
 $uninstallHash=(Get-FileHash $uninstaller).Hash
 $uninstallFiles=@(Get-ChildItem $app -Filter 'unins*' -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}
 $registration=RegistrationSnapshot
 $checks.Add('Fresh installation and correct ARP hive')
 if($WinGetPath){
  Assert ((WinGet @('list','--name','SuperPuTTY CE Packaging Prototype','--exact','--scope',$Scope,'--accept-source-agreements','--disable-interactivity') 'inventory-old') -eq 0) 'WinGet cannot find prototype in the correct scope.'
  $checks.Add('WinGet inventory finds correct scope')
 }
 if($CatalogCommand -eq 'none'){foreach($failure in @('payload','registration')){
  Assert ((Setup '1.8.4.101' $failure "fail-$failure") -ne 0) "Deliberate $failure failure returned success."
  Assert ((Get-Content "$result/fail-$failure.log" -Raw) -match 'SPCE_TEST_FAILURE:') 'Failure did not reach its checkpoint.'
  Assert ((Version) -eq '1.8.4.100') 'Failure changed old package registration.'
  Assert ((RegistrationSnapshot) -ceq $registration) 'Failure changed old uninstall registry values.'
  foreach($file in $files){Assert ((Test-Path $file.path) -and (Get-FileHash $file.path).Hash -eq $file.hash) 'Failure changed old payload.'}
  Assert ((Get-FileHash $shortcut).Hash -eq $shortcutHash -and (Get-FileHash $uninstaller).Hash -eq $uninstallHash) 'Failure changed old shortcut/uninstaller.'
  foreach($file in $uninstallFiles){Assert ((Test-Path $file.path) -and (Get-FileHash $file.path).Hash -eq $file.hash) 'Failure changed old uninstall ownership log.'}
  Assert (-not (Test-Path (Join-Path $app 'versions\1.8.4.101\SuperPutty.exe'))) 'Failure left replacement executable.'
  Preserved
  $checks.Add("Handled $failure failure preserved old installation")
 }}
 if($CatalogCommand -eq 'none'){
  Assert ((Setup '1.8.4.101' 'none' 'upgrade') -eq 0) 'Prototype upgrade failed.'
 }else{
  $arguments=@('upgrade','--source','SPCEInnoReview','--silent','--disable-interactivity','--accept-package-agreements','--accept-source-agreements','--verbose-logs')
  if($CatalogCommand -eq 'all'){$arguments+='--all'}else{$arguments+=@('--id','ChrisThornton.SuperPuTTYCEPackagingPrototype','--exact')}
  if($CatalogCommand -eq 'explicit'){$arguments+=@('--scope',$Scope)}
  Assert ((WinGet $arguments 'catalog-upgrade') -eq 0) 'Catalog upgrade failed.'
  $checks.Add("WinGet catalog upgrade: $CatalogCommand")
 }
 Assert ((Version) -eq '1.8.4.101') 'Candidate registration incorrect.'
 $candidate=Join-Path $app 'versions\1.8.4.101\SuperPutty.exe'
 Assert ((Get-FileHash $candidate).Hash -eq (Get-FileHash "$oldDir/SuperPutty.exe").Hash -and (Get-AuthenticodeSignature $candidate).Status -eq 'Valid') 'Candidate payload changed.'
 $shell=New-Object -ComObject WScript.Shell
 Assert ($shell.CreateShortcut($shortcut).TargetPath -eq $candidate) 'Shortcut did not select candidate.'
 Assert (-not (Test-Path "Registry::$other\$key")) 'Upgrade changed installation scope.'
 if($WinGetPath){Assert ((WinGet @('list','--name','SuperPuTTY CE Packaging Prototype','--exact','--scope',$Scope,'--accept-source-agreements','--disable-interactivity') 'inventory-candidate') -eq 0) 'WinGet lost upgraded package scope.'}
 Preserved
 $checks.Add('Successful same-scope upgrade and signed payload')
 Assert ((Setup '1.8.4.100' 'none' 'downgrade') -ne 0 -and (Version) -eq '1.8.4.101') 'Downgrade was not rejected.'
 $checks.Add('Downgrade rejected')
 $success=$true
}finally{
 if(Test-Path $arp){
  $location=(Get-ItemProperty $arp).InstallLocation.TrimEnd('\')
  if(-not $location.EndsWith('\SuperPuTTY-CE-Prototype',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected cleanup target.'}
  $null=Run (Join-Path $location 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$result\uninstall.log`"")
 }
 if($ownsMsi -and (MsiState) -eq 5){$null=Run msiexec.exe @('/x',$oldMsi,'/qn','/norestart')}
 $cleaned=(-not (Test-Path $arp)) -and (-not $app -or -not (Test-Path "$app/versions/1.8.4.100/SuperPutty.exe")) -and (-not $app -or -not (Test-Path "$app/versions/1.8.4.101/SuperPutty.exe"))
 $preserved=$true;try{Preserved}catch{$preserved=$false}
 [pscustomobject]@{success=($success -and $cleaned -and $preserved);scope=$Scope;catalogCommand=$CatalogCommand;identity=$identity.Name;checks=@($checks.ToArray());settingsFiles=$baseline.Count;settingsPreserved=$preserved;cleanupSucceeded=$cleaned}|ConvertTo-Json -Depth 4|Set-Content "$result/summary.json"
}

