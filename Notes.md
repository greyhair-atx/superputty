# Development Notes

This file records build, test, and implementation details that are useful when working on this repository.

## CSV session import work

- Created branch `feature/csv-session-import` from the fork's `master` branch.
- Added **File > Import Sessions > From CSV File**.
- Added a CSV parser based on `Microsoft.VisualBasic.FileIO.TextFieldParser`, which is already available to the .NET Framework 4.8 project.
- Added support for `#` comment lines, quoted CSV fields, case-insensitive headers, protocol and port defaults, nested folders, and optional session properties.
- Required each row to contain `SessionName` and either `Host` or an explicitly supplied `PuttySession`.
- Added whole-file validation for headers, field counts, required values, duplicate session paths, protocols, ports, and malformed CSV records.
- Invalid files return no partial import list. The application shows all errors with physical file line numbers and does not modify `Sessions.XML`.
- Valid sessions are passed to `SuperPuTTY.ImportSessions`, which now rolls back in-memory additions if persistence fails.
- Session files are serialized to a same-directory temporary file, flushed to disk, and atomically moved or replaced so a failed save leaves the prior `Sessions.XML` intact.
- Added `Sessions.example.csv` with comments and three example sessions.
- Added `SessionCsvImporterTests` covering valid input, quoting/comments/defaults, physical line numbers, malformed rows, optional properties, duplicate paths, rollback, atomic replacement, and invalid headers.
- Updated SSH.NET to 2026.0.0 and log4net to 3.3.2.
- Generated installers and manual-test bundles belong in Gitea releases or CI artifacts; `/artifacts/` is intentionally ignored.

## Verification completed

- Built the `SuperPutty` and `SuperPuttyUnitTests` solution targets successfully with Visual Studio MSBuild.
- Built the complete Release solution, including the SDK-style WiX 6 installer, with Visual Studio 18 MSBuild.
- Generated `SuperPuttyInstaller\bin\x64\Release\SuperPuttySetup.msi` successfully.
- Verified both executable PE headers report x64 and the MSI Summary Information template reports `x64;1033`.
- Ran all 24 isolated NUnit 3 tests through 64-bit VSTest: 24 passed, 0 failed.
- Verified the x64 application exits with code 0 through title-bar close with confirmation enabled and disabled, and through File > Exit.
- Administratively extracted the MSI and verified its application payload is placed under `PFiles64` with all 47 theme icons.
- Verified the installer contains SSH.NET 2026.0.0 and all of its runtime dependency DLLs.
- Ran the ten CSV importer and persistence tests: 10 passed, 0 failed.
- NuGet vulnerability audit reports no vulnerable direct or transitive packages in either project.
- Parsed `Sessions.example.csv` with the built application: valid, 3 sessions.
- `git diff --check` passes.

## Requirements to run all tests

1. Install the .NET Framework 4.8 targeting/developer pack and Visual Studio or Build Tools with MSBuild.
2. Restore the repository packages before building.
3. Build through `SuperPutty.sln` using its x64 solution configuration. The application, test harness, and installer are 64-bit only.
4. Run the SDK-style `SuperPuttyUnitTests` project through 64-bit VSTest with `TestCategory!=NetworkTest` for the normal isolated suite. CI requires at least 24 discovered tests so a missing adapter cannot produce a false-green run.
5. Set `SuperPuTTY.ScpTests.PscpLocation` in `SuperPuttyUnitTests/app.config` to an existing `pscp.exe`.
6. Provide a disposable SSH/SCP test service matching the `UserName`, `Password`, `KnownHost`, and `UnKnownHost` values in `SuperPuttyUnitTests/app.config`. The known-host address must have its PuTTY host key cached; the unknown-host address must not be cached. The account must be able to list its home directory and accept a test file upload.
7. Expect the eight `NetworkTest` SCP fixtures to make real localhost network connections, exercise bad-password and unknown-host behavior, and write temporary files. They are not isolated unit tests and are intentionally excluded from normal CI.

## Complete solution and installer build

- `SuperPuttyInstaller` uses `WixToolset.Sdk` 6.0.2 and restores its UI and Util extensions through NuGet. A separately installed WiX toolset is not required.
- The application remains a non-SDK .NET Framework 4.8 project and all supported outputs target x64.
- The unit-test project is SDK-style, targets .NET Framework 4.8 and x64, and uses NUnit 3 with a repository-local adapter supplied through PackageReference.
- The solution intentionally exposes only `Debug|x64` and `Release|x64`; x86 and AnyCPU builds are unsupported.

From PowerShell, restore and build the complete Release solution with:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    .\SuperPutty.sln `
    /restore `
    /t:Build `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /m `
    /v:minimal `
    /nologo
```

The successful release build produces:

- `bin\x64\Release\SuperPutty.exe`
- `SuperPuttyUnitTests\bin\x64\Release\SuperPuttyUnitTests.exe`
- `SuperPuttyInstaller\bin\x64\Release\SuperPutty-1.7.3-current-user-win-x64-signed.msi`
- `SuperPuttyInstaller\bin\x64\Release\SuperPutty-1.7.3-all-users-win-x64-signed.msi`

The WiX 6 installers use product version 1.7.3 while preserving the original upgrade code, license UI, shortcuts, themes, and post-install launch option. The fixed-scope current-user package installs under Local AppData without elevation. The fixed-scope all-users package installs under native 64-bit Program Files and requests elevation. ICE validation completes without warnings.

The complete Release build currently succeeds with zero warnings and zero errors.

## Version 1.7.3 changes

