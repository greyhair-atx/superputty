[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId
if(-not [Environment]::UserInteractive -or $sessionId -eq 0){
 throw 'Interactive tests require a signed-in Windows desktop. Run the Windows agent interactively, not as a service.'
}
if(-not ('SpceInteractiveDesktopNative' -as [type])){
 Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SpceInteractiveDesktopNative {
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint="GetUserObjectInformationW", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int size, out int needed);
}
'@
}
function DesktopName([IntPtr]$Handle){
 $name=[Text.StringBuilder]::new(256);$needed=0
 if($Handle -eq [IntPtr]::Zero -or -not [SpceInteractiveDesktopNative]::GetUserObjectInformation($Handle,2,$name,$name.Capacity*2,[ref]$needed)){
  throw 'Cannot inspect the Windows desktop required by interactive tests.'
 }
 $name.ToString()
}
# Read/write access is required by the mouse tests. Do not switch, unlock or
# otherwise change the desktop; reject a locked/inaccessible session instead.
$inputDesktop=[SpceInteractiveDesktopNative]::OpenInputDesktop(0,$false,0x81)
if($inputDesktop -eq [IntPtr]::Zero){throw 'Interactive tests require an unlocked, accessible Windows desktop. Leave the agent session signed in and unlocked.'}
try{
 $current=DesktopName ([SpceInteractiveDesktopNative]::GetThreadDesktop([SpceInteractiveDesktopNative]::GetCurrentThreadId()))
 $inputName=DesktopName $inputDesktop
 if($current -ne $inputName){throw 'The agent desktop is not the active input desktop. Leave its Windows session unlocked before running interactive tests.'}
 [pscustomobject]@{interactive=$true;sessionId=$sessionId;desktop=$current}
}finally{$null=[SpceInteractiveDesktopNative]::CloseDesktop($inputDesktop)}
