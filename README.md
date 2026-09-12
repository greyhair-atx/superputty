# SuperPuTTY Community Edition

**A tabbed Windows workspace for PuTTY, PowerShell, Command Prompt, RDP, VNC, and SCP.**

SuperPuTTY Community Edition is an actively maintained, independently published fork of the original SuperPuTTY project. It provides signed Windows x64 releases, current documentation, security improvements, and expanded session-management features.

**[Download latest release](https://github.com/greyhair-atx/superputty/releases/latest)** · **[User manual](docs/manual/README.md)** · **[Install](docs/manual/installation.md)** · **[Upgrade](docs/manual/upgrading.md)**

[Security policy](SECURITY.md) · [Issue tracker](https://github.com/greyhair-atx/superputty/issues) · [Original upstream](https://github.com/jimradford/superputty)

> **Independent community fork maintained by Chris Thornton.** This is a community edition, not an official upstream SuperPuTTY or PuTTY release. SuperPuTTY was created by Jim Radford and the original contributors. No upstream endorsement is implied.

**Launch status:** 1.8.0 is the first fully rebranded public-launch candidate and is **not yet published**. The latest-release button currently serves the existing stable release. The [1.8.0 draft release notes](docs/releases/1.8.0.md) and [launch checklist](docs/PUBLIC-LAUNCH-CHECKLIST.md) describe the pending launch; published 1.7.x downloads remain unchanged.

## See the workspace

[![PowerShell and session navigation in SuperPuTTY](docs/images/latest-build/powershell.png)](docs/README.md)

This existing screenshot illustrates an earlier community build. It is not a 1.8.0 capture. [View the screenshot gallery and capture notes](docs/README.md).

## What the community edition adds

- Signed, timestamped Windows x64 packages for current-user or all-users installation.
- Embedded Windows PowerShell and Command Prompt consoles, RDP integration, improved TigerVNC window capture, and SCP sessions with private-key support.
- Session search, CSV import, named layouts, active-tab close confirmation, and a configurable restart-session shortcut.
- HTTPS restrictions for remote scripts and collections, password-argument redaction, and PSCP password delivery through a one-use named pipe.
- Community updates by default for new profiles, atomic preference saves with a previous-save backup, and a maintained [user manual](docs/manual/README.md).

PuTTY and other connection programs perform their respective protocol work. SuperPuTTY organizes and hosts them; it is not a credential vault. See [protocols](docs/manual/protocols.md) and [security behavior](SECURITY.md).

## Requirements and installation choices

| Requirement | Support |
| --- | --- |
| Operating system | Tested on Windows 11 x64. Windows 10 x64 compatibility is expected but untested for 1.8.0; use an OS edition still receiving security updates |
| Architecture | x64; no x86 or native ARM64 package |
| Runtime | .NET Framework 4.8 |
| SSH, Telnet, serial, raw connections | Separately installed PuTTY |
| SCP file transfers | Separately installed PSCP |
| RDP / VNC | Windows RDP components or configured external RDP client; separately installed VNC viewer |
| Local consoles | Windows PowerShell and Command Prompt; MinTTY when separately installed |

Choose **current user** for Local AppData without elevation, or **all users** for 64-bit Program Files with administrator approval. The prepared 1.8.0 no-install ZIP contains the same application payload; settings still follow existing profile/portable discovery rules. [Installation details and unattended commands](docs/manual/installation.md).

**Existing users:** executable and settings names, registry paths, sessions, layouts, and command-line behavior stay compatible. Same-scope community upgrades are supported; ambiguous upstream-era 1.5 installations require manual removal. Do not share settings between running editions. [Read the upgrade guide first](docs/manual/upgrading.md).

## Verify a download

Download packages and checksums from the same [community release](https://github.com/greyhair-atx/superputty/releases). For the prepared 1.8.0 release:

```powershell
Get-AuthenticodeSignature .\SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi | Format-List
Get-FileHash .\SuperPuTTY-CE-1.8.0-current-user-win-x64-signed.msi -Algorithm SHA256
# After extracting the ZIP or installing:
Get-AuthenticodeSignature .\SuperPutty.exe | Format-List
```

Require signature **Status: Valid**, signer **Christopher Thornton** (the signing identity used by Chris Thornton), and a nonempty `TimeStamperCertificate`. Compare the hash with the matching line in `SuperPuTTY-CE-1.8.0-SHA256SUMS.txt`. The ZIP is not Authenticode signed: verify its checksum and the EXE inside it. Checksums detect changed downloads; signatures also verify publisher identity. [Detailed instructions](docs/manual/installation.md#verify-signatures-and-checksums).

## Help, bugs, and security

Start with the [manual](docs/manual/README.md) and [troubleshooting guide](docs/manual/troubleshooting.md). Report reproducible bugs in the [community issue tracker](https://github.com/greyhair-atx/superputty/issues), including version, Windows version, and redacted logs. Issues are enabled.

Report vulnerabilities privately through the enabled [GitHub security reporting form](https://github.com/greyhair-atx/superputty/security/advisories/new). Do not put credentials, private keys, or unpatched vulnerability details in public issues. See [SECURITY.md](SECURITY.md) for the reporting policy.

## Build and contribute

Build the complete solution with Visual Studio/MSBuild and the .NET Framework 4.8 developer pack. [Build, tests, installer verification, and signing instructions](docs/testing.md) are maintained alongside the source. [Release preparation](docs/RELEASE-PREPARATION.md) describes the local bundle, SBOM, staged WinGet manifests, and publication gates. Signing uses Azure Artifact Signing; credentials are never included in source or packages.

## License and attribution

Distributed under the **MIT License** in [License.txt](License.txt). Jim Radford's original copyright is preserved; community modifications credit Chris Thornton. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and [documentation provenance](docs/ORIGINAL-WIKI-NOTICE.md). The [original SuperPuTTY repository](https://github.com/jimradford/superputty) and [PuTTY project](https://www.chiark.greenend.org.uk/~sgtatham/putty/) remain independently maintained projects.
