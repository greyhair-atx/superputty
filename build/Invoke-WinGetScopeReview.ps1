[CmdletBinding()]
param([Parameter(Mandatory)][string]$StageDirectory,[switch]$RetryFailedBootstrap)
$ErrorActionPreference='Stop'
$stage=[IO.Path]::GetFullPath($StageDirectory)
$principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this review controller from Administrator PowerShell in the authorized VM.'}
if(-not (Test-Path "$stage/host-baseline.json")){throw 'Prepared baseline missing.'}
if(Test-Path "$stage/results/controller-started.txt"){
    if(-not $RetryFailedBootstrap){throw 'This stage has already run. Review its results before preparing another run.'}
    # Only retry the reviewed pre-installation failure. Later failures need
    # separate investigation; never discard an installed product's account.
    $previous=Get-Content "$stage/results/controller-summary.json" -Raw|ConvertFrom-Json
    $scopeResult=Get-Content "$stage/results/user/summary.json" -Raw|ConvertFrom-Json
    $bootstrapOnly=$true
    if(Test-Path "$stage/results/user/operations.json"){
        # Windows PowerShell 5.1 emits a JSON array as one pipeline object.
        # Direct assignment preserves its three entries instead of wrapping it.
        $ops=Get-Content "$stage/results/user/operations.json" -Raw|ConvertFrom-Json
        $bootstrapOnly=($ops.Count -eq 3 -and $ops[0].name -eq 'version' -and $ops[0].exitCode -eq 0 -and $ops[1].name -eq 'validate' -and $ops[1].exitCode -eq 0 -and $ops[2].name -eq 'install' -and $ops[2].exitCode -eq -1978335230 -and -not (Test-Path "$stage/results/user/install-msi.log"))
    }
    if($previous.success -or -not $previous.hostFilesPreserved -or -not $previous.localManifestSettingRestored -or ($previous.PSObject.Properties.Name -contains 'localManifestPolicyRestored' -and -not $previous.localManifestPolicyRestored) -or -not $scopeResult.cleanupSucceeded -or -not $scopeResult.settingsPreserved -or -not $bootstrapOnly -or (Test-Path "$stage/results/machine")){throw 'Retry is limited to a clean pre-installation bootstrap failure.'}
    $oldName=$previous.temporaryAccount
    if($oldName -notmatch '^SPCEtest[0-9a-f]{8}$'){throw 'Unexpected temporary account name.'}
    $oldUser=Get-LocalUser -Name $oldName
    if($oldUser.Description -ne 'Temporary SuperPuTTY WinGet test'){throw 'Unexpected temporary account description.'}
    $oldProfile=Get-CimInstance Win32_UserProfile|Where-Object SID -EQ $oldUser.SID.Value
    if($oldProfile){
        if($oldProfile.Loaded -or [IO.Path]::GetFullPath($oldProfile.LocalPath) -ne "C:\Users\$oldName"){throw 'Temporary profile loaded or unexpected path; retained for review.'}
        $oldProfile|Remove-CimInstance
    }
    Remove-LocalUser -Name $oldName
    $source=[IO.Path]::GetFullPath("$stage/results")
    $archive=[IO.Path]::GetFullPath("$stage/attempts/bootstrap-"+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    $stagePrefix=$stage.TrimEnd('\')+'\'
    if(-not $source.StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase) -or -not $archive.StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive paths must remain inside the prepared stage.'}
    New-Item -ItemType Directory -Path (Split-Path $archive -Parent) -Force|Out-Null
    Move-Item -LiteralPath $source -Destination $archive
}
New-Item -ItemType Directory -Path "$stage/results" -Force|Out-Null
[DateTime]::UtcNow.ToString('o')|Set-Content "$stage/results/controller-started.txt"
$env:SUPERPUTTY_DISPOSABLE_TEST='1'
$winget=(Get-AppxPackage Microsoft.DesktopAppInstaller).InstallLocation+'\winget.exe'
if(-not (Test-Path $winget)){throw 'WinGet package is missing from the administrator profile.'}
$settings=& $winget settings export|ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Cannot read WinGet admin settings.'}
$wasEnabled=[bool]$settings.adminSettings.LocalManifestFiles
$policyPath='HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppInstaller'
$policyKeyExisted=Test-Path $policyPath
$policy=Get-ItemProperty $policyPath -Name EnableLocalManifestFiles -ErrorAction SilentlyContinue
$policyExisted=$null -ne $policy
if($policyExisted -and ($policy.EnableLocalManifestFiles -ne 1 -or (Get-Item $policyPath).GetValueKind('EnableLocalManifestFiles') -ne 'DWord')){throw 'An existing policy restricts local manifests; this runner will not override it.'}
$name='SPCEtest'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$user=$null
$markerPath='HKCU:\Software\Jim Radford\SuperPuTTY'
$marker=Get-ItemProperty $markerPath -Name installed -ErrorAction SilentlyContinue
$markerExisted=$null -ne $marker
$markerValue=if($markerExisted){$marker.installed}else{$null}
$markerKind=if($markerExisted){(Get-Item $markerPath).GetValueKind('installed')}else{$null}
$controllerSuccess=$false
try{
    # WinGet admin settings are scoped to the invoking user's SID. Use the
    # documented machine policy for the duration of this cross-account test.
    # https://github.com/microsoft/winget-cli/blob/master/doc/admx/DesktopAppInstaller.admx
    if(-not $policyExisted){
        New-Item -Path $policyPath -Force|Out-Null
        New-ItemProperty -Path $policyPath -Name EnableLocalManifestFiles -PropertyType DWord -Value 1 -Force|Out-Null
    }
    $password=ConvertTo-SecureString ('Aa!9'+[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N')) -AsPlainText -Force
    $user=New-LocalUser -Name $name -Password $password -Description 'Temporary SuperPuTTY WinGet test' -AccountNeverExpires
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $name
    $name|Set-Content "$stage/results/test-account.txt"
    & icacls "$stage/results" /grant "${name}:(OI)(CI)M" /T /Q *> "$stage/results/acl.log"
    if($LASTEXITCODE -ne 0){throw 'Cannot grant temporary account result-directory access.'}
    $escapedStage=$stage.Replace("'","''")
    @"
`$ErrorActionPreference='Stop'
`$env:SUPERPUTTY_DISPOSABLE_TEST='1'
try {
    # A newly created profile has not completed first-login App Installer
    # registration. Register for this standard account, then use its package.
    Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe *> '$escapedStage/results/user-appinstaller-registration.log'
    `$userPackage=Get-AppxPackage Microsoft.DesktopAppInstaller
    if(-not `$userPackage){throw 'App Installer registration did not complete for the temporary account.'}
    `$userWinget=Join-Path `$userPackage.InstallLocation 'winget.exe'
    & '$escapedStage/Test-WinGetScopes.ps1' -StageDirectory '$escapedStage' -Scope user -WingetPath `$userWinget *> '$escapedStage/results/user-output.log'
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
    if(-not $policyExisted){
        Remove-ItemProperty -Path $policyPath -Name EnableLocalManifestFiles -ErrorAction SilentlyContinue
        if(-not $policyKeyExisted -and (Test-Path $policyPath)){
            $key=Get-Item $policyPath
            if($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0){Remove-Item -LiteralPath $policyPath}
        }
    }
    $policyAfter=Get-ItemProperty $policyPath -Name EnableLocalManifestFiles -ErrorAction SilentlyContinue
    $policyRestored=if($policyExisted){$null -ne $policyAfter -and $policyAfter.EnableLocalManifestFiles -eq 1}else{$null -eq $policyAfter}
    $restored=& $winget settings export|ConvertFrom-Json
    $settingRestored=([bool]$restored.adminSettings.LocalManifestFiles -eq $wasEnabled)
    $preserved=$true
    foreach($entry in (Get-Content "$stage/host-baseline.json" -Raw|ConvertFrom-Json)){
        if(-not (Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.hash){$preserved=$false}
    }
    [pscustomobject]@{success=($controllerSuccess -and $settingRestored -and $policyRestored -and $preserved);localManifestSettingRestored=$settingRestored;localManifestPolicyRestored=$policyRestored;hostFilesPreserved=$preserved;temporaryAccount=$name;temporaryAccountRemoved=($null -eq (Get-LocalUser -Name $name -ErrorAction SilentlyContinue));completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$stage/results/controller-summary.json"
    Write-Output "Review finished. Results: $stage/results/controller-summary.json"
}
