# Testing SuperPuTTY Community Edition

This guide describes the checks used for the current 64-bit .NET Framework 4.8 codebase. It replaces older release-specific test notes and unfinished migration plans.

For the 1.8.0 release, see [release preparation](RELEASE-PREPARATION.md) and the [local validation report](LAUNCH-PREPARATION-REPORT.md). The release bundle adds exact-byte payload checks, a no-install ZIP, SHA-256 checksums and a CycloneDX runtime-file inventory. WinGet files and the Pages site remain local staging.

The published **1.7.5** build passed 127 isolated tests and the installer, signing, shutdown, and console capture checks. See the [1.7.5 release validation and VirusTotal summary](releases/1.7.5.md) for recorded results. The commands below describe how to run checks on your own build.

## Prerequisites

- Windows 11 x64 for the validated release test environment. Windows 10 x64 testing is deferred with disclosed limitations for 1.8.0; expected compatibility is not a passing test result.
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

The isolated suite covers TigerVNC argument generation, renamed executable recognition, early process exits, and desktop discovery across login and reconnect transitions using local test windows. It also covers active-tab close confirmation and restart-shortcut configuration. These checks do not establish a live VNC connection; run the manual checks below to verify authentication and embedding with the installed viewer.

Regression coverage also exercises automatic preference-backup recovery and preservation of damaged originals, owner-thread window-hook disposal, pointer-sized native messaging, escaped-password log redaction, bounded PSCP output and carriage-return progress, cancellable session loads, stale-load rejection, and timer-based focus retries. The SCP overflow tests use local fake processes. Interactive console cases still require an interactive Windows desktop.

`SharedFileRaceTests` covers stale preference/session saves, simultaneous recovery, a valid file appearing during backup validation, readers held across replacement, transient and persistent sharing failures, and missing-file creation conflicts. Local helper processes verify that file locks coordinate across processes and MSTSC profiles use different paths in different processes. Window-frame regressions verify that an unrelated or reparented window is not styled, resized, or closed.

`HostedCloseLifecycleTests` moves a live local client through document, side-docked, floating, and document positions. It forces both host and tab handle recreation and checks the original process/window, native parent, client bounds, activation target, and final graceful shutdown. This does not establish keyboard focus behavior with real PuTTY or Windows foreground restrictions; include floating-window input and Alt+Tab in interactive smoke testing.

Recreation failure tests cover failed detach, failed restore, and disposal after interrupted host initialization. Console ownership tests preserve a local native fixture across recreation for both console protocols. The console integration script also recreates real CMD and PowerShell hosts three times. RDP recreation tests preserve the ActiveX window and pending-connection state without contacting a server; live RDP connection continuity still requires an interactive endpoint check.

Catalog and Options regressions cover failed preference saves, folder changes, cancellation during a blocked catalog read, first-run catalog acceptance, import revision isolation, and deletion rollback. Layout tests cover bounded snapshots, paired deletion, concurrent saves, and primary/backup rename collisions. IPC tests exercise real pipes, stalled clients, UI admission, and forwarding from the actual secondary executable. SCP tests verify batched framing, quiet-output flushing, and disconnected-pipe errors.

Session admission is capped at 200 open terminal/SCP tabs per application window, including docked and floating sessions. Boundary tests cover shared capacity, reuse after closing a tab, and layout preflight at 200/201 sessions. Overflow and selection stress tests exercise 200 tabs.

`TabCloseGestureTests` checks that the desktop cursor can actually move and reports skipped cases when desktop input is unavailable. The `RegistryIntegrationTest` category similarly reports a skip when its temporary HKCU key cannot be created. Options tests use a temporary settings folder. Skips indicate missing integration coverage; run the gesture and registry cases in a suitable Windows session before release. The 200-tab stress cases can take over a minute as a group; use `--logger:"console;verbosity=normal"` to see progress before treating a quiet run as stalled.

Branding regressions cover the rendered About dialog, lineage and MIT attribution, executable and assembly identity, legacy profile/portable preferences, and existing session XML. The shutdown script also checks the real main-window title and loads the fixed `Fixtures/LegacyLayout.xml` through the application's named-layout path. This fixture uses the retained DockPanel 1.0 format and `SuperPutty.SessionTreeview` identifier.

The Windows integration scripts require an interactive desktop and an existing Release build:

```powershell
.\build\Test-ConsoleApplicationPanel.ps1
.\build\Test-ApplicationShutdown.ps1
```

`Test-ConsoleApplicationPanel.ps1` launches Win CMD and Windows PowerShell and confirms that each console window is captured and parented to its SuperPuTTY panel. `Test-ApplicationShutdown.ps1` verifies title-bar and **File > Exit** shutdown behavior with confirmation both enabled and disabled.

## Installer and release checks

Follow the [release preparation guide](RELEASE-PREPARATION.md) to build and verify the signed Inno setup and portable ZIP. Run fresh-install and upgrade checks in both scopes on disposable Windows test accounts. Reserve the WiX project and MSI scripts for historical installer and migration checks.

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
12. Install and uninstall the Inno setup in both scopes on clean test profiles. Confirm scope, shortcuts, manual link, license page, and third-party notices.
13. Select a versioned TigerVNC executable and connect using a non-default port. Confirm the log uses `host::port`, complete authentication, and verify the remote desktop replaces the login dialog inside the tab. Test resizing, switching tabs, and reconnecting; confirm an options dialog does not replace the desktop.
14. Close an active tab using its X, context menu, and assigned **CloseTab** shortcut. Verify **Cancel** preserves it and **OK** closes it. Verify a session that exits on its own closes without prompting and an approved bulk close does not prompt per tab.
15. Assign **RestartSession** under **Tools > Options > Shortcuts**, restart SuperPuTTY, and verify the binding is retained. Invoke it on a disconnected PuTTY session that remains open and confirm it reconnects. Verify it does not send PuTTY commands to VNC, RDP, MinTTY, or local shell tabs.
16. Switch repeatedly between terminal tabs and application controls to check focus with the default three attempts at 40 ms intervals.

## Network tests

Tests marked `NetworkTest` exercise real PSCP listings and transfers. Run them from an isolated Windows test account with a disposable SSH service that accepts password authentication. Set these process environment variables before starting the test runner:

| Variable | Value |
| --- | --- |
| `SUPERPUTTY_TEST_PSCP` | Full path to the PSCP executable |
| `SUPERPUTTY_TEST_SSH_HOST` | DNS name or address with its verified host key cached for this Windows account |
| `SUPERPUTTY_TEST_SSH_UNTRUSTED_HOST` | Second name or address for the test server with no host key cached |
| `SUPERPUTTY_TEST_SSH_USER` and `SUPERPUTTY_TEST_SSH_PASSWORD` | Credentials for the disposable account |
| `SUPERPUTTY_TEST_SSH_PORT` | SSH port, defaults to 22 |

Disable Pageant or other key authentication for the disposable account so the incorrect-password cases exercise password rejection. Place a fixture file in the account's home directory for the listing test. The upload test writes `superputty-network-test.txt` in that directory. Supply the password through the test runner's secret environment and execute the category explicitly:

```powershell
dotnet vstest .\SuperPuttyUnitTests\bin\x64\Release\SuperPuttyUnitTests.exe --TestCaseFilter:"TestCategory=NetworkTest"
```

Record the commit, Windows version, PuTTY/PSCP versions, commands, failures, and generated artifact names with release results.
