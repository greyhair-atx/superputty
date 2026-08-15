[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $repoRoot "bin\$Platform\$Configuration\SuperPutty.exe"
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) { throw "Missing application: $appPath" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SuperPuttyShutdownNative {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr hDlg, int id);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
'@

function Wait-ForMainWindow {
    param([Diagnostics.Process] $Process, [int] $TimeoutSeconds = 20)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 200
        $Process.Refresh()
    } until ($Process.HasExited -or $Process.MainWindowHandle -ne [IntPtr]::Zero -or (Get-Date) -gt $deadline)
    if ($Process.HasExited) { throw "SuperPutty exited early with code $($Process.ExitCode)." }
    if ($Process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'SuperPutty did not create its main window.' }
    return $Process.MainWindowHandle
}

function Find-ProcessWindow {
    param([int] $ProcessId, [string] $Title)
    $matches = [Collections.Generic.List[IntPtr]]::new()
    [SuperPuttyShutdownNative]::EnumWindows({
        param($window, $unused)
        $owner = [uint32]0
        [SuperPuttyShutdownNative]::GetWindowThreadProcessId($window, [ref]$owner) | Out-Null
        if ($owner -eq $ProcessId) {
            $text = [Text.StringBuilder]::new(256)
            [SuperPuttyShutdownNative]::GetWindowText($window, $text, $text.Capacity) | Out-Null
            if ($text.ToString() -eq $Title) { $matches.Add($window) }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    if ($matches.Count -gt 0) { return $matches[0] }
    return [IntPtr]::Zero
}

function Confirm-ExitDialog {
    param([Diagnostics.Process] $Process)
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $dialog = Find-ProcessWindow $Process.Id 'Confirm Exit'
    } until ($dialog -ne [IntPtr]::Zero -or $Process.HasExited -or (Get-Date) -gt $deadline)
    if ($dialog -eq [IntPtr]::Zero) { throw 'The expected Confirm Exit dialog did not appear.' }
    $okButton = [SuperPuttyShutdownNative]::GetDlgItem($dialog, 1)
    if ($okButton -eq [IntPtr]::Zero) { throw 'The Confirm Exit dialog has no OK button.' }
    [SuperPuttyShutdownNative]::PostMessage($okButton, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
}

function Invoke-FileExit {
    param([Diagnostics.Process] $Process, [IntPtr] $MainWindow)
    $root = [Windows.Automation.AutomationElement]::FromHandle($MainWindow)
    $fileCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty,
        'File')
    $fileItem = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $fileCondition)
    if ($null -eq $fileItem) { throw 'UI Automation could not find the File menu.' }
    $fileItem.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()

    $exitCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty,
        'Exit')
    $exitItem = $fileItem.FindFirst([Windows.Automation.TreeScope]::Descendants, $exitCondition)
    if ($null -eq $exitItem) { throw 'UI Automation could not find File > Exit.' }
    $exitItem.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function New-IsolatedProfile {
    param([bool] $ExitConfirmation)
    $profile = Join-Path ([IO.Path]::GetTempPath()) ('SuperPutty-Shutdown-' + [Guid]::NewGuid().ToString('N'))
    $settingsDirectory = Join-Path $profile 'settings'
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $puttyPath = Join-Path $profile 'putty.exe'
    New-Item -ItemType File -Path $puttyPath | Out-Null
    $escapedPuttyPath = [Security.SecurityElement]::Escape($puttyPath)
    $escapedSettingsDirectory = [Security.SecurityElement]::Escape($settingsDirectory)
    $confirmationText = if ($ExitConfirmation) { 'True' } else { 'False' }
    $settings = @"
<?xml version="1.0" encoding="utf-8"?>
<Settings>
  <PuttyExe>$escapedPuttyPath</PuttyExe>
  <SettingsFolder>$escapedSettingsDirectory</SettingsFolder>
  <ExitConfirmation>$confirmationText</ExitConfirmation>
  <RestoreWindowLocation>False</RestoreWindowLocation>
  <SingleInstanceMode>False</SingleInstanceMode>
  <DefaultLayoutName></DefaultLayoutName>
</Settings>
"@
    [IO.File]::WriteAllText((Join-Path $profile 'SuperPuTTY.settings'), $settings)
    return $profile
}

function Invoke-ShutdownScenario {
    param(
        [string] $Name,
        [bool] $ExitConfirmation,
        [ValidateSet('TitleBar', 'FileMenu')] [string] $Action
    )
    $profile = New-IsolatedProfile $ExitConfirmation
    $startInfo = [Diagnostics.ProcessStartInfo]::new($appPath)
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = Split-Path -Parent $appPath
    $startInfo.EnvironmentVariables['USERPROFILE'] = $profile
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $mainWindow = Wait-ForMainWindow $process
        if ($Action -eq 'TitleBar') {
            [SuperPuttyShutdownNative]::PostMessage($mainWindow, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        }
        else {
            Invoke-FileExit $process $mainWindow
        }
        if ($ExitConfirmation) { Confirm-ExitDialog $process }
        if (-not $process.WaitForExit(15000)) { throw "$Name did not exit within 15 seconds." }
        if ($process.ExitCode -ne 0) { throw "$Name exited with code $($process.ExitCode)." }
        Write-Host "Shutdown test passed: $Name"
    }
    finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        if (Test-Path -LiteralPath $profile) { Remove-Item -LiteralPath $profile -Recurse -Force }
    }
}

Invoke-ShutdownScenario 'title bar without confirmation' $false 'TitleBar'
Invoke-ShutdownScenario 'title bar with confirmation' $true 'TitleBar'
Invoke-ShutdownScenario 'File > Exit without confirmation' $false 'FileMenu'
Write-Host 'All application shutdown tests passed.'
