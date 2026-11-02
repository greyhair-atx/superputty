[CmdletBinding()]
param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$ExpectedUninstallerSha256,
 [Parameter(Mandatory)][string]$ResultDirectory,[ValidateSet('user','machine')][string]$Scope='user')
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Scope -eq 'machine' -and -not $admin) -or ($Scope -eq 'user' -and ($admin -or $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'))){throw 'Wrong test identity.'}
if($ExpectedUninstallerSha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Test-Path $ResultDirectory)){throw 'Expected a pinned digest and new result directory.'}
New-Item -ItemType Directory $ResultDirectory|Out-Null
$result=(Resolve-Path $ResultDirectory).Path
$app=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Programs\SuperPuTTY Community Edition'}else{Join-Path $env:ProgramFiles 'SuperPuTTY Community Edition'}
$hive=if($Scope -eq 'user'){'HKCU'}else{'HKLM'}
$key="Registry::$hive\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EBCA29DD-46C1-4A93-9340-79DF5375711D}_is1"
if(Test-Path $key){throw 'Candidate already installed.'}
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function ProjectSignature([string]$Path){$signature=Get-AuthenticodeSignature $Path;Assert ($signature.Status -eq 'Valid' -and $signature.SignerCertificate.GetNameInfo('SimpleName',$false) -eq 'Christopher Thornton' -and $null -ne $signature.TimeStamperCertificate) "Invalid project signature/timestamp: $Path"}
ProjectSignature $Setup
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings';$baseline=@()
if(Test-Path $preferences){[xml]$settings=Get-Content $preferences -Raw;$baseline=@(Get-Item $preferences;Get-ChildItem ([string]$settings.Settings.SettingsFolder) -Recurse -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}}
$success=$false
try{
 $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/$(if($Scope -eq 'user'){'CURRENTUSER'}else{'ALLUSERS'})","/LOG=`"$result\install.log`"")
 $process=Start-Process $Setup -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
 Assert ($process.ExitCode -eq 0 -and (Test-Path $key) -and -not (Test-Path "$app/.upgrade-recovery")) 'Signed normal-mode build did not install cleanly.'
 ProjectSignature "$app/unins000.exe"
 Assert ((Get-FileHash "$app/unins000.exe").Hash -eq $ExpectedUninstallerSha256) 'Installed uninstaller differs from the recovery digest.'
 $success=$true
}finally{
 if((Test-Path $key) -and -not (Test-Path "$app/.upgrade-recovery")){$null=Start-Process "$app/unins000.exe" -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru -Wait -WindowStyle Hidden}
 $cleaned=(-not (Test-Path $key)) -and -not (Test-Path "$app/unins000.exe")
 $preserved=$true;foreach($entry in $baseline){if(-not (Test-Path $entry.path) -or (Get-FileHash $entry.path).Hash -ne $entry.hash){$preserved=$false}}
 [pscustomobject]@{success=($success -and $cleaned -and $preserved);scope=$Scope;identity=$identity.Name;uninstallerSha256=$ExpectedUninstallerSha256;settingsPreserved=$preserved;cleanupSucceeded=$cleaned}|ConvertTo-Json|Set-Content "$result/summary.json"
}
