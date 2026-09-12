[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StageDirectory,
    [Parameter(Mandatory)][ValidateSet('StandardUser','Machine')][string]$Mode
)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Installer matrix requires an explicitly authorized disposable VM.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$elevated=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Mode -eq 'Machine') -ne $elevated){throw 'Wrong elevation for this matrix mode.'}
if($Mode -eq 'StandardUser' -and $identity.Name -notmatch '\\SPCEtest[0-9a-f]+$'){throw 'Standard-user tests require a dedicated temporary SPCEtest account.'}
$stage=[IO.Path]::GetFullPath($StageDirectory)
$results=Join-Path $stage "results/$Mode"
New-Item -ItemType Directory -Path $results -Force|Out-Null
$scope=if($Mode -eq 'StandardUser'){'current-user'}else{'all-users'}
$fixtureScope=if($Mode -eq 'StandardUser'){'perUser'}else{'perMachine'}
$installFolder=if($Mode -eq 'StandardUser'){Join-Path $env:LOCALAPPDATA 'Apps/SuperPuTTY'}else{Join-Path $env:ProgramFiles 'SuperPuTTY'}
if(Test-Path $installFolder){throw "Test installation folder must initially be absent: $installFolder"}
$installer=New-Object -ComObject WindowsInstaller.Installer
function Get-ProductCode([string]$Path){
 $database=$installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@([IO.Path]::GetFullPath($Path),0))
 $view=$database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@('SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode'''))
 $null=$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
 $record=$view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)
 return $record.GetType().InvokeMember('StringData','GetProperty',$null,$record,1)
}
function Get-ProductState([string]$Code){return $installer.GetType().InvokeMember('ProductState','GetProperty',$null,$installer,@($Code))}
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
$candidate=Join-Path $stage "packages/SuperPuTTY-CE-1.8.0-$scope-win-x64-signed.msi"
$candidateCode=Get-ProductCode $candidate
$operations=[Collections.Generic.List[object]]::new()
$owned=[Collections.Generic.HashSet[string]]::new()
$settingDirectory=Join-Path $results 'legacy-settings'
New-Item -ItemType Directory -Path "$settingDirectory/layouts" -Force|Out-Null
'<ArrayOfSessionData><SessionData SessionId="Lab/Compatibility" SessionName="Compatibility" Host="example.invalid" Port="22" Proto="SSH" PuttySession="Default Settings" /></ArrayOfSessionData>'|Set-Content "$settingDirectory/Sessions.XML"
Copy-Item (Join-Path $stage 'LegacyLayout.xml') "$settingDirectory/layouts/Operations.xml"
Copy-Item (Join-Path $stage 'LegacyLayout.xml') "$settingDirectory/AutoRestoreLayout.XML"
# Never replace the workstation user's real preferences. Machine tests hash them
# in place; the temporary standard account uses its own real profile discovery path.
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
if($Mode -eq 'StandardUser'){
 Assert (-not (Test-Path $preferences)) 'Temporary profile already has preferences.'
 $escaped=[Security.SecurityElement]::Escape($settingDirectory)
 "<Settings><SettingsFolder>$escaped</SettingsFolder><CheckForUpdates>false</CheckForUpdates></Settings>"|Set-Content $preferences
}
$baseline=@(Get-ChildItem $settingDirectory -Recurse -File|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}})
if(Test-Path $preferences){$baseline+=[pscustomobject]@{path=$preferences;hash=(Get-FileHash $preferences).Hash}}
if($Mode -eq 'Machine' -and (Test-Path (Join-Path $stage 'host-settings-hashes.json'))){
    # Windows PowerShell 5.1 can return the JSON array as one pipeline object.
    # Add each record explicitly so comparisons never receive an array of paths.
    $hostBaseline=Get-Content (Join-Path $stage 'host-settings-hashes.json') -Raw|ConvertFrom-Json
    foreach($entry in $hostBaseline){$baseline+=$entry}
}
$baseline|ConvertTo-Json|Set-Content (Join-Path $results 'settings-baseline.json')
function Assert-Settings{foreach($file in $baseline){Assert ((Test-Path -LiteralPath $file.path) -and ((Get-FileHash -LiteralPath $file.path).Hash -eq $file.hash)) "A preferences/session/layout file changed or disappeared: $($file.path)"}}
function Invoke-Msi([string]$Name,[string]$Action,[string]$Target,[int]$Expected=0){
 $log=Join-Path $results "$Name.log"
 $arguments=@($Action,"`"$Target`"",'/qn','/norestart','/l*v',"`"$log`"")
 $process=Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(-not $process.WaitForExit(180000)){throw "MSI operation timed out: $Name (PID $($process.Id)); inspect before retrying."}
 $exit=$process.ExitCode
 $operations.Add([pscustomobject]@{name=$Name;exitCode=$exit;expected=$Expected;elevated=$elevated})
 $operations|ConvertTo-Json -Depth 4|Set-Content (Join-Path $results 'operations.json')
 Assert ($exit -eq $Expected -or ($Expected -eq 0 -and $exit -eq 3010)) "MSI operation $Name returned $exit instead of $Expected. See $log"
 Assert-Settings
}
function Install-Package([string]$Name,[string]$Path){
 $code=Get-ProductCode $Path
 Assert ((Get-ProductState $code) -ne 5) "Package already installed before scenario: $Name"
 $null=$owned.Add($code)
 Invoke-Msi $Name '/i' $Path
 Assert ((Get-ProductState $code) -eq 5) "Installed product is not registered: $Name"
 return $code
}
function Remove-Package([string]$Name,[string]$Code){
 Invoke-Msi $Name '/x' $Code
 Assert ((Get-ProductState $Code) -ne 5) "Product remains after uninstall: $Name"
 $null=$owned.Remove($Code)
}
function Assert-Candidate{
 $exe=Join-Path $installFolder 'SuperPutty.exe'
 Assert (Test-Path $exe) 'Candidate executable missing from expected scope directory.'
 Assert ((Get-Item $exe).VersionInfo.FileVersion -eq '1.8.0.0') 'Installed executable version mismatch.'
 Assert ((Get-AuthenticodeSignature $exe).Status -eq 'Valid') 'Installed executable signature invalid.'
 Assert ((Get-FileHash $exe).Hash -eq (Get-Content (Join-Path $stage 'candidate-exe-sha256.txt')).Trim()) 'Installed executable differs from signed candidate.'
 Assert (Test-Path (Join-Path $installFolder 'License.txt')) 'Original MIT license missing.'
 $startMenu=[Environment]::GetFolderPath($(if($Mode -eq 'StandardUser'){'Programs'}else{'CommonPrograms'}))
 Assert (Test-Path (Join-Path $startMenu 'SuperPuTTY/SuperPuTTY Community Edition.lnk')) 'Expected branded Start menu shortcut missing.'
 Assert-Settings
}
$success=$false
try{
 $null=Install-Package 'clean-install-1.8.0' $candidate
 Assert-Candidate
 Remove-Package 'clean-uninstall-1.8.0' $candidateCode
 Assert (-not (Test-Path (Join-Path $installFolder 'SuperPutty.exe'))) 'Executable remains after uninstall.'
 foreach($previous in @(
  @{name='ce-1.7.6';file="SuperPuTTY-CE-1.7.6-$scope-win-x64-signed.msi"},
  @{name='historical-1.7.5';file="SuperPutty-1.7.5-$scope-win-x64-signed.msi"},
  @{name='bridge-lower-bound-1.6.0-fixture';file="fixture-1.6.0-$fixtureScope.msi"}
 )){
  $oldCode=Install-Package ("install-"+$previous.name) (Join-Path $stage "packages/$($previous.file)")
  $null=Install-Package ("upgrade-"+$previous.name+'-to-1.8.0') $candidate
  Assert ((Get-ProductState $oldCode) -ne 5) "Previous product not removed by upgrade: $($previous.name)"
  $null=$owned.Remove($oldCode)
  Assert-Candidate
  Remove-Package ("uninstall-after-"+$previous.name) $candidateCode
 }
 $blocked=@(
  @{name='ambiguous-1.5.0.1-fixture';file="fixture-1.5.0.1-$fixtureScope.msi";property='LEGACYUPSTREAMFOUND'},
  @{name='unknown-legacy-1.7.6-fixture';file="fixture-1.7.6-$fixtureScope.msi";property='UNKNOWNLEGACYFOUND'}
 )
 if($Mode -eq 'Machine'){$blocked+=@{name='actual-upstream-1.5.0.0';file='upstream-1.5.0.0.msi';property='LEGACYUPSTREAMFOUND'}}
 foreach($previous in $blocked){
  $oldCode=Install-Package ("install-"+$previous.name) (Join-Path $stage "packages/$($previous.file)")
  Invoke-Msi ("blocked-by-"+$previous.name) '/i' $candidate 1603
  $log=Get-Content (Join-Path $results ("blocked-by-"+$previous.name+'.log')) -Raw
  Assert ($log.Contains($previous.property) -and $log.Contains($oldCode)) 'Failure did not identify the intended blocking package.'
  Assert ($log -match 'Return value 3' -and $log -match 'LaunchConditions') 'Expected launch-condition failure not found.'
  Assert ((Get-ProductState $candidateCode) -ne 5) 'Candidate registered despite blocked installation.'
  Assert ((Get-ProductState $oldCode) -eq 5) 'Blocking package was removed unexpectedly.'
  Remove-Package ("remove-"+$previous.name) $oldCode
 }
 $success=$true
}finally{
 foreach($code in @($owned)){if((Get-ProductState $code) -eq 5){Invoke-Msi ('cleanup-'+$code.Trim('{}')) '/x' $code}}
 Assert-Settings
 [pscustomobject]@{mode=$Mode;success=$success;elevated=$elevated;identity=$identity.Name;operations=$operations.Count;settingsFilesChecked=$baseline.Count;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $results 'summary.json')
}
