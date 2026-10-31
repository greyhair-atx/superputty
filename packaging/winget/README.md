# WinGet submission

## Latest submission: 1.8.3

The existing [PR #434081](https://github.com/microsoft/winget-pkgs/pull/434081) now submits **1.8.3**. Its three manifests are under `ChrisThornton.SuperPuTTYCommunityEdition/1.8.3/` and were generated from the exact signed current-user and all-users MSI files published at [sp-1.8.3](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.3). Public download hashes, timestamped Authenticode signatures, MSI identities and local `winget validate --manifest` checks passed. Schema 1.28.0, version-specific PrivacyUrl and omission of redundant DisplayVersion are retained. Both scopes have now passed clean installation/uninstallation and MSI upgrades from 1.8.0 in a disposable Windows 11 guest. Catalog tests pass package correlation and machine-scope uninstall-first upgrades; current-user catalog upgrades remain blocked by a scope mismatch. See the [catalog review](CATALOG-REVIEW-1.8.3.md) and [earlier lifecycle report](REVIEW-1.8.3.md). Microsoft validation/review and catalog publication remain pending.

### Local correction awaiting second review (October 31, 2026)

Follow-up catalog testing: [catalog upgrade results](CATALOG-REVIEW-1.8.3.md). Package correlation and machine-scope uninstall-first upgrades pass. Current-user upgrades with `--scope user` fail because WinGet inventories the existing current-user MSI under machine scope; that issue remains to be resolved before claiming a complete two-scope catalog upgrade pass.

The 1.8.3 installer manifest and `build/New-WinGetManifest.ps1` now set `MinimumOSVersion: 10.0.19041.0` and `UpgradeBehavior: uninstallPrevious`, as requested in the latest PR feedback, plus `ElevationRequirement: elevationRequired` on the machine-scope entry. This sets the WinGet eligibility floor to Windows 10 build 19041; it is not evidence that earlier Windows releases were tested or that the MSI enforces the same floor. The upgrade field requests WinGet to uninstall the previous package first; that workflow has not yet passed testing. These changes are local and have not been pushed to the submission branch.

Validation performed against the existing signed 1.8.3 release bundle:

- `winget validate --manifest` passed for the checked-in 1.8.3 manifests.
- The generator ran successfully under PowerShell 7 and Windows PowerShell 5.1, including its checks of both MSI hashes and Authenticode signatures. All three generated manifests matched the checked-in files after normalizing line endings, and both generated sets passed WinGet validation.
- Schema references and `ManifestVersion` remain 1.28.0 in all three manifests. The PrivacyUrl remains pinned to `sp-1.8.3`, and redundant DisplayVersion remains omitted. Installer URLs, hashes and MSI identities are unchanged.

The [1.8.3 review report](REVIEW-1.8.3.md) separates passing clean lifecycle and MSI upgrade tests from unresolved local-manifest upgrade matching and remaining launch/compatibility checks. Each upgrade test verifies removal of the old ProductCode, registration and scope of the new ProductCode, the candidate executable, and preservation of preferences, sessions and layouts through final uninstall. `build/Test-WinGetScopes.ps1` now consumes staged release metadata; `build/Test-WinGetUpgrade.ps1` explicitly distinguishes WinGet upgrade testing from MSI upgrade testing. Historical test evidence below is retained for context.

After second review and lifecycle verification, push the manifests to the existing `new-package/superputty-ce-1.8.0` branch of `greyhair-atx/winget-pkgs`. Inspect validation against the new head commit: the latest deprecated-schema warning conflicts with the submitted files already using 1.28.0. Record the actual test results and request moderator re-review.

The sections below retain the historical 1.8.0 validation evidence and environment-specific test setup; those lifecycle results are not a 1.8.3 pass.


`ChrisThornton.SuperPuTTYCommunityEdition/1.8.0/` contains a multi-file WinGet manifest generated from the actual signed MSI bundle. Both `Scope: user` and `Scope: machine` are explicitly represented under separate x64 installers. MSI ProductCodes and AppsAndFeatures UpgradeCodes are read from the finished packages, never guessed from the edition name. Publisher is Chris Thornton; certificate identity is Christopher Thornton.

```powershell
pwsh -NoProfile -File .\build\New-WinGetManifest.ps1 -Version 1.8.0 -ReleaseDirectory .\artifacts\release-1.8.0\final
winget validate .\packaging\winget\ChrisThornton.SuperPuTTYCommunityEdition\1.8.0
```

The generated schema version is 1.28.0, as requested in the WinGet review. `PrivacyUrl` links to the version-specific Security and Privacy page. `wix` is the MSI installer type; unattended switches are `/qn /norestart` and `/passive /norestart`. The direct HTTPS asset URLs point only to `greyhair-atx/superputty` and the published `sp-1.8.0` tag. **Both URLs are live.** On September 12, 2026, both MSIs were downloaded again: their SHA-256 values matched these manifests and their Authenticode signatures were valid. `winget validate` also passed. Schema validation does not prove installation success.

The package was submitted to `microsoft/winget-pkgs` as [pull request #434081](https://github.com/microsoft/winget-pkgs/pull/434081). The three manifests come from [the submission branch](https://github.com/greyhair-atx/winget-pkgs/tree/new-package/superputty-ce-1.8.0/manifests/c/ChrisThornton/SuperPuTTYCommunityEdition/1.8.0). After correcting temporary-account App Installer registration and local-manifest policy setup, the complete current-user WinGet lifecycle passed in a dedicated standard account: validation, silent installation, published executable hash/signature, branding, license, shortcut, Windows Installer per-user assignment, settings preservation, silent WinGet uninstallation, and cleanup all passed. WinGet exposes this per-user MSI under `ARP\Machine\X64`, so scope is verified independently through Windows Installer AssignmentType and removal uses the unique ProductCode without a conflicting scope filter. Both MSI scopes also passed the earlier silent lifecycle and upgrade matrix. An explicit all-users installation through WinGet remains pending while Microsoft's submission validation runs.

For the authorized Windows 11 workstation VM, `build/Invoke-WinGetScopeReview.ps1` runs a prepared stage from Administrator PowerShell. It creates a temporary standard account for current-user installation and runs the all-users test elevated. `build/Test-WinGetScopes.ps1` installs the published 1.8.0 packages through WinGet, checks registration, executable hash/signature/branding, installed license, shortcut and settings preservation, then uninstalls through WinGet using the unique ProductCode. Scope is verified independently through Windows Installer AssignmentType. The controller restores the previous local-manifest setting and historical shortcut registry marker, checks the host file baseline, and removes the temporary account/profile after success. On failure it retains the account and logs for investigation. These tests are specific to the immutable 1.8.0 release identities.

After reviewing a pre-installation WinGet launch failure, `-RetryFailedBootstrap` archives the previous results and removes the verified, unloaded temporary profile/account before starting again. It permits only the reviewed launch failure or the exact local-manifest-disabled error before MSI execution, and refuses retries if machine testing began or the recorded cleanup/preservation checks failed. Other failures require separate investigation.

The October 1, 2026 review of PR #434081 requested removing `AppsAndFeaturesEntries.DisplayVersion` because it equals `PackageVersion`. Both installer entries and the manifest generator omit this redundant field. Submit this correction by pushing to the existing `new-package/superputty-ce-1.8.0` branch; the open PR updates automatically. No new PR or rebuilt installer is needed for this metadata correction.

The subsequent review requested schema 1.28.0 in all three manifests and the version-specific `PrivacyUrl`. Those corrections are included in both the checked-in 1.8.0 manifests and generator; installer URLs, hashes, product codes and upgrade codes remain unchanged. The PR checklist must also refer to schema 1.28.0 when submitting these changes for re-review.

## Windows Sandbox test plan

Enable Windows Sandbox on a supported Windows edition if available, or use a disposable Windows x64 VM. This is a manual environment setup step; no host feature or WinGet setting is changed by this repository. Copy the verified bundle, scripts and manifests into the disposable environment. Do not map real profile settings or credentials into it.

1. Verify signatures and hashes inside the guest. Run `Test-InstallerLifecycle.ps1` in an elevated guest session with `SUPERPUTTY_DISPOSABLE_TEST=1`; see [release preparation](../../docs/RELEASE-PREPARATION.md).
2. Independently test the current-user MSI from a standard (non-administrator) account. Confirm no elevation, expected install directory, Start menu entry, first launch, and uninstall.
3. Test the all-users MSI with elevation and launch it as a standard user. Check installed attribution, settings persistence and uninstall.
4. After the approved release URLs are live, enable local manifests **in the disposable guest only** if needed (`winget settings --enable LocalManifestFiles`, elevated), then run `winget install --manifest <directory> --scope user` and uninstall the exact package. Repeat in a fresh guest for `--scope machine`. Use `--disable-interactivity` when evaluating unattended behavior. Record logs, product registration and downloaded hashes.
5. Test same-scope upgrades from published CE 1.7.6 and the bounded historical community range, and blocking of an ambiguous upstream 1.5 installation. Restore fresh VM snapshots between scenarios.

Regenerate and revalidate these files after **any** rebuild or signing change. Candidate hashes are not a promise about future rebuilt artifacts. The tag/source commit must also match the approved final build. See the [official manifest reference](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest).

The reviewed ProductCode-discovery failure can be retried with `-RetryCleanedUninstallFailure` only when the exact recorded operation sequence, successful MSI cleanup, settings preservation, and restored policy checks match. Failed-run evidence is archived before creating a new temporary account.

`-RetryCleanedRegistrationFailure` supports the reviewed HKCU-probe failure only when the successful installation, exact diagnostic error, successful cleanup, and preservation records match. Both PowerShell 5.1 and 7 retry-guard checks passed.
