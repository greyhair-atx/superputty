# Explicit MSI-to-Inno migration retest

This focused test uses the published signed 1.8.3 MSI and the normal-mode, signed 1.8.3 Inno packaging proof (`signing-proof-v6`). Both contain the same application executable. It tests changing installer ownership, not upgrading the application version. The Inno proof is private and must not replace a published 1.8.3 asset.

`build/Test-InnoExplicitMigration.ps1` performs the following in the authorized disposable Windows 11 VM:

1. Require a clean starting state and the correct interactive identity/elevation.
2. Verify the original MSI and Inno setup signatures, timestamps and pinned hashes. Back up existing preferences/session/layout files and retain a verified MSI for recovery before removing anything.
3. Install the original MSI and require installed ProductState 5 and its signed executable. Confirm Inno refuses to install over that MSI without modifying it.
4. Run released WinGet's exact ProductCode uninstall without a scope filter. Require ProductState -1, removal of the old executable and absence of its uninstall key in both hives/views.
5. Separately install the signed Inno EXE with `/CURRENTUSER` or `/ALLUSERS`. Verify its version, intended registry hive, unchanged signed application bytes, signed uninstaller digest and shortcut target.
6. Launch/close the migrated application using an isolated copy of preferences and an empty runtime settings directory. Require the original settings hashes to remain unchanged.
7. Uninstall the Inno proof and verify final cleanup and unchanged original settings.

The standard-user test **passed**, including both original settings files and final uninstall. Its copied recovery MSI matches SHA-256 `5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36`; no candidate application executable remained afterward. Guest evidence: `C:\Users\SPCEtest7385b2a5\SPCEExplicitMigration-20261102-v1`.

The machine control **passed** in the administrator interactive session, including all four original settings files and final uninstall. Its evidence directory is `C:\SPCEInnoCandidate\explicit-machine-20261102-v1`.

| Scope | Explicit MSI removal | Separate Inno install and launch | Settings | Final uninstall |
| --- | --- | --- | --- | --- |
| Current user, standard account | Passed | Passed | 2 files unchanged | Passed |
| Machine, elevated administrator | Passed | Passed | 4 files unchanged | Passed |

Local evidence is retained under `artifacts/inno-explicit-migration/20261102/`. Final cleanup removed both test tasks and confirmed no candidate application executable remained installed. All six original settings files still matched their baselines. The test script matches the copy executed in the VM. These results use the unchanged published application bytes; a future release with new application code still requires its own validation.

The released client still labels the current-user MSI as `ARP\Machine\X64`. The exact ProductCode uninstall succeeds despite that inventory defect. This test does not approve automatic MSI-to-Inno conversion and does not make removal and installation atomic. It verifies the successful explicit migration; reinstalling the old MSI after a failed replacement remains a separate recovery procedure, not an automatic rollback guarantee.

No temporary catalog, certificate trust, release, public manifest or installed WinGet binary is changed by this test.
