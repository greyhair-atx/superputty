[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StageDirectory,
    [Parameter(Mandatory)][ValidateSet('user','machine')][string]$Scope,
    [Parameter(Mandatory)][string]$WingetPath
)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Requires an authorized disposable Windows VM.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$elevated=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Scope -eq 'machine') -ne $elevated){throw 'Unexpected elevation for selected scope.'}
if($Scope -eq 'user' -and $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'){throw 'User tests require a dedicated temporary standard account.'}
$stage=[IO.Path]::GetFullPath($StageDirectory)
$results=Join-Path $stage "results/$Scope"
New-Item -ItemType Directory -Force -Path $results|Out-Null
$folder=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Apps/SuperPuTTY'}else{Join-Path $env:ProgramFiles 'SuperPuTTY'}
if(Test-Path $folder){throw "Installation directory already exists: $folder"}
$code=if($Scope -eq 'user'){'{7093D12F-0585-4E0C-A625-D2BB3867C54F}'}else{'{C1A2912D-A780-4AE3-92C3-89A2366C3608}'}
$msi=New-Object -ComObject WindowsInstaller.Installer
function Product-State { $msi.GetType().InvokeMember('ProductState','GetProperty',$null,$msi,@($code)) }
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
Assert ((Product-State) -ne 5) 'Test product already registered.'
$baseline=@()
if($Scope -eq 'user'){
    $settings=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
    Assert (-not (Test-Path $settings)) 'Temporary profile already contains preferences.'
    $data=Join-Path $results 'legacy-settings'
    New-Item -ItemType Directory -Path "$data/layouts" -Force|Out-Null
    Copy-Item "$stage/LegacyLayout.xml" "$data/layouts/Operations.xml"
    Copy-Item "$stage/LegacyLayout.xml" "$data/AutoRestoreLayout.XML"
    '<ArrayOfSessionData><SessionData SessionId="Lab/Compatibility" SessionName="Compatibility" Host="example.invalid" Port="22" Proto="SSH" /></ArrayOfSessionData>'|Set-Content "$data/Sessions.XML"
    $escaped=[Security.SecurityElement]::Escape($data)
    "<Settings><SettingsFolder>$escaped</SettingsFolder><CheckForUpdates>false</CheckForUpdates></Settings>"|Set-Content $settings
    $baseline=@(Get-ChildItem $data -Recurse -File|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}})
    $baseline+=[pscustomobject]@{path=$settings;hash=(Get-FileHash $settings).Hash}
}else{foreach($entry in (Get-Content "$stage/host-baseline.json" -Raw|ConvertFrom-Json)){$baseline+=$entry}}
function Assert-Settings {foreach($entry in $baseline){Assert ((Test-Path -LiteralPath $entry.path) -and (Get-FileHash -LiteralPath $entry.path).Hash -eq $entry.hash) "Preserved file changed: $($entry.path)"}}
$baseline|ConvertTo-Json|Set-Content "$results/settings-baseline.json"
$operations=[Collections.Generic.List[object]]::new()
function Invoke-WinGet([string]$Name,[string[]]$Arguments){
    & $WingetPath @Arguments *> "$results/$Name.log"
    $exit=$LASTEXITCODE
    $operations.Add([pscustomobject]@{name=$Name;exitCode=$exit;arguments=$Arguments})
    $operations|ConvertTo-Json -Depth 5|Set-Content "$results/operations.json"
    Assert ($exit -eq 0) "WinGet $Name failed ($exit); see $results/$Name.log"
}
$success=$false
$cleanupSucceeded=$false
try{
    Invoke-WinGet 'version' @('--version')
    Invoke-WinGet 'effective-settings' @('settings','export')
    $effective=Get-Content "$results/effective-settings.log" -Raw|ConvertFrom-Json
    Assert ([bool]$effective.adminSettings.LocalManifestFiles) 'Local manifests are not enabled for the test account.'
    Invoke-WinGet 'validate' @('validate',"$stage/manifests")
    Invoke-WinGet 'install' @('install','--manifest',"$stage/manifests",'--scope',$Scope,'--silent','--disable-interactivity','--accept-package-agreements','--accept-source-agreements','--verbose-logs','--log',"$results/install-msi.log")
    Assert ((Product-State) -eq 5) 'Product registration missing.'
    $exe=Join-Path $folder 'SuperPutty.exe'
    Assert (Test-Path $exe) 'Expected executable is missing.'
    Assert ((Get-FileHash $exe).Hash -eq '29A7B0D634B0ABABC5D95A925CE55FA56B5865BAFEDDDCE67AFB1D2AE68D3969') 'Installed executable differs from published release.'
    Assert ((Get-AuthenticodeSignature $exe).Status -eq 'Valid') 'Executable signature invalid.'
    Assert ((Get-Item $exe).VersionInfo.FileDescription -eq 'SuperPuTTY Community Edition') 'File description branding differs.'
    Assert (Test-Path "$folder/License.txt") 'MIT license missing.'
    $menu=[Environment]::GetFolderPath($(if($Scope -eq 'user'){'Programs'}else{'CommonPrograms'}))
    Assert (Test-Path "$menu/SuperPuTTY/SuperPuTTY Community Edition.lnk") 'Start menu shortcut missing.'
    Assert-Settings
    Invoke-WinGet 'uninstall' @('uninstall','--product-code',$code,'--exact','--scope',$Scope,'--silent','--disable-interactivity','--accept-source-agreements','--verbose-logs','--log',"$results/uninstall-msi.log")
    Assert ((Product-State) -ne 5) 'Product remains registered.'
    Assert (-not (Test-Path $exe)) 'Executable remains after uninstall.'
    Assert-Settings
    $success=$true
}finally{
    if((Product-State) -eq 5){
        $p=Start-Process msiexec.exe -ArgumentList @('/x',$code,'/qn','/norestart','/l*v',"`"$results/cleanup-msi.log`"") -WindowStyle Hidden -PassThru
        if(-not $p.WaitForExit(180000)){throw 'Cleanup timed out; retain test account for investigation.'}
    }
    $cleanupSucceeded=((Product-State) -ne 5)
    $settingsPreserved=$true
    try{Assert-Settings}catch{$settingsPreserved=$false}
    [pscustomobject]@{scope=$Scope;success=($success -and $settingsPreserved);cleanupSucceeded=$cleanupSucceeded;elevated=$elevated;identity=$identity.Name;settingsFilesChecked=$baseline.Count;settingsPreserved=$settingsPreserved;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$results/summary.json"
}
