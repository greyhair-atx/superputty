# WinGet staging — not submitted

`ChrisThornton.SuperPuTTYCommunityEdition/1.8.0/` contains a multi-file WinGet manifest generated from the actual signed MSI bundle. Both `Scope: user` and `Scope: machine` are explicitly represented under separate x64 installers. MSI ProductCodes and AppsAndFeatures UpgradeCodes are read from the finished packages, never guessed from the edition name. Publisher is Chris Thornton; certificate identity is Christopher Thornton.

```powershell
pwsh -NoProfile -File .\build\New-WinGetManifest.ps1 -Version 1.8.0
winget validate .\packaging\winget\ChrisThornton.SuperPuTTYCommunityEdition\1.8.0
```

The generated schema version is 1.10.0. `wix` is the MSI installer type; unattended switches are `/qn /norestart` and `/passive /norestart`. The direct HTTPS asset URLs point only to `greyhair-atx/superputty` and the published `sp-1.8.0` tag. **Both URLs are live.** On September 12, 2026, both MSIs were downloaded again: their SHA-256 values matched these manifests and their Authenticode signatures were valid. `winget validate` also passed. Installation through WinGet in a disposable guest remains pending. Schema validation does not prove installation or download success. Do not create a fork or pull request until the immutable approved artifacts are published and their downloadable hashes match.

## Windows Sandbox test plan

Enable Windows Sandbox on a supported Windows edition if available, or use a disposable Windows x64 VM. This is a manual environment setup step; no host feature or WinGet setting is changed by this repository. Copy the verified bundle, scripts and manifests into the disposable environment. Do not map real profile settings or credentials into it.

1. Verify signatures and hashes inside the guest. Run `Test-InstallerLifecycle.ps1` in an elevated guest session with `SUPERPUTTY_DISPOSABLE_TEST=1`; see [release preparation](../../docs/RELEASE-PREPARATION.md).
2. Independently test the current-user MSI from a standard (non-administrator) account. Confirm no elevation, expected install directory, Start menu entry, first launch, and uninstall.
3. Test the all-users MSI with elevation and launch it as a standard user. Check installed attribution, settings persistence and uninstall.
4. After the approved release URLs are live, enable local manifests **in the disposable guest only** if needed (`winget settings --enable LocalManifestFiles`, elevated), then run `winget install --manifest <directory> --scope user` and uninstall the exact package. Repeat in a fresh guest for `--scope machine`. Use `--disable-interactivity` when evaluating unattended behavior. Record logs, product registration and downloaded hashes.
5. Test same-scope upgrades from published CE 1.7.6 and the bounded historical community range, and blocking of an ambiguous upstream 1.5 installation. Restore fresh VM snapshots between scenarios.

Regenerate and revalidate these files after **any** rebuild or signing change. Candidate hashes are not a promise about future rebuilt artifacts. The tag/source commit must also match the approved final build. See the [official manifest reference](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest).
