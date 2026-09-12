# Future Chocolatey distribution

Plan only; no package is published or submitted. Start after the WinGet distribution has demonstrated stable upgrades.

## Proposed package

Use `superputty-community-edition`, subject to availability and community-repository approval. Do not take over `superputty` or imply that this package replaces the upstream project. Publisher: Chris Thornton; product: SuperPuTTY Community Edition; license: MIT, with original Jim Radford attribution.

The first package should install the **all-users x64 MSI** only. Download directly from `https://github.com/greyhair-atx/superputty/releases/download/sp-<version>/SuperPuTTY-CE-<version>-all-users-win-x64-signed.msi`. Pin its SHA-256 in `chocolateyInstall.ps1`, require a valid Christopher Thornton Authenticode signature, and use Chocolatey's supported download/install helpers. Never download from a private mirror or execute a remote script.

Pass `/qn /norestart` to MSI installation; document exit codes 0, 1641 and 3010 and avoid forcing a reboot. MSI already supplies Add/Remove Programs metadata and scope; do not introduce a second settings path. Uninstall by the **exact version's ProductCode** using `msiexec /x {ProductCode} /qn /norestart`, or verified Chocolatey automatic-uninstaller metadata. Never uninstall by the shared historical upstream UpgradeCode or a fuzzy product name.

## Moderation and testing

Include description, public project/source/license/release-note URLs, approved icon, architecture restriction, checksums, and silent/uninstall instructions. Follow [community moderation requirements](https://docs.chocolatey.org/en-us/community-repository/moderation/) and test clean install, repeat install, reboot-required handling, upgrade from the previous CE release, uninstall, and settings preservation in disposable Windows machines. Test the upstream 1.5 rejection separately.

Do not redistribute PuTTY, PSCP, private keys, .NET Framework, or optional viewers inside this package. Document dependencies and prerequisites. Keep attribution and third-party notices in the installed payload.

## Updates

An updater may read only stable releases from `greyhair-atx/superputty`, verify the exact tag, fetch the versioned checksum/SBOM, verify MSI identity/signature, and generate a reviewable package change. Re-read ProductCode and hash for every build; never reuse them merely because a filename matches. Follow [Chocolatey's automatic packaging guidance](https://docs.chocolatey.org/en-us/create/automatic-packages/). Keep submission credentials in the chosen service's secret store, not the repository. Require review before submission until the process is proven.
