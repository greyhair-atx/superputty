# Automatic current-user upgrade recovery investigation

Automatic current-user upgrades are not approved for production yet.

The corrected experimental WinGet client passed scope selection and catalog upgrade tests described in [WINGET-CLIENT-SCOPE-FIX.md](WINGET-CLIENT-SCOPE-FIX.md). Those tests do not establish recovery after an installer failure. Published 1.8.3 uses `uninstallPrevious`, which removes the old package in a separate operation.

## Failure-injection result

An unsigned, test-only 1.8.4 user MSI moved RemoveExistingProducts inside the transaction (afterInstallInitialize) and rejected upgrades when RollbackDisabled was set. A separate fixture injected a deliberate failure after InstallExecute. No published binary was changed.

On the disposable Windows 11 VM, running as the interactive standard account:

- The old signed 1.8.3 package installed successfully.
- Old-package removal and the replacement execution script completed before the deliberate failure returned 1603.
- Rollback restored the signed old executable, but left the old product advertised (ProductState 1), rather than installed (5).
- The MSI log recorded access-denied errors while restoring Windows Installer registration. Both Limited and Highest task token probes were non-administrative and unrestricted; this does not establish the underlying cause.
- The regression correctly failed. A successful normal upgrade and the RollbackDisabled rejection scenario were not reached/validated.

Candidate ProductCode: `{C7D19D83-D43E-4AA8-8DDA-38C44BA69EF3}`. Candidate SHA-256: `EA1D7A700AE7B9FC67873BEA3B6ED6F35664115B507ADFAE3E0AE52E4BE53F0B`.

The first experimental source changes are retained in [upstream/transactional-upgrade-experiment.patch](upstream/transactional-upgrade-experiment.patch), not enabled in the production installer or manifest generator. The structural preflight checks sequencing only; passing it does not certify runtime recovery. The three new build scripts preserve the fixture construction, structural check and disposable standard-user regression.

Local evidence: `artifacts/winget-scope-fix/guest/rollback/` and VM `C:\SPCECatalogReview\transactional-fixture-v4`. Supported MSI reinstall/uninstall recovered the VM; final recovery returned 0 for both operations, old ProductState -1 and executable absent. No private Windows Installer registry edits were used.

## Required before approval

1. Diagnose the registration rollback errors and validate an MSI transaction schedule that restores the complete previous installation after failure.
2. Pass the forced-failure regression, successful replacement, disabled-rollback rejection, scope isolation and settings-preservation checks on a clean VM. Improve failed-test cleanup to handle advertised registrations before reusing the runner unattended.
3. Repeat catalog integration with a corrected released WinGet client, including unattended `upgrade --all`, incompatible candidates and failure recovery. The upstream client correction is still experimental.
4. Only then create a new signed release and review catalog behavior allowing MSI to perform its own upgrade transaction. Do not change immutable published 1.8.3 artifacts or assume `UpgradeBehavior: install` alone fixes recovery.

Retain the explicit migration workaround until these checks pass. Nothing was committed, pushed or released during this investigation.

## Follow-up implementation and VM verification

The second candidate used `afterInstallExecute`: InstallExecute 6500, RemoveExistingProducts 6501, injected failure 6502, InstallFinalize 6600. The complete Component table matched the signed 1.8.3 package exactly (including GUIDs, directories, attributes, conditions and key paths). This candidate still failed restoration of the old MSI registration with registry access-denied errors. A repeated forced-failure run confirmed the failure while the improved runner successfully recovered/removed the disposable installation through supported MSI operations; settings remained unchanged. Successful cleanup is not counted as successful rollback.

The separate disabled-rollback scenario passed: the authored launch condition rejected the candidate before removal, the old installed registration and executable remained unchanged, settings were preserved and cleanup passed. This defensive condition is retained in Product.wxs. It does not repair rollback when rollback is enabled. The unsuccessful transaction scheduling is kept only in [upstream/transactional-upgrade-after-execute-experiment.patch](upstream/transactional-upgrade-after-execute-experiment.patch), against the repository HEAD used for the experiment. Production MajorUpgrade scheduling and WinGet manifest behavior remain unchanged.

The fixture builder now supports an existing InstallExecute action and injects the deliberate failure after both InstallExecute and RemoveExistingProducts, before finalization. The runner rejects installed or advertised starting products, handles advertised-product cleanup, and recovers stranded ownership by reinstalling the signed old MSI before uninstalling. No private MSI registration/ACL editing or manual payload deletion is used.

Evidence: `artifacts/winget-scope-fix/guest/rollback-v5/` (later sequence), `rollback-v6/` (disabled rollback), `rollback-v7/` (failed recovery with successful automatic cleanup), and `rollback-v8/` (disabled-rollback guard with final production sequencing). The candidate version 1.8.4 was an unsigned test artifact only; no release version was changed.

### Related upstream limitation

[WiX issue #8574](https://github.com/wixtoolset/issues/issues/8574) remains open and describes per-user registration rollback failures. A recent independent reproduction reports restored files but broken subsequent removal on several Windows versions, including WiX 6.0.2. That supports investigating beyond this application's component authoring; it does not prove whether WiX or Windows Installer causes our failure. The attached Advanced Installer comparison fails before InstallExecute, so its apparent success cannot establish recovery after executed changes.

A complete fix has **not** been implemented or validated. The next engineering step is a minimal reproduction and comparison of registration rollback operations, with upstream investigation of #8574. If MSI cannot provide reliable non-elevated recovery, a separately designed installer/recovery mechanism will be needed. Moving RemoveExistingProducts after InstallFinalize would move old removal outside the transaction; it is not a complete recovery fix and was not adopted.