- Added a custom DockPanelSuite VS2015 document-tab renderer while retaining the packaged docking library and existing docking behavior.
- Active tabs have a dark-gray background with white text and outline; inactive tabs have a light-gray background with black text and a middle-gray outline; hovered tabs have a middle-gray background with black text and a white outline.
- Added a focused theme test and retained the upstream DockPanelSuite MIT license with the derived renderer source.
- The executable version is `1.7.3.0`; both MSIs use product version `1.7.3`.

## Version 1.7.2 changes

- Replaced the unreliable 1.7.1 dual-scope installer with separate fixed-scope current-user and all-users MSIs.
- The current-user MSI installs under `%LocalAppData%\Apps\SuperPuTTY` without elevation; the all-users MSI installs under native x64 Program Files and requests elevation.
- Replaced the Word-generated installer RTF with a compact Windows Installer-compatible license document and added a compiled-MSI license-content check.
- Retracted the defective 1.7.1 binary release while retaining its tag and warning page for traceability.
- The executable version is `1.7.2.0`; both MSIs use product version `1.7.2`.

## Version 1.7.1 changes

- Profiles without an explicit settings path reuse an existing writable `Documents\SuperPuTTY` folder. When no legacy folder exists, sessions and layouts default to `%LocalAppData%\SuperPuTTY`, avoiding new dependencies on OneDrive redirection and protected-folder access.
- If an existing configured settings folder is not writable, startup selects the Local AppData fallback and copies any readable sessions, auto-restore layout, and named layouts without overwriting existing fallback files.
- The options dialog verifies that a selected settings directory is writable, and a late auto-restore write failure is logged without surfacing an exception while the application is closing.
- The installer supports current-user and all-users scopes. Current user is the default and installs below `%LocalAppData%\Apps`; all users retains the native x64 Program Files location.
- The executable version is `1.7.1.0`; the MSI uses product version `1.7.1`.

## Version 1.7.0 changes

- Credentials are redacted from logs, and PSCP/VNC plaintext password arguments require an explicit compatibility setting.
- FreeRDP certificate bypass is now an explicit per-session option; certificate validation is the default.
- Remote SPSL and session collections require credential-free HTTPS and enforce download, timeout, redirect, recursion, and circular-reference safeguards.
- Session-collection expansion, transfer cancellation, window tracking, and RDP argument construction were corrected.
- The update checker now validates HTTPS responses, bounds downloads, disposes network resources deterministically, and marshals results safely to the UI thread.
- Obsolete patch files, the SSH.NET prototype and dependency tree, and other unreachable legacy components were removed.
- The executable version is `1.7.0.0`; the MSI uses the Windows Installer-compatible product version `1.7.0`.

## Version 1.6.1 changes

- RDP ActiveX sessions debounce tab-size changes and call `IMsRdpClient9.UpdateSessionDisplaySettings` with the available tab resolution. Width and height are normalized to protocol limits, and smart sizing remains enabled when dynamic resizing is unavailable.
- The update checker now exposes `Official upstream` (`jimradford/superputty`) and `Community fork` (`greyhair-atx/superputty`) channels in Options. Unknown saved channel values safely fall back to official upstream, and prefixed release tags such as `sp-1.6.1` are supported.
- The docking UI now uses DockPanelSuite's VS2015 Light theme, placing a close button directly on every document tab instead of at the far right of the tab bar.
- The VS2015 theme keeps the project on .NET Framework 4.8. Its tabs are rectangular; recreating the angled VS2005 tab shape while retaining per-tab close buttons would require a custom DockPanelSuite strip renderer.
- The executable version is `1.6.1.0`; the MSI uses the Windows Installer-compatible product version `1.6.1`.
- Added isolated tests for update-channel routing, fallback behavior, and official/community release-tag parsing.

## Things to do, maybe?

The following are possible future improvements, in no particular order:

1. **Secure credential storage.** Replace persisted PuTTY `-pw` arguments with optional Windows Credential Manager storage, encourage key-based authentication and Pageant, and retain plaintext command-line passwords only as a clearly marked compatibility option. Continue redacting credentials from logs.
2. **Per-session RDP controls.** Add settings for dynamic resolution versus scaling, clipboard redirection, desktop size, color depth, multi-monitor mode, audio, drives, printers, and RD Gateway. Do not force clipboard redirection on for security-sensitive sessions.
3. **Automated release publishing.** Extend CI so a version tag signs, submits, publishes, and independently verifies matching GitHub and Gitea release artifacts. The current release process performs these steps manually after all 80 isolated tests pass.
4. **Incremental project modernization.** Keep .NET Framework 4.8 for the near term because it remains serviced and compatible with the current WinForms, COM, and ActiveX integration. First modernize the project structure and isolate native interoperability; evaluate a modern .NET migration separately after equivalent application, installer, and RDP tests exist.

The `MsRdpClient*NotSafeForScripting` control is intentional for this managed desktop host. Microsoft documents the nonscriptable ActiveX control as the variant that exposes additional desktop-client functionality; its name alone is not a reason to replace it.

Relevant Microsoft documentation:

- [SmartScreen reputation for Windows app developers](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)
- [Credential Locker for Windows apps](https://learn.microsoft.com/windows/apps/develop/security/credential-locker)
- [IMsRdpClient9 interface](https://learn.microsoft.com/windows/win32/termserv/imsrdpclient9)
- [Using the Remote Desktop ActiveX control](https://learn.microsoft.com/windows/win32/termserv/using-remote-desktop-web-connection)
- [.NET Framework versions and dependencies](https://learn.microsoft.com/dotnet/framework/install/versions-and-dependencies)
