# Testing SuperPuTTY

This guide describes the checks used for the current 64-bit .NET Framework 4.8 codebase. It replaces older release-specific test notes and unfinished migration plans.

## Prerequisites

- 64-bit Windows 10 or Windows 11
- Visual Studio or Build Tools with MSBuild and the .NET Framework 4.8 developer pack
- The .NET SDK, used to run the NUnit test executable with `dotnet vstest`
- 64-bit PuTTY and PSCP for manual connection tests
- WiX dependencies restored by NuGet when installer validation is required

Run commands from a Visual Studio Developer PowerShell prompt at the repository root so `MSBuild.exe` is available on `PATH`.

## Build

```powershell
MSBuild.exe .\SuperPutty.sln /restore /t:Build /p:Configuration=Release /p:Platform=x64 /m /v:minimal
```

The application is written to `bin\x64\Release\SuperPutty.exe`. The test assembly is written under `SuperPuttyUnitTests\bin\x64\Release`.

## Automated tests

Run the isolated suite, which excludes tests requiring a real SSH server:

```powershell
dotnet vstest .\SuperPuttyUnitTests\bin\x64\Release\SuperPuttyUnitTests.exe --TestCaseFilter:"TestCategory!=NetworkTest"
```

The Windows integration scripts require an interactive desktop and an existing Release build:

```powershell
.\build\Test-ConsoleApplicationPanel.ps1
.\build\Test-ApplicationShutdown.ps1
```

`Test-ConsoleApplicationPanel.ps1` launches Win CMD and Windows PowerShell and confirms that each console window is captured and parented to its SuperPuTTY panel. `Test-ApplicationShutdown.ps1` verifies title-bar and **File > Exit** shutdown behavior with confirmation both enabled and disabled.

## Installer and release checks

Build both fixed-scope test MSIs after building the application:

```powershell
MSBuild.exe .\SuperPuttyInstaller\SuperPuttyInstaller.wixproj /restore /t:Build /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /p:InstallerScope=PerUser
MSBuild.exe .\SuperPuttyInstaller\SuperPuttyInstaller.wixproj /restore /t:Build /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /p:InstallerScope=PerMachine
```

Then validate their architecture, version, runtime files, license UI, installation scope, and extracted payload:

```powershell
.\build\Verify-ReleaseArtifacts.ps1 -InstallerScope PerUser
.\build\Verify-ReleaseArtifacts.ps1 -InstallerScope PerMachine
```

The verifier defaults to the current test artifact names. Use `-ExpectedVersion` and `-MsiName` when checking another version or filename.

For signed release artifacts, run:

```powershell
.\build\Verify-CodeSignatures.ps1
```

This checks the application and both installer scopes for a valid expected signer and timestamp. Override the script parameters when validating a different release version, artifact suffix, or authorized signer.

## Manual smoke test

Use a disposable account and non-production endpoints where possible.

1. Start with an isolated or backed-up settings directory.
2. Confirm first-run discovery and editing of the PuTTY, PSCP, VNC, RDP, MinTTY, WinSCP, and FileZilla program paths.
3. Create, edit, move, rename, import, export, and delete saved sessions.
4. Open a quick connection and verify **File > Save Current Session** creates it; repeat from a saved session and verify edits are applied only after confirmation.
5. Open SSH, Telnet, Rlogin, Raw, and Serial sessions appropriate to the test environment.
6. Open Win CMD and Windows PowerShell sessions; verify focus, resizing, multiple tabs, closing, and application shutdown.
7. Open an RDP session with the Microsoft client. If testing FreeRDP, verify certificate validation is on unless the session's insecure override is deliberately enabled.
8. Test an SCP saved session with a `.ppk` key or Pageant, then with a password prompt. Verify listing, upload, download, cancellation, and verbose `-v` logging.
9. Verify **Tools > Open Log File Location** opens the active log directory and that credentials are absent from the log.
10. Save and reload a named layout. Restart with `<Auto Restore>` and confirm the window arrangement returns without reopening the previously active connections.
11. Import `Sessions.example.csv` and confirm fields including `PrivateKeyFile` are preserved.
12. Install and uninstall both current-user and all-users MSI packages on clean test profiles. Confirm the requested scope, shortcuts, manual link, license page, and third-party notices.

## Network tests

Tests marked `NetworkTest` exercise real PSCP listings and transfers. They are intentionally excluded from normal CI and local runs because they need host, account, credential, and executable settings in the test configuration.

Run them only against a disposable SSH service after reviewing `SuperPuttyUnitTests\app.config`. Never commit live credentials or private-key material. Execute the category explicitly when the environment is ready:

```powershell
dotnet vstest .\SuperPuttyUnitTests\bin\x64\Release\SuperPuttyUnitTests.exe --TestCaseFilter:"TestCategory=NetworkTest"
```

Record the commit, Windows version, PuTTY/PSCP versions, commands, failures, and generated artifact names with release results.
