# Interactive Windows regression: real SuperPuTTY docking with three duplicate PuTTY sessions.
# Uses raw loopback peers to exercise PuTTY window activation and typed input without SSH credentials.
param([string]$Configuration='Debug',[string]$PuttyPath='C:/Program Files/PuTTY/putty.exe',[switch]$InspectOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$captureProfile=Join-Path ([IO.Path]::GetTempPath()) ('SuperPuttyThreeSessionTest-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $captureProfile|Out-Null
[xml]$prefs='<Settings><SettingsFolder/><PuttyExe/><AutoUpdateCheck/><ExitConfirmation/><SingleInstanceMode/><DefaultLayoutName/></Settings>'
$prefs.Settings.PuttyExe=$PuttyPath
$prefs.Settings.SettingsFolder=[string]$captureProfile
$prefs.Settings.AutoUpdateCheck='False';$prefs.Settings.ExitConfirmation='False';$prefs.Settings.SingleInstanceMode='False';$prefs.Settings.DefaultLayoutName='Default'
$prefs.Save("$captureProfile/SuperPuTTY.settings")
$env:USERPROFILE=$captureProfile
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Copy-Item "$captureProfile/SuperPuTTY.settings" (Join-Path $captureProfile ([Windows.Forms.Application]::ProductName + '.settings'))
[void][Reflection.Assembly]::LoadFrom("$repo/bin/x64/$Configuration/SuperPutty.exe")
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class LiveNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect{public int l,t,r,b;}
 [StructLayout(LayoutKind.Sequential)] public struct Gui{public int size,flags;public IntPtr active,focus,capture,menu,move,caret;public Rect rect;}
 [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint t,ref Gui g);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 public static Gui State(IntPtr h){var g=new Gui();g.size=Marshal.SizeOf(g);GetGUIThreadInfo(GetWindowThreadProcessId(h,IntPtr.Zero),ref g);return g;}
}
"@
function Pump([int]$ms){$end=[DateTime]::UtcNow.AddMilliseconds($ms);while([DateTime]::UtcNow -lt $end){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 10}}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$panels=@();$clients=@();$form=$null
try{
 $listener.Start()
 [SuperPutty.SuperPuTTY]::Initialize([string[]]@())
 $form=New-Object SuperPutty.frmSuperPutty
 [SuperPutty.SuperPuTTY]::MainForm=$form
 $form.Show();Pump 300
 $form.Location=New-Object Drawing.Point 20,20;$form.Size=New-Object Drawing.Size 1200,760
 $session=New-Object SuperPutty.Data.SessionData
 $session.SessionId='Test_SSH';$session.SessionName='Test_SSH';$session.Host='127.0.0.1';$session.Port=$listener.LocalEndpoint.Port;$session.Proto=[SuperPutty.Data.ConnectionProtocol]::Raw
 $session.PuttySession='Default Settings'
 for($i=0;$i -lt 3;$i++){
  $accept=$listener.AcceptTcpClientAsync()
  $p=[SuperPutty.SuperPuTTY]::OpenProtoSession($session);$panels+=,$p
  $end=[DateTime]::UtcNow.AddSeconds(15)
  while(-not ([SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($p,$null)).ExternalProcessCaptured -and [DateTime]::UtcNow -lt $end){Pump 30}
  if(-not ([SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($p,$null)).ExternalProcessCaptured -or -not $accept.IsCompleted){throw 'Capture failed'}
  $client=$accept.Result;$clients+=,$client
  $bytes=[Text.Encoding]::ASCII.GetBytes("Test terminal $i`r`n");$client.GetStream().Write($bytes,0,$bytes.Length)
  Pump 300
 }
 foreach($index in @(0,1,2,0,2,1,0)){
  $p=$panels[$index];[void][SuperPutty.ctlPuttyPanel].GetMethod('Show',[Type[]]@()).Invoke($p,@());Pump 250
  $h=([SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($p,$null)).AppWindowHandle;$g=[LiveNative]::State($h)
  $style=[LiveNative]::GetWindowLong($h,-16)
  "tab=$index handle=$h active=$($g.active) focus=$($g.focus) foreground=$([LiveNative]::GetForegroundWindow()) style=$($style.ToString('X8'))"
  if(-not $InspectOnly -and ($g.focus -ne $h -or $g.active -ne $h)){throw 'PuTTY is not both active and focused'}
  if(-not $InspectOnly){
   $panel=([SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($p,$null))
   $origin=$panel.PointToScreen([Drawing.Point]::Empty)
   $rect=New-Object LiveNative+Rect;[LiveNative]::GetWindowRect($h,[ref]$rect)|Out-Null
   if($rect.l -ne $origin.X -or $rect.t -ne $origin.Y -or ($rect.r-$rect.l) -ne $panel.Width -or ($rect.b-$rect.t) -ne $panel.Height){throw 'Embedded window extends beyond its panel'}
   $marker="focus-test-$index"
   [Windows.Forms.SendKeys]::SendWait($marker+'{ENTER}');Pump 300
   $stream=$clients[$index].GetStream();$received=''
   $end=[DateTime]::UtcNow.AddSeconds(2)
   while(-not $received.Contains($marker) -and [DateTime]::UtcNow -lt $end){
    if($stream.DataAvailable){$buffer=New-Object byte[] 4096;$count=$stream.Read($buffer,0,$buffer.Length);$received += [Text.Encoding]::ASCII.GetString($buffer,0,$count)}
    Pump 20
   }
   if(-not $received.Contains($marker)){throw 'Typed input did not reach the selected session'}
   for($other=0;$other -lt $clients.Count;$other++){if($other -ne $index -and $clients[$other].Available -gt 0){throw 'Typed input reached an unselected session'}}
  }
 }
 if($InspectOnly){Pump 1000}else{'PASS: Three duplicate sessions retain activation, keyboard focus, exact window bounds, and exclusive typed-input delivery after switching.'}
}finally{
 foreach($p in $panels){[void][SuperPutty.ctlPuttyPanel].GetMethod('CloseWithoutConfirmation',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($p,@())};if($form){$form.Dispose()}
 foreach($c in $clients){$c.Dispose()};$listener.Stop()
}









