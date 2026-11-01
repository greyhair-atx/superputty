# Run manually as administrator in the authorized disposable VM.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'WIN11-TEST' -or -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Disposable WIN11-TEST administrator required.'}
$root='C:\SPCEInnoReview'
$policy='HKLM:\Software\Policies\Microsoft\Windows\AppInstaller'
if(-not (Test-Path "$root/catalog-v1/catalog.json") -or (Test-Path "$root/certificate-thumbprint.txt")){throw 'Missing catalog or prior setup marker exists.'}
if(Get-ScheduledTask -TaskName SPCEInnoCatalog -ErrorAction SilentlyContinue){throw 'Existing catalog task requires review.'}
if((Get-ItemProperty $policy -ErrorAction SilentlyContinue).PSObject.Properties.Name -contains 'EnableAdditionalSources'){throw 'Existing source policy needs preservation; no changes made.'}
if((Get-ItemProperty "$policy/AdditionalSources" -ErrorAction SilentlyContinue).PSObject.Properties.Name -contains 'SPCEInnoReview'){throw 'Existing review source needs preservation.'}
foreach($ip in @('0.0.0.0:9443','[::]:9443')){netsh http show sslcert "ipport=$ip" *> $null;if($LASTEXITCODE -eq 0){throw 'An existing SSL binding must not be overwritten.'}}
$cert=New-SelfSignedCertificate -DnsName localhost -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddDays(2)
$cert.Thumbprint|Set-Content "$root/certificate-thumbprint.txt"
Export-Certificate -Cert $cert -FilePath "$root/localhost.cer"|Out-Null
Import-Certificate -FilePath "$root/localhost.cer" -CertStoreLocation Cert:\LocalMachine\Root|Out-Null
foreach($ip in @('0.0.0.0:9443','[::]:9443')){
 netsh http add sslcert "ipport=$ip" "certhash=$($cert.Thumbprint)" 'appid={D29A1674-C729-4B99-BCE6-0DA668BEE8A7}'
 if($LASTEXITCODE){throw 'SSL binding failed; retain marker and inspect setup.'}
}
New-Item $policy -Force|Out-Null
New-ItemProperty $policy -Name EnableAdditionalSources -Value 1 -PropertyType DWord|Out-Null
New-Item "$policy/AdditionalSources" -Force|Out-Null
$source=@{Name='SPCEInnoReview';Arg='https://localhost:9443';Type='Microsoft.Rest';Data='';Identifier='SuperPuttyDisposableReview';Explicit=$true}|ConvertTo-Json -Compress
New-ItemProperty "$policy/AdditionalSources" -Name SPCEInnoReview -Value $source -PropertyType String|Out-Null
$principal=New-ScheduledTaskPrincipal -UserId SYSTEM -LogonType ServiceAccount -RunLevel Highest
$action=New-ScheduledTaskAction -Execute powershell.exe -Argument '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\SPCEInnoReview\build\Start-WinGetReviewCatalog.ps1 -Directory C:\SPCEInnoReview\catalog-v1 -InstallerDirectory C:\SPCEInnoReview\output-v4'
Register-ScheduledTask SPCEInnoCatalog -Principal $principal -Action $action -Force|Out-Null
Start-ScheduledTask SPCEInnoCatalog
Write-Host 'Temporary Inno catalog setup complete. Tell Codex it is ready.'
