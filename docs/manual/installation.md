# Installation

[Back to the manual](README.md)

## Choose a package

Community releases provide separate 64-bit installers:

- **Current user** installs under the current user's Local AppData directory without elevation.
- **All users** installs under 64-bit Program Files and requests administrator approval.
- A portable ZIP, when provided, can be extracted to a writable directory and run without MSI installation.

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

Community release executables and installers may be Authenticode signed by Christopher Thornton. Check a release's notes before installation. The application license is [License.txt](../../License.txt), and installed runtime components are described in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt).
