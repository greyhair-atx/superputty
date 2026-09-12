# SuperPuTTY Community Edition 1.8.0 — release results

Published September 12, 2026 as **[sp-1.8.0](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.0)** and marked the latest stable GitHub release.

## Source and artifacts

The application and installers were rebuilt from clean commit `20b78c60ec02adc0af970e2d975ef615b63e58b8`, which is the target of the annotated release tag. Subsequent documentation and package-manifest updates do not change that release source or its published bytes.

Nine assets accompany the release: both signed x64 MSIs, the no-install ZIP, the CycloneDX runtime-file SBOM, the build manifest, SHA-256 checksums for those five core assets, VirusTotal Markdown/JSON reports and a standalone copy of the release notes. Uploaded assets were downloaded again and their hashes matched the local files before publication. Existing 1.7.x releases were preserved.

| MSI | SHA-256 |
| --- | --- |
| Current user | `4674a1f238712314ab5b2335b87bf43e8c6e62528357d8d65228bb42375f1e0f` |
| All users | `8297d4b7de572d40ced1cfa287756338083098412e91dcfc24f691696a3e5b1c` |

## Validation

- Complete Release/x64 solution rebuild, 142 isolated tests, live shutdown/legacy-layout checks and Command Prompt/PowerShell capture checks passed.
- Final EXE and MSI signatures are valid, timestamped and signed by Christopher Thornton.
- Both final MSI payloads match the verified executable, five DLLs, config and notices; original MIT license bytes are intact.
- Five core asset checksums and all 57 ZIP/SBOM payload-file hashes passed; CycloneDX 1.6 schema and final WinGet manifests validated.
- GitHub CodeQL and Azure build 30 succeeded on the exact release commit. Accessible final GitHub CI logs and local build/signing logs had zero Gitleaks detections. The previous Azure build 29 archive review remains separately documented; no claim is made that build 30's raw Azure logs were downloaded.
- The pre-freeze Windows 11 installer matrix passed 37 operations using the same application/installer sources: both scopes, a real standard-user account, silent install/uninstall, upgrades, historical boundaries and upstream blocking. Final rebuilt packages received fresh signature and exact-payload checks. See [matrix scope and evidence](INSTALLER-MATRIX-1.8.0.md).
- Windows 10 testing is deferred with disclosed limitations and is not a release blocker. Expected compatibility with .NET Framework 4.8 or later is not a Windows 10 test pass.

## VirusTotal publication condition

All six final EXE/DLL analyses completed with **zero malicious and zero suspicious detections** before publication. The signed EXE had 68 undetected results; DLL counts varied from 63 to 71. Neither MSI was submitted. Engine failures, unsupported formats and timeouts are recorded in the [release's VirusTotal report](https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.0/SuperPuTTY-CE-1.8.0-VirusTotal.md). Zero detections is a scan-time observation, not a guarantee of safety.

The scanned binary hashes were checked against the final MSI payloads and ZIP inventory. No binary was rebuilt or re-signed after those scans and before publication.

## Discoverability and remaining distribution work

The public README and website now identify 1.8.0 as released, provide prominent downloads and preserve community-fork attribution. The project website includes social-preview metadata and the prepared 1280×640 image. Issues and private vulnerability reporting are enabled; the repository's 15 discovery topics are retained.

WinGet manifests are generated from the actual published MSI identities and hashes and remain staged for a separately reviewed submission. No Chocolatey package or social-network/upstream message was posted. The release, project landing page and repository metadata provide the public launch entry points.
