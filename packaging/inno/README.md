# Inno Setup packaging evaluation

The subsequent durable-recovery authoring, signing workflow and real-identity migration tests are documented in [the candidate review](CANDIDATE-REVIEW.md). Results below describe the earlier isolated prototype.

This is an isolated prototype, not a replacement release. Builds are unsigned by default; two normal fixtures were subsequently signed privately for catalog testing. Existing MSI releases and the [tested WinGet migration workaround](../winget/CURRENT-USER-MIGRATION.md) remain unchanged. Automatic current-user upgrades of the existing MSI remain unapproved.

## Design

One EXE supports `/CURRENTUSER` and `/ALLUSERS`; machine mode requires elevation. The prototype has its own AppId `{3C8A31DE-74CA-48B7-889B-F44B19DE8714}`, display name and `SuperPuTTY-CE-Prototype` directory. It cannot be substituted into the public MSI manifest. Packaging versions `1.8.4.100` and `1.8.4.101` are disposable fixtures, not application release versions; the executable remains the published signed 1.8.3 application.

Payloads install into immutable version directories. A successful upgrade changes the shortcut and registration to the new version while retaining old payloads until final uninstall. Handled failures before commit preserve the previous payload. The script backs up/restores the previous shortcut because overwriting a shortcut must not destroy the old launch path on a failed attempt. Old-version removal is not part of an upgrade.

Windows Installer enumeration blocks any visible CE or historical MSI from being silently replaced. The prototype does not uninstall an MSI automatically. Migration requires obtaining and verifying recovery installers, backing up settings, explicit removal by the actual ProductCode, verification of removal, and a separate installation. This is not a transaction spanning the old MSI and new EXE; a failure in that removal step retains the existing MSI recovery limitation.

The failure fixtures are compiled separately. One creates a directory/file collision after candidate files were written; the other uses an invalid registry key component after candidate files and the shortcut were written. These produce real engine failures and rollback. An initial experiment using callback exceptions was rejected because Inno logged the exception but completed setup; it is not counted as a recovery pass. No runtime failure switch is included in the normal fixture.

## Reproduction

