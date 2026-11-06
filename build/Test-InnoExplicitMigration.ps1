[CmdletBinding()]
param([Parameter(Mandatory)][string]$PreviousMsi,[Parameter(Mandatory)][string]$CandidateDirectory,
 [Parameter(Mandatory)][string]$WinGetPath,[Parameter(Mandatory)][string]$ResultDirectory,
 [ValidateSet('user','machine')][string]$Scope='user')
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if([Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0 -or ($Scope -eq 'machine' -and -not $admin) -or ($Scope -eq 'user' -and ($admin -or $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'))){throw 'Reviewed interactive test identity required.'}
if(Test-Path $ResultDirectory){throw 'Retain previous evidence; use a new result directory.'}
New-Item -ItemType Directory $ResultDirectory|Out-Null
$result=(Resolve-Path $ResultDirectory).Path
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Signed([string]$Path){$sig=Get-AuthenticodeSignature $Path;Assert ($sig.Status -eq 'Valid' -and $sig.SignerCertificate.GetNameInfo('SimpleName',$false) -eq 'Christopher Thornton' -and $null -ne $sig.TimeStamperCertificate) "Invalid project signature/timestamp: $Path"}
function Run([string]$File,[string[]]$Arguments){$p=Start-Process $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru;return $p.ExitCode}
if(Test-Path "$CandidateDirectory/candidate-build.json"){
 $metadata=Get-Content "$CandidateDirectory/candidate-build.json" -Raw|ConvertFrom-Json
}else{
 $release=@(Get-ChildItem $CandidateDirectory -Filter '*-build-manifest.json' -File)
 Assert ($release.Count -eq 1) 'Expected one release manifest.'
 $bundle=Get-Content $release[0].FullName -Raw|ConvertFrom-Json
 & "$PSScriptRoot/Verify-InnoReleaseBundle.ps1" -ReleaseDirectory $CandidateDirectory -Version $bundle.version -RequireInstaller
 $metadata=$bundle.installerBuild
}
Assert (-not $metadata.testFixture -and $metadata.testFault -eq 'none') 'Normal-mode candidate required.'
$setup=Join-Path $CandidateDirectory $metadata.setup
Assert ((Get-FileHash $setup).Hash -eq $metadata.sha256) 'Candidate setup hash changed.'
Signed $setup;Signed $PreviousMsi
$code=if($Scope -eq 'user'){'{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}'}else{'{A339D1A0-E398-46D4-A1A3-1CE0477E69BE}'}
$expected=if($Scope -eq 'user'){'5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36'}else{'CAC64BD522C4DFF97501290E62C69E55D638895C4FF5213C8412879A5512D60B'}
Assert ((Get-FileHash $PreviousMsi).Hash -eq $expected) 'Published recovery MSI hash changed.'
$hive=if($Scope -eq 'user'){'HKCU'}else{'HKLM'};$other=if($Scope -eq 'user'){'HKLM'}else{'HKCU'}
$arp='Software\Microsoft\Windows\CurrentVersion\Uninstall\{EBCA29DD-46C1-4A93-9340-79DF5375711D}_is1'
$key="Registry::$hive\$arp";$otherKey="Registry::$other\$arp"
$app=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Programs\SuperPuTTY Community Edition'}else{Join-Path $env:ProgramFiles 'SuperPuTTY Community Edition'}
$oldExe=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Apps\SuperPuTTY\SuperPutty.exe'}else{Join-Path $env:ProgramFiles 'SuperPuTTY\SuperPutty.exe'}
$msi=New-Object -ComObject WindowsInstaller.Installer
function MsiState{$msi.GetType().InvokeMember('ProductState','GetProperty',$null,$msi,@($code))}
function OldArp{foreach($root in @('HKCU','HKLM')){foreach($branch in @('Software','Software\WOW6432Node')){if(Test-Path "Registry::$root\$branch\Microsoft\Windows\CurrentVersion\Uninstall\$code"){"$root/$branch/$code"}}}}
Assert ((MsiState) -eq -1 -and -not (Test-Path $key) -and -not (Test-Path $otherKey) -and -not (Test-Path $oldExe) -and @(OldArp).Count -eq 0) 'Pre-existing installation requires review.'
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
[xml]$settings=Get-Content $preferences -Raw
$baseline=@(Get-Item $preferences;Get-ChildItem ([string]$settings.Settings.SettingsFolder) -File -Recurse)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}
Assert ($baseline.Count -ge 2) 'Prepared settings baseline required.'
$baseline|ConvertTo-Json|Set-Content "$result/settings-baseline.json"
$index=0;foreach($entry in $baseline){Copy-Item -LiteralPath $entry.path -Destination "$result/settings-backup-$index";$index++}
Copy-Item -LiteralPath $PreviousMsi -Destination "$result/recovery.msi"
function Preserved{foreach($entry in $baseline){Assert ((Test-Path $entry.path) -and (Get-FileHash $entry.path).Hash -eq $entry.hash) "Settings changed: $($entry.path)"}}
$mode=if($Scope -eq 'user'){'/CURRENTUSER'}else{'/ALLUSERS'}
function InstallCandidate([string]$Label){Run $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',$mode,"/LOG=`"$result\$Label.log`"")}
$checks=[Collections.Generic.List[string]]::new();$success=$false;$ownsMsi=$false;$runtime=$null
try{
 $ownsMsi=$true
 Assert ((Run msiexec.exe @('/i',"`"$PreviousMsi`"",'/qn','/norestart','/l*v',"`"$result\install-msi.log`"")) -eq 0) 'Published MSI installation failed.'
 Assert ((MsiState) -eq 5 -and (Test-Path $oldExe)) 'Old MSI not fully installed.'
 $oldHash=(Get-FileHash $oldExe).Hash;Signed $oldExe;Preserved
 $checks.Add('Verified published MSI installed; recovery installer and settings backed up')
 Assert ((InstallCandidate 'blocked-before-removal') -ne 0) 'Candidate accepted an existing MSI.'
 Assert ((MsiState) -eq 5 -and (Get-FileHash $oldExe).Hash -eq $oldHash -and -not (Test-Path $key)) 'MSI guard modified the existing installation.'
 $checks.Add('Inno rejected the existing MSI without changes')
 & $WinGetPath uninstall --product-code $code --exact --silent --accept-source-agreements --disable-interactivity *> "$result/explicit-uninstall.txt"
 Assert ($LASTEXITCODE -eq 0) 'Exact ProductCode uninstall failed.'
 Assert ((MsiState) -eq -1 -and -not (Test-Path $oldExe) -and @(OldArp).Count -eq 0) 'MSI removal incomplete.'
 $ownsMsi=$false;Preserved;$checks.Add('WinGet exact ProductCode uninstall removed MSI registration and executable')
 Assert ((InstallCandidate 'install-inno') -eq 0) 'Separate Inno installation failed; retain verified recovery MSI and settings backups.'
 Assert ((Get-ItemProperty $key).DisplayVersion -eq $metadata.version -and -not (Test-Path $otherKey) -and -not (Test-Path "$app/.upgrade-recovery")) 'Wrong Inno version/scope or unresolved journal.'
 $exe="$app/versions/$($metadata.version)/SuperPutty.exe"
 $expectedAppHash=if($metadata.applicationSha256){$metadata.applicationSha256}else{$oldHash}
 Assert ((Get-FileHash $exe).Hash -eq $expectedAppHash) 'Application payload differs from the verified candidate.'
 Assert ([version][Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -eq [version]$metadata.applicationVersion) 'Installed application version differs from the candidate.'
 Signed $exe;Signed "$app/unins000.exe"
 Assert ((Get-FileHash "$app/unins000.exe").Hash -eq $metadata.uninstallerSha256) 'Installed uninstaller digest mismatch.'
 $programs=if($Scope -eq 'user'){[Environment]::GetFolderPath('Programs')}else{Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'}
 $shell=New-Object -ComObject WScript.Shell
 Assert ($shell.CreateShortcut((Join-Path $programs 'SuperPuTTY Community Edition.lnk')).TargetPath -eq [IO.Path]::GetFullPath($exe)) 'Shortcut target incorrect.'
 Preserved;$checks.Add('Separate Inno install verified scope, version, payload, signature, uninstaller and shortcut')
 $profileRoot=Join-Path $result 'runtime-profile';$dataRoot=Join-Path $result 'runtime-data'
 New-Item -ItemType Directory $profileRoot,$dataRoot|Out-Null
 foreach($setting in @{SettingsFolder=[string]$dataRoot;DefaultLayoutName='';SingleInstanceMode='False'}.GetEnumerator()){$node=$settings.SelectSingleNode('/Settings/'+$setting.Key);if(-not $node){$node=$settings.CreateElement($setting.Key);$null=$settings.DocumentElement.AppendChild($node)};$node.InnerText=[string]$setting.Value}
 $settings.Save((Join-Path $profileRoot 'SuperPuTTY.settings'))
 $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.WorkingDirectory=Split-Path $exe;$info.WindowStyle='Hidden';$info.EnvironmentVariables['USERPROFILE']=$profileRoot
 $runtime=[Diagnostics.Process]::Start($info);$deadline=(Get-Date).AddSeconds(30)
 do{Start-Sleep -Milliseconds 250;$runtime.Refresh();if($runtime.HasExited){throw 'Migrated app exited before initialization.'}}while($runtime.MainWindowTitle -notmatch 'SuperPuTTY' -and (Get-Date) -lt $deadline)
 Assert ($runtime.MainWindowTitle -match 'SuperPuTTY') 'Migrated application did not open its main window.'
 $null=$runtime.CloseMainWindow();Assert ($runtime.WaitForExit(15000)) 'Application did not close normally.';$runtime=$null
 Preserved;$checks.Add('Migrated application launched and closed; original settings unchanged');$success=$true
}finally{
 if($runtime -and -not $runtime.HasExited){Stop-Process -Id $runtime.Id -Force}
 if((Test-Path $key) -and -not (Test-Path "$app/.upgrade-recovery")){$null=Run "$app/unins000.exe" @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$result\final-uninstall.log`"")}
 if($ownsMsi -and (MsiState) -eq 5){$null=Run msiexec.exe @('/x',$code,'/qn','/norestart')}
 $cleaned=(MsiState) -eq -1 -and @(OldArp).Count -eq 0 -and -not (Test-Path $oldExe) -and -not (Test-Path $key) -and -not (Test-Path "$app/unins000.exe") -and -not (Test-Path "$app/.upgrade-recovery")
 $preserved=$true;foreach($entry in $baseline){if(-not (Test-Path $entry.path) -or (Get-FileHash $entry.path).Hash -ne $entry.hash){$preserved=$false}}
 [pscustomobject]@{success=($success -and $cleaned -and $preserved);scope=$Scope;identity=$identity.Name;msiProductCode=$code;candidateVersion=$metadata.version;checks=@($checks.ToArray());settingsFilesChecked=$baseline.Count;settingsPreserved=$preserved;cleanupSucceeded=$cleaned;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 6|Set-Content "$result/summary.json"
}
if(-not $cleaned -or -not $preserved){throw 'Migration cleanup or settings preservation failed; review summary.json.'}
