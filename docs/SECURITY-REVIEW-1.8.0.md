# Security review — 1.8.0 local launch candidate

> **Published September 12, 2026:** [sp-1.8.0](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.0), built from clean commit `20b78c60ec02adc0af970e2d975ef615b63e58b8`. Final EXE/DLL scans completed with zero malicious or suspicious detections. The earlier preparation history below describes candidate-stage evidence; see the live release for final artifacts, identities and validation. Publication and the public project website were approved by the maintainer.


Reviewed September 12, 2026 on Windows 11 Pro x64, build 28000. This is a bounded review of the local candidate and accessible CI evidence, not a guarantee of freedom from vulnerabilities. No release, scanner upload or repository setting was changed.

## Results and scope

| Check | Result |
| --- | --- |
| Git history secret scan | Gitleaks 8.30.1, `git --log-opts=--all`, 618 commits / about 30 MB: zero detections |
| Current source snapshot | Gitleaks scan of tracked and nonignored untracked source, about 3.12 MB: zero detections |
| Local static analysis | Semgrep 1.171.0, `p/csharp`, 27 rules / 117 tracked C# targets, approximately 100% parsed lines: zero findings or parser errors |
| GitHub CodeQL | Latest recorded analysis for base commit `ae432924b37039cc197dbd97a0a60bb39966a0fa` succeeded; zero open alerts returned at inspection. This is the 1.7.6 base, not a remote scan of the unpushed 1.8.0 changes |
| GitHub Actions logs | Downloaded logs for runs 34709574471, 34709417669 and 34703408984; Gitleaks scan of about 493 KB found zero detections |
| Azure pipeline logs | **Completed from the maintainer-provided archive.** Build 29: 20 files / 382,086 bytes; zero Gitleaks or supplemental credential-pattern detections. This was an unsigned 1.7.6 test run; see [detailed review](AZURE-BUILD-29-REVIEW.md) |
| Signing | Local EXE and both MSI candidates have valid Authenticode signatures, expected Christopher Thornton signer and timestamp certificates |
| VirusTotal | Not run for 1.8.0; no upload authorized by this preparation request |

Evidence is retained in ignored `artifacts/launch-review/`. CI logs and raw scan reports are not release assets. Public CodeQL evidence: [successful analysis run](https://github.com/greyhair-atx/superputty/actions/runs/34709574471). Azure evidence requiring access: [build 29](https://dev.azure.com/greyhair-atx/5a2cb1e4-9de1-4fd6-af3b-b14dc92770da/_build/results?buildId=29).

The automated CI log review detects recognizable secret patterns; it cannot prove that every arbitrary credential or sensitive identifier is absent. The configured signing tasks mark federated token variables secret, but build 29 did not execute signing. The supplied archive resolved the earlier API-access limitation. Review the final approved 1.8.0 CI run, including signing tasks if enabled, before publication.

## Source and credentials

No signing private key or live signing credential was identified in source or the scanned Git history. `build/ArtifactSigningMetadata.json` contains the signing endpoint/account/profile identifiers, not credentials. Signing obtains credentials externally through Azure; the signed artifact includes a public certificate, not a private key.

Removed the populated **sample network-test password** from `SuperPuttyUnitTests/app.config`. It was a fixture value, not established to be a live credential. Its contents are intentionally not repeated here. Network-dependent tests remain excluded from the isolated suite and must be explicitly configured for a disposable test endpoint.

Release ZIP construction uses an explicit file allowlist. It cannot silently include developer settings, PDBs, arbitrary files, credentials or private keys from `bin`. The SBOM hashes the exact runtime payload. Checksums and the WinGet manifest are regenerated from finished signed MSI bytes.

## Licenses and dependencies

The repository's original MIT license is **`License.txt`**, not an absent file literally named `LICENSE`. It remains unchanged, as do Jim Radford's original notice and the installer license RTF. The complete original license is now copied into both MSI installations and the no-install ZIP. `SECURITY.md` and `THIRD-PARTY-NOTICES.txt` are present. Community modifications retain a separate Chris Thornton copyright.

Runtime and build/test dependencies and licenses are documented in the notices and project/package files. Runtime inventory includes log4net 3.4.0, DockPanelSuite and ThemeVS2015 3.1.1, RDP interop assemblies and assets. Windows/.NET and separately installed clients are prerequisites rather than falsely represented as bundled software. The CycloneDX inventory is a file inventory, not a vulnerability database or complete transitive dependency audit.

## Installer privileges and compatibility

Current-user MSI targets Local AppData with per-user scope; all-users MSI targets native Program Files with per-machine scope and administrative installation privileges. The application is launched only by the interactive Finish action, impersonating the user; unattended installation does not request that launch. The existing settings/registry identities are retained. The only added installer component is the original MIT license, with a distinct per-user component GUID and a component key under the retained registry root.

Separate CE UpgradeCodes and the fixed 1.6.0–1.7.5 historical bridge are preserved. Unknown historical packages are detect-only blocks. Upstream coexistence is unsupported: install scope, another Windows user and manually extracted copies complicate detection, and shared settings are not isolated. See [the upgrade guide](manual/upgrading.md). The subsequent authorized Windows 11 VM matrix passed 17 real standard-user and 20 elevated operations, covering both scopes, silent installation/uninstallation, upgrades, version-boundary blocking and actual upstream 1.5.0.0 blocking. All 20 original settings files remained unchanged; test products and the temporary account/profile were removed. Windows 10 and cross-user/cross-scope coexistence were not tested. See [full results](INSTALLER-MATRIX-1.8.0.md).

## Public-facing review and remaining gates

Prepared asset URLs point to `https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.0/`. The tag and asset URLs are intentionally pending. The website uses local screenshots/CSS and no scripts, trackers, analytics or remote fonts. Browser previews made no external asset requests. Older screenshot versions are clearly labeled. Community lineage and non-endorsement notices are explicit; original attribution is retained.

At initial inspection, GitHub Issues, Discussions and Pages were disabled; all 15 requested topics were present. The maintainer has since confirmed private vulnerability reporting is enabled. A fallback email is optional; no contact address was invented. Historical Azure build 29 log review and the requested Windows 11 installer matrix are complete. Before launch, confirm reporting notifications and update `SECURITY.md`, enable Issues, review the final 1.8.0 CI/signing results, and approve publication separately. Windows 10 testing is **deferred with disclosed limitations** by maintainer decision and is **not a release blocker** for 1.8.0. Expected compatibility with .NET Framework 4.8 or later is not a claim of tested installation or runtime behavior.
