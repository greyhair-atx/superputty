# Installer matrix — 1.8.0 candidate

**Passed on September 12, 2026, on the maintainer-authorized Windows 11 Pro x64 VM (build 28000).** Both scopes completed: 17 operations under a real standard-user account and 20 under an elevated account. All expected successful MSI operations returned 0; intentional blocked installations returned 1603 and were checked for the intended launch condition and detected ProductCode.

## Results

| Scenario | Current user, non-administrator | All users, elevated |
| --- | --- | --- |
| Clean 1.8.0 silent install/uninstall | Passed | Passed |
| Installed EXE bytes, version, signature, MIT license and Start menu shortcut | Passed | Passed |
| Upgrade from published CE 1.7.6, then uninstall | Passed | Passed |
| Historical bridge from published 1.7.5, then uninstall | Passed | Passed |
| Historical lower bound, 1.6.0 fixture | Passed | Passed |
| Block ambiguous 1.5.0.1 fixture; preserve old registration | Passed | Passed |
| Block unknown historical 1.7.6 fixture; preserve old registration | Passed | Passed |
| Block actual upstream 1.5.0.0 MSI; preserve upstream registration | Not applicable: upstream asset is machine scope | Passed |
| Preferences, sessions, named layout and auto-restore layout survive every operation | Passed | Passed |

Version-boundary fixtures use the historical UpgradeCode and explicit matching scopes. They are registry-only test packages, not modified copies of published releases. The 1.7.5, 1.7.6 and actual upstream 1.5.0.0 scenarios use real release packages. The current-user token was verified non-elevated; the dedicated account belonged to the standard Users group.

MSI commands used `/qn /norestart`, with verbose logs. Upgrade checks required the old ProductCode to disappear and the new product to be registered. Blocking checks required the expected detection property, the old ProductCode and a failed LaunchConditions action; a generic 1603 alone was not accepted as a pass. After a blocked installation, the candidate had to remain unregistered and the blocking package had to remain installed.

## Settings and restoration

The standard account's preferences and three legacy session/layout files were hashed before testing and checked after every install, upgrade, failed install and uninstall. The elevated matrix checked its test fixtures plus the workstation's actual preferences and configured settings folder. The final independent comparison found **20/20 original settings files unchanged**.

The original 1.7.4 installation remains registered and its executable is byte-for-byte unchanged. Original per-user registry markers were restored from the pretest export. Test products were uninstalled, the 1.8.0 machine executable is absent, and the temporary account and its profile were removed. The original application remains closed; it was gracefully closed before testing.

## Windows 10 disposition

Windows 10 testing is **deferred with disclosed limitations** by maintainer decision and is **not a release blocker** for 1.8.0. Installation and runtime compatibility are expected on Windows 10 x64 with .NET Framework 4.8 or later, but have not been tested. This disposition records an accepted limitation, not a test pass.

## Evidence and scope

- Standard-user summary: success, 17 operations, 4 checked preference/session/layout files; completed 2026-09-12 21:11:02 UTC.
- All-users summary: success, 20 operations, 24 baseline entries including host settings; completed 2026-09-12 21:13:45 UTC. One host preference path is checked both directly and in the host inventory; this is not a claim of 24 unique host files.
- Tested 1.8.0 MSI hashes match the exact signed packages in `artifacts/release-1.8.0/win-x64-signed/`.
- Package identities/hashes, host backups and final result copies: ignored `artifacts/launch-review/installer-matrix-20260912-152303/`.
- Gitleaks scan of the retained installer results: approximately 5.53 MB, zero detections. Raw installer logs are not release assets.
- Assertions: `build/Test-InstallerUpgradeMatrix.ps1`.

Earlier attempts exposed runner issues (account-description length, environment inheritance and Windows PowerShell 5.1 JSON-array enumeration), which were corrected. The old `machine-error.log` records the earlier harness failure; it predates the successful final machine summary and is retained only as diagnostic history. The original user settings never failed the independent hash checks.

This completes the requested installer matrix for these candidate bytes on **Windows 11 x64**. It does not establish Windows 10 behavior, cross-user/cross-scope coexistence, interactive network sessions, or download/install through WinGet's still-unpublished URLs. Upstream coexistence remains unsupported. A rebuilt or re-signed final release must receive validation appropriate to its changed bytes.

No source was committed or pushed and no release was published. The previous resumption command is no longer needed: the temporary account has been removed.
