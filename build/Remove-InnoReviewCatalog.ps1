[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'WIN11-TEST' -or -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Disposable WIN11-TEST administrator required.'}
$root='C:\SPCEInnoReview'
$thumb=(Get-Content "$root/certificate-thumbprint.txt" -Raw).Trim()
if($thumb -notmatch '^[A-Fa-f0-9]{40}$'){throw 'Invalid owned-certificate marker.'}
$policy='HKLM:\Software\Policies\Microsoft\Windows\AppInstaller'
$source=(Get-ItemProperty "$policy/AdditionalSources" -ErrorAction SilentlyContinue).SPCEInnoReview
if($source){$value=$source|ConvertFrom-Json;if($value.Name -ne 'SPCEInnoReview' -or $value.Arg -ne 'https://localhost:9443'){throw 'Source configuration changed; preserve for review.'}}
foreach($ip in @('0.0.0.0:9443','[::]:9443')){
 $binding=netsh http show sslcert "ipport=$ip" 2>&1
 if($LASTEXITCODE -eq 0 -and (($binding -join '') -replace '\s','') -notmatch [regex]::Escape($thumb)){throw 'SSL binding belongs to a different certificate.'}
}
if(Get-ScheduledTask SPCEInnoCatalog -ErrorAction SilentlyContinue){Stop-ScheduledTask SPCEInnoCatalog;Unregister-ScheduledTask SPCEInnoCatalog -Confirm:$false}
Remove-ItemProperty "$policy/AdditionalSources" -Name SPCEInnoReview -ErrorAction SilentlyContinue
$remaining=@((Get-ItemProperty "$policy/AdditionalSources" -ErrorAction SilentlyContinue).PSObject.Properties|Where-Object {$_.Name -notmatch '^PS'})
if(-not $remaining.Count -and (Get-ItemProperty $policy -ErrorAction SilentlyContinue).EnableAdditionalSources -eq 1){Remove-ItemProperty $policy -Name EnableAdditionalSources}
foreach($ip in @('0.0.0.0:9443','[::]:9443')){netsh http delete sslcert "ipport=$ip" *> $null}
$bindingsRemoved=$true
foreach($ip in @('0.0.0.0:9443','[::]:9443')){netsh http show sslcert "ipport=$ip" *> $null;if($LASTEXITCODE -eq 0){$bindingsRemoved=$false}}
foreach($store in @('My','Root')){if(Test-Path "Cert:\LocalMachine\$store\$thumb"){Remove-Item "Cert:\LocalMachine\$store\$thumb"}}
[pscustomobject]@{
 sourceRemoved=($null -eq (Get-ItemProperty "$policy/AdditionalSources" -ErrorAction SilentlyContinue).SPCEInnoReview)
 sourcePolicyAbsent=($null -eq (Get-ItemProperty $policy -ErrorAction SilentlyContinue).EnableAdditionalSources)
 certificateRemoved=(-not (Test-Path "Cert:\LocalMachine\My\$thumb") -and -not (Test-Path "Cert:\LocalMachine\Root\$thumb"))
 taskRemoved=(@(Get-ScheduledTask SPCEInnoCatalog -ErrorAction SilentlyContinue).Count -eq 0)
 sslBindingsRemoved=$bindingsRemoved
}|ConvertTo-Json|Set-Content "$root/catalog-cleanup.json"
