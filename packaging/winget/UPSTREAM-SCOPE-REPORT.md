# Follow-up for microsoft/winget-cli#3011

Report text for https://github.com/microsoft/winget-cli/issues/3011.

## Reproduced on WinGet 1.29.380, with wrong-scope replacement after uninstall

We reproduced the per-user MSI scope mismatch with SuperPuTTY Community Edition on a disposable Windows 11 x64 guest (build 26200.9457), running WinGet 1.29.380 from a standard user's interactive session.

Independent native MSI checks of the published 1.8.3 current-user package show:

- `MsiEnumProductsEx`: exactly one user-unmanaged instance (context 2), owned by the calling user's SID; no machine-context instance (context 4).
- MSI `AssignmentType`: 0.
- Executable under `%LOCALAPPDATA%\Apps\SuperPuTTY`.
- Windows Installer's ARP entry is nevertheless in HKLM's 64-bit view.
- Direct `msiexec` installation gives the same result with default properties and with `ALLUSERS=2 MSIINSTALLPERUSER=1`.

WinGet inventories the earlier 1.8.0 current-user MSI as `ARP\Machine\X64\{7093D12F-0585-4E0C-A625-D2BB3867C54F}`, despite MSI AssignmentType 0. A representative REST 1.4 catalog built from our proposed manifests correlates that installation to `ChrisThornton.SuperPuTTYCommunityEdition` and advertises 1.8.3. Correlation also succeeds when only 1.8.3 is exposed in catalog metadata. This is a client-side REST-source reproduction, not a claim that the package is already in Microsoft's public catalog.

### Upgrade results

With `--scope user`, the upgrade finds no installed package. Without a scope filter:

```powershell
winget upgrade --id ChrisThornton.SuperPuTTYCommunityEdition --exact --source SPCEReview --silent --disable-interactivity --accept-package-agreements --accept-source-agreements --verbose-logs
```

WinGet selects the **all-users** MSI, removes the existing current-user installation, and then requests administrator credentials. The relevant output is:

```text
Downloading https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.3/SuperPuTTY-CE-1.8.3-all-users-win-x64-signed.msi
Successfully verified installer hash
Starting package uninstall...
Successfully uninstalled
Starting package install...
```

We canceled the elevation prompt without supplying credentials. Installation subsequently returned MSI error 1625 (WinGet exit `-1978334961`); neither version remained installed. This error followed deliberate cancellation, rather than proving a separate installer policy defect. Both existing settings files retained their original SHA-256 hashes. Providing credentials and completing a machine installation was not tested.

The reviewed candidate manifest has explicit user and machine installers and `UpgradeBehavior: uninstallPrevious`. The machine installer has `ElevationRequirement: elevationRequired`. The machine-scope control upgrade passes, including uninstall-first ordering and settings preservation.

### Public artifacts and reproduction tools

- Current-user MSI: https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.3/SuperPuTTY-CE-1.8.3-current-user-win-x64-signed.msi
- SHA-256: `5520A1F857AB84451D5E2F212CAC4805BA79FD18A02E8833F3E8ABC0CAFC5F36`
- Current-user ProductCode: `{24BC6BA3-E866-46FD-B950-8D6E16FE2FF0}`; UpgradeCode: `{3E4059D9-E5A5-48DE-B787-FC88268C1B95}`.
- Previous current-user MSI: https://github.com/greyhair-atx/superputty/releases/download/sp-1.8.0/SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi
- [Native context repro](https://github.com/greyhair-atx/superputty/blob/master/build/Test-PerUserMsiRegistration.ps1) (requires a dedicated disposable test account; see the investigation report).
- Catalog fixture: [converter](https://github.com/greyhair-atx/superputty/blob/master/build/New-WinGetReviewCatalog.ps1) and [server](https://github.com/greyhair-atx/superputty/blob/master/build/Start-WinGetReviewCatalog.ps1); [upgrade runner](https://github.com/greyhair-atx/superputty/blob/master/build/Test-WinGetUpgrade.ps1), methods `Catalog` and `CatalogUnscoped`.
- [Full findings](https://github.com/greyhair-atx/superputty/blob/master/packaging/winget/USER-SCOPE-INVESTIGATION.md) and [migration workaround](https://github.com/greyhair-atx/superputty/blob/master/packaging/winget/CURRENT-USER-MIGRATION.md).

### Requested correction

Could MSI installation context be resolved through the documented Windows Installer APIs for the current user and machine, rather than inferred solely from the ARP hive? The existing scope compatibility check should remain: relaxing it would allow an unintended change to machine installation.

A correction should handle user-managed/unmanaged contexts, actual machine installations, duplicate/ambiguous contexts, and API errors without accidentally attributing another user's installation to the caller. It should also consider the performance cost of inventory enumeration. This is a proposed correction direction, not a tested WinGet patch.

Acceptance cases are: user-scoped listing finds this MSI; ordinary and explicitly user-scoped upgrades select the user installer without elevation; machine upgrades remain machine-scoped; and an unresolved scope or incompatible installer is rejected before uninstalling the existing package. We can rerun the disposable-VM catalog matrix against a candidate client build.

Our interim workaround is an explicit uninstall by the old user ProductCode without a scope filter, followed by installation of the reviewed candidate with `--scope user`. That migration passed scope/payload/signature/version, settings-preservation and final-uninstall checks. It is not an automatic catalog upgrade pass or a transactional recovery implementation.
