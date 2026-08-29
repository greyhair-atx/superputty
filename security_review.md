# Security Review

Review date: August 28, 2026
Reviewed version: SuperPuTTY 1.6.1 (`38731ae`)
Scope: application source, dependencies, build outputs, installer, Git history, and GitHub repository security

This was a read-only review. No application source, configuration, release, or repository settings were changed during the audit.

## Clean results

- NuGet reported no known vulnerable direct or transitive packages in the application, unit-test, or installer projects using the configured advisory sources.
- Gitleaks found no committed secrets across 578 commits.
- Semgrep reported no findings after running 29 C# security rules against 169 tracked files.
- GitHub reported zero open CodeQL, Dependabot, or secret-scanning alerts.
- The Release executable is x64 and has high-entropy ASLR, dynamic-base ASLR, and NX compatibility enabled.
- The application manifest requests `asInvoker`; SuperPuTTY does not automatically request administrative elevation.
- `SessionData.Password` is excluded from XML serialization. Passwords can still be persisted when a user explicitly places `-pw` in `ExtraArgs`, for which the application displays a warning.
- No `BinaryFormatter`, unsafe C# blocks, obvious insecure cryptographic primitives, or committed private-key files were found.
- Session-file saves use a same-directory temporary file, flush data before replacement, and preserve the prior file if serialization fails.
- The MSI installs application files under native 64-bit Program Files and runs its optional post-install application launch as the installing user.
- Trivy reported seven vulnerabilities only in stale, untracked .NET 10 test-build output. Those generated files are not part of the tracked .NET Framework 4.8 product or the published MSI and are not applicable to release 1.6.1.

## High-priority findings

### 1. Credentials can be written to logs or exposed in process arguments

The raw SuperPuTTY invocation is logged without removing a supplied `-pw` value:

