# Inno candidate review

The implementation is private and unpublished. Existing signed MSI/ZIP assets and the public 1.8.3 manifests are unchanged. The earlier investigation/prototype checkpoint is commit `183097c`.

## Authoring and recovery

`SuperPuttyCandidate.iss` uses the proposed persistent AppId `{EBCA29DD-46C1-4A93-9340-79DF5375711D}` and display name **SuperPuTTY Community Edition**. One EXE supports `/CURRENTUSER` and elevated `/ALLUSERS`, with separate fixed application directories and registry views. It is distinct from the earlier packaging prototype.

Payloads are immutable version directories. Before modifying an existing installation, `Recovery.iss` durably backs up its complete uninstall registration, signed uninstaller and ownership logs, shortcut, and candidate file manifest. A flushed, hash-checked journal marks when installation may begin. The shortcut activates only after payload verification and registration finalization; a durable commit marker distinguishes a completed installation from an interrupted one. Retrying setup restores an uncommitted installation before considering another upgrade. Uninstall refuses an unresolved journal.

Recovery removes declared candidate files and only those random Inno uninstaller staging files whose bytes match recorded hashes. It rejects corrupt backups, unexpected paths/reparse points, modified or locked previous payloads, repeated versions, and downgrades. A global operation mutex excludes concurrent setup/uninstall operations and is released by Windows even after process termination. Preferences, sessions and layouts are never restored over user data.

`Build-InnoCandidate.ps1` verifies the entire extracted portable payload against a pinned archive and requires a valid project signature/timestamp. Normal builds require matching application/package versions and integrated signing. `Sign-InnoCandidateArtifact.ps1` embeds the exact reviewed signed uninstaller cache instead of re-signing it with different bytes; setup and its other temporary copies are signed through the compiler. The final build records installer and uninstaller hashes. A seed compile has no usable recovery digest and is not a distributable candidate.

## Qualification

The disposable VM is Windows 11 with released WinGet 1.29.380. Fixtures `.100` and `.101` deliberately contain the unchanged signed 1.8.3 application. The separate normal-mode signing proof also uses version 1.8.3 so the application/package version check is exercised honestly. **None of these files is a new application release.**

Regression runners:

- `build/Test-InnoCandidateRecovery.ps1`: signed uninstaller; real file-write and registry-write engine failures; repeat/downgrade rejection; locked payload; main-window launch from a version directory; running-app rejection; actual shortcut activation failure; corrupt-backup rejection; forced process-tree termination after file writes and after registration/shortcut activation; concurrent uninstall exclusion; complete previous-file/registry/shortcut restoration; normal upgrade; settings preservation and final uninstall.
- `build/Test-InnoCandidateCatalog.ps1`: correlation under the real `ChrisThornton.SuperPuTTYCommunityEdition` identity, automatic MSI-to-Inno attempts preserving the MSI, explicit ProductCode removal followed by catalog installation, then explicit-scope, unscoped and source-restricted `upgrade --all` Inno upgrades.
- `build/Test-InnoCandidateSigning.ps1`: a normal-mode signed build installs the exact signed uninstaller hash compiled into recovery, preserves settings and uninstalls cleanly.
- `build/Test-InnoReviewCatalogTransport.ps1 -CandidateInstallers`: HEAD/range/full download, canceled response survival and rejection of fault-fixture downloads.

The initial user controls passed in `SPCECandidateRecovery-v2`, `SPCECandidateCatalog-v2`, and `SPCECandidateSigning-v2` under the dedicated test account's profile. Corresponding machine controls passed in `machine-recovery-v8`, `machine-catalog-v1`, and `machine-signing-v1` under `C:\SPCEInnoCandidate`, including concurrent uninstall rejection at both interruption checkpoints. The separate transport run passed in `transport-final.json`. Local retained evidence is under `artifacts/inno-candidate/guest/`.

The subsequent review added real file-write and registry-write failures to the durable candidate itself. The strengthened machine runner passed in `machine-recovery-v9`, followed by the final normal-mode signing proof in `machine-signing-v2` (`signing-proof-v6`). The equivalent current-user runs **passed** in `SPCECandidateRecovery-v3` and `SPCECandidateSigning-v3`, including main-window launch, both real engine failures, both process-termination checkpoints, concurrent uninstall exclusion, and complete restoration of previous installation bytes/registration. Password-task batch logon was denied by Windows; the unused password task was removed and no batch-logon policy was changed. The successful final runs used interactive sessions. The catalog controls used signed `fixtures-v7`; the supplementary engine-failure controls use `fixtures-v8`. Catalog selection/transport code did not change between these runs.

