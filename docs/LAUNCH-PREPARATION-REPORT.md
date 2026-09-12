# SuperPuTTY Community Edition 1.8.0 — local preparation report

Prepared September 12, 2026. **Not published, pushed, tagged, deployed, submitted or announced.** Repository/account settings were not changed. Existing published 1.7.x artifacts were not modified.

## Outcome

The public-launch material, signed Windows x64 review packages, no-install ZIP, checksums, runtime SBOM, responsive site, social preview and dual-scope WinGet manifests are prepared locally. Source changes remain uncommitted on top of `ae432924b37039cc197dbd97a0a60bb39966a0fa`. The build manifest correctly records `sourceDirty: true`; these are review candidates, not artifacts attributed to an exact published 1.8.0 tag.

The initial working tree was clean. Existing documentation, screenshots, security policy, tests, signing and installer behavior were reviewed before editing. Useful manual and historical release material remains linked. Original MIT license files, Jim Radford attribution, third-party license bodies, executable and settings identities were preserved.

## Build and validation

| Validation | Result |
| --- | --- |
| Complete `SuperPutty.sln`, Release/x64, restore + rebuild | Passed; no warnings or errors reported |
| NUnit isolated suite | **142 passed, 0 failed, 0 skipped**; network-dependent category deliberately excluded |
| Live application shutdown/title/legacy-layout checks | All three shutdown scenarios passed |
| Live console capture | Command Prompt and Windows PowerShell passed |
| Current-user and all-users MSI builds | Both passed, 1.8.0 / x64 |
| Executable metadata | `SuperPutty.exe`, x64 PE, file/product version 1.8.0.0, CE file description and original settings product identity verified |
| Authenticode | EXE and both MSIs valid; Christopher Thornton signer and timestamp certificates verified |
| MSI administrative extraction | Both passed unattended `/a /qn`; original license, five DLLs, configuration, notices, 47 icons and exact EXE/DLL/config/notice bytes verified |
| MSI identity and UI checks | Product/ARP/shortcut branding, version, scope, CE UpgradeCodes, fixed historical bridge and blocking conditions passed |
| Live installer matrix on authorized Windows 11 VM | 17 standard-user and 20 elevated operations passed: silent install/uninstall, CE/historical upgrades, boundary and upstream blocking, settings preservation and cleanup |
| Release bundle | Five asset checksums, two MSI build-manifest hashes and all 57 ZIP/SBOM files verified |
| CycloneDX | Official 1.6 schema validation passed with PowerShell `Test-Json` |
| WinGet 1.29.290 | `winget validate packaging/winget/ChrisThornton.SuperPuTTYCommunityEdition/1.8.0` passed |
| PowerShell scripts | All build scripts parsed without errors; executed positive bundle/manifests and missing-asset, unsigned-signed-name, host-safety rejection checks passed |
| Azure YAML | Parses locally; new pipeline has not been pushed or run remotely |
| Website | Edge/Playwright desktop 1280px, mobile 390px and dark-mode previews passed: all images loaded, no page overflow, no external asset requests or browser errors |
| Social image | Inspected PNG at 1280×640; promotional composition based on an older real workspace screenshot |
| Source/history/CI secret scans | Zero Gitleaks detections in source snapshot, 618-commit history and three recent GitHub Actions run logs |
| Azure build 29 log review | Completed from supplied archive: 20 files, zero secret-pattern detections; unsigned 1.7.6 run, not final 1.8.0 signing evidence |
| Static analysis | Semgrep: 27 C# rules, 117 targets, zero findings/errors; existing base-commit CodeQL successful, zero open alerts at inspection |

Logs, TRX results, scan reports and browser screenshots are retained in ignored `artifacts/launch-review/`. These may contain local paths and operational metadata and are not publication assets.

## Candidate artifacts

Directory: `artifacts/release-1.8.0/win-x64-signed/`.

| File | Bytes |
| --- | ---: |
| `SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi` | 2,154,496 |
| `SuperPuTTY-CE-1.8.0-all-users-win-x64-signed.msi` | 2,154,496 |
| `SuperPuTTY-CE-1.8.0-portable-win-x64.zip` | 1,165,919 |
| `SuperPuTTY-CE-1.8.0-SHA256SUMS.txt` | 551 |
| `SuperPuTTY-CE-1.8.0-sbom.cdx.json` | 18,873 |
| `SuperPuTTY-CE-1.8.0-build-manifest.json` | 977 |

