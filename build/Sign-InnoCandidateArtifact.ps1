[CmdletBinding()]
param([Parameter(Mandatory)][string]$File,[Parameter(Mandatory)][string]$Configuration)
$ErrorActionPreference='Stop'
$config=Get-Content -LiteralPath $Configuration -Raw|ConvertFrom-Json
if([IO.Path]::GetFileName($File) -match '^uninst\.e(32|64)\.tmp$'){
 # Keep the embedded uninstaller byte-for-byte identical to the signed cache
 # whose digest was compiled into recovery. Re-signing would change its hash.
 if((Get-FileHash -LiteralPath $config.cache).Hash -ne $config.sha256){throw 'Reviewed uninstaller cache changed.'}
 if([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($File).ProductVersion.Trim()) -ne [version]$config.version){throw 'Unexpected uninstaller resource version.'}
 Copy-Item -LiteralPath $config.cache -Destination $File -Force
}else{
 & $config.tool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $config.dlib /dmdf $config.metadata $File
 if($LASTEXITCODE -ne 0){throw 'Artifact signing failed.'}
}
$signature=Get-AuthenticodeSignature -LiteralPath $File
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName',$false) -ne 'Christopher Thornton' -or -not $signature.TimeStamperCertificate){throw 'Project signature and timestamp verification failed.'}
