# SuperPuTTY Community Edition 1.8.3 validation

Source commit: 340dc88398b95577701b400b102c3c0211f5515a, built from a clean worktree on October 28, 2026.

- Release/x64 solution rebuild succeeded.
- All 273 non-network unit tests passed, including protocol visibility, default selection and preservation of hidden protocols in existing sessions.
- Light, Dark and Dark Modern theme checks passed. Dark Modern toolbar/input border pixel checks, focus changes, window-handle recreation, docking rendering and tab activation passed.
- Application shutdown checks passed: title-bar close with/without confirmation and File > Exit.
- Command Prompt and PowerShell process capture/parenting checks passed.
- Real PuTTY scripted input against a loopback peer passed for Ctrl+C, Shift+C, Alt+x, repeated Enter and following text.

## Installer and package verification

The executable and both MSI installers are signed by Christopher Thornton with valid Authenticode signatures and timestamps. Both installer scopes are checked for version, architecture, product/upgrade identities, license and exact extracted application payload. The portable ZIP, SHA-256 manifest, build provenance and 57-file runtime SBOM are verified against the release files.

## Limits and inconclusive checks

The automated three-session PuTTY focus test could not establish PuTTY activation/focus on the current desktop. It failed at the first tab in both final 1.8.3 and the already published 1.8.3-rc.1 baseline. This test is not recorded as passed; a new focus regression was not isolated. The separate scripted-input check passed. Earlier 1.8.2 live focus results do not constitute a final 1.8.3 pass.

No final installation/upgrade/uninstallation matrix, new authenticated SSH/PSCP/RDP/VNC end-to-end tests, Windows 10 test, high-DPI matrix or new VirusTotal scan was performed. MSI administrative extraction verifies package contents, not installation behavior. Users of 1.8.3-rc.1 should uninstall that package before installing final 1.8.3 because both use MSI version 1.8.3. Back up settings first.
Published on [GitHub](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.3) and [Gitea](https://gitea.uberx.org/vscode/superputty/releases/tag/sp-1.8.3). All eight assets were downloaded from both hosts and hash-verified before publication. The existing [WinGet PR #434081](https://github.com/microsoft/winget-pkgs/pull/434081) now submits 1.8.3; both public installer downloads were checked again for hashes and valid timestamped signatures. Catalog publication is pending Microsoft review.