Inno Setup 7.1.1 x64 was downloaded from its official release, Authenticode-verified as Pyrsys B.V., and installed only in the disposable VM. Review [Inno's license information](https://jrsoftware.org/isorder.php) for the intended use before adopting it for distribution.

Extract the immutable published 1.8.3 portable ZIP. The build script verifies archive SHA-256 `653773EC9E3F126684CA9DAD8BCACDF833984047C3E4795902E66FCB2A4E6263`, the complete extracted file set/bytes and the application's signature before compiling.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/Build-InnoPrototype.ps1 `
  -Compiler 'C:\SPCEInnoReview\Compiler\ISCC.exe' `
  -PayloadDirectory 'C:\SPCEInnoReview\payload' `
  -PortableArchive 'C:\SPCEInnoReview\payload.zip' `
  -OutputDirectory 'C:\SPCEInnoReview\new-output'
```

`-VerifyOnly` checks the input without compiling. Four fixtures and `prototype-hashes.json` are produced. Place the signed 1.8.3 current-user MSI as `previous.msi` alongside them for the migration test. In the authorized VM, set `SUPERPUTTY_DISPOSABLE_TEST=1` and run `Invoke-InnoPrototypeMatrix.ps1` with a new StageRoot, the InstallerDirectory and the installed WinGet executable path. Use `-Scope user` from the dedicated standard account's interactive session and `-Scope machine` from an administrator. Direct tests require no custom catalog. `-TestSet Catalog` exercises explicit, unscoped and `upgrade --all` commands against only the temporary `SPCEInnoReview` source.

`New-InnoReviewCatalog.ps1` generates a REST fixture. `Setup-InnoReviewCatalog.ps1` is a manual disposable-VM administrator setup for temporary localhost HTTPS trust and the source policy. `Start-WinGetReviewCatalog.ps1 -InstallerDirectory ...` serves only the two normal prototype filenames listed in the pinned hash manifest; failure fixtures and arbitrary paths are not downloadable. Without that option, existing MSI catalog testing stays metadata-only.

## Validation and remaining gates

The initial direct user and machine runs passed: successful fresh/same-scope upgrade, injected file-write and registry-write failures preserving the old executable/payload, shortcut, uninstaller and registration version, downgrade rejection, settings preservation and final uninstall. The user run also rejected an existing signed 1.8.3 MSI unchanged, then passed explicit MSI removal followed by fresh prototype installation. Two existing settings files were preserved for the standard user and four for the administrator.

The strengthened runner additionally compares the complete old uninstall registry values and uninstall ownership logs, and checks installed scope through the released WinGet client. Results and catalog outcomes are recorded below after completion. Local evidence lives under `artifacts/inno-prototype/`; guest stages are under `C:\SPCEInnoReview`.

The strengthened direct runs subsequently passed for both scopes on November 1, 2026, including exact uninstall registry/log preservation and released WinGet `v1.29.380` inventory. User inventory reported `ARP\User\X64\{3C8A31DE-74CA-48B7-889B-F44B19DE8714}_is1`; scope selection for this EXE does not reproduce the MSI's hive mismatch. Direct matrix stages are `direct-user-v6` and `direct-machine-v6`. The compiled ISS source SHA-256 is `ABDD94F3F623C763E9FB58D841721D388D5AF8B8E508BB5CA1B4510BA4219197`; the local and guest source hashes match.

After the administrator manually prepared localhost trust, the first catalog attempts exposed a fixture transport defect: a canceled download probe terminated the listener. The server now supports HEAD, byte ranges, invalid-range rejection, and continued service after a canceled response. `Test-InnoReviewCatalogTransport.ps1` passed those checks, full-download hash verification, and rejection of a failure-fixture download. The original failed attempts are retained, not counted as upgrade passes.

The unsigned candidate then waited at Windows SmartScreen. That attempt was stopped, its old installation was cleaned up, and no Windows security policy was weakened. Only the two normal installers were Authenticode-signed and RFC3161-timestamped using the project's existing signing profile; failure fixtures remain unsigned. Updated hashes are pinned in a separate private catalog (`catalog-v2`, fixtures `signed-output-v1`). No production package, release version, tag or asset was changed.

The signed current-user catalog matrix **passed all three commands** on released WinGet 1.29.380: explicit `--scope user`, unscoped package-specific upgrade, and `upgrade --all --source SPCEInnoReview`. Each scenario freshly installed the old fixture, selected/installed the candidate without interaction, verified correct registry scope, signed payload, shortcut target, settings preservation, downgrade rejection and final uninstall. Two existing settings files remained unchanged. `--all` was restricted to the one-package disposable source; it did not upgrade unrelated software. Evidence: `catalog-signed-user-v1`.

The initial signed machine control stalled at installer launch in a noninteractive WinRM session and was stopped. Its cleanup and all four settings hashes passed. After the administrator signed in through RDP, the elevated interactive retry **passed all three machine catalog commands**. Evidence: `catalog-signed-machine-v2`. The session-dependent launch failure is retained as a failed environment attempt, not counted as an upgrade pass.

| Scope | Explicit scope | Unscoped package upgrade | `upgrade --all` for test source |
| --- | --- | --- | --- |
| Current user, standard interactive account | Passed | Passed | Passed |
| Machine, elevated interactive administrator | Passed | Passed | Passed |

All six final catalog scenarios preserved settings and passed final uninstall. The machine cases preserved four settings files; the user cases preserved two. The combined local evidence is `artifacts/inno-prototype/catalog-matrix-summary.json`. This covers the Inno-to-Inno prototype transition with both versions in a private REST catalog; it does not approve automatic MSI-to-Inno migration or public catalog ingestion.

After catalog tests, run `Remove-InnoReviewCatalog.ps1` as administrator to remove only the owned source/task/certificate and TLS bindings. Setup checks for pre-existing policy/bindings/tasks and is intentionally one-use; it does not overwrite an existing environment. Keep the cleanup evidence and setup marker rather than rerunning it blindly.

Cleanup completed after the final matrix. Both temporary TLS bindings, the owned certificates from My/Root, additional-source policy/source, and every temporary test/catalog task were removed. Effective administrator source listing no longer contains the review source. No prototype executable remained installed, and all six existing preference/session/layout files still matched their baselines. Evidence: `guest/catalog-cleanup.json` and `guest/final-cleanup.json`. Prepared fixture files, compiler and the review accounts remain available in the disposable VM; no installed production WinGet binary was replaced.

These tests do not establish recovery after power loss/process termination, failure after Inno finalization, disk exhaustion, locked/running application files or every registry failure. The versioned layout retains old payloads and therefore consumes additional disk space. Launch behavior from the versioned directory, concurrent user/machine installations, the production signing workflow, Windows 10 and public WinGet catalog ingestion require separate validation. A production candidate also needs reviewed upgrade/repair behavior for repeated versions and interrupted attempts.

Do not change the published package identifier or public manifests to this prototype. First review the recovery design and its boundaries, complete the migration/catalog/runtime tests, choose a production identity and scope policy, and only then prepare a new signed release for approval. Inno's documentation explicitly places a commit boundary at uninstaller finalization; switching authoring tools alone cannot promise complete recovery for every later failure. See [installation order](https://jrsoftware.org/ishelp/topic_installorder.htm).
