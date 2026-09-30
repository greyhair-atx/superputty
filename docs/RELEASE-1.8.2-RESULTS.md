# SuperPuTTY Community Edition 1.8.2 validation

Release source: b37ac140e8895f040bcd9bc320acba95b6f6babb, from a clean worktree.

- Release/x64 rebuild succeeded; 220 nonnetwork unit tests passed.
- The live three-session regression used the actual SuperPuTTY docking interface and three real PuTTY processes with loopback raw connections. Repeated tab switches verified activation, keyboard focus, exact window bounds and exclusive delivery of typed input.
- The maintainer separately confirmed correct SSH behavior with three Test_SSH sessions in the second pre-release test candidate. This is distinct from the automated loopback test and final signed-package checks.
- PuTTY scripted terminal input, application shutdown and local console capture checks passed.
- Final source GitHub CodeQL and Azure build checks passed.
- The application and both MSI installers have valid, timestamped signatures from Christopher Thornton.
- Both MSI metadata and extracted payload checks passed. ZIP, runtime-file SBOM, build manifest and SHA-256 checksums were verified.
- All eight VirusTotal analyses completed without malicious or suspicious detections. See the accompanying reports for engine failures, unsupported formats and timeouts; zero detections is not a guarantee of safety.

## Limitations

No installation/upgrade/uninstallation matrix was executed for the final 1.8.2 packages. Static installer verification does not establish runtime upgrade behavior. Windows 11 x64 was used locally; Windows 10 and new live remote RDP/VNC authentication were not tested. RDP login readiness unit tests use simulated events.

Version 1.8.1 remains withdrawn because of its SSH session regression. Its initial SetFocus-only test candidate was unsuccessful and is not included in 1.8.2.

Published on [GitHub](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.2) and [Gitea](https://gitea.uberx.org/vscode/superputty/releases/tag/sp-1.8.2). All ten release assets were downloaded from both hosts and hash-verified before publication. The release tag stays on the frozen build source; documentation updates do not change published binaries. [VirusTotal report](https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.2/SuperPuTTY-CE-1.8.2-VirusTotal.md).
