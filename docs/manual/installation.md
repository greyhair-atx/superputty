# Installation

[Back to the manual](README.md)

## Choose a package

Download SuperPuTTY **1.7.5** from [GitHub](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.7.5) or [Gitea](https://gitea.uberx.org/vscode/superputty/releases/tag/sp-1.7.5). This release provides two signed 64-bit installers:

- **Current user:** `SuperPutty-1.7.5-current-user-win-x64-signed.msi` installs under the current user's Local AppData directory without elevation.
- **All users:** `SuperPutty-1.7.5-all-users-win-x64-signed.msi` installs under 64-bit Program Files and requests administrator approval.

Use only one installed scope on a machine unless you have a specific reason to keep both. The application version is shown under **Help > About SuperPuTTY**.

## Install PuTTY and PSCP

Install the 64-bit PuTTY package from the [official PuTTY site](https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html). On first launch, open **Tools > Options > General** and verify:

- `putty.exe` location
- `pscp.exe` location, if integrated file transfer is required
- Settings folder

Other executable locations are optional and are needed only for their associated protocols or shortcuts. These include MinTTY, a VNC viewer, FreeRDP or MSTSC, WinSCP, and FileZilla.

## First launch

On the initial configuration screen:

1. Select `putty.exe`.
2. Select `pscp.exe` if SCP will be used.
3. Accept or choose a writable settings folder.
4. Leave **Default Layout** set to `<Auto Restore>` to restore the window arrangement at startup without reopening the prior connections.
5. Save the options.

## Authenticity and licensing

The 1.7.5 executable and both installers are Authenticode signed by Christopher Thornton and timestamped. Both release pages include `SHA256SUMS.txt`, `VirusTotal-1.7.5.md`, and `virustotal-results.json`. VirusTotal completed scans of the signed executable and all five shipped DLLs with zero malicious or suspicious findings; the MSI files were not submitted. See the [release summary](../releases/1.7.5.md) for engine limitations and validation details.

The application license is [License.txt](../../License.txt), and installed runtime components are described in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt).
