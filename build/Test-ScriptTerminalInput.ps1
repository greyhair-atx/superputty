param(
    [string]$PuttyPath = 'C:\Program Files\PuTTY\putty.exe',
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64'
)

# Exercise real PuTTY input against a loopback TCP listener, with an isolated temporary session.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = Join-Path $repositoryRoot "bin\$Platform\$Configuration\SuperPutty.exe"
[void][Reflection.Assembly]::LoadFrom($applicationPath)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class TerminalReviewWindow {
 delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder name,int length);
 public static IntPtr Find(int pid){IntPtr result=IntPtr.Zero; EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==pid){var name=new StringBuilder(128);GetClassName(h,name,name.Capacity);if(name.ToString()=="PuTTY"){result=h;return false;}}return true;},IntPtr.Zero);return result;}
}
"@
$name = 'SuperPuttyInputTest-' + [guid]::NewGuid().ToString('N')
$key = 'HKCU:\Software\SimonTatham\PuTTY\Sessions\' + $name
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$process = $null
$client = $null
try {
 $listener.Start()
 $port = $listener.LocalEndpoint.Port
 [void](New-Item -Path $key)
 New-ItemProperty -Path $key -Name HostName -Value '127.0.0.1' -PropertyType String | Out-Null
 New-ItemProperty -Path $key -Name Protocol -Value 'raw' -PropertyType String | Out-Null
 foreach ($setting in @{ PortNumber=$port; LocalEcho=1; LocalEdit=1; CloseOnExit=2 }.GetEnumerator()) {
  New-ItemProperty -Path $key -Name $setting.Key -Value $setting.Value -PropertyType DWord | Out-Null
 }
 $accept = $listener.AcceptTcpClientAsync()
 $process = Start-Process $PuttyPath -ArgumentList @('-load',$name) -WindowStyle Hidden -PassThru
 if (-not $accept.Wait(10000)) {throw 'Local PuTTY connection timed out.'}
 $client = $accept.Result
 $window = [IntPtr]::Zero
 $watch = [Diagnostics.Stopwatch]::StartNew()
 while ($window -eq [IntPtr]::Zero -and $watch.ElapsedMilliseconds -lt 5000) {
  $window = [TerminalReviewWindow]::Find($process.Id)
  Start-Sleep -Milliseconds 25
 }
 if ($window -eq [IntPtr]::Zero) {throw 'PuTTY window was not found.'}
 foreach ($line in @('SENDKEY ^c','SENDKEY +c','SENDKEY %x','SENDKEY {ENTER 3}','SENDKEY {ENTER}','SENDCHAR x','SENDKEY {ENTER}')) {
  $command = $null
  [void][SuperPuTTY.Scripting.SPSL]::TryParseScriptLine($line,[ref]$command)
  $command.SendToTerminal($window)
 }
 $stream = $client.GetStream()
 $stream.ReadTimeout=2000
 $bytes = [Collections.Generic.List[byte]]::new()
 $buffer = [byte[]]::new(1024)
 do {
  $count = $stream.Read($buffer,0,$buffer.Length)
  for ($i=0; $i -lt $count; $i++) {$bytes.Add($buffer[$i])}
 } while ($bytes.Count -lt 10 -and $count -gt 0)
 $actual = ($bytes | ForEach-Object {$_.ToString('X2')}) -join ' '
 $expected = '03 43 1B 78 0D 0D 0D 0D 78 0D'
 if ($actual -ne $expected) {throw "Unexpected PuTTY input: $actual (expected $expected)"}
 'PASS: PuTTY received Ctrl+C, Shift+C, Alt+x, repeated Enter, and following text in order.'
}
finally {
 if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit(2000) | Out-Null }
 if ($client) {$client.Dispose()}
 $listener.Stop()
 if (Test-Path -LiteralPath $key) {Remove-Item -LiteralPath $key}
}
