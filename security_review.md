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