The checksum file records the exact reviewed bytes. The SBOM is a CycloneDX runtime **file inventory**, not an exhaustive external-client or transitive build-dependency SBOM. The ZIP supports running without MSI installation but retains legacy preference discovery, so it is not a fully isolated portable profile. Its executable is signed; ZIP/JSON/text containers are not Authenticode signed.

Actual candidate MSI identities:

| Scope | ProductCode | UpgradeCode |
| --- | --- | --- |
| Current user | `{E0A7C475-7C41-4313-B1C8-335EDD4DBDFD}` | `{3E4059D9-E5A5-48DE-B787-FC88268C1B95}` |
| All users | `{02D55DE9-7CF4-4031-A887-C41AD36585F6}` | `{98CB966B-60D4-46C3-AADA-1990464696B8}` |

The staged WinGet YAML contains these real ProductCodes/UpgradeCodes and SHA-256 values. Future HTTPS URLs target `greyhair-atx/superputty`, `sp-1.8.0`; they are not live and no download/install-through-WinGet test is claimed. Rebuilding or re-signing requires regeneration of hashes and manifests.

## Compatibility and security limits

`SuperPutty.exe`, namespace `SuperPutty`, `AssemblyProduct("SuperPuTTY")`, `SuperPuTTY.settings`, existing settings folders, registry roots, command-line behavior and session/layout formats stay unchanged. Separate CE scope identities and the fixed legacy 1.6.0–1.7.5 bridge remain intact. Ambiguous upstream-era 1.5 packages are blocked for manual removal. Upstream coexistence remains unsupported because package separation does not isolate settings and installation paths.

The requested live installer matrix is **complete on the authorized Windows 11 Pro x64 VM**: 17 standard-user and 20 elevated operations passed. Both scopes passed silent install/uninstall, CE 1.7.6 and historical 1.7.5 upgrades, historical boundary-fixture checks and settings preservation. The original upstream 1.5.0.0 machine package correctly blocked CE installation without being removed. All 20 original settings files and the original 1.7.4 executable are unchanged; original registry markers were restored, and the temporary account/profile and test products were removed. See [installer matrix results](INSTALLER-MATRIX-1.8.0.md). Windows 10 and cross-user/cross-scope coexistence were not tested; upstream coexistence remains unsupported.

Azure build 29's log review is **complete** using the maintainer-supplied archive, resolving the earlier API-access limitation. No exposed secrets were identified across its 20 files. It was an unsigned 1.7.6 test run, so final 1.8.0 CI/signing execution still needs review. See [Azure review](AZURE-BUILD-29-REVIEW.md). GitHub log scans covered the three most recent runs only. No signing private key/live credential was identified in scanned source/history; a populated sample network-test password was removed without repeating it. No VirusTotal upload or new remote CodeQL run occurred. See [security review](SECURITY-REVIEW-1.8.0.md).

## Review the website and social image

Run `node build/Preview-Site.cjs` from the repository root, then open **http://127.0.0.1:8080**. Ctrl+C stops the local server. `node build/Test-LaunchSite.cjs` reruns the browser checks when Playwright and Edge are available. The Pages source is `docs/index.html` with local CSS/images and no application JavaScript, analytics or tracking. Candidate banners intentionally say 1.8.0 is pending.

Review [the 1280×640 social image](assets/social-preview-1.8.0.png) and its [specification and generation notes](SOCIAL-PREVIEW.md). The built-in image tool created the composition; a local browser export set exact dimensions. Neither asset was uploaded to GitHub settings.

## Manual steps and approval order

