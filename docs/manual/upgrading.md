# Upgrading SuperPuTTY Community Edition

[Back to the manual](README.md) · [Installation and recovery](installation.md)

## 1.8.4 candidate: explicit MSI migration

1.8.4 is an unpublished Inno candidate. The published 1.8.3 uses MSI. Automatic MSI-to-Inno replacement is not supported. Back up active preferences and the entire configured settings folder, close applications/clients, and retain a verified recovery MSI before removal.

Use the actual installed ProductCode with `winget uninstall --product-code "{PRODUCT-CODE}" --exact` or `msiexec /x {PRODUCT-CODE}`. Do not pass a scope filter to WinGet removal: catalog matching can omit current-user MSI registration. Elevate for machine scope, wait, inspect the exit code and confirm removal. Then install the signed Inno EXE in the original scope. See the installation guide for commands.

Removal and installation are separate, non-atomic operations. If replacement cannot complete, preserve logs and backups and restore the matching verified MSI. Never claim an Inno installer is MSI in a WinGet manifest. The withdrawn package submission stays closed while release validation is completed.

## Inno-to-Inno upgrades

Use the same signed EXE for either scope, selecting `/CURRENTUSER` or `/ALLUSERS`. Current-user roots are `%LOCALAPPDATA%\Programs\SuperPuTTY Community Edition`; machine roots are `%ProgramFiles%\SuperPuTTY Community Edition`, differing from historical MSI paths.

Install a strictly newer version in the same scope. Immutable `versions/<version>` directories retain the previous payload while registration and shortcuts activate. Same-version reinstall, downgrade, running/locked files, conflicting MSI registration and unsafe recovery data are rejected. Scope changes require deliberate uninstall/install after backups. Avoid concurrent installations sharing a profile.

After interruption, close setup and retry the **same installer and scope**. Preserve logs and `.upgrade-recovery`; do not edit/delete recovery data or registration. Missing, corrupt or locked backups require investigation. Recovery protects Inno activation, not earlier independent MSI removal. Repeat normal upgrade, deliberate failure and interruption tests on the release candidate.

## Settings and compatibility

The executable remains `SuperPutty.exe`, namespace `SuperPutty`, and AssemblyProduct `SuperPuTTY`, preserving preference discovery. `%USERPROFILE%\SuperPuTTY.settings` takes precedence over preferences beside the executable. Sessions and layouts use the configured settings folder. Atomic preference saves retain `.bak`; restoration is manual. Keep private backups until launch, sessions and layouts are checked.

The ZIP does not migrate registrations or isolate settings. Extract into a fresh directory and deliberately select existing settings. Coexistence with upstream remains unsupported because settings and shortcuts can overlap.

## Historical MSI behavior

CE 1.7.6–1.8.3 used distinct current-user and machine MSI identities. Current-user installs used `%LOCALAPPDATA%\Apps\SuperPuTTY`; machine installs used native Program Files in `SuperPuTTY`. Current-user automatic upgrades exhibited catalog matching and failed-upgrade recovery problems; use explicit removal instead of treating these identities as a safety guarantee.

The historical UpgradeCode was shared with upstream. Legacy MSI bridging covered known community versions **1.6.0 through 1.7.5 inclusive**. MSI ignores the fourth version field, so upstream 1.5.0.0 and community 1.5.0.1 cannot safely be distinguished: remove ambiguous products manually after backups. Do not expand that bridge or use UpgradeCode as an uninstall identifier. See [branding and compatibility](../branding.md) for historical identities.