| Check | Current user | Machine |
| --- | --- | --- |
| Recovery, finalization failure and concurrent uninstall | Passed | Passed |
| Supplementary real file-write / registry-write engine failures | Passed | Passed |
| Existing MSI preserved by automatic upgrade attempts | Passed | Passed |
| Explicit MSI removal then real-identity catalog installation | Passed | Passed |
| Inno explicit-scope / unscoped / `upgrade --all` | All three passed | All three passed |
| Normal-mode signing and exact installed uninstaller digest | Passed | Passed |
| Settings preservation and final uninstall | Passed | Passed |

Catalog cleanup removed the owned additional source/policy, both TLS bindings and certificate trust from My/Root. Final cleanup also removed the supplementary engine-test tasks. No candidate executable remained installed, and all six original settings files still matched their baselines. Evidence: `catalog-cleanup.json` and `final-cleanup.json`. `artifacts/inno-candidate/validation-summary.json` combines the final recovery/signing runs, catalog controls and source hashes. The final ISS and runtime test scripts match the copies tested in the VM.

Earlier failed attempts remain available. They exposed a 32-bit registry-tool view mismatch, live-log sharing defects in the test runner, a leftover staged replacement uninstaller, and the need to preserve the signed uninstaller's exact hash through integrated signing. Those failed attempts are not counted as passes. Recovery was used to restore the interrupted installation; an orphan from the earlier authoring was preserved as evidence rather than silently deleted. The final engine-failure fixtures also verify cleanup of an empty directory at a declared candidate-file path, without recursive deletion of unknown files. All fault hooks are compile-time fixtures; the normal build contains no runtime failure switch.

## Catalog and migration policy

A subsequent [focused explicit migration retest](EXPLICIT-MIGRATION-TEST.md) exercises the published MSI and the normal-mode signed Inno proof directly, without recreating a temporary catalog. It records its outcomes separately from the fixture catalog matrix below.

`New-InnoCandidateCatalog.ps1` retains the actual published MSI ProductCodes/UpgradeCodes and truthful `wix`/`inno` installer types. It does not fabricate an MSI AppsAndFeatures entry on the Inno candidate. Released WinGet rejects the unscoped cross-type upgrade with “the install technology is different”; explicit user scope also encounters the known MSI scope-correlation defect. In every tested user command the old MSI remained installed and unchanged. An `upgrade --all` exit code of zero alone is not treated as proof of migration.

The existing [explicit migration procedure](../winget/CURRENT-USER-MIGRATION.md) remains necessary for MSI installations. Preserve verified recovery installers and back up settings first. Remove the actual MSI by exact ProductCode without the misleading current-user scope filter, verify complete removal, then install the Inno package in the intended scope. Removal and installation are separate operations, not one rollback transaction. The Inno installer also independently rejects a visible CE/historical MSI.

Future Inno-to-Inno upgrades can use `UpgradeBehavior: install` without removing the working old version first. The upstream WinGet scope correction remains useful for existing MSI installations; this new packaging does not require replacing the user's installed WinGet binary.

For a fresh disposable catalog setup, stage `catalog-v1`, `signed-output-v1` and the build scripts under `C:\SPCEInnoCandidate`, then manually run `Setup-InnoReviewCatalog.ps1 -Profile Candidate` as administrator. Later fixture revisions use separately retained directories and an updated owned server task. Use `Remove-InnoReviewCatalog.ps1 -Profile Candidate` for the owned source, certificate, bindings and server task. Certificate trust creation remains a manual step because automatic approval review previously rejected that action.

## Release gates

Do not overwrite or relabel a published 1.8.3 asset. A new release still needs an actual new application version, a frozen reviewed source commit, a normal-mode signed build from that version, final artifact hashes/manifests, and release review. The legacy MSI release/manifest helpers do not generate an Inno release manifest.

The evidence covers handled finalization failure and abrupt process termination in this Windows 11 VM. It does not establish every hardware power-loss scenario, disk exhaustion, Windows 10 behavior, or public WinGet ingestion. Recovery after an interruption is completed by retrying the reviewed installer; it is not a background recovery service. Old payloads remain until final uninstall and consume disk space. Keep automatic upgrades of the existing MSI unapproved, and keep publication disabled until the remaining release gates are satisfied.
