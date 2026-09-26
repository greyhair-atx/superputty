# SuperPuTTY Community Edition 1.8.1 validation

Source: 4438a02ca5596cf05597442ac829d256bec700cd (clean worktree).

- Release/x64 rebuild succeeded; 219 nonnetwork tests passed.
- PuTTY loopback input, shutdown, and console capture checks passed.
- GitHub CodeQL and Azure source build checks passed.
- Application and both MSI signatures validated, including timestamps.
- Both MSI metadata and extracted payload verification passed.
- ZIP, SBOM, manifest and SHA-256 checksum consistency passed.
- All eight VirusTotal analyses completed with zero malicious and zero suspicious detections.

## Limitations

Installer upgrade limitation: automated 1.8.0-to-1.8.1 installation/upgrade/uninstallation was not executed. Creation of the disposable hosted validation workflow was blocked by automatic approval review. Installer metadata, extracted contents, signatures, and checksums passed verification; this does not establish runtime upgrade behavior.

Windows 11 x64 was used locally. Windows 10 and live remote RDP/VNC/SSH authentication were not tested. RDP readiness tests simulate login events. VirusTotal results describe detections at scan time and do not guarantee safety.

Published September 26, 2026 on [GitHub](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.1) and [Gitea](https://gitea.uberx.org/vscode/superputty/releases/tag/sp-1.8.1). All ten assets were downloaded from both hosts and their hashes matched before publication. The release includes signed installers, a portable ZIP, checksums, an SBOM, a build manifest, release notes, validation details, and [VirusTotal reports](https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.1/SuperPuTTY-CE-1.8.1-VirusTotal.md). The release tag stays on the frozen build source; subsequent documentation updates do not change published binaries.