- [`CommandLineOptions.cs`](SuperPutty/Utils/CommandLineOptions.cs#L62) logs all arguments when parsing fails.
- [`CommandLineOptions.cs`](SuperPutty/Utils/CommandLineOptions.cs#L69) logs the complete normal command line.
- [`App.config`](SuperPutty/App.config#L38) persists logs under `%TEMP%\SuperPuTTY.log`.

Additional password-exposure paths exist:

- [`VNCStartinfo.cs`](SuperPutty/Utils/VNCStartinfo.cs#L49) always places the VNC password on the child-process command line.
- [`PuttyStartInfo.cs`](SuperPutty/Utils/PuttyStartInfo.cs#L91) can log that complete VNC argument string.
- [`PscpClient.cs`](SuperPutty/Scp/PscpClient.cs#L245) adds a password to PSCP file-copy arguments without applying `AllowPlainTextPuttyPasswordArg`.
- [`PscpTransfer.cs`](SuperPutty/PscpTransfer.cs#L468) logs its complete password-bearing command. This legacy path appears unreachable in the current UI but remains compiled.

Passwords in process arguments can be observed by local process-inspection tools. Persistent logs can also expose them through diagnostics, backups, or support bundles.

Recommendations:

1. Redact `-pw`, `/p:`, VNC password options, and credential-bearing URLs before every log call.
2. Never log the original argument array from an exception path.
3. Apply `AllowPlainTextPuttyPasswordArg` consistently to PuTTY, PSCP, VNC, FileZilla, and WinSCP launches.
4. Prefer Pageant, key authentication, interactive password prompts, or Windows Credential Manager over command-line passwords.
5. Remove or isolate legacy transfer code that cannot meet the current credential policy.
6. Add automated tests asserting that representative passwords never appear in constructed log messages.

### 2. FreeRDP certificate validation is disabled by default

[`RDPStartinfo.cs`](SuperPutty/Utils/RDPStartinfo.cs#L67) adds `/cert-ignore` to every external FreeRDP connection. FreeRDP documents that this option ignores certificate checks altogether and overrides other certificate policies.

This permits server impersonation and man-in-the-middle attacks. It affects sessions configured to use external FreeRDP; ordinary ActiveX MSTSC sessions do not use this argument.

Recommendations:

1. Remove `/cert-ignore` from the default arguments.
2. Use normal certificate-chain and host-name validation by default.
3. Support TOFU or explicit certificate fingerprints for private RDP deployments.
4. If certificate bypass remains available, make it a clearly warned per-session compatibility option rather than a global default.
5. Add unit tests confirming secure certificate behavior is the default.

Reference: [FreeRDP command-line certificate options](https://github.com/FreeRDP/FreeRDP/wiki/CommandLineInterface/a26a079bf836548bf23e08046bf62ba7ff7c9b08).

## Medium-priority findings

### 3. Remote HTTP scripts can automate active terminals without integrity verification

Session scripts can be downloaded over plain HTTP and executed immediately:

- [`SuperPuTTY.cs`](SuperPutty/SuperPuTTY.cs#L476) accepts HTTP and HTTPS script URLs.
- [`SuperPuTTY.cs`](SuperPutty/SuperPuTTY.cs#L484) reads the entire response without a size limit.
- [`SuperPuTTY.cs`](SuperPutty/SuperPuTTY.cs#L508) begins SPSL execution without an integrity check or trust prompt.

SPSL can send text and keystrokes into terminals and can open or close sessions. Tampering with a remote script can therefore cause commands to run under the connected user's remote account.

Recommendations:

1. Require HTTPS for remote scripts.
2. Display the source and require explicit trust before a newly encountered remote script runs.
3. Support pinned SHA-256 hashes or signed scripts.
4. Add response timeouts and a conservative maximum script size.
5. Cache only verified content and record its origin.
6. Consider disabling remote scripts by default while continuing to allow explicitly selected local scripts.

### 4. Recursive remote session collections lack resource and destination controls

[`SessionData.cs`](SuperPutty/Data/SessionData.cs#L420) accepts session collections over HTTP and HTTPS. [`SessionData.cs`](SuperPutty/Data/SessionData.cs#L486) recursively loads nested collections without tracking visited locations or limiting recursion depth, response size, total session count, or request duration.

A malicious or malformed collection can cause request loops, memory exhaustion, application hangs, or blind requests to internal services.

Recommendations:

1. Require HTTPS for remote collections.
2. Track normalized visited URLs and reject cycles.
3. Set recursion-depth, total-session, and total-download limits.
4. Configure connection and response timeouts.
5. Reject link-local, loopback, and private-network destinations when a collection came from an untrusted remote source.
6. Use an `XmlReader` configured to prohibit DTD processing and external resource resolution.

Reference: [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html).

### 5. Public release and GitHub supply-chain controls are incomplete

Verified state for release 1.6.1:

- `SuperPutty.exe` has no Authenticode signature.
- `SuperPuttySetup.msi` has no Authenticode signature.
- The release commit and `sp-1.6.1` tag are unsigned.
- GitHub `master` has no branch-protection rule.
- Package versions are specified, but the repository has no NuGet lock file or pinned SDK definition.

Checksums help detect accidental corruption, but a checksum hosted beside an unsigned artifact does not establish publisher identity if the hosting account is compromised.

Recommendations:

1. Authenticode-sign the executable and MSI with a publicly trusted certificate.
2. Timestamp signatures and verify them before publishing.
3. Sign release tags and, where practical, release commits.
4. Protect GitHub `master` with required reviews, required status checks, and force-push/deletion restrictions.
5. Generate checksums, an SBOM, and build provenance in CI.
6. Enable locked NuGet restores and pin the build SDK/toolchain.
7. Publish releases through an automated least-privilege workflow rather than a maintainer workstation.

Reference: [Microsoft Authenticode signing guidance](https://learn.microsoft.com/windows/win32/dxtecharts/authenticode-signing-for-game-developers).

#### Implementation guide for this repository

Implement these controls in stages. Branch protection and repeatable restore can be enabled without a signing certificate; artifact signing and automated public releases should follow after the release workflow is proven with test artifacts.

##### Stage 1: Protect `master` and release tags

1. Make the existing Azure Pipeline run for pull requests targeting `master`. Add this top-level block to [`azure-pipelines.yml`](azure-pipelines.yml):

   ```yaml
   pr:
     branches:
       include:
       - master
   ```

2. Open the GitHub repository's **Settings > Rules > Rulesets** (or **Settings > Branches > Add branch protection rule**) and create a rule for `master` with:

   - pull requests required before merging;
   - the Azure Pipelines build/test result required;
   - conversation resolution required;
   - force pushes and branch deletion blocked;
   - administrator bypass disabled except for a documented emergency role; and
   - signed commits required after maintainer signing keys are configured.

3. If this remains a single-maintainer project, do not require an approval that nobody else can provide. Still require a pull request and passing CI so GitHub records the proposed change and validation result.
4. Add a tag ruleset matching `sp-*` that restricts tag creation, update, and deletion to the release role or release workflow.
5. Enable **Settings > General > Releases > Enable release immutability**. Create future releases as drafts, attach every asset, and only then publish them. Immutability locks the published tag and assets and produces a release attestation; it applies only to future releases.

GitHub documents the available [protected-branch controls](https://docs.github.com/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches) and [immutable-release behavior](https://docs.github.com/code-security/concepts/supply-chain-security/immutable-releases).

##### Stage 2: Lock dependencies and the build environment

1. Add this property to all three project files. A root `Directory.Build.props` is less repetitive, but use it only after confirming the classic non-SDK application project and both SDK-style projects all import it:

   ```xml
   <Project>
     <PropertyGroup>
       <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
     </PropertyGroup>
   </Project>
   ```

2. Restore the solution once without locked mode and confirm that it generates `packages.lock.json` files for the application, tests, and installer. If the root property was not honored by any project, put the property directly in that project file. Review and commit all three transitive dependency graphs.
3. Change CI restore to locked mode. With MSBuild, use `/restore /p:RestoreLockedMode=true`; with `dotnet restore`, use `--locked-mode`. A dependency change must then include an intentional lock-file update.
4. Add a `global.json` using an SDK version that is installed on both maintainer machines and the CI image. Prefer `rollForward: latestPatch` to accept security patches within the selected feature band, or `rollForward: disable` when exact SDK reproducibility is more important and the CI image is explicitly provisioned with that version.
5. Replace `windows-latest` with a reviewed fixed runner image such as `windows-2025`, pin `NuGetToolInstaller@1` with `versionSpec`, and record the expected Visual Studio, Windows SDK, .NET SDK, and WiX versions in the release notes.
6. When adding GitHub Actions, pin every third-party action to a full 40-character commit SHA. A version tag can be left in a comment for readability. GitHub identifies a full commit SHA as the only immutable way to reference an action.

Microsoft documents [NuGet lock files and locked restore](https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files); GitHub documents [secure workflow use and action pinning](https://docs.github.com/actions/reference/security/secure-use).

##### Stage 3: Obtain and protect a public code-signing identity

The preferred CI option is Microsoft Artifact Signing (formerly Trusted Signing) with a Public Trust certificate profile and workload-identity/OIDC authentication. The private signing key remains in the managed service instead of being copied into a repository secret. Create the Artifact Signing account, complete public identity validation, create a certificate profile, grant only the signing identity the **Artifact Signing Certificate Profile Signer** role, and authorize only the protected GitHub release environment.

If Artifact Signing is unavailable, obtain an OV or EV Authenticode certificate from a public certificate authority and keep its key in a hardware token, HSM, or dedicated signing service. A PFX stored as a GitHub secret is a compatibility fallback, not the preferred long-term design. Never commit a certificate, PFX, password, client secret, or exported private key.

See Microsoft's [Artifact Signing quickstart](https://learn.microsoft.com/azure/artifact-signing/quickstart) and [supported signing integrations](https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations).

##### Stage 4: Sign in the correct build order

The current solution build creates the application and installer together. Split the release job so the MSI embeds the signed application:

1. Restore in locked mode.
2. Build and test `SuperPutty` and `SuperPuttyUnitTests` without building the installer.
3. Sign `bin\x64\Release\SuperPutty.exe` and any project-owned executable DLLs intended to carry the publisher identity.
4. Verify those signatures and fail immediately if verification fails.
5. Build `SuperPuttyInstaller\SuperPuttyInstaller.wixproj`; it will now package the signed EXE.
6. Sign `SuperPuttyInstaller\bin\x64\Release\SuperPuttySetup.msi`.
7. Verify the MSI signature and extract it using [`Verify-ReleaseArtifacts.ps1`](build/Verify-ReleaseArtifacts.ps1) to confirm the embedded EXE is the already signed file.

For a locally available certificate, the equivalent SignTool operations are:

```powershell
signtool sign /fd SHA256 /tr <RFC3161-timestamp-url> /td SHA256 /a `
  bin\x64\Release\SuperPutty.exe
signtool verify /pa /all /v bin\x64\Release\SuperPutty.exe

signtool sign /fd SHA256 /tr <RFC3161-timestamp-url> /td SHA256 /a `
  SuperPuttyInstaller\bin\x64\Release\SuperPuttySetup.msi
signtool verify /pa /all /v `
  SuperPuttyInstaller\bin\x64\Release\SuperPuttySetup.msi
```

Use the Artifact Signing integration rather than `/a` when the managed signing service is selected. In both cases, SHA-256 file and RFC 3161 timestamp digests should be explicit. Timestamping allows a valid signature to remain verifiable after the certificate expires. SignTool returns `0` for success, `1` for failure, and `2` for a warning; treat every nonzero result as a failed release. See the current [SignTool command reference](https://learn.microsoft.com/windows/win32/seccrypto/signtool).

##### Stage 5: Add a least-privilege GitHub release workflow

Create `.github/workflows/release.yml` only after the commands above work locally or in a nonpublishing CI job. Recommended controls:

- Trigger it with `workflow_dispatch` for a specific version and commit, or with a protected `sp-*` tag.
- Use a protected GitHub environment named `public-release`; require manual approval initially and limit which tags can deploy to it.
- Set workflow-level `permissions: contents: read`. Grant `contents: write` only to the release job and grant `id-token: write` only to the signing/attestation job that uses OIDC.
- Do not use `pull_request_target` or expose signing credentials to pull-request builds.
- Pin actions to full commit SHAs and use only GitHub- or Microsoft-maintained actions in the privileged job.
- Ensure the requested version matches the EXE/MSI version and that the tag resolves to the checked-out commit.
- Build from a clean checkout; never upload binaries produced on a maintainer workstation.

The release job should create these files in a new staging directory:

- `SuperPuttySetup.msi`;
- an optional ZIP containing the signed portable application;
- `SHA256SUMS.txt` generated with `Get-FileHash -Algorithm SHA256`;
- a CycloneDX or SPDX SBOM generated from the locked dependency graph; and
- release notes identifying the source commit, toolchain, and signing certificate subject.

Attest the final MSI, ZIP, checksum file, and SBOM with the full commit SHA corresponding to the reviewed `actions/attest` v4 release. The job requires only these additional permissions:

```yaml
permissions:
  contents: read
  id-token: write
  attestations: write
```

Use the attestation action's `subject-path` for the staged release files. GitHub documents the exact syntax and consumer verification command in [Using artifact attestations](https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations). Build attestations establish provenance; they supplement Authenticode and do not claim the binaries are vulnerability-free.

Create a draft release, upload all staged assets, verify their signatures and hashes once more, and then publish it. With release immutability enabled, assets cannot be repaired in place after publication, so a bad release must be superseded with a new version.

##### Stage 6: Sign commits and release tags

GitHub supports GPG, SSH, and S/MIME signatures. SSH signing is usually the simplest for an individual maintainer using Git 2.34 or later:

```powershell
git config --global gpg.format ssh
git config --global user.signingkey <path-to-public-ssh-key>
git config --global commit.gpgsign true
git config --global tag.gpgSign true
```

Add the public key to GitHub as a **signing** key. Create an annotated signed release tag only after CI passes, verify it locally, and push that exact tag:

```powershell
git tag -s sp-1.6.2 -m "SuperPuTTY 1.6.2"
git tag -v sp-1.6.2
git push origin sp-1.6.2
```

Do not move or reuse a published release tag. See GitHub's [tag-signing instructions](https://docs.github.com/authentication/managing-commit-signature-verification/signing-tags).

##### Stage 7: Define release acceptance checks

Before publishing, make the workflow fail unless all of these are true:

1. locked restore, build, unit tests, shutdown tests, and release-artifact validation pass;
2. the source commit is the protected, expected commit and has the requested version;
3. Authenticode verification succeeds for both the EXE and MSI;
4. the MSI contains the same signed EXE that was verified before packaging;
5. SHA-256 checksums are regenerated from the final signed artifacts;
6. the SBOM and GitHub build attestations exist and refer to those final artifacts; and
7. a clean Windows sandbox can install, launch, close, and uninstall the MSI.

Adopt the stages in this order: branch/tag protection, locked restore, signing identity, split signed build, nonpublishing workflow test, then immutable automated release. This avoids making release automation authoritative before its inputs and signing path are protected.

## Lower-priority findings

### 6. Incomplete SSH.NET prototype contains insecure placeholder behavior

[`ctlSshNetPanel.cs`](SuperPutty/ctlSshNetPanel.cs#L44) overwrites the session password with the literal value `something`, attempts an SSH connection, and writes exceptions and terminal output to the console.

The class does not appear to be instantiated by the current application path, although `SSH.Net` is exposed as a session protocol choice. This makes it a dormant security and maintenance risk rather than a currently reachable credential bypass.

Recommendations:

1. Remove the unfinished protocol option and prototype if it is not supported.
2. Otherwise, implement proper credential acquisition and explicit SSH host-key validation.
3. Do not write terminal output or detailed connection exceptions to an uncontrolled console or persistent log.
4. Add integration tests before enabling the implementation.

### 7. HTTP clients lack defensive response limits

The update checker and remote-content loaders do not consistently configure cancellation, response-size limits, or request timeouts. For example, [`HttpRequest.cs`](SuperPutty/HttpRequest.cs#L68) begins reading an update response without a maximum size.

The update URL is fixed to GitHub, which limits practical exposure, but compromised endpoints or network failures can still consume memory or leave operations pending.

Recommendations:

1. Set explicit connect and read timeouts.
2. Reject responses above a small expected limit.
3. Validate HTTP success status and content type.
4. Dispose response objects consistently.
5. Replace the legacy asynchronous `WebRequest` implementation with a shared, bounded HTTP client when the framework architecture permits it.

## Recommended remediation order

1. Remove FreeRDP `/cert-ignore` and stop all plaintext password logging.
2. Correct VNC and PSCP password handling and add credential-redaction tests.
3. Require trusted HTTPS plus integrity controls for remote SPSL scripts.
4. Bound and cycle-protect remote session collection loading.
5. Sign release artifacts and protect the GitHub release branch.
6. Remove or complete the dormant SSH.NET prototype.
7. Add common HTTP timeout and response-size protections.

## Audit methods

- NuGet direct and transitive vulnerability audit for all three projects
- Gitleaks history scan over 578 commits
- Semgrep C# and security-audit rules
- Trivy vulnerability, secret, and configuration scan
- GitHub CodeQL, Dependabot, and secret-scanning alert review
- Authenticode and PE mitigation inspection of Release artifacts
- Manual review of credential, process-launch, update, HTTP, XML, session import, SPSL, RDP, native interop, installer, and release paths

## Limitations

This review does not constitute an independent security certification. It did not include fuzzing, a live man-in-the-middle test, penetration testing of external servers, or security testing of separately installed PuTTY, PSCP, VNC, or FreeRDP binaries. Static-analysis tools can miss vulnerabilities, so high-risk paths should receive regression tests and a second review after remediation.
