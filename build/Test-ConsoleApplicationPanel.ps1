param(
    [ValidateSet("WinCMD", "PS", "Both")]
    [string]$Protocol = "Both",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot "bin\$Platform\$Configuration"
$applicationPath = Join-Path $outputDirectory "SuperPutty.exe"
if (-not (Test-Path -LiteralPath $applicationPath)) {
    throw "Build output not found: $applicationPath"
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Reflection.Assembly]::LoadFrom($applicationPath) | Out-Null

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class ConsolePanelTestNativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetParent(IntPtr window);
}
"@

function Test-ConsoleProtocol {
    param(
        [SuperPutty.Data.ConnectionProtocol]$ConnectionProtocol
    )

    $form = New-Object System.Windows.Forms.Form
    $panel = $null
    try {
        $form.Text = "SuperPuTTY console panel integration test"
        $form.ClientSize = New-Object System.Drawing.Size(800, 500)
        $form.ShowInTaskbar = $false
        $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
        $form.Location = New-Object System.Drawing.Point(-20000, -20000)

        $panel = New-Object SuperPutty.ConsoleApplicationPanel($ConnectionProtocol)
        $panel.Dock = [System.Windows.Forms.DockStyle]::Fill
        $panel.ApplicationName = [Environment]::ExpandEnvironmentVariables("%SystemRoot%\System32\conhost.exe")
        $panel.ApplicationWorkingDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if ($ConnectionProtocol -eq [SuperPutty.Data.ConnectionProtocol]::WINCMD) {
            $client = [Environment]::ExpandEnvironmentVariables("%SystemRoot%\System32\cmd.exe")
            $panel.ApplicationParameters = '"' + $client + '" /d /q'
        }
        else {
            $client = [Environment]::ExpandEnvironmentVariables("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
            $panel.ApplicationParameters = '"' + $client + '" -NoLogo'
        }

        $form.Controls.Add($panel)
        $form.Show()

        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $panel.ExternalProcessCaptured -and [DateTime]::UtcNow -lt $deadline) {
            [System.Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 25
        }

        if (-not $panel.ExternalProcessCaptured) {
            throw "$ConnectionProtocol did not capture a console window within $TimeoutSeconds seconds."
        }

        $capturedHandle = $panel.AppWindowHandle
        $actualParent = [ConsolePanelTestNativeMethods]::GetParent($capturedHandle)
        if ($actualParent -ne $panel.Handle) {
            throw "$ConnectionProtocol captured HWND $capturedHandle but its parent is $actualParent instead of panel $($panel.Handle)."
        }

        $panel.RefreshAppWindow()
        [System.Windows.Forms.Application]::DoEvents()
        Write-Host "PASS: $ConnectionProtocol captured and parented HWND $capturedHandle."
    }
    finally {
        if ($form -ne $null) {
            $form.Close()
            $form.Dispose()
            [System.Windows.Forms.Application]::DoEvents()
        }
    }
}

if ($Protocol -eq "WinCMD" -or $Protocol -eq "Both") {
    Test-ConsoleProtocol -ConnectionProtocol ([SuperPutty.Data.ConnectionProtocol]::WINCMD)
}
if ($Protocol -eq "PS" -or $Protocol -eq "Both") {
    Test-ConsoleProtocol -ConnectionProtocol ([SuperPutty.Data.ConnectionProtocol]::PS)
}
