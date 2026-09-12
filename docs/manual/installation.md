# Installation

[Back to the manual](README.md)

## Choose a package

Download the [latest stable release](https://github.com/greyhair-atx/superputty/releases/latest). **1.8.0 is currently a local launch candidate, not a published release.** The filenames below describe the prepared 1.8.0 packages; use the version actually shown on the release page. Choose one of the two Windows x64 packages:

- **Current user:** `SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi` installs under the current user's Local AppData directory without elevation.
- **All users:** `SuperPuTTY-CE-1.8.0-all-users-win-x64-signed.msi` installs under 64-bit Program Files and requests administrator approval.

Use only one installed scope on a machine unless you have a specific reason to keep both. The application version is shown under **Help > About SuperPuTTY CE**.

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

## Verify signatures and checksums

Check the MSI before installing, or the extracted executable before launching:

```powershell
Get-AuthenticodeSignature .\SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi | Format-List
Get-AuthenticodeSignature .\SuperPutty.exe | Format-List
Get-FileHash .\SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi -Algorithm SHA256
```

Expect `Valid`, signer **Christopher Thornton**, and a timestamp certificate. Compare the complete SHA-256 value with the matching filename in `SuperPuTTY-CE-1.8.0-SHA256SUMS.txt`. For earlier releases use their own checksum files. Prior VirusTotal results apply only to their respective bytes; no 1.8.0 VirusTotal scan is claimed.

## No-install ZIP

Extract `SuperPuTTY-CE-1.8.0-portable-win-x64.zip` in full and run `SuperPutty.exe`. Requires Windows x64 and .NET Framework 4.8, plus separately installed clients. Verify the ZIP checksum and then the EXE signature. No installer registration or automatic uninstall is provided.

This is not a fully isolated portable profile. An existing `%USERPROFILE%\SuperPuTTY.settings` is discovered first; an existing preferences file beside the EXE is used only if there is no profile file. Sessions and layouts use the configured settings folder. Back up that folder before switching copies.

## Unattended installation

```powershell
msiexec.exe /i .\SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi /qn /norestart
# Run an elevated shell for the all-users package:
msiexec.exe /i .\SuperPuTTY-CE-1.8.0-all-users-win-x64-signed.msi /qn /norestart
```

Wait for Windows Installer to complete when scripting and inspect its exit code (0 success; 3010 success requiring restart). Uninstall through Windows Settings, or use `msiexec /x {ProductCode} /qn /norestart` with the exact ProductCode from the installed product or matching build manifest. Never use the shared historical UpgradeCode as an uninstall identifier. Read the [upgrade guide](upgrading.md) before replacing an existing installation.

The application license is [License.txt](../../License.txt), and installed runtime components are described in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt).

Read [branding and compatibility](../branding.md) before upgrading: CE preserves settings, upgrades known community 1.6.0–1.7.5 packages in the same scope, and requires manual removal of ambiguous 1.5 packages. Upstream coexistence is unsupported.
