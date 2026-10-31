[CmdletBinding()]
param([Parameter(Mandatory)][string]$StageDirectory,[switch]$ExplicitPerUserProperties)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Requires an authorized disposable Windows VM.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=[Security.Principal.WindowsPrincipal]$identity
if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $identity.Name -notmatch '\\SPCEtest[0-9a-f]{8}$'){throw 'Requires the temporary standard account in its interactive session.'}
$stage=[IO.Path]::GetFullPath($StageDirectory)
if((Test-Path "$stage/direct-registration.json") -or (Test-Path "$stage/direct-summary.json")){throw 'Diagnostic evidence already exists; use a fresh stage directory.'}
$code='{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}'
$package=Join-Path $stage 'SuperPuTTY-CE-1.8.3-current-user-win-x64-signed.msi'
if((Get-FileHash $package).Hash -ne '5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36' -or (Get-AuthenticodeSignature $package).Status -ne 'Valid'){throw 'Expected the exact signed published current-user MSI.'}
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class MsiRegistrationContext {
 public string ProductCode;
 public uint Context;
 public string UserSid;
}
public static class MsiRegistrationInspector {
 [DllImport("msi.dll", CharSet=CharSet.Unicode)]
 private static extern uint MsiEnumProductsEx(string product,string userSid,uint context,uint index,StringBuilder installedProduct,out uint installedContext,StringBuilder sid,ref uint sidLength);
 public static MsiRegistrationContext[] Find(string product) {
  var results=new List<MsiRegistrationContext>();
  for(uint index=0;;index++) {
   var code=new StringBuilder(39);var sid=new StringBuilder(256);uint length=256,context;
   uint result=MsiEnumProductsEx(product,null,7,index,code,out context,sid,ref length);
   if(result==259) break;
   if(result!=0) throw new System.ComponentModel.Win32Exception((int)result);
   results.Add(new MsiRegistrationContext {ProductCode=code.ToString(),Context=context,UserSid=sid.ToString()});
  }
  return results.ToArray();
 }
}
'@
if([MsiRegistrationInspector]::Find($code).Count){throw 'Product already installed; refusing diagnostic run.'}
$preferences=Join-Path $env:USERPROFILE 'SuperPuTTY.settings'
$baseline=@()
if(Test-Path $preferences){
    $baseline+=Get-Item $preferences
    [xml]$xml=Get-Content $preferences -Raw
    if($xml.Settings.SettingsFolder -and (Test-Path $xml.Settings.SettingsFolder)){$baseline+=Get-ChildItem $xml.Settings.SettingsFolder -Recurse -File}
}
$hashes=@($baseline|ForEach-Object {[pscustomobject]@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}})
$hashes|ConvertTo-Json|Set-Content "$stage/settings-baseline.json"
function Get-Arp {
    foreach($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)){
        foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
            $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
            try{
                $key=$base.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Uninstall\$code")
                if($key){
                    try{[pscustomobject]@{hive=$hive.ToString();view=$view.ToString();displayName=$key.GetValue('DisplayName');displayVersion=$key.GetValue('DisplayVersion');uninstallString=$key.GetValue('UninstallString')}}finally{$key.Dispose()}
                }
            }finally{$base.Dispose()}
        }
    }
}
$success=$false
try{
    $arguments=@('/i',"`"$package`"",'/qn','/norestart','/l*v',"`"$stage/direct-install.log`"")
    if($ExplicitPerUserProperties){$arguments+=@('ALLUSERS=2','MSIINSTALLPERUSER=1')}
    $process=Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(180000)){throw 'Direct installation timed out.'}
    if($process.ExitCode -notin @(0,3010)){throw "Direct installation failed: $($process.ExitCode)"}
    $msi=New-Object -ComObject WindowsInstaller.Installer
    $assignment=$msi.GetType().InvokeMember('ProductInfo','GetProperty',$null,$msi,@($code,'AssignmentType'))
    $contexts=@([MsiRegistrationInspector]::Find($code))
    $userExecutableExists=Test-Path "$env:LOCALAPPDATA/Apps/SuperPuTTY/SuperPutty.exe"
    [pscustomobject]@{identity=$identity.Name;sid=$identity.User.Value;sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId;elevated=$false;explicitPerUserProperties=[bool]$ExplicitPerUserProperties;assignmentType=$assignment;contexts=$contexts;arp=@(Get-Arp);userExecutableExists=$userExecutableExists}|ConvertTo-Json -Depth 6|Set-Content "$stage/direct-registration.json"
    # MSI APIs define installation context; ARP hive location is diagnostic
    # evidence and must not be substituted for this authoritative check.
    if($contexts.Count -ne 1 -or $contexts[0].Context -ne 2 -or $contexts[0].UserSid -ne $identity.User.Value -or [string]$assignment -ne '0' -or -not $userExecutableExists){throw 'Published current-user MSI did not install in the expected user-unmanaged context.'}
    $success=$true
}finally{
    if([MsiRegistrationInspector]::Find($code).Count){
        $process=Start-Process msiexec.exe -ArgumentList @('/x',$code,'/qn','/norestart','/l*v',"`"$stage/direct-uninstall.log`"") -WindowStyle Hidden -PassThru
        if(-not $process.WaitForExit(180000)){throw 'Diagnostic cleanup timed out.'}
    }
    $preserved=$true
    foreach($file in $hashes){if(-not (Test-Path $file.path) -or (Get-FileHash $file.path).Hash -ne $file.hash){$preserved=$false}}
    [pscustomobject]@{diagnosticCompleted=$success;productsRemoved=([MsiRegistrationInspector]::Find($code).Count -eq 0);arpRemoved=(@(Get-Arp).Count -eq 0);settingsPreserved=$preserved;settingsFilesChecked=$hashes.Count;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content "$stage/direct-summary.json"
}
