# SuperPuTTY Community Edition branding and compatibility

SuperPuTTY Community Edition (SuperPuTTY CE) is an unofficial community-maintained fork by Chris Thornton, based on [Jim Radford's original SuperPuTTY](https://github.com/jimradford/superputty). It is not an official upstream release. The original [MIT License](../License.txt), copyright attribution, and [third-party notices](../THIRD-PARTY-NOTICES.txt) remain intact. Assembly metadata additionally credits Copyright © 2026 Chris Thornton for community modifications.

Public branding appears in the window title, tray tooltip, menus, About dialog, application file description, MSI product/ARP name, Start menu shortcut, release filenames, and pipeline artifacts. Compact menu labels use SuperPuTTY CE.

Compatibility identifiers deliberately remain unchanged:

- Executable and project: `SuperPutty.exe`, `SuperPutty`; existing C# namespaces, including `SuperPuTTY.Scripting`.
- `AssemblyProduct("SuperPuTTY")`: `PortableSettingsProvider.ApplicationName` uses `Application.ProductName` to select `SuperPuTTY.settings`. `AssemblyTitle` supplies the new display name. `AssemblyCompany` is not used by this settings provider.
- Preference discovery order: `%USERPROFILE%\SuperPuTTY.settings`, then the file beside the executable. An existing profile file takes precedence over a portable file.
- `SettingsFolderResolver`: explicit settings folder, existing Documents `SuperPuTTY` folder, and `%LOCALAPPDATA%\SuperPuTTY` fallback.
- `Sessions.XML`, `AutoRestoreLayout.XML`, `layouts`, serialized type names and session fields, command-line switches, mutex and IPC identifiers.
- `Software\Jim Radford\SuperPuTTY` registry paths, component identities, application installation directories, and the Start menu folder. Shortcut display names change inside the existing folder.

## Installer identity and upgrade boundary

Inspection of local tags `1.5.0.0`, `1.5.0.1-greyhair-atx.1`, `sp-1.6.0`, and `sp-1.7.5` found that upstream and prior community MSIs shared UpgradeCode `42567F59-2F27-4E5B-A900-9141DC2DD929`. Keeping an unrestricted MajorUpgrade on that code would remove upstream installations. Changing only the product name or WiX Package Id would not isolate them.

CE therefore uses permanent, separate UpgradeCodes:

| Scope | CE UpgradeCode | Existing installation directory |
| --- | --- | --- |
| Current user | `3E4059D9-E5A5-48DE-B787-FC88268C1B95` | `%LOCALAPPDATA%\Apps\SuperPuTTY` |
| All users | `98CB966B-60D4-46C3-AADA-1990464696B8` | 64-bit Program Files `SuperPuTTY` |

A bounded legacy Upgrade row removes known community versions **1.6.0 through 1.7.5 inclusive**, including the pre-branding 1.7.5 build, in the same installation context. Keep the legacy upper bound fixed for future CE releases. Ordinary higher-version CE upgrades use the corresponding scope's new code. The current-user and all-users package IDs, component GUID separation, elevation behavior, and directories remain intact. Switching scope requires a separate uninstall/install; it is not an automatic scope migration.

MSI compares only the first three product-version fields, so community **1.5.0.1** cannot be distinguished from upstream **1.5.0.0** by the Upgrade table. These and older versions are detected only and blocked with instructions to back up settings and uninstall manually. Unknown legacy versions above 1.7.5 are also blocked rather than removed. These boundaries use the known release history, not a cryptographic assertion of package provenance.

**Safe coexistence with upstream is not supported.** Legacy registry markers, preferences, layouts, mutex/IPC names, and potentially custom installation paths are shared. The same-context guard does not detect every cross-scope, other-user, or portable installation. Uninstall upstream before using CE and keep a settings backup; do not run both against one settings folder. No settings or registry migration is performed.

Windows Installer's [Upgrade table](https://learn.microsoft.com/en-us/windows/win32/msi/upgrade-table) defines version matching and detection-only flags; [major upgrades](https://learn.microsoft.com/en-us/windows/win32/msi/major-upgrades) require matching installation contexts. WiX's [UpgradeVersion](https://docs.firegiant.com/wix/schema/wxs/upgradeversion/) authors these rows.

## Release artifacts and validation

Signed release builds use `SuperPuTTY-CE-1.7.5-current-user-win-x64-signed.msi` and `SuperPuTTY-CE-1.7.5-all-users-win-x64-signed.msi`. Local unsigned test builds use the same prefix with `test-x64.msi`. Pipeline signing, verification, and publishing paths follow these names; `SuperPutty.exe` remains unchanged.

Existing `sp-1.7.5` downloads, hashes, signatures, screenshots, and scan reports predate CE branding. Rebuilt files need fresh signatures and validation before publication. Local checks are documented in [testing.md](testing.md). The release verifier inspects compiled MSI product/shortcut names, scope, upgrade identities and bridge bounds, license text, and extracted payload. Live install/upgrade/uninstall scenarios should also be exercised in disposable Windows environments before release, including both scopes, legacy 1.7.4 and 1.7.5 upgrades, a subsequent CE upgrade, and upstream 1.5 rejection.

## Branding implementation validation (September 12, 2026)

- Complete Release/x64 solution build: passed, 0 warnings and 0 errors.
- Both fixed-scope MSI builds: passed; outputs use the CE prefix and unsigned test suffix.
- NUnit isolated suite: 132 passed, 0 failed, 0 skipped; SSH NetworkTest cases excluded because they require a configured live server.
- Both compiled MSI verification/extraction checks: passed, including names, upgrade tables, scope, license, architecture, DLLs, and icons.
- Real application title, existing named-layout loading, and three shutdown scenarios: passed using isolated profiles.
- Command Prompt and PowerShell capture/parenting integration checks: passed.
- Whitespace check: passed. License.txt, License.rtf, InstallerLicense.rtf, About license resources, and THIRD-PARTY-NOTICES.txt are unchanged.

Live MSI upgrade/uninstall/coexistence scenarios were not executed on this workstation. The install matrix above remains required before publication. These local builds are unsigned; no release was published and existing release assets were not renamed.

## Changed-file inventory

- `.github/ISSUE_TEMPLATE/bug_report.md`
- `azure-pipelines.yml`
- `build/Test-ApplicationShutdown.ps1`
- `build/Test-ConsoleApplicationPanel.ps1`
- `build/Verify-CodeSignatures.ps1`
- `build/Verify-ReleaseArtifacts.ps1`
- `docs/branding.md`
- `docs/manual/getting-started.md`
- `docs/manual/installation.md`
- `docs/manual/README.md`
- `docs/manual/settings.md`
- `docs/manual/troubleshooting.md`
- `docs/releases/1.7.5.md`
- `docs/testing.md`
- `README.md`
- `ReleaseNotes.txt`
- `SuperPutty/AboutBox1.cs`
- `SuperPutty/AboutBox1.Designer.cs`
- `SuperPutty/dlgEditSession.cs`
- `SuperPutty/dlgFindPutty.Designer.cs`
- `SuperPutty/frmSuperPutty.cs`
- `SuperPutty/frmSuperPutty.Designer.cs`
- `SuperPutty/Properties/AssemblyInfo.cs`
- `SuperPutty/Scripting/frmPrivatePrompt.cs`
- `SuperPutty/SessionDetail.cs`
- `SuperPutty/SuperPuTTY.cs`
- `SuperPutty/Utils/SettingsFolderResolver.cs`
- `SuperPuttyInstaller/Product.wxs`
- `SuperPuttyInstaller/SuperPuttyInstaller.wixproj`
- `SuperPuttyUnitTests/AboutBoxTests.cs`
- `SuperPuttyUnitTests/BrandingCompatibilityTests.cs`
- `SuperPuttyUnitTests/Fixtures/LegacyLayout.xml`
