[CmdletBinding()]
param([Parameter(Mandatory)][string]$StageDirectory)
$ErrorActionPreference='Stop'
$stage=[IO.Path]::GetFullPath($StageDirectory)
$principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this review controller from Administrator PowerShell in the authorized VM.'}
if(-not (Test-Path "$stage/host-baseline.json")){throw 'Prepared baseline missing.'}
if(Test-Path "$stage/results/controller-started.txt"){throw 'This stage has already run. Review its results before preparing another run.'}
New-Item -ItemType Directory -Path "$stage/results" -Force|Out-Null
[DateTime]::UtcNow.ToString('o')|Set-Content "$stage/results/controller-started.txt"
$env:SUPERPUTTY_DISPOSABLE_TEST='1'
$winget=(Get-AppxPackage Microsoft.DesktopAppInstaller).InstallLocation+'\winget.exe'
if(-not (Test-Path $winget)){throw 'WinGet package is missing from the administrator profile.'}
$settings=& $winget settings export|ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Cannot read WinGet admin settings.'}
$wasEnabled=[bool]$settings.adminSettings.LocalManifestFiles
$name='SPCEtest'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$user=$null
$markerPath='HKCU:\Software\Jim Radford\SuperPuTTY'
$marker=Get-ItemProperty $markerPath -Name installed -ErrorAction SilentlyContinue
$markerExisted=$null -ne $marker
$markerValue=if($markerExisted){$marker.installed}else{$null}
$markerKind=if($markerExisted){(Get-Item $markerPath).GetValueKind('installed')}else{$null}
$controllerSuccess=$false
try{
    if(-not $wasEnabled){& $winget settings --enable LocalManifestFiles *> "$stage/results/enable-local-manifests.log";if($LASTEXITCODE -ne 0){throw 'Cannot enable local manifests.'}}
    $password=ConvertTo-SecureString ('Aa!9'+[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N')) -AsPlainText -Force
    $user=New-LocalUser -Name $name -Password $password -Description 'Temporary SuperPuTTY WinGet test' -AccountNeverExpires
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $name
    $name|Set-Content "$stage/results/test-account.txt"
    & icacls "$stage/results" /grant "${name}:(OI)(CI)M" /T /Q *> "$stage/results/acl.log"
    if($LASTEXITCODE -ne 0){throw 'Cannot grant temporary account result-directory access.'}
    $escapedStage=$stage.Replace("'","''")
    $escapedWinget=$winget.Replace("'","''")
    @"
`$ErrorActionPreference='Stop'
`$env:SUPERPUTTY_DISPOSABLE_TEST='1'
try {
    & '$escapedStage/Test-WinGetScopes.ps1' -StageDirectory '$escapedStage' -Scope user -WingetPath '$escapedWinget' *> '$escapedStage/results/user-output.log'
} catch { `$_ | Out-String | Add-Content '$escapedStage/results/user-error.log'; exit 1 }
"@|Set-Content "$stage/Run-StandardUser.ps1"
    $credential=[Management.Automation.PSCredential]::new("$env:COMPUTERNAME\$name",$password)
    $child=Start-Process "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe" -Credential $credential -LoadUserProfile -WorkingDirectory $stage -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$stage/Run-StandardUser.ps1`"") -WindowStyle Hidden -PassThru
    if(-not $child.WaitForExit(600000)){throw 'Standard-user test timed out; inspect running process before cleanup.'}
    if($child.ExitCode -ne 0){throw 'Standard-user WinGet test failed; see results/user-error.log and results/user/.'}
    & "$stage/Test-WinGetScopes.ps1" -StageDirectory $stage -Scope machine -WingetPath $winget *> "$stage/results/machine-output.log"
    $summaries=@('user','machine'|ForEach-Object {Get-Content "$stage/results/$_/summary.json" -Raw|ConvertFrom-Json})
    if(@($summaries|Where-Object {-not $_.success -or -not $_.cleanupSucceeded}).Count){throw 'One or more scope tests failed.'}
    $profile=Get-CimInstance Win32_UserProfile|Where-Object SID -EQ $user.SID.Value
    if($profile){
        if($profile.Loaded -or [IO.Path]::GetFullPath($profile.LocalPath) -ne "C:\Users\$name"){throw 'Temporary profile loaded or unexpected path; retained for review.'}
        $profile|Remove-CimInstance
    }
    Remove-LocalUser -Name $name
    $controllerSuccess=$true
}catch{
    $_|Out-String|Set-Content "$stage/results/controller-error.log"
    Write-Warning "Review incomplete. See $stage/results/controller-error.log. Temporary account is retained if created."
}finally{
    # Machine MSI's shortcut component writes this historical HKCU marker.
    # Restore only that value; do not migrate or replace settings registry keys.
    if($markerExisted){New-Item -Path $markerPath -Force|Out-Null;New-ItemProperty -Path $markerPath -Name installed -Value $markerValue -PropertyType $markerKind -Force|Out-Null}
    elseif(Get-ItemProperty $markerPath -Name installed -ErrorAction SilentlyContinue){Remove-ItemProperty -Path $markerPath -Name installed}
    if(-not $wasEnabled){& $winget settings --disable LocalManifestFiles *> "$stage/results/restore-local-manifests.log";if($LASTEXITCODE -ne 0){$controllerSuccess=$false}}
    $restored=& $winget settings export|ConvertFrom-Json
    $settingRestored=([bool]$restored.adminSettings.LocalManifestFiles -eq $wasEnabled)
    $preserved=$true
    foreach($entry in (Get-Content "$stage/host-baseline.json" -Raw|ConvertFrom-Json)){
        if(-not (Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.hash){$preserved=$false}
    }
    [pscustomobject]@{success=($controllerSuccess -and $settingRestored -and $preserved);localManifestSettingRestored=$settingRestored;hostFilesPreserved=$preserved;temporaryAccount=$name;temporaryAccountRemoved=($null -eq (Get-LocalUser -Name $name -ErrorAction SilentlyContinue));completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$stage/results/controller-summary.json"
    Write-Output "Review finished. Results: $stage/results/controller-summary.json"
}
