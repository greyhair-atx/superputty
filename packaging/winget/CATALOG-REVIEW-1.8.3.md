# Catalog upgrade review: 1.8.0 to 1.8.3

October 31, 2026. No release, public catalog or submission branch was changed.

## Result

Catalog correlation succeeds: WinGet recognizes the installed 1.8.0 MSI as `ChrisThornton.SuperPuTTYCommunityEdition`, including when the catalog exposes only 1.8.3. Machine-scope upgrades pass and explicitly uninstall 1.8.0 before installing 1.8.3.

Current-user upgrades with `--scope user` fail because WinGet inventories the installed current-user MSI under machine scope. The catalog resolves the package identity, but the scope filter excludes the installed product. This is separate from the earlier local-manifest identity-matching failure. Do not mark the complete two-scope catalog upgrade lifecycle as passed.

| Catalog visibility | Requested scope | Identity correlation | Upgrade result |
| --- | --- | --- | --- |
| 1.8.0 and 1.8.3 | machine | Passed: installed 1.8.0, available 1.8.3, correct package ID | Passed: explicit uninstall then install, expected MSI assignment/hash/signature/version, four settings files preserved, final uninstall passed |
| 1.8.0 and 1.8.3 | user | Passed: installed 1.8.0, available 1.8.3, correct package ID | Failed before upgrade: `--scope user` finds no installed package; cleanup succeeded and two settings files were preserved |
| 1.8.3 only | machine | Passed without publishing the old ProductCodes in catalog search metadata | Passed: explicit uninstall then install, expected MSI assignment/hash/signature/version, four settings files preserved, final uninstall passed |
| 1.8.3 only | user | Passed: installed 1.8.0, available 1.8.3, correct package ID | Failed before upgrade with the same scope mismatch; cleanup succeeded and two settings files were preserved |

The successful machine upgrade logs contain, in order:

```text
Starting package uninstall...
Successfully uninstalled
Starting package install...
Successfully installed
```

The current-user MSI has Windows Installer AssignmentType 0, but its uninstall entry is in HKLM and WinGet inventories it as `ARP\Machine\X64\{7093D12F-0585-4E0C-A625-D2BB3867C54F}`. The earlier 1.8.3 clean-install evidence also records its user ProductCode in HKLM. WinGet's installed-package source selects the scope from the registry hive; its installer comparator requires the new installer scope to match the installed scope. Omitting `--scope user` was not tested as a workaround and must not be claimed to preserve the user installation scope.

Next investigate why the current-user MSI registers there, validate its actual Windows Installer context as well as AssignmentType, and determine the migration needed for existing installations. Do not alter the real ProductCodes, invent a machine scope for the user installer, or treat package identity correlation as proof that scope selection works.

## Test setup and limits

Tests ran on the authorized disposable `Win11-Test` guest, WinGet 1.29.380. Machine tests ran elevated; user tests ran in the dedicated standard account's interactive RDP session. Installations started from the exact published signed 1.8.0 packages through local manifests. Candidate metadata and signed 1.8.3 download URLs came from the proposed checked-in manifests. No signed binaries were rebuilt.

`build/New-WinGetReviewCatalog.ps1` converts the two reviewed manifest sets to the REST representation, flattening installer defaults. It is a fixture for these manifest shapes, not a general YAML converter. `build/Start-WinGetReviewCatalog.ps1` serves a minimal WinGet REST 1.4 catalog over guest-local HTTPS. The client uses the real `Microsoft.Rest` source implementation. Search filtering respects published versions, ProductCodes and UpgradeCodes; the latest-only fixture was checked to reject an old ProductCode and accept its stable UpgradeCode. An additional-source machine policy made the same endpoint available to both test accounts.

`build/Test-WinGetUpgrade.ps1 -UpgradeMethod Catalog -CatalogSource SPCEReview -ResultName <unique-name>` first checks `winget list --id ... --source SPCEReview`, then runs `winget upgrade --id ... --source SPCEReview --scope <scope>` and verifies product registration, scope, candidate payload and settings. English WinGet output is used to assert uninstall-first ordering. Distinct result names retain all scenarios.

This validates client-side correlation and upgrade behavior against a representative REST catalog. It does not certify Microsoft's preindexed catalog build, publishing pipeline, security scans or moderator approval.

## Evidence

Local evidence is retained under `artifacts/winget-catalog-review/guest/`, including per-scenario summaries, operation logs, MSI logs, settings baselines, client diagnostics, catalog request/response logs, and the exact generated catalog metadata. The request logs also record which versions were visible for each request. The complete test environment is temporary; its source policy, local-manifest policy, HTTPS bindings, certificate trust and scheduled tasks are removed after testing. The existing temporary standard account remains available for review.

References: [WinGet REST reference implementation](https://github.com/microsoft/winget-cli-restsource), [REST 1.4 client tests](https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerCLITests/RestInterface_1_4.cpp), [scope comparison](https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerCommonCore/Manifest/ManifestComparator.cpp), [ARP inventory](https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerRepositoryCore/Microsoft/ARPHelper.cpp).
