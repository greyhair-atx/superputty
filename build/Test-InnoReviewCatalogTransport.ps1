[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerDirectory,[Parameter(Mandatory)][string]$ResultFile)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
$name='SuperPuTTY-CE-Prototype-1.8.4.101-none.exe'
$url="https://localhost:9443/installers/$name"
$expected=[IO.File]::ReadAllBytes((Join-Path $InstallerDirectory $name))
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Request([string]$Url,[string]$Method='GET',[int]$First=-1,[int]$Last=-1,[switch]$AbortBody){
 $request=[Net.HttpWebRequest]::Create($Url);$request.Method=$Method;$request.Timeout=15000
 if($First -ge 0){$request.AddRange($First,$Last)}
 try{$response=$request.GetResponse()}catch [Net.WebException]{if(-not $_.Exception.Response){throw};$response=$_.Exception.Response}
 try{
  $status=[int]$response.StatusCode;$length=$response.ContentLength
  if($AbortBody){$request.Abort();return [pscustomobject]@{status=$status;length=$length}}
  $buffer=[IO.MemoryStream]::new();try{$response.GetResponseStream().CopyTo($buffer);$bytes=$buffer.ToArray()}finally{$buffer.Dispose()}
  [pscustomobject]@{status=$status;length=$length;bytes=$bytes;range=$response.Headers['Content-Range']}
 }finally{$response.Close()}
}
$head=Request $url 'HEAD';Assert ($head.status -eq 200 -and $head.length -eq $expected.Length) 'HEAD length incorrect.'
$range=Request $url 'GET' 0 31
Assert ($range.status -eq 206 -and $range.bytes.Length -eq 32 -and [BitConverter]::ToString($range.bytes) -eq [BitConverter]::ToString($expected[0..31])) 'Byte-range response incorrect.'
$invalid=Request $url 'GET' $expected.Length ($expected.Length+10);Assert ($invalid.status -eq 416) 'Invalid range not rejected.'
$denied=Request 'https://localhost:9443/installers/SuperPuTTY-CE-Prototype-1.8.4.101-registration.exe';Assert ($denied.status -eq 404) 'Failure fixture was downloadable.'
$null=Request $url 'GET' -AbortBody
$full=Request $url
$sha=[Security.Cryptography.SHA256]::Create()
try{Assert ($full.status -eq 200 -and [BitConverter]::ToString($sha.ComputeHash($full.bytes)) -eq [BitConverter]::ToString($sha.ComputeHash($expected))) 'Server failed after client cancellation or full download hash differed.'}finally{$sha.Dispose()}
[pscustomobject]@{success=$true;head=$true;byteRange=$true;invalidRangeRejected=$true;failureFixtureRejected=$true;clientCancellationSurvived=$true;fullDownloadHash=$true}|ConvertTo-Json|Set-Content $ResultFile
