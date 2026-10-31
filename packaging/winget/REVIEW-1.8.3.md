# Local WinGet review: 1.8.3

Review date: October 31, 2026. Changes remain local and unpushed.

Follow-up: the [catalog upgrade review](CATALOG-REVIEW-1.8.3.md) resolves the package identity question. Machine-scope catalog upgrades pass, including uninstall-first behavior. Current-user catalog upgrades expose a separate installed-scope mismatch and remain unsuccessful. The local-manifest failures below are retained as historical evidence.

## Proposed manifest changes

- `MinimumOSVersion: 10.0.19041.0`.
- `UpgradeBehavior: uninstallPrevious`.
- `ElevationRequirement: elevationRequired` on the machine-scope installer only.

The generator produces the same fields. The three manifest schemas remain 1.28.0; version-specific privacy and license links remain intact. Signed MSI URLs, hashes, ProductCodes and UpgradeCodes are unchanged.

Both PowerShell 7 and Windows PowerShell 5.1 generated manifests matching the checked-in files after line-ending normalization, including validation of the original MSI hashes and Authenticode signatures. Local `winget validate --manifest` passed.

## Disposable guest results

Tests used the authorized Windows 11 evaluation guest `Win11-Test`, WinGet 1.29.380, the published installers and corrected local manifests. Current-user operations ran as a dedicated non-administrator account in an interactive RDP session. Machine operations ran elevated as the guest's test administrator.

| Test | Result | Evidence / limits |
| --- | --- | --- |
| Clean user installation and WinGet uninstall | Passed | Correct MSI AssignmentType, executable SHA-256/signature/version, branding, installed license and shortcut; four preference/session/layout files preserved; registration and executable removed. |
| Clean machine installation and WinGet uninstall | Passed | Correct MSI AssignmentType, executable SHA-256/signature/version, branding, installed license and shortcut; registration and executable removed. The initial machine clean test had no pre-existing settings. |
| User MSI upgrade from 1.8.0 to 1.8.3 | Passed | Candidate installed using `winget install --manifest --scope user`; MSI removed the previous ProductCode. Candidate scope/hash/signature/version and preservation of four settings files checked; final WinGet uninstall passed. This does not establish WinGet uninstall-first behavior. |
| Machine MSI upgrade from 1.8.0 to 1.8.3 | Passed | Candidate installed using `winget install --manifest --scope machine`; old ProductCode removed, new scope/hash/signature/version verified, four settings files preserved, final WinGet uninstall passed. This also tests MSI upgrade behavior rather than WinGet uninstall-first. |
| Explicit `winget upgrade --manifest` | Unresolved in both scopes | WinGet reports no installed package matching input criteria, despite `winget list` showing the earlier package's ARP identity. Cleanup and all four settings-preservation checks passed. No WinGet `uninstallPrevious` pass is claimed. |
| First launch without PuTTY configured | Setup window opened | A responding `SuperPuTTY CE Options` window appeared. Closing the first-run setup dialog returned exit code 1, as implemented when initial configuration is cancelled. |
| Machine installation launched as a standard user | Passed | With the user-provided, signed PuTTY client configured and an empty launch-specific session folder, a responding `SuperPuTTY Community Edition` main window appeared and exited normally with code 0. Test settings disable exit confirmation and update checks for unattended execution. |
| User installation launched as a standard user | Passed | Same main-window responsiveness and normal code-0 exit checks, followed by successful WinGet uninstall. |
| User installation while machine package exists | Both scopes coexist | Explicit `winget install --manifest --scope user` succeeded with the machine 1.8.3 package already present. Both ProductCodes remained registered. User uninstall preserved the machine installation and all four settings files. Do not claim `uninstallPrevious` prevents cross-scope coexistence. |
| Final guest cleanup | Passed | No SuperPuTTY ARP registrations, executables or shortcuts remained in either scope. Temporary scheduled tasks and local-manifest policy removed; original batch-logon right restored. Temporary standard account and test evidence retained for review; the RDP session may remain open. |

The batch-session standard-user attempt failed before MSI execution with Windows Installer error 1601 and COM access denied. An interactive standard-user session resolved that environment issue; the clean lifecycle then passed. Failed attempts are retained separately from passing results.

The initial legacy session preservation fixture omitted `Username`, which caused the published executable's session display code to raise a null-reference error when that fixture was used for launching. The fixture now includes explicit empty Username and a PuTTY session name, and uses the actual `AutoUpdateCheck` setting name. The earlier byte-preservation assertions remain valid for installer testing; launch smoke tests use separate empty session settings. Application source and signed release binaries were not changed.

Local evidence is retained under `artifacts/winget-1.8.3-review/guest/`: `results` holds the final summaries and logs, and `attempts` retains failed environment/fixture/argument attempts and the unresolved WinGet upgrade tests. The historical controller summary records the failed bootstrap attempt, not the final manual orchestration result; use the per-test summaries and `results/final-cleanup.json`. Launch and opposite-scope scripts and the exact staged release metadata are included with the evidence. PuTTY was provided by the user at `C:\Program Files\PuTTY\putty.exe` and its Authenticode signature was Valid.

Package matching through a published or representative test catalog still needs verification. The local-manifest matching failure alone does not establish which production manifest field, if any, needs correction. Do not substitute an MSI upgrade pass for verification of WinGet's uninstall-first workflow. The reverse opposite-scope scenario, minimum-Windows execution, and the complete historical upgrade matrix also remain unverified.

## Test runner setup

`build/Test-WinGetScopes.ps1` now reads exact candidate identities from a staged `test-release.json` instead of hardcoding 1.8.0. `build/Test-WinGetUpgrade.ps1` uses the same file and separate `manifests` and `previous-manifests` directories. It defaults to `-UpgradeMethod WinGet`; `-UpgradeMethod Installer` explicitly tests MSI upgrade behavior using `winget install`. Summaries identify the chosen method. Both runners require `SUPERPUTTY_DISPOSABLE_TEST=1` and enforce the expected elevation; user tests require a dedicated `SPCEtest` account.

Example staged metadata (use identities extracted from the actual signed release packages):

```json
{
  "version": "1.8.3",
  "executableSha256": "F123253CCAFC519C4D925A0B6F0463FA406253FA69F3D2296E1750CDE7ACAC31",
  "packages": [
    { "scope": "user", "productCode": "{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}" },
    { "scope": "machine", "productCode": "{A339D1A0-E398-46D4-A1A3-1CE0477E69BE}" }
  ],
  "previous": {
    "version": "1.8.0",
    "packages": [
      { "scope": "user", "productCode": "{7093D12F-0585-4E0C-A625-D2BB3867C54F}" },
      { "scope": "machine", "productCode": "{C1A2912D-A780-4AE3-92C3-89A2366C3608}" }
    ]
  }
}
```

Preserve operation logs, summaries and settings baselines before any retry. Older staged controllers need this metadata file before using the updated scope runner. Never run these scripts against an installed developer copy.
