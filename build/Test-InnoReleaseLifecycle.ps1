[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[Parameter(Mandatory)][string]$ResultDirectory,
 [ValidateSet('user','machine')][string]$Scope='machine',[string]$PreviousInnoSetup,[switch]$LaunchTest)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Scope -eq 'machine' -and -not $admin) -or ($Scope -eq 'user' -and ($admin -or $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'))){throw 'Reviewed disposable test identity required.'}
if(Test-Path $ResultDirectory){throw 'Use a new results directory.'}
$files=@(Get-ChildItem $ReleaseDirectory -File -Filter '*-build-manifest.json')
if($files.Count -ne 1){throw 'Expected one release manifest.'}
$release=Get-Content $files[0].FullName -Raw|ConvertFrom-Json
& "$PSScriptRoot/Verify-InnoReleaseBundle.ps1" -ReleaseDirectory $ReleaseDirectory -Version $release.version -RequireInstaller
$metadata=$release.installerBuild
$app=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Programs\SuperPuTTY Community Edition'}else{Join-Path $env:ProgramFiles 'SuperPuTTY Community Edition'}
$hive=if($Scope -eq 'user'){'HKCU'}else{'HKLM'};$other=if($Scope -eq 'user'){'HKLM'}else{'HKCU'}
$arp='Software\Microsoft\Windows\CurrentVersion\Uninstall\{EBCA29DD-46C1-4A93-9340-79DF5375711D}_is1'
$key="Registry::$hive\$arp"
if((Test-Path $key) -or (Test-Path "Registry::$other\$arp")){throw 'Pre-existing Inno installation requires review.'}
New-Item -ItemType Directory $ResultDirectory|Out-Null
$result=(Resolve-Path $ResultDirectory).Path
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Setup([string]$File,[string]$Label){$p=Start-Process $File -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/$(if($Scope -eq 'user'){'CURRENTUSER'}else{'ALLUSERS'})","/LOG=`"$result\$Label.log`"") -PassThru -Wait -WindowStyle Hidden;return $p.ExitCode}
$baseline=@();$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
if(Test-Path $preferences){[xml]$prefs=Get-Content $preferences -Raw;$baseline=@(Get-Item $preferences;Get-ChildItem ([string]$prefs.Settings.SettingsFolder) -Recurse -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}}
$baseline|ConvertTo-Json|Set-Content "$result/settings-baseline.json"
$checks=[Collections.Generic.List[string]]::new();$success=$false;$runtime=$null
try{
 if($PreviousInnoSetup){
  $sig=Get-AuthenticodeSignature $PreviousInnoSetup
  Assert ($sig.Status -eq 'Valid' -and $sig.SignerCertificate.GetNameInfo('SimpleName',$false) -eq 'Christopher Thornton') 'Previous installer verification failed.'
  Assert ((Setup $PreviousInnoSetup 'previous') -eq 0) 'Previous Inno installation failed.'
  $previousVersion=(Get-ItemProperty $key).DisplayVersion
  Assert ([version]$previousVersion -lt [version]$release.version) 'Previous installer must be an older Inno version.'
  $previousExe="$app/versions/$previousVersion/SuperPutty.exe";$previousHash=(Get-FileHash $previousExe).Hash
 }
 Assert ((Setup (Join-Path $ReleaseDirectory $metadata.setup) 'candidate') -eq 0) 'Candidate install/upgrade failed.'
 Assert ((Get-ItemProperty $key).DisplayVersion -eq $release.version -and -not (Test-Path "Registry::$other\$arp") -and -not (Test-Path "$app/.upgrade-recovery")) 'Incorrect scope/version or unresolved journal.'
 $exe="$app/versions/$($release.version)/SuperPutty.exe"
 Assert ((Get-FileHash $exe).Hash -eq $metadata.applicationSha256 -and (Get-FileHash "$app/unins000.exe").Hash -eq $metadata.uninstallerSha256) 'Installed bytes differ from the candidate.'
 & "$PSScriptRoot/Verify-InnoPayload.ps1" -PayloadDirectory (Split-Path $exe) -SbomPath (Join-Path $ReleaseDirectory $release.sbom) -Installed
 foreach($path in @($exe,"$app/unins000.exe")){$sig=Get-AuthenticodeSignature $path;Assert ($sig.Status -eq 'Valid' -and $null -ne $sig.TimeStamperCertificate) 'Installed signature/timestamp invalid.'}
 $checks.Add('Correct scope, version, installed payload and signed uninstaller')
 if($PreviousInnoSetup){Assert ((Get-FileHash $previousExe).Hash -eq $previousHash) 'Upgrade modified the previous payload.';$checks.Add('Normal Inno-to-Inno upgrade retained unchanged previous payload')}
 Assert ((Setup (Join-Path $ReleaseDirectory $metadata.setup) 'repeat') -ne 0) 'Same-version reinstall was accepted.'
 Assert ((Get-ItemProperty $key).DisplayVersion -eq $release.version -and (Get-FileHash $exe).Hash -eq $metadata.applicationSha256 -and -not (Test-Path "$app/.upgrade-recovery")) 'Rejected reinstall changed the candidate.'
 & "$PSScriptRoot/Verify-InnoPayload.ps1" -PayloadDirectory (Split-Path $exe) -SbomPath (Join-Path $ReleaseDirectory $release.sbom) -Installed
 $checks.Add('Same-version reinstall rejected without changes')
 if($LaunchTest){
  Assert ([Diagnostics.Process]::GetCurrentProcess().SessionId -gt 0 -and (Test-Path $preferences)) 'Configured interactive profile required for launch test.'
  $profileRoot=Join-Path $result 'runtime-profile';$dataRoot=Join-Path $result 'runtime-data';New-Item -ItemType Directory $profileRoot,$dataRoot|Out-Null
  foreach($setting in @{SettingsFolder=[string]$dataRoot;DefaultLayoutName='';SingleInstanceMode='False'}.GetEnumerator()){$node=$prefs.SelectSingleNode('/Settings/'+$setting.Key);if(-not $node){$node=$prefs.CreateElement($setting.Key);$null=$prefs.DocumentElement.AppendChild($node)};$node.InnerText=[string]$setting.Value}
  $prefs.Save((Join-Path $profileRoot 'SuperPuTTY.settings'))
  $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.WorkingDirectory=Split-Path $exe;$info.UseShellExecute=$false;$info.EnvironmentVariables['USERPROFILE']=$profileRoot
  $runtime=[Diagnostics.Process]::Start($info);$deadline=(Get-Date).AddSeconds(30)
  do{Start-Sleep -Milliseconds 250;$runtime.Refresh();if($runtime.HasExited){throw 'Candidate exited before main-window initialization.'}}while($runtime.MainWindowTitle -notmatch 'SuperPuTTY' -and (Get-Date) -lt $deadline)
  Assert ($runtime.MainWindowTitle -match 'SuperPuTTY') 'Candidate main window did not initialize.'
  $null=$runtime.CloseMainWindow();Assert ($runtime.WaitForExit(15000)) 'Candidate did not close normally.';$runtime=$null;$checks.Add('Candidate launched and closed from its version directory')
 }
 $success=$true
}finally{
 if($runtime -and -not $runtime.HasExited){Stop-Process -Id $runtime.Id -Force}
 if((Test-Path $key) -and -not (Test-Path "$app/.upgrade-recovery")){$null=Start-Process "$app/unins000.exe" -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru -Wait -WindowStyle Hidden}
 $cleaned=(-not (Test-Path $key)) -and -not (Test-Path "$app/.upgrade-recovery") -and (@(Get-ChildItem "$app/versions" -Filter SuperPutty.exe -File -Recurse -ErrorAction SilentlyContinue).Count -eq 0)
 $preserved=$true;foreach($entry in $baseline){if(-not (Test-Path $entry.path) -or (Get-FileHash $entry.path).Hash -ne $entry.hash){$preserved=$false}}
 [pscustomobject]@{success=($success -and $cleaned -and $preserved);scope=$Scope;version=$release.version;checks=@($checks.ToArray());settingsFilesChecked=$baseline.Count;settingsPreserved=$preserved;cleanupSucceeded=$cleaned}|ConvertTo-Json -Depth 5|Set-Content "$result/summary.json"
}
if(-not $cleaned -or -not $preserved){throw 'Lifecycle cleanup or settings preservation failed; review summary.json.'}
