# Current-user registration investigation

October 31, 2026. This follows the [catalog upgrade review](CATALOG-REVIEW-1.8.3.md).

## Finding

The published current-user MSI installs in the correct Windows Installer context. Its HKLM uninstall entry is not sufficient evidence of a machine installation. The automated current-user WinGet upgrade failure matches the open upstream scope-detection bug [microsoft/winget-cli#3011](https://github.com/microsoft/winget-cli/issues/3011).

On the disposable Windows 11 guest with WinGet 1.29.380, two independent direct `msiexec` installations of the exact signed 1.8.3 current-user MSI produced:

| Check | Default MSI properties | Explicit per-user properties |
| --- | --- | --- |
| Calling account | Dedicated interactive standard account | Same |
| Additional properties | None | `ALLUSERS=2 MSIINSTALLPERUSER=1` |
| MSI AssignmentType | 0 (per-user) | 0 (per-user) |
| `MsiEnumProductsEx` | Exactly one user-unmanaged instance, context 2, with the calling user's SID | Same |
| Machine-context instance | None | None |
| Installed executable | `%LOCALAPPDATA%\Apps\SuperPuTTY\SuperPutty.exe` | Same |
| Uninstall registration | HKLM, 64-bit view | Same |
| Uninstall and settings preservation | Passed; no product contexts or ARP entries left; both settings files preserved | Same |

Direct installation bypasses WinGet entirely. The same outcome with explicit per-user properties rules out adding those switches to the manifest as a correction for this MSI.

The signed MSI's property table also agrees with the intended scope: the current-user package has no `ALLUSERS=1` and its Summary Information WordCount is 10 (compressed plus limited-user package flag). The machine package has `ALLUSERS=1` and WordCount 2. No custom ARP registration was found in the installer source.

## Why WinGet disagrees

WinGet's [ARP inventory](https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerRepositoryCore/Microsoft/ARPHelper.cpp) records installed scope from the registry hive it scans. Its [installer comparator](https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerCommonCore/Manifest/ManifestComparator.cpp) then requires candidate scope to match installed scope. This loses the distinction between a genuine machine-context MSI and a per-user MSI whose Windows Installer registration is visible in HKLM.

Issue #3011 describes this behavior and includes a report against WinGet 1.29.380 with one user-unmanaged MSI instance, no machine instance, and an HKLM uninstall entry. A WinGet maintainer identifies the registry-based scope detection as a client bug. Windows Installer's [`MsiEnumProductsEx`](https://learn.microsoft.com/en-us/windows/win32/api/msi/nf-msi-msienumproductsexw) API exposes installation context directly; this investigation uses that API rather than inferring it from ARP.

## Approval decision and next steps

- The MSI's per-user installation context is confirmed. No SuperPuTTY application-code change or blind change to `Scope`, `ALLUSERS`, ProductCode or UpgradeCode is justified by this finding.
- Automated current-user WinGet catalog upgrades remain unapproved on the tested client. Package correlation passes, but correct scope selection does not.
- A production resolution needs a WinGet client correction or a separately designed and tested packaging alternative. Editing or moving Windows Installer's private registry entries is not a supported correction.
- The explicit migration from 1.8.0 to 1.8.3 passed end to end in the standard account: uninstall by the old current-user ProductCode without a scope filter, then install the reviewed candidate with `--scope user`. Candidate scope/hash/signature/version, preservation of both existing settings files, and final uninstall all passed. This is a tested manual migration path, not an automatic catalog upgrade pass.
- Provide the repro and MSI-context evidence to the WinGet submission reviewers, referencing #3011, before claiming full two-scope upgrade support. No external comment has been posted by this investigation.

## Automatic upgrade without a scope filter

A further test used the same interactive standard account, WinGet 1.29.380, the exact published 1.8.0 current-user MSI, and the review REST source exposing only 1.8.3. The command was `winget upgrade --id ChrisThornton.SuperPuTTYCommunityEdition --exact --source SPCEReview --silent --disable-interactivity --accept-package-agreements --accept-source-agreements --verbose-logs` (with an additional MSI log path). It omitted `--scope` and supplied no installer-scope override.

The result does not preserve current-user scope:

1. WinGet correlated the package and downloaded `SuperPuTTY-CE-1.8.3-all-users-win-x64-signed.msi`.
2. It successfully uninstalled the existing 1.8.0 current-user package before starting the new installation.
3. The machine installer displayed an administrator credential prompt despite the noninteractive command options. The test canceled that prompt without supplying administrator credentials. WinGet then reported MSI error 1625 and exited with `-1978334961`.
4. After the failed upgrade, neither old nor new user/machine ProductCodes were installed, and neither executable path existed. Both existing settings files retained their original SHA-256 hashes.

The observed installer choice and removal of the old user package rule out omitting `--scope user` as a safe workaround. The final installation error followed deliberate cancellation of the elevation prompt; it is not independent evidence of a new package policy defect. No claim is made about a completed machine installation after providing administrator credentials, since that was not tested.

The runner now supports `-UpgradeMethod CatalogUnscoped` and captures both scopes' product states and executable paths before cleanup, including on failure. Evidence from this run is retained locally under `artifacts/automatic-user-upgrade/guest/`, including the raw summary, operation logs, client diagnostics, catalog requests, post-upgrade state and final cleanup verification. The raw summary's `uninstallFirstObserved` is null because the failed command threw before that field was populated; the operation log directly records the successful uninstall. The runner now records this field in its failure path as well.

Temporary source/local-manifest policies, HTTPS bindings, certificate trust and scheduled tasks were removed. Final checks confirmed no test installations remained and both settings files were unchanged. The standard review account remains signed in. Automatic current-user catalog upgrades remain unapproved; the separately tested explicit migration remains the available workaround.

## Reproduction

`build/Test-PerUserMsiRegistration.ps1` requires the authorized disposable guest, `SUPERPUTTY_DISPOSABLE_TEST=1`, and the dedicated standard account's interactive session. Place the exact published signed current-user MSI in a new stage directory and run the script with `-StageDirectory <directory>`. Use a separate stage with `-ExplicitPerUserProperties` for the second case. The script checks the published hash and signature, captures native 32-/64-bit HKCU/HKLM ARP views, checks the current user's MSI contexts, uninstalls only its owned ProductCode, and checks preference/session/layout hashes afterward.

Evidence is retained locally under `artifacts/registration-review/guest/`, including registration JSON, verbose install/uninstall logs, preservation baselines and cleanup summaries. Temporary scheduled tasks are removed; the review account remains available. Published MSI files, application source, release tags and the public submission were not changed.

The explicit migration runner is `build/Test-WinGetUpgrade.ps1 -UpgradeMethod ExplicitMigration -ResultName <unique-name>` with the usual staged metadata and manifests. The tested 1.8.0 current-user ProductCode is `{7093D12F-0585-4E0C-A625-D2BB3867C54F}`. Do not add `--scope user` to its uninstall command: that invokes the faulty installed-scope filter. ProductCodes for other versions must be read from their actual MSI packages.
