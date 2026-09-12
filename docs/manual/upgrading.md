# Upgrading SuperPuTTY Community Edition

[Back to the manual](README.md) · [Installation](installation.md)

Close the application and back up `%USERPROFILE%\SuperPuTTY.settings` (or the existing file beside `SuperPutty.exe` if that is the one in use), plus the complete settings folder shown in **Tools > Options**. That folder contains your sessions and layouts. Treat these backups as private. Atomic saves retain the preceding preferences in `SuperPuTTY.settings.bak`; restoration is manual, not automatic.

## Choose the same installation scope

CE 1.7.6 and later use stable, distinct current-user and all-users MSI upgrade identities. Install the newer package in the same scope to upgrade. The 1.8.0 launch preparation does not change these identities. Avoid installing multiple rebuilt packages of the same version; their generated ProductCodes can differ and MSI major-upgrade logic does not treat them as a normal version increase.

The current-user installation remains under `%LOCALAPPDATA%\Apps\SuperPuTTY`; the all-users installation remains under native Program Files in `SuperPuTTY`. Scope changes require a deliberate uninstall/install operation after backups. Although the scopes have separate registrations, their applications can share user settings and shortcuts. We do not recommend running both against the same profile.

## Historical packages and upstream

The old UpgradeCode was shared with upstream. The installer only bridges known community versions **1.6.0 through 1.7.5 inclusive** in its applicable install scope. This range must stay fixed as new CE versions are released.

MSI ignores the fourth version field, so upstream 1.5.0.0 cannot reliably be distinguished from historical community 1.5.0.1. The installer detects and blocks these ambiguous packages instead of automatically removing them. Back up settings and uninstall the old package manually first. Unrecognized newer packages using the historical UpgradeCode also block installation.

**Coexistence with upstream is unsupported.** Different package identities do not isolate existing settings, folders and shortcuts. Detection by MSI also does not guarantee detection of another user's installation, another scope, or a manually extracted copy. Check those yourself before installing. Detailed codes and constraints are recorded in [branding and compatibility](../branding.md).

## Compatibility preserved

The executable remains `SuperPutty.exe`, the namespace remains `SuperPutty`, and `AssemblyProduct` remains `SuperPuTTY` so preference discovery still finds `SuperPuTTY.settings`. Registry roots, configured settings directories, session/layout formats, and command-line behavior are unchanged. Existing layout fixtures and preference-discovery tests remain part of release validation.

For the no-install ZIP, extract the entire new archive into a clean directory, verify it, and point to your existing settings deliberately. Never extract over a running application. The ZIP does not isolate settings from another installation.
