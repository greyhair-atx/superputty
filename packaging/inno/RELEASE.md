# Inno release preparation

The 1.8.4 release path is `azure-pipelines.yml` → `build/New-InnoReleaseBundle.ps1`. It builds the application/test projects directly, excluding the historical WiX project. The old MSI scripts remain for recovery/history and are not the new release path.

Default CI produces an unsigned `portable-dev` bundle only. Enabling code signing requires the configured Azure service connection, Artifact Signing task extension and permission to the existing signing profile. Both application and setup/uninstaller are signed and timestamped. The pipeline retains review artifacts; it never creates release tags, public GitHub/Gitea releases or WinGet submissions.

`build/InnoToolchain.psd1` pins the official Inno SDK installer and compiler SHA-256 and signing client version. `Install-InnoToolchain.ps1` verifies the SDK publisher/hash before running it. `-VerifyOnly -Installer <cached official SDK>` checks without installation. Review upstream release information before changing any pin.

After building and signing `bin/x64/Release/SuperPutty.exe`:

```powershell
./build/New-InnoReleaseBundle.ps1 -Version 1.8.4 -OutputDirectory <new-bundle-dir> -WorkDirectory <new-private-work-dir> -Compiler <pinned-ISCC.exe> -SigningTool <signtool.exe> -SigningDlib <Azure.CodeSigning.Dlib.dll> -SigningMetadata <metadata.json>
./build/Verify-InnoReleaseBundle.ps1 -ReleaseDirectory <bundle-dir> -RequireInstaller
./build/Test-InnoReleaseTooling.ps1 -ReleaseDirectory <bundle-dir> -ResultDirectory <new-test-dir>
./build/New-InnoWinGetManifest.ps1 -ReleaseDirectory <bundle-dir> -ManifestDirectory <new-review-dir>
```

Use fresh output/work/cache directories, preserve earlier evidence, and keep private signing/work files outside distributable assets. The compiler's initial unsigned-uninstaller seed pass is expected to fail; the builder signs that cache and substitutes its exact pinned bytes into the final installer. Fault hooks require `-TestFixture` and are forbidden in a release bundle.

Bundle verification includes hidden entries and rejects all unlisted files/directories and reparse points. It checks checksums, runtime inventory/SBOM, version consistency, timestamps and project signatures. Source dirty status must be an explicit Boolean; the clean-source gate accepts only false. Checksums do not authenticate a source by themselves. Build metadata starts all acceptance gates as pending. A signed build alone is not release approval.

Lifecycle tests call `build/Verify-InnoPayload.ps1` after installation and rejected reinstall to compare every installed payload file and the internal payload manifest against the verified SBOM. Hidden extras, unexpected directories, reparse points, missing files and changed bytes fail validation. The CI diagnostics artifact retains regression, tooling and lifecycle outputs after either success or failure; candidate publication still requires success.

Run the [candidate test plan](../../docs/releases/1.8.4-TEST-PLAN.md), review and commit, then rebuild from a clean commit. `Verify-InnoReleaseBundle.ps1 -RequireCleanSource -RequireInstaller` rejects a dirty preparation build. Manually approve publication only after final-byte validation and acceptance results. Regenerate WinGet review files after every rebuild; URLs for an unpublished release intentionally do not exist yet.
