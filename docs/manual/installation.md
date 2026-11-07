# Installation

The latest published stable release is [1.8.3](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.3). **1.8.4 is an unpublished release candidate.** These instructions describe its Inno installer; use the assets and instructions supplied with older releases when installing them.

## Package and scope

`SuperPuTTY-CE-1.8.4-setup-win-x64.exe` supports both scopes:

- **Current user:** `%LOCALAPPDATA%\Programs\SuperPuTTY Community Edition`, without administrator rights.
- **All users:** `%ProgramFiles%\SuperPuTTY Community Edition`, with administrator approval.

Use one scope and retain it for upgrades. Requires Windows x64, Windows 10 build 19041 or later, .NET Framework 4.8, and separately installed connection clients. Release acceptance testing targets Windows 11 x64; Windows 10 is outside the validation scope. Help > About SuperPuTTY CE should show **1.8.4.0**.

**Existing MSI installations must be explicitly removed before installing Inno.** Back up settings and retain the verified recovery MSI first. Removal and replacement are not atomic; automatic MSI-to-Inno upgrades are not supported.

## Verify before running

Compare the full SHA-256 with the matching filename in `SuperPuTTY-CE-1.8.4-SHA256SUMS.txt`. Obtain both from the same trusted release/review source:

```powershell
Get-FileHash .\SuperPuTTY-CE-1.8.4-setup-win-x64.exe -Algorithm SHA256
Get-AuthenticodeSignature .\SuperPuTTY-CE-1.8.4-setup-win-x64.exe | Format-List
# For an extracted ZIP:
Get-AuthenticodeSignature .\SuperPutty.exe | Format-List
```

Expect `Valid`, signer **Christopher Thornton**, and a timestamp certificate. Archives containing `portable-dev` in their name are unsigned CI artifacts.

## Back up and migrate from MSI

Close SuperPuTTY and its connection clients. Back up `%USERPROFILE%\SuperPuTTY.settings` (or the active preferences beside the EXE) and the entire settings folder shown in Tools > Options. Keep backups private. Retain the verified MSI matching the installed version and scope before removal.

Find the actual installed **ProductCode** in its uninstall registration or matching build manifest. Inno's detection message reports the detected code. Never use the shared UpgradeCode. Remove that exact product, without a scope filter:

```powershell
winget uninstall --product-code "{ACTUAL-INSTALLED-PRODUCT-CODE}" --exact
# Alternatively, wait for Windows Installer and inspect its exit code:
$p = Start-Process msiexec.exe -ArgumentList '/x {ACTUAL-INSTALLED-PRODUCT-CODE} /qn /norestart' -Wait -PassThru
$p.ExitCode
```

Elevate for machine-scope removal. MSI exit 0 means success; 3010 requires a restart before continuing. Confirm removal, then install Inno in the original scope. If replacement fails, preserve logs and backups and restore the verified previous MSI if necessary. Do not delete registration or files manually to bypass detection. **Uninstall followed by installation is not a transaction.**

## Install and first launch

Run the installer and select the scope. Install PuTTY/PSCP from the [official PuTTY site](https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html). In Tools > Options > General, select `putty.exe`, optionally `pscp.exe`, and a writable settings folder. Other clients are required only for their associated protocols. Default Layout `<Auto Restore>` restores window arrangement without reopening prior connections.

For unattended setup, wait and check the exit code. Use an elevated shell and `/ALLUSERS` for machine scope:

```powershell
$p = Start-Process .\SuperPuTTY-CE-1.8.4-setup-win-x64.exe -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CURRENTUSER /LOG="setup.log"' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Setup failed: $($p.ExitCode). Review setup.log." }
```

## Subsequent Inno upgrades and recovery

Install a strictly newer Inno version in the same scope with the application and clients closed. The installer stages a separate version directory and retains the previous payload. Same-version reinstall and downgrade are rejected.

After failure or interruption, close setup and rerun **the same installer in the same scope**. Pending recovery restores the previous registered state before proceeding. Preserve the setup log and recovery files. If recovery reports missing, changed or locked backup data, stop and retain logs for support. Do not delete `.upgrade-recovery`, edit registration, switch scopes or force uninstall to bypass recovery. This recovery does not undo a separately removed MSI.

Uninstall through Windows Settings > Apps or the registered Inno uninstaller. Do not use `msiexec` for Inno. Keep settings backups until launch, sessions and layouts have been checked.

## No-install ZIP

Extract `SuperPuTTY-CE-1.8.4-portable-win-x64.zip` completely into a new writable directory, verify the EXE signature, and run `SuperPutty.exe`. Never extract over a running application. The ZIP has no uninstall registration and does not remove an MSI.

This is not an isolated portable profile: `%USERPROFILE%\SuperPuTTY.settings` takes precedence; preferences beside the EXE are used only if there is no profile file. Sessions and layouts use the configured settings folder. Back up before switching copies.

Further details: [upgrade compatibility](https://github.com/greyhair-atx/superputty/blob/master/docs/manual/upgrading.md), [license](https://github.com/greyhair-atx/superputty/blob/master/License.txt), [third-party notices](https://github.com/greyhair-atx/superputty/blob/master/THIRD-PARTY-NOTICES.txt), and [security](https://github.com/greyhair-atx/superputty/blob/master/docs/manual/security.md).
