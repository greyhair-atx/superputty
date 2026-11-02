# Current-user migration while WinGet scope detection is unresolved

Use the existing separate user and machine MSI packages. Automatic current-user WinGet upgrades remain unapproved on the tested client (1.29.380). Neither dropping `--scope user` nor adding explicit per-user MSI properties corrects its installed-scope detection. See the [investigation](USER-SCOPE-INVESTIGATION.md).

## Tested path

The disposable standard-account test passed the following sequence for **1.8.0 current-user to 1.8.3 current-user**:

1. Uninstall the old MSI by its exact ProductCode, without a scope filter.
2. Install the reviewed 1.8.3 local manifest with `--scope user`.
3. Verify old ProductCode removal, new user AssignmentType 0, executable hash/signature/version, and unchanged settings. Final uninstall also passed.

The exact operations, run from the original user's non-elevated session after preparing the manifests, are:

```powershell
winget uninstall --product-code '{7093D12F-0585-4E0C-A625-D2BB3867C54F}' --exact --silent --disable-interactivity --accept-source-agreements
if ($LASTEXITCODE -ne 0) { throw 'Uninstall failed; inspect logs before continuing.' }

# Run from the repository root; local manifests must be enabled by an administrator.
winget install --manifest .\packaging\winget\ChrisThornton.SuperPuTTYCommunityEdition\1.8.3 --scope user --silent --disable-interactivity --accept-package-agreements --accept-source-agreements
if ($LASTEXITCODE -ne 0) { throw 'Installation failed; retain settings and use the recovery installer.' }
```

This ProductCode applies only to the published **1.8.0 user MSI**. Other versions require their actual MSI identities. Do not use this sequence to convert a machine installation or another user's installation. Do not use `--scope user` on the uninstall command: the faulty inventory filter hides the old package.

## Preparation and recovery

Before uninstalling, close SuperPuTTY, back up `SuperPuTTY.settings` and the configured settings directory (sessions/layouts), and obtain both the signed candidate user MSI and the previous user MSI for recovery. Verify their signatures and hashes against the reviewed release metadata before removing the installed application. WinGet's manifest install performs its own hash check, but should not be the only plan for obtaining an installer after removal.

The candidate current-user MSI has SHA-256 `5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36` and ProductCode `{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}`. Its published file is `SuperPuTTY-CE-1.8.3-current-user-win-x64-signed.msi` under release `sp-1.8.3`.

If the candidate cannot be installed, preserve the settings and logs and use the verified previous user installer from the same non-elevated account. The migration sequence is not transactional: restoration after an injected installation failure has not been tested. An automated migration/recovery wrapper must pass those tests before being advertised as safe unattended recovery.

## Gate for resuming automatic upgrades

After a candidate WinGet correction is available, repeat both scoped and unscoped catalog upgrades in a standard interactive account, with both-version and latest-only catalog visibility. Require the user installer, no elevation, correct native MSI user context, old-version removal only as part of a valid upgrade, unchanged settings, and final uninstall. Repeat machine controls and test failure before installation, ambiguity, and cancellation without losing the old installation. Record the exact corrected client version before changing the approval status.

Correct scope selection is insufficient by itself: the existing MSI also failed the executed-script rollback tests. Require complete restoration of the old installed registration and files after failure before approving unattended MSI upgrades.

## Qualified private Inno path

The [Inno candidate review](../inno/CANDIDATE-REVIEW.md) records a separately qualified packaging path. On released WinGet 1.29.380, both user and machine tests correlated the existing signed 1.8.3 MSI under the real package identity. Automatic cross-type upgrade attempts preserved the MSI; exact ProductCode removal followed by explicit-scope catalog installation succeeded. Subsequent Inno-to-Inno upgrades passed explicit-scope, unscoped and source-restricted `upgrade --all` commands in both scopes, including settings preservation and final uninstall. The new installer also passed durable recovery and signing checks.

These are private packaging fixtures, not a published replacement release. The existing MSI commands above remain version-specific. Do not advertise automatic MSI-to-Inno conversion or substitute a private fixture into the public manifest. A new signed Inno release and reviewed manifests are still required; after that initial explicit migration, future Inno upgrades can retain the working previous payload during installation.
