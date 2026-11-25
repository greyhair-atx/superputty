# Preparing a release locally

Use a Windows x64 developer shell with MSBuild, .NET Framework 4.8 developer tools, the .NET SDK, and PowerShell 7. See the [Inno release workflow](../packaging/inno/RELEASE.md) for the pinned toolchain and signing configuration.

## Build and run regressions

```powershell
MSBuild.exe .\SuperPuttyUnitTests\SuperPuttyUnitTests.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /v:minimal
dotnet vstest .\SuperPuttyUnitTests\bin\x64\Release\SuperPuttyUnitTests.exe --TestCaseFilter:"TestCategory!=NetworkTest"
powershell.exe -NoProfile -File .\build\Test-ApplicationShutdown.ps1
powershell.exe -NoProfile -File .\build\Test-ConsoleApplicationPanel.ps1
```

Run desktop and HKCU checks under an interactive Windows 11 account. Run live RDP sign-in, disconnect and reconnect, and SCP password and MFA checks using disposable accounts. Record the commit, OS, client versions, test counts, and any skipped scenarios. See [testing](testing.md) for prerequisites.

## Prepare and verify the signed candidate

Match the three-part release version in `azure-pipelines.yml` to the executable's four-part assembly and file versions. Commit the reviewed changes before building the signed candidate. The pipeline builds one signed x64 Inno setup for both install scopes and a portable ZIP containing the signed application. Enable code signing with the configured Azure service connection. For an unsigned local review, use `New-InnoReleaseBundle.ps1 -Version 1.8.5 -PortableOnly` with a fresh output directory.

Run the checks on the final signed files:

```powershell
$bundle = 'C:\review\win-x64-signed'
.\build\Verify-InnoReleaseBundle.ps1 -ReleaseDirectory $bundle -Version 1.8.5 -RequireCleanSource -RequireInstaller
.\build\Test-InnoReleaseTooling.ps1 -ReleaseDirectory $bundle -Version 1.8.5 -ResultDirectory 'C:\review\tooling-1.8.5'
.\build\New-InnoWinGetManifest.ps1 -ReleaseDirectory $bundle -Version 1.8.5 -ManifestDirectory 'C:\review\winget-1.8.5'
```

The bundle verifier checks the application's version, signatures, checksums, and the CycloneDX 1.6 runtime file inventory. The WinGet review manifests record the SHA-256 of the exact signed setup. Regenerate manifests and reverify the bundle after rebuilding or re-signing any artifact.

## Test both install scopes in a disposable Windows VM

Run the lifecycle script as a standard test account for current-user installs and an elevated test account for machine installs. Use the verified public Inno installer from the previous release to check upgrades in each scope:

```powershell
.\build\Test-InnoReleaseLifecycle.ps1 -ReleaseDirectory $bundle -Scope user -PreviousInnoSetup 'C:\review\SuperPuTTY-CE-1.8.4-setup-win-x64.exe' -ResultDirectory 'C:\review\user-upgrade' -LaunchTest
.\build\Test-InnoReleaseLifecycle.ps1 -ReleaseDirectory $bundle -Scope machine -PreviousInnoSetup 'C:\review\SuperPuTTY-CE-1.8.4-setup-win-x64.exe' -ResultDirectory 'C:\review\machine-upgrade' -LaunchTest
```

Verify fresh installs, preserved preferences and session data, interrupted upgrade recovery, and uninstall in each scope. The older MSI migration remains an explicit uninstall and install procedure. Use the [installer validation procedures](releases/1.8.4-TEST-PLAN.md) for recovery and legacy migration coverage.

Tag the exact commit identified in the verified build manifest after approval. Keep the published files immutable and compare the downloaded hashes from GitHub and Gitea with the approved bundle.

## Website preview

```powershell
node .\build\Preview-Site.cjs
# Open http://127.0.0.1:8080 ; Ctrl+C stops the local server.
node .\build\Test-LaunchSite.cjs
```

The browser test requires Playwright and installed Microsoft Edge. It checks desktop, mobile and dark-mode rendering, images, horizontal overflow and unexpected network requests. It retains screenshots under ignored `artifacts/launch-review`. Serve `docs/` with GitHub Pages only after approval. Before publication, replace the candidate banner with the actual published version and remove the prelaunch Issues warning only after confirming the setting.
