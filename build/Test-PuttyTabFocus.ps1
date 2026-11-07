# Interactive Windows regression: real SuperPuTTY docking with three duplicate PuTTY sessions.
# Uses raw loopback peers to exercise PuTTY window activation and typed input without SSH credentials.
param([string]$Configuration='Debug',[string]$PuttyPath='C:/Program Files/PuTTY/putty.exe',
 [string]$ApplicationPath,[string]$ScreenshotPath,
 [ValidateSet('Light','Dark','Dark Modern','System')][string]$Theme='Light',
 [switch]$HideScrollbar,[switch]$InspectOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$captureProfile=Join-Path ([IO.Path]::GetTempPath()) ('SuperPuttyThreeSessionTest-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $captureProfile|Out-Null
[xml]$prefs='<Settings><SettingsFolder/><PuttyExe/><AutoUpdateCheck/><ExitConfirmation/><SingleInstanceMode/><DefaultLayoutName/><Theme/></Settings>'
$prefs.Settings.PuttyExe=$PuttyPath
$prefs.Settings.SettingsFolder=[string]$captureProfile
$prefs.Settings.AutoUpdateCheck='False';$prefs.Settings.ExitConfirmation='False';$prefs.Settings.SingleInstanceMode='False';$prefs.Settings.DefaultLayoutName='Default'
$prefs.Settings.Theme=$Theme
$prefs.Save("$captureProfile/SuperPuTTY.settings")
$env:USERPROFILE=$captureProfile
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Copy-Item "$captureProfile/SuperPuTTY.settings" (Join-Path $captureProfile ([Windows.Forms.Application]::ProductName + '.settings'))
$app=if($ApplicationPath){(Resolve-Path -LiteralPath $ApplicationPath).Path}else{"$repo/bin/x64/$Configuration/SuperPutty.exe"}
[void][Reflection.Assembly]::LoadFrom($app)
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class LiveNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect{public int l,t,r,b;}
 [StructLayout(LayoutKind.Sequential)] public struct Point{public int x,y;}
 [StructLayout(LayoutKind.Sequential)] public struct Gui{public int size,flags;public IntPtr active,focus,capture,menu,move,caret;public Rect rect;}
 [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint t,ref Gui g);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Point p);
 [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h,int index,int style);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,int flags);
 [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int height,bool repaint);
 [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
 public static void ActivateTestWindow(IntPtr h){
  uint current=GetCurrentThreadId(), foreground=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero);
  bool attached=current!=foreground && AttachThreadInput(current,foreground,true);
  try{BringWindowToTop(h);SetForegroundWindow(h);}
  finally{if(attached)AttachThreadInput(current,foreground,false);}
 }
 public static Gui State(IntPtr h){var g=new Gui();g.size=Marshal.SizeOf(g);GetGUIThreadInfo(GetWindowThreadProcessId(h,IntPtr.Zero),ref g);return g;}
}
"@
function Pump([int]$ms){$end=[DateTime]::UtcNow.AddMilliseconds($ms);while([DateTime]::UtcNow -lt $end){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 10}}
function Get-AppPanel($Tab){[SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($Tab,$null)}
function Assert-Frame($Panel){
 if(-not $Panel.Visible){throw 'Frame geometry must be checked on a visible selected tab'}
 $h=$Panel.AppWindowHandle
 $style=[LiveNative]::GetWindowLong($h,-16);$extended=[LiveNative]::GetWindowLong($h,-20)
 if(($style -band 0x00C40000) -ne 0 -or ($extended -band 0x00020301) -ne 0){throw 'PuTTY native frame was not removed'}
 if(($style -band 0x40000000) -ne 0){throw 'PuTTY was converted to WS_CHILD; this breaks terminal activation'}
 $origin=$Panel.PointToScreen([Drawing.Point]::Empty)
 $rect=New-Object LiveNative+Rect;[void][LiveNative]::GetWindowRect($h,[ref]$rect)
 if($rect.l -ne $origin.X+2 -or $rect.t -ne $origin.Y+2 -or ($rect.r-$rect.l) -ne $Panel.ClientSize.Width-4 -or ($rect.b-$rect.t) -ne $Panel.ClientSize.Height-4){throw "Embedded PuTTY does not have a 2px host border: window=[$($rect.l),$($rect.t),$($rect.r-$rect.l),$($rect.b-$rect.t)] host=[$($origin.X),$($origin.Y),$($Panel.ClientSize.Width),$($Panel.ClientSize.Height)]"}
 $clientOrigin=New-Object LiveNative+Point;[void][LiveNative]::ClientToScreen($h,[ref]$clientOrigin)
 $client=New-Object LiveNative+Rect;[void][LiveNative]::GetClientRect($h,[ref]$client)
 $scrollbar=if(($style -band 0x00200000) -ne 0){[Windows.Forms.SystemInformation]::VerticalScrollBarWidth}else{0}
 if($clientOrigin.x -ne $rect.l -or $clientOrigin.y -ne $rect.t -or $rect.b-$rect.t -ne $client.b -or $rect.r-$rect.l-$client.r -ne $scrollbar){throw 'PuTTY still has an extra native frame inside the 2px host border'}
}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$panels=@();$clients=@();$form=$null
$profileName='SPCEFrameTest'+[guid]::NewGuid().ToString('N')
$profileKey='Software\SimonTatham\PuTTY\Sessions\'+$profileName
try{
 # Dedicated PuTTY settings exercise the sunken edge without modifying saved profiles.
 $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($profileKey)
 try{
  $key.SetValue('Present',1);$key.SetValue('WindowBorder',0);$key.SetValue('SunkenEdge',1)
  $key.SetValue('ScrollBar',[int](-not $HideScrollbar));$key.SetValue('WarnOnClose',0)
 }finally{$key.Dispose()}
 $listener.Start()
 [SuperPutty.SuperPuTTY]::Initialize([string[]]@())
 $form=New-Object SuperPutty.frmSuperPutty
 [SuperPutty.SuperPuTTY]::MainForm=$form
 $form.Show();Pump 300
 $form.Location=New-Object Drawing.Point 20,20;$form.Size=New-Object Drawing.Size 1200,760
 $session=New-Object SuperPutty.Data.SessionData
 $session.SessionId='Test_SSH';$session.SessionName='Test_SSH';$session.Host='127.0.0.1';$session.Port=$listener.LocalEndpoint.Port;$session.Proto=[SuperPutty.Data.ConnectionProtocol]::Raw
 $session.PuttySession=$profileName
 for($i=0;$i -lt 3;$i++){
  $accept=$listener.AcceptTcpClientAsync()
  $p=[SuperPutty.SuperPuTTY]::OpenProtoSession($session);$panels+=,$p
  $end=[DateTime]::UtcNow.AddSeconds(15)
  while(-not ([SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($p,$null)).ExternalProcessCaptured -and [DateTime]::UtcNow -lt $end){Pump 30}
  if(-not ([SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($p,$null)).ExternalProcessCaptured -or -not $accept.IsCompleted){throw 'Capture failed'}
  $client=$accept.Result;$clients+=,$client
  $bytes=[Text.Encoding]::ASCII.GetBytes("Test terminal $i`r`n");$client.GetStream().Write($bytes,0,$bytes.Length)
  Pump 300
  if(-not $InspectOnly -and (Get-AppPanel $p).Visible){Assert-Frame (Get-AppPanel $p)}
 }
 if(-not $InspectOnly){
  [void][SuperPutty.ctlPuttyPanel].GetMethod('Show',[Type[]]@()).Invoke($panels[2],@());Pump 250
  foreach($size in @([Drawing.Size]::new(920,620),[Drawing.Size]::new(1400,850),[Drawing.Size]::new(1200,760))){
   $form.Size=$size;Pump 250;Assert-Frame (Get-AppPanel $panels[2])
  }
  $form.WindowState='Minimized';Pump 200;$form.WindowState='Normal';Pump 300;Assert-Frame (Get-AppPanel $panels[2])
  # Simulate the style/frame recalculation performed by PuTTY Change Settings.
  # Its process-scoped location hook must repair this without a host resize.
  $h=(Get-AppPanel $panels[2]).AppWindowHandle
  [void][LiveNative]::SetWindowLong($h,-16,([LiveNative]::GetWindowLong($h,-16) -bor 0x00C40000))
  [void][LiveNative]::SetWindowLong($h,-20,([LiveNative]::GetWindowLong($h,-20) -bor 0x00000300))
  [void][LiveNative]::SetWindowPos($h,[IntPtr]::Zero,0,0,0,0,0x0037)
  Pump 500;Assert-Frame (Get-AppPanel $panels[2])
  # PuTTY can also resize itself after applying settings, with clean frame styles.
  [void][LiveNative]::MoveWindow($h,0,0,800,400,$true)
  Pump 500;Assert-Frame (Get-AppPanel $panels[2])
 }
 foreach($index in @(0,1,2,0,2,1,0)){
  # SendKeys targets the desktop foreground window, not a background GUI thread's focus.
  $form.TopMost=$true;$form.Activate();[LiveNative]::ActivateTestWindow($form.Handle);Pump 100
  $p=$panels[$index];[void][SuperPutty.ctlPuttyPanel].GetMethod('Show',[Type[]]@()).Invoke($p,@());Pump 250
  $h=([SuperPutty.ctlPuttyPanel].GetProperty('AppPanel').GetValue($p,$null)).AppWindowHandle;$g=[LiveNative]::State($h)
  $style=[LiveNative]::GetWindowLong($h,-16)
  "tab=$index handle=$h active=$($g.active) focus=$($g.focus) foreground=$([LiveNative]::GetForegroundWindow()) style=$($style.ToString('X8'))"
  if(-not $InspectOnly -and ($g.focus -ne $h -or $g.active -ne $h)){throw 'PuTTY is not both active and focused'}
  if(-not $InspectOnly){
   Assert-Frame (Get-AppPanel $p)
   $marker="focus-test-$index"
   $foreground=[LiveNative]::GetForegroundWindow()
   if($foreground -ne $form.Handle -and $foreground -ne $h){throw 'Cannot establish test-window foreground; typed-input test is inconclusive'}
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
 if($ScreenshotPath){
  $capture=[Drawing.Bitmap]::new($form.Width,$form.Height)
  $graphics=[Drawing.Graphics]::FromImage($capture)
  try{$graphics.CopyFromScreen($form.Location,[Drawing.Point]::Empty,$form.Size);$capture.Save([IO.Path]::GetFullPath($ScreenshotPath))}
  finally{$graphics.Dispose();$capture.Dispose()}
 }
 if($InspectOnly){Pump 1000}else{'PASS: Three PuTTY sessions retain a 2px border, activation, keyboard focus, and exclusive input after resizing, minimize/restore, frame restoration, and tab switching.'}
}finally{
 foreach($p in $panels){[void][SuperPutty.ctlPuttyPanel].GetMethod('CloseWithoutConfirmation',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($p,@())};if($form){$form.Dispose()}
 foreach($c in $clients){$c.Dispose()};$listener.Stop()
 [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($profileKey,$false)
}









