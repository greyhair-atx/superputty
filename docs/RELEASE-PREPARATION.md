# Preparing a release locally

These commands prepare review artifacts. They do not publish, tag, push, deploy Pages, upload to scanners, or submit packages. See [the launch checklist](PUBLIC-LAUNCH-CHECKLIST.md) for approval gates.

Use a Windows x64 developer shell with Visual Studio/MSBuild, .NET Framework 4.8 developer tools, the .NET SDK and restored WiX packages. Start with the complete solution:

```powershell
MSBuild.exe .\SuperPutty.sln /restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /v:minimal
dotnet vstest .\SuperPuttyUnitTests\bin\x64\Release\SuperPuttyUnitTests.exe --TestCaseFilter:"TestCategory!=NetworkTest"
powershell.exe -NoProfile -File .\build\Test-ApplicationShutdown.ps1
powershell.exe -NoProfile -File .\build\Test-ConsoleApplicationPanel.ps1
```

For an unsigned review build, package both scopes with `InstallerArtifactSuffix=test-x64`, then run `New-ReleaseBundle.ps1` without `-RequireSignatures`. Unsigned artifacts must never use signed names.

For the signed candidate, use the existing [Artifact Signing setup](../build/ArtifactSigningMetadata.json) and credential configuration. The metadata contains service identifiers, not a private key. Sign `bin\x64\Release\SuperPutty.exe` with SHA-256 and an RFC3161 SHA-256 timestamp **before** creating the final MSIs. The Azure pipeline implements this ordering. Do not rebuild project references after signing the executable:

```powershell
MSBuild.exe .\SuperPuttyInstaller\SuperPuttyInstaller.wixproj /restore /t:Build /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /p:InstallerScope=PerUser /p:InstallerProductVersion=1.8.0 /p:InstallerArtifactSuffix=win-x64-signed
MSBuild.exe .\SuperPuttyInstaller\SuperPuttyInstaller.wixproj /restore /t:Build /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /p:InstallerScope=PerMachine /p:InstallerProductVersion=1.8.0 /p:InstallerArtifactSuffix=win-x64-signed
```

Sign both resulting MSIs with the same signing service and timestamp settings. Then:

```powershell
pwsh -NoProfile -File .\build\Verify-CodeSignatures.ps1 -InstallerVersion 1.8.0 -InstallerArtifactSuffix win-x64-signed
pwsh -NoProfile -File .\build\New-ReleaseBundle.ps1 -Version 1.8.0 -InstallerArtifactSuffix win-x64-signed -RequireSignatures
pwsh -NoProfile -File .\build\New-WinGetManifest.ps1 -Version 1.8.0
winget validate .\packaging\winget\ChrisThornton.SuperPuTTYCommunityEdition\1.8.0
```

`New-ReleaseBundle.ps1` checks both MSIs, x64 PE/version/branding, scope and upgrade rows, original license bytes, exact executable bytes, runtime files and 47 icons. It performs unattended **administrative extraction** (`msiexec /a /qn`), which is not a full installation test. Signing verification requires `Valid`, signer `Christopher Thornton`, and a timestamp certificate. Missing inputs fail the bundle. The ZIP uses an explicit allowlist and excludes user settings and PDBs.

The CycloneDX 1.6 SBOM is an exact runtime **file inventory**, with SHA-256 values for all ZIP payload files. It does not pretend to resolve external clients, Windows, .NET Framework or every transitive build dependency. [Third-party notices](../THIRD-PARTY-NOTICES.txt), `packages.config` and project PackageReferences document the separate dependency inventory. Validate the SBOM against the [official 1.6 schema](https://cyclonedx.org/schema/bom-1.6.schema.json).

The output is `artifacts/release-1.8.0/win-x64-signed`. Review its six top-level assets for release. Temporary payload directories beside the output are local staging, not additional release assets. The checksum file lists five assets: two MSIs, ZIP, SBOM and build manifest. It cannot hash itself.

The build manifest identifies the base commit and whether the source was dirty. **A dirty-tree candidate is not the final release.** After approval, commit the reviewed changes, rebuild/sign from that commit, regenerate checksums and WinGet manifests, then create the release tag on exactly that commit. Do not rebuild or re-sign an MSI after its WinGet hash/ProductCode has been frozen without regenerating all dependent files. Keep published 1.7.x assets immutable.

## Disposable installer tests

Never run lifecycle tests against a developer's installed copy. In an elevated, disposable Windows VM or hosted agent with no SuperPuTTY installations or preferences:

```powershell
$env:SUPERPUTTY_DISPOSABLE_TEST = '1'
pwsh -NoProfile -File .\build\Test-InstallerLifecycle.ps1 -ReleaseDirectory C:\review\win-x64-signed
```

The script verifies hashes, `/qn /norestart` installation and exact ProductCode uninstallation in both scopes, product display names and preservation of a legacy preferences sentinel. It refuses existing installs/settings. It does not simulate live session authentication, a non-admin token, every supported OS, upgrades or cross-scope conflicts. Run the manual matrix in [branding.md](branding.md), including upgrades from 1.7.6, the legacy bridge, blocked upstream 1.5, and a real non-administrator current-user install. Azure's `testInstallerLifecycle` parameter is opt-in and must only be used on disposable agents.

## Website preview

```powershell
node .\build\Preview-Site.cjs
# Open http://127.0.0.1:8080 ; Ctrl+C stops the local server.
node .\build\Test-LaunchSite.cjs
```

The browser test requires Playwright and installed Microsoft Edge. It checks desktop, mobile and dark-mode rendering, images, horizontal overflow and unexpected network requests. It retains screenshots under ignored `artifacts/launch-review`. Serve `docs/` with GitHub Pages only after approval. Before publication, replace the candidate banner with the actual published version and remove the prelaunch Issues warning only after confirming the setting.
