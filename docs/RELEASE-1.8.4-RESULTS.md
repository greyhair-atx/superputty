# SuperPuTTY Community Edition 1.8.4 publication and validation

Published **November 8, 2026** on [GitHub](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.4) and [Gitea](https://gitea.uberx.org/vscode/superputty/releases/tag/sp-1.8.4), and verified as the latest stable release on both hosts. Annotated tag `sp-1.8.4` targets frozen source `36dc25e51a296cadab0926ccf7e8be153d335872`; subsequent documentation commits do not change the published binaries.

## New installer, new look

**One signed modern installer now offers both current-user and machine-wide installation.** Run `SuperPuTTY-CE-1.8.4-setup-win-x64.exe` and choose installation just for the current account, without administrator rights, or for all users on the machine, with administrator approval. Separate per-scope MSI downloads are no longer needed.

Existing MSI users must back up settings and explicitly remove their installed MSI before installing Inno in the same scope. This is a non-atomic uninstall/install migration, not an automatic MSI upgrade. The [installation guide](manual/installation.md) and [upgrade guide](manual/upgrading.md) describe the supported process.

## Validation of the published bytes

- Clean x64 Release build from `36dc25e`.
- 490/490 CI-default regressions passed locally and under each Windows 11 administrator/standard-user test account.
- 40/40 interactive tab/UI and 5/5 interactive-console cases passed.
- Both scopes passed four Inno lifecycle checks, five published-1.8.3-MSI migration checks and 12 recovery/interruption checks each: 42 installer checks in total.
- Live RDP login/reconnect passed in both accounts; server login/disconnect/reconnect events were verified.
- Signed executable, installer and uninstaller have valid Christopher Thornton signatures and timestamps. All 57 runtime files match the SBOM and installed-payload inventory.
- Package tooling passed 22 checks in each of PowerShell 7 and Windows PowerShell 5.1. Azure build 86 and GitHub build/CodeQL checks passed for the frozen source.
- Nine 96-DPI form/pane renders were reviewed across Light, Dark and Dark Modern. This was a rendering-fixture review, not 30 live sessions or a new physical-DPI test. Prior maintainer scaling, RDP network-loss recovery and real-server MFA/sharing acceptance remains accepted.
- Cleanup preserved all 65 original VM installation/settings-file hashes and removed temporary test accounts, profiles, tasks, sessions and the stage write grant.

See [the complete validation record](releases/1.8.4-VALIDATION-20261108.md) for exact hashes, scope, qualifications and retained evidence. Windows 10 compatibility remains unvalidated.

## VirusTotal publication condition

All **ten** submitted files completed analysis with **zero malicious and zero suspicious detections** before publication: the exact signed application, five shipped DLLs, signed installer, signed uninstaller, portable ZIP and frozen source archive. Consult the [Markdown report](https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.4/SuperPuTTY-CE-1.8.4-VirusTotal.md) and [JSON report](https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.4/SuperPuTTY-CE-1.8.4-VirusTotal.json) for per-file hashes, analysis IDs, engine counts, timestamps and report links. Failures, timeouts and unsupported types are recorded separately. Zero detections is a scan-time observation, not a guarantee of safety.

## Release assets and download verification

Both hosts supply the same **11 assets**: the signed setup EXE, portable ZIP, frozen source ZIP, core SHA-256 manifest, runtime SBOM, build manifest, installation guide, release notes, validation report and two VirusTotal reports. The core checksum file covers the setup, portable ZIP, SBOM, build manifest and installation guide. The supplementary reports record the source/archive and scanned-file identities.

All 11 assets were downloaded through each host's authenticated draft route and compared to the local inventory before publication. After publication, all **22 public downloads** were downloaded again and their SHA-256 values matched. Both latest-release endpoints resolve to `sp-1.8.4`. The tested application, installer and portable ZIP bytes were not rebuilt or re-signed during publication. Approval/guide/report metadata and its checksums were updated separately.

Gitea's attachment API advertised HTTP links; verification used the working HTTPS routes. One build-manifest attachment duplicated during a resumed upload was removed before the final public-download pass. The final asset set contains no duplicate names. Existing releases were preserved; no WinGet catalog submission or unrelated announcement was performed.

Private publication evidence is retained locally under `artifacts/release-publication-1.8.4-20261108-113025/`, including raw scan analyses, public scan reports, the asset inventory, draft/public download verification, publication summary and release state. Credentials, signing configuration and test artifacts are not release assets.
