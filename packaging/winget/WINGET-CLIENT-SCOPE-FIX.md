# Candidate WinGet correction for MSI scope detection

This is an experimental client correction for [WinGet issue #3011](https://github.com/microsoft/winget-cli/issues/3011), separate from the SuperPuTTY application and installers. Retain the [tested explicit migration](CURRENT-USER-MIGRATION.md) for released WinGet clients until a corrected client has been reviewed, released and validated. No public release was created. The later submission metadata update is recorded below.

## Implementation

The implementation checkout is `C:\Users\chris\source\winget-cli-scope-fix`, branch `fix/msi-installation-context-3011`, based on upstream commit `c608376673762a10971e6bf2cbeedb6b6a7cae88` (client version 1.31.0).

The complete [upstream patch](upstream/3011-msi-installation-context.patch) includes the implementation, regression tests and release-note fragment. Its SHA-256 is `C8F1F0CF3D47A340A5DC0BBD4A080ABEB5175EDB3AEA9C4AA40584876E912566`. An isolated baseline index verified that it applies cleanly to the upstream commit above. The tested implementation is published on the contributor fork as [commit db0ab80](https://github.com/greyhair-atx/winget-cli/commit/db0ab80d938ac64827f742483fbbe15b801f122a), branch `fix/msi-installation-context-3011`. After the contributor confirmed review, [upstream PR #6644](https://github.com/microsoft/winget-cli/pull/6644) was opened with the [reviewed description](upstream/3011-PULL-REQUEST.md). It awaits upstream review/checks; the correction is not a released WinGet fix.

`ARPHelper` enumerates Windows Installer contexts once per inventory helper using `MsiEnumProductsExW`, passing a null user SID so user registrations are restricted to the caller while machine registrations remain visible. Case-insensitive ProductCodes map user-managed/unmanaged contexts to user scope and machine context to machine scope. A ProductCode seen in conflicting user/machine scopes is marked ambiguous. Failed enumeration discards the partial snapshot.

Production ARP inventory uses that snapshot for MSI InstalledScope metadata. It omits unavailable or ambiguous MSI entries rather than assigning their hive's scope. Non-MSI entries retain registry-derived scope. User/machine inventory filters scan both ARP hives and filter the resolved scope; a user search must not skip HKLM when a per-user MSI is registered there. The existing installer scope compatibility check is retained.

The conservative failure behavior can hide MSI entries from WinGet inventory while native context lookup is failing or ambiguous. This is a deliberate safety tradeoff requiring upstream review, not a claim that all inventory edge cases are resolved.

## Regression coverage

New tests in WinGet's `PredefinedInstalledSource.cpp` cover:

- Native enumeration of managed/unmanaged user and machine contexts, case-insensitive ProductCodes, and a conflicting user/machine registration that remains ambiguous after further duplicates.
- Restriction to the current user's registrations rather than all user SIDs.
- Failure after a partial enumeration, and retrying a short SID buffer without advancing the enumeration index.
- User MSI registration in machine ARP, correct metadata, user/machine filters, and preserving non-MSI inventory when MSI lookup fails.
- The actual predefined user source finding a user MSI in machine ARP while the machine-filtered source excludes it.

Existing manifest-comparator tests cover matching the installer to the installed scope. The VM runner additionally checks native MSI context before/after upgrade, installer payload hash/signature/version, settings preservation, uninstall-first ordering, and final removal.

## Build and validation

The candidate reports `v1.31.0-preview`. Its CLI target and regression executable built successfully. The clean checkout required NuGet restore and Visual Studio's bundled vcpkg integration. The local test build uses Release x64, `PreferredToolArchitecture=x64`, and bounded parallel compilation. Missing Spectre libraries require a local `/p:SpectreMitigation=false` override. Existing upstream NuGet advisories NU1902/NU1903 remain warnings through a local `WarningsNotAsErrors` override. These overrides do not change the patch or qualify the binaries for production distribution. This was a targeted native build, not a successful build/test of the entire solution: unrelated solution targets also encountered missing DesktopBridge tooling and an E2E restore warning treated as an error.

An English resource index is generated from upstream `winget.resw` and `Resources.resw` using the installed Windows SDK. Local logs and candidate binaries are retained under `artifacts/winget-scope-fix/` and the separate checkout's build output. The VM runs a separate candidate client; the installed production client is not replaced. Private VC runtime DLLs accompany the test executables instead of installing a system runtime. The temporary user-wide vcpkg integration was removed after building, restoring its original absence.

The selection `[msiscope],[arphelper],[manifest_comparator],[installed]` passed locally (45 test cases, 1,521 assertions) and in the disposable VM's non-elevated standard-user interactive session (45 test cases, 1,289 assertions). Existing inventory tests account for the environment-dependent assertion count. Five new test cases cover MSI context resolution and filtering; the other selected tests provide existing inventory/comparator regression coverage. The full test suite was not run.

Tested binary SHA-256 values:

| File | SHA-256 |
| --- | --- |
| `WindowsPackageManager.dll` (contains the correction) | `540F0B92AE53E9638600F7011D23EBDC37B54650D4BC0EC8B7925112BB8DC394` |
| `winget.exe` | `EFB8734366F9752CC0192174EED6222F24E0E7C5356FF3BE15D3630286558CDE` |
| `AppInstallerCLITests.exe` | `DD8BAB9243476F8098F45F6E04857CEF165279DBC2A8B1904DD2896B95ED1306` |

## Disposable VM catalog results

Windows 11 x64 build 26200.9457; exact published signed 1.8.0 and 1.8.3 SuperPuTTY packages; representative REST 1.4 review catalog. Each successful upgrade verified explicit uninstall-before-install ordering, native candidate MSI context (2 for user, 4 for machine), AssignmentType, executable hash/signature/version, unchanged settings, and final uninstall. The user runs checked two existing settings files, and machine runs checked four. Each starting installation was recreated separately.

| Starting scope | Catalog visibility | Explicit scope command | Unscoped command |
| --- | --- | --- | --- |
| User, non-elevated interactive account | 1.8.3 only | Passed | Passed |
| User, non-elevated interactive account | 1.8.0 and 1.8.3 | Passed | Passed |
| Machine, elevated account | 1.8.3 only | Passed | Passed |
| Machine, elevated account | 1.8.0 and 1.8.3 | Passed | Passed |

All eight upgrades passed. The ordinary unscoped user upgrade selected the current-user MSI, correcting the previously observed machine-installer selection. Explicit `--scope user` also found the installed package and completed the upgrade. Machine controls continued to select machine scope.

A ninth scenario removed the user installer from the candidate catalog while retaining the existing user installation and package correlation. The unscoped upgrade returned `No applicable upgrade found` before starting any uninstall. The old ProductCode remained installed in native user context 2, its executable hash was unchanged, and both settings files were preserved. The runner's `CatalogIncompatible` mode asserts this rejection, records the pre-cleanup registration state and then removes its owned old test installation.

The original catalog metadata was restored byte-for-byte. Cleanup verified no SuperPuTTY test registrations or executables remained, settings hashes were unchanged, and all temporary catalog/test tasks, source/local-manifest policies, localhost HTTPS bindings and certificate trust were removed. The standard review account and separate candidate files remain available for review; the production WinGet installation was not replaced.

Evidence is retained locally under `artifacts/winget-scope-fix/guest/`: per-scenario summaries, previous/candidate native contexts, operation/MSI logs, settings baselines, client info/diagnostics, regression results, catalog requests and final cleanup JSON. The staged candidate manifest records SHA-256 values for all executable/runtime/fixture files, and the VM runner verifies them before execution.

## Approval boundary

Automatic package-specific catalog upgrades pass with this experimental corrected client in the tested matrix. Released WinGet 1.29.380 remains affected, and the explicit migration workaround remains in place. This does not approve Microsoft's publishing/preindexed catalog pipeline, `upgrade --all`, every Windows release, or transactional recovery after an installation failure. `uninstallPrevious` can still remove the old version before a later installer failure; the negative test establishes rejection of an incompatible scope before removal, not general rollback support.

The next step is review of the patch and its conservative handling of missing/ambiguous MSI contexts, followed by an upstream pull request and validation of an eventual corrected release. No release/tag, public submission or additional upstream comment was created during implementation/testing.

Failure-recovery follow-up: [AUTOMATIC-USER-UPGRADE-RECOVERY.md](AUTOMATIC-USER-UPGRADE-RECOVERY.md). The forced-failure experiment failed registration recovery; production sequencing and manifest generation remain unchanged.

## November 1, 2026 submission preparation

The selected local regressions were repeated successfully (45 cases, 1,521 assertions). The upstream master still matched the tested base. The contributor fork contains the six-file scope correction, tests and release note plus a five-word spelling-dictionary follow-up; the SuperPuTTY installer experiments are separate.

The existing package-submission branch was updated in [commit d436108](https://github.com/greyhair-atx/winget-pkgs/commit/d4361080550a7132191897646b7a7f141b774de0) to match the reviewed manifests. The remaining delta was minimum OS `10.0.19041.0`, `uninstallPrevious`, and machine `elevationRequired`; schema 1.28.0 and prior metadata corrections were already on the remote branch. Local manifest validation passed. Published installer identities and bytes were unchanged. A [status update](https://github.com/microsoft/winget-pkgs/pull/434081#issuecomment-6560568359) requests metadata re-review and maintainer guidance while explicitly withholding an automatic current-user safety claim. This is not a merge or release approval.

The alternative installer evaluation is documented in [the Inno prototype report](../inno/README.md). It does not replace the MSI submission or the migration workaround.


Upstream PR #6644 was opened after contributor review. Follow-up commit `b7de1abf9fb000955a6d46db3b03ae607d97df97` adds five MSI API/test terms to the spelling dictionary; it changes no executable code. The exported patch now includes that metadata and was rechecked against the same baseline. Native binaries/regression results still correspond to the unchanged implementation in `db0ab80`. The initial CI spelling failure is retained in local evidence; CLA passed.

The follow-up's CLA, Check Spelling and Report checks passed. Native Azure Pipelines validation requires an authorized upstream maintainer to start it; it is not reported as a completed native CI pass. The package-submission status comment now links to the opened PR. The separate signed Inno prototype passed six private-catalog upgrades on released WinGet (user/machine × explicit/unscoped/`--all`), with complete temporary source/trust/task cleanup. Those results do not change the existing MSI's rollback limitation or replace the explicit migration workaround.