1. Review the completed Windows 11 installer matrix and historical Azure log review. The maintainer has confirmed GitHub private vulnerability reporting is enabled. Confirm reporting notifications and update the public reporting instructions. Windows 10 testing is **deferred with disclosed limitations** by maintainer decision and is **not a release blocker** for 1.8.0. Compatibility is expected with .NET Framework 4.8 or later, but installation and runtime behavior remain untested; no Windows 10 pass is claimed.
2. Review and approve the local source/content. Commit the approved tree, rebuild and sign from that commit, then regenerate/verify checksums, SBOM and WinGet identities. No new commit or push was performed during preparation.
3. Obtain explicit approval to push/tag and publish **SuperPuTTY Community Edition 1.8.0 — Signed Windows x64 Release**, preserve all 1.7.x assets, and mark 1.8.0 latest. Verify actual downloadable hashes and exact tag/source linkage. Any VirusTotal upload needs separate authorization.
4. With separate settings/deployment approval, enable Issues and Discussions, configure description/site/social preview and profile pin, enable/test private vulnerability reporting, and deploy Pages. All 15 topics already existed. Update candidate banners only when the release is live.
5. Test WinGet against live immutable URLs in Sandbox/VMs, then obtain approval before a package-repository fork/PR. Approve individual announcements and upstream outreach before sending. Consider Chocolatey after WinGet is stable.

Detailed settings are in [PUBLIC-LAUNCH-CHECKLIST.md](PUBLIC-LAUNCH-CHECKLIST.md), distribution tests in [WinGet staging](../packaging/winget/README.md), and publishing prose in [draft release notes](releases/1.8.0.md) and [announcements](ANNOUNCEMENTS.md).

## Files changed or created

Modified existing files:

- `README.md`, `ReleaseNotes.txt`: launch landing page and version history.
- `SuperPutty/Properties/AssemblyInfo.cs`: application version 1.8.0.0; compatibility product identity retained.
- `SuperPuttyInstaller/Product.wxs`: add original MIT license payload component.
- `SuperPuttyInstaller/SuperPuttyInstaller.wixproj`: default MSI version 1.8.0.
- `SuperPuttyUnitTests/AboutBoxTests.cs`: expected displayed version; `SuperPuttyUnitTests/app.config`: remove populated sample password.
- `THIRD-PARTY-NOTICES.txt`: current edition/version heading; license bodies retained.
- `azure-pipelines.yml`: complete solution, test minimum, bundle/signing checks, optional disposable lifecycle and staged WinGet artifacts.
- `build/Verify-CodeSignatures.ps1`, `build/Verify-ReleaseArtifacts.ps1`: version defaults, exact payload/license verification and five runtime DLLs.
- `docs/manual/README.md`, `docs/manual/installation.md`, `docs/testing.md`: candidate navigation, verification, install/ZIP behavior and test links.

Created files:

- `build/New-ReleaseBundle.ps1`, `build/Verify-ReleaseBundle.ps1`: allowlisted ZIP, SBOM, checksums and exact-file validation.
- `build/New-WinGetManifest.ps1`: manifests generated from actual signed packages.
- `build/Test-InstallerLifecycle.ps1`: guarded disposable install/uninstall verification.
- `build/Preview-Site.cjs`, `build/Test-LaunchSite.cjs`: loopback-only preview and browser checks.
- `docs/index.html`, `docs/assets/site.css`, `docs/.nojekyll`: static Pages source.
- `docs/assets/social-preview-1.8.0.png`, `docs/SOCIAL-PREVIEW.md`: review image and provenance/specification.
- `docs/PUBLIC-LAUNCH-CHECKLIST.md`, `docs/RELEASE-PREPARATION.md`, `docs/SECURITY-REVIEW-1.8.0.md`, this report: release procedure, evidence and remaining gates.
- `docs/CHOCO-PACKAGE-PLAN.md`, `docs/ANNOUNCEMENTS.md`: future distribution plan and five unsent drafts.
- `docs/manual/upgrading.md`, `docs/releases/1.8.0.md`: compatibility guide and full draft release notes.
- `packaging/winget/README.md`: schema and disposable/live-URL testing instructions.
- `packaging/winget/ChrisThornton.SuperPuTTYCommunityEdition/1.8.0/ChrisThornton.SuperPuTTYCommunityEdition.yaml`, `.installer.yaml`, `.locale.en-US.yaml`: three staged manifests.

Ignored build artifacts and review logs are intentionally outside the source-change inventory.
