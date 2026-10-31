[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StageDirectory,
    [Parameter(Mandatory)][ValidateSet('user','machine')][string]$Scope,
    [Parameter(Mandatory)][string]$WingetPath,
    [ValidateSet('WinGet','Installer')][string]$UpgradeMethod='WinGet'
)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Requires an authorized disposable Windows VM.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$elevated=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Scope -eq 'machine') -ne $elevated){throw 'Unexpected elevation for selected scope.'}
if($Scope -eq 'user' -and $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'){throw 'User tests require a dedicated temporary standard account.'}
$stage=[IO.Path]::GetFullPath($StageDirectory)
$release=Get-Content "$stage/test-release.json" -Raw|ConvertFrom-Json
$candidate=@($release.packages|Where-Object scope -EQ $Scope)
$previous=@($release.previous.packages|Where-Object scope -EQ $Scope)
if($candidate.Count -ne 1 -or $previous.Count -ne 1){throw 'Expected one previous and candidate package per scope.'}
$codes=@($previous[0].productCode,$candidate[0].productCode)
if(@($codes|Where-Object {$_ -notmatch '^\{[0-9A-Fa-f-]{36}\}$'}).Count -or $codes[0] -eq $codes[1]){throw 'Expected distinct valid MSI ProductCodes.'}
$results=Join-Path $stage "results/upgrade-$Scope"
if(Test-Path $results){throw 'Upgrade results already exist; review before preparing another run.'}
New-Item -ItemType Directory -Path $results|Out-Null
$folder=if($Scope -eq 'user'){Join-Path $env:LOCALAPPDATA 'Apps/SuperPuTTY'}else{Join-Path $env:ProgramFiles 'SuperPuTTY'}
if(Test-Path $folder){throw 'Installation folder must initially be absent.'}
$msi=New-Object -ComObject WindowsInstaller.Installer
function State([string]$Code){$msi.GetType().InvokeMember('ProductState','GetProperty',$null,$msi,@($Code))}
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
foreach($code in $codes){Assert ((State $code) -ne 5) "Product already installed: $code"}
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
if(-not (Test-Path $preferences)){
    $settings=Join-Path $results 'settings'
    New-Item -ItemType Directory "$settings/layouts" -Force|Out-Null
    Copy-Item "$stage/LegacyLayout.xml" "$settings/layouts/Operations.xml"
    Copy-Item "$stage/LegacyLayout.xml" "$settings/AutoRestoreLayout.XML"
    '<ArrayOfSessionData><SessionData SessionId="Lab/Upgrade" SessionName="Upgrade" Host="example.invalid" Port="22" Proto="SSH" Username="" PuttySession="Default Settings" /></ArrayOfSessionData>'|Set-Content "$settings/Sessions.XML"
    $escaped=[Security.SecurityElement]::Escape($settings)
    "<Settings><SettingsFolder>$escaped</SettingsFolder><AutoUpdateCheck>false</AutoUpdateCheck></Settings>"|Set-Content $preferences
}
[xml]$settingsXml=Get-Content $preferences -Raw
$settingsFolder=[string]$settingsXml.Settings.SettingsFolder
Assert (Test-Path $settingsFolder) 'Test settings folder missing.'
$baseline=@(Get-Item $preferences; Get-ChildItem $settingsFolder -Recurse -File)|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}}
$baseline|ConvertTo-Json|Set-Content "$results/settings-baseline.json"
function Assert-Settings{foreach($entry in $baseline){Assert ((Test-Path $entry.path) -and (Get-FileHash $entry.path).Hash -eq $entry.hash) "Settings changed: $($entry.path)"}}
$operations=[Collections.Generic.List[object]]::new()
function Run([string]$Name,[string[]]$Arguments){
    & $WingetPath @Arguments *> "$results/$Name.log"
    $exit=$LASTEXITCODE
    $operations.Add([pscustomobject]@{name=$Name;exitCode=$exit;arguments=$Arguments})
    $operations|ConvertTo-Json -Depth 5|Set-Content "$results/operations.json"
    Assert ($exit -eq 0) "WinGet $Name failed ($exit); see $results/$Name.log"
    Assert-Settings
}
$success=$false
try{
    Run 'install-previous' @('install','--manifest',"$stage/previous-manifests",'--scope',$Scope,'--silent','--disable-interactivity','--accept-package-agreements','--accept-source-agreements','--verbose-logs','--log',"$results/previous-msi.log")
    Assert ((State $codes[0]) -eq 5) 'Previous version not registered.'
    $assignment=$msi.GetType().InvokeMember('ProductInfo','GetProperty',$null,$msi,@($codes[0],'AssignmentType'))
    Assert ([string]$assignment -eq $(if($Scope -eq 'user'){'0'}else{'1'})) 'Previous install has wrong scope.'
    if($UpgradeMethod -eq 'WinGet'){
        # Scope, source agreements and installer log arguments are query-only
        # for upgrade. Verify actual MSI scope after manifest matching.
        Run 'upgrade' @('upgrade','--manifest',"$stage/manifests",'--silent','--disable-interactivity','--accept-package-agreements','--verbose-logs')
    }else{
        # This exercises MSI MajorUpgrade, not WinGet's uninstallPrevious.
        Run 'installer-upgrade' @('install','--manifest',"$stage/manifests",'--scope',$Scope,'--silent','--disable-interactivity','--accept-package-agreements','--accept-source-agreements','--verbose-logs','--log',"$results/upgrade-msi.log")
    }
    Assert ((State $codes[0]) -ne 5) 'Previous ProductCode remains after upgrade.'
    Assert ((State $codes[1]) -eq 5) 'Candidate ProductCode missing after upgrade.'
    $assignment=$msi.GetType().InvokeMember('ProductInfo','GetProperty',$null,$msi,@($codes[1],'AssignmentType'))
    Assert ([string]$assignment -eq $(if($Scope -eq 'user'){'0'}else{'1'})) 'Upgraded install has wrong scope.'
    $exe=Join-Path $folder 'SuperPutty.exe'
    Assert ((Get-FileHash $exe).Hash -eq $release.executableSha256) 'Upgraded executable hash mismatch.'
    Assert ((Get-AuthenticodeSignature $exe).Status -eq 'Valid') 'Upgraded executable signature invalid.'
    Assert ((Get-Item $exe).VersionInfo.FileVersion -eq "$($release.version).0") 'Upgraded executable version mismatch.'
    Run 'uninstall' @('uninstall','--product-code',$codes[1],'--exact','--silent','--disable-interactivity','--accept-source-agreements','--verbose-logs','--log',"$results/uninstall-msi.log")
    Assert ((State $codes[1]) -ne 5) 'Candidate remains registered after uninstall.'
    Assert (-not (Test-Path $exe)) 'Executable remains after uninstall.'
    $success=$true
}finally{
    $diagnostics=Join-Path $env:LOCALAPPDATA 'Packages/Microsoft.DesktopAppInstaller_8wekyb3d8bbwe/LocalState/DiagOutputDir'
    if(Test-Path $diagnostics){
        New-Item -ItemType Directory "$results/winget-diagnostics" -Force|Out-Null
        Get-ChildItem $diagnostics -File|Copy-Item -Destination "$results/winget-diagnostics"
    }
    foreach($code in $codes){if((State $code) -eq 5){
        $process=Start-Process msiexec.exe -ArgumentList @('/x',$code,'/qn','/norestart','/l*v',"`"$results/cleanup-$($code.Trim('{}')).log`"") -WindowStyle Hidden -PassThru
        if(-not $process.WaitForExit(180000)){throw 'Cleanup timed out; inspect guest before retrying.'}
    }}
    $cleaned=@($codes|Where-Object {(State $_) -eq 5}).Count -eq 0
    $preserved=$true
    try{Assert-Settings}catch{$preserved=$false}
    [pscustomobject]@{success=($success -and $cleaned -and $preserved);scope=$Scope;upgradeMethod=$UpgradeMethod;previousVersion=$release.previous.version;version=$release.version;identity=$identity.Name;elevated=$elevated;cleanupSucceeded=$cleaned;settingsPreserved=$preserved;settingsFilesChecked=@($baseline).Count;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$results/summary.json"
}
