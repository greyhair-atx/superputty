# SuperPuTTY

[![Community release](https://img.shields.io/github/v/release/greyhair-atx/superputty?display_name=tag&sort=semver&label=community%20release)](https://github.com/greyhair-atx/superputty/releases/latest)
[![Release downloads](https://img.shields.io/github/downloads/greyhair-atx/superputty/total?label=release%20downloads)](https://github.com/greyhair-atx/superputty/releases)
[![Release build](https://img.shields.io/badge/release%20build-verified-brightgreen)](https://github.com/greyhair-atx/superputty/releases/latest)
[![CodeQL](https://github.com/greyhair-atx/superputty/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/greyhair-atx/superputty/actions/workflows/github-code-scanning/codeql)
[![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
[![Windows x64](https://img.shields.io/badge/Windows-x64-0078D4)](https://github.com/greyhair-atx/superputty/releases/latest)
[![License: MIT](https://img.shields.io/github/license/greyhair-atx/superputty)](License.txt)
[![Signed builds](https://img.shields.io/badge/builds-Authenticode%20signed-brightgreen)](https://github.com/greyhair-atx/superputty/releases/latest)

> **Community fork:** This is an active, unofficial fork of
> [jimradford/superputty](https://github.com/jimradford/superputty). It carries
> community-maintained fixes and publishes its own versioned x64 releases on
> [GitHub](https://github.com/greyhair-atx/superputty/releases) and
> [Gitea](https://gitea.uberx.org/vscode/superputty/releases). These builds are
> not official upstream releases. Starting with 1.7.1, release builds are
> Authenticode signed by Christopher Thornton and timestamped.

SuperPuTTY is a Windows application for managing PuTTY SSH terminals. It also
supports embedded Win CMD and Windows PowerShell consoles, RDP sessions, and a
simple scripting language for common tasks. Local console windows use a
dedicated host panel so their capture, focus, resizing, and shutdown behavior
does not alter the PuTTY hosting path.

The current [SuperPuTTY User Manual](docs/manual/README.md) is maintained with
the source so each release tag preserves its matching documentation.

Current contributor verification steps are in [docs/testing.md](docs/testing.md),
and private vulnerability reporting guidance is in [SECURITY.md](SECURITY.md).

## System requirements

The `master` branch and `sp-1.7.3` release produce **64-bit Windows builds
only**. The application, test harness, and MSI installer all target x64; x86
and AnyCPU configurations are not supported.

- 64-bit Windows 10 or Windows 11
- .NET Framework 4.8
- PuTTY and PSCP installed or supplied separately

The current-user x64 MSI installs under Local AppData without elevation. A
separate all-users x64 MSI installs under native 64-bit Program Files and asks
Windows for elevation. Neither installer can be installed on 32-bit Windows.

Community release artifacts use an explicit `x64` suffix. The current release
contains:

- `SuperPutty-1.7.3-current-user-win-x64-signed.msi` — signed, non-elevated current-user installer
- `SuperPutty-1.7.3-all-users-win-x64-signed.msi` — signed, elevated all-users installer

## Automated tests

`SuperPuttyUnitTests` is an SDK-style .NET Framework 4.8 project using NUnit 3,
the NUnit 3 adapter, and Microsoft.NET.Test.Sdk. Normal release validation runs
the isolated test suite and excludes tests that require a configured SCP
environment.

The Windows-only console integration test in
`build\Test-ConsoleApplicationPanel.ps1` launches real Win CMD and PowerShell
sessions, verifies that each console HWND is parented to its dedicated panel,
and closes the test sessions cleanly.

CI also verifies the x64 PE header, product version, runtime DLL set, WiX MSI
architecture and payload, theme icons, title-bar shutdown with confirmation on
and off, and File > Exit. The verification entry points are:

- `build\Verify-ReleaseArtifacts.ps1`
- `build\Test-ApplicationShutdown.ps1`
- `build\Test-ConsoleApplicationPanel.ps1`
- `build\Verify-CodeSignatures.ps1` (signed builds only)

The eight `NetworkTest` cases require a disposable SSH server and separately
configured PuTTY/PSCP environment; they are not run on normal pull requests.

## Code signing

The Azure pipeline supports optional public-trust Authenticode signing through
Microsoft Azure Artifact Signing. Signing is disabled by default so normal CI
builds do not require access to the signing service.

To configure a signed build:

1. Install Microsoft's Artifact Signing extension in the Azure DevOps
   organization.
2. Create an Azure Resource Manager service connection that uses workload
   identity federation.
3. Assign its service principal the `Artifact Signing Certificate Profile
   Signer` role on the `greyhair-atx` signing account.
4. Manually run the pipeline, enable **Sign release artifacts with Azure
   Artifact Signing**, and enter the service-connection name.

The pipeline signs `SuperPutty.exe` before WiX embeds it, signs both completed
MSIs, and verifies that all three signatures are publicly trusted, identify
`Christopher Thornton`, and contain timestamps before publishing the installer.
The signing key remains managed by Azure and is never exported to the pipeline.

## Project resources

- [Official SuperPuTTY repository](https://github.com/jimradford/superputty)
- [Official releases](https://github.com/jimradford/superputty/releases)
- [Community user manual](docs/manual/README.md)
- [Build and test guide](docs/testing.md)
- [Security policy](SECURITY.md)
- [Original Jim Radford documentation](https://github.com/jimradford/superputty/wiki/Documentation)
- [Candidate fixes in this fork](https://github.com/greyhair-atx/superputty/branches)
- [Community releases from this fork](https://github.com/greyhair-atx/superputty/releases)

Community updates are maintained by C. Thornton at
[greyhair-atx/superputty](https://github.com/greyhair-atx/superputty).

Use the community issue tracker for behavior introduced by this fork. The
upstream repository and original wiki remain the authoritative historical
references for Jim Radford's SuperPuTTY through version 1.5.0.0.

## License

SuperPuTTY is licensed under the MIT License. See `License.txt` for details.
