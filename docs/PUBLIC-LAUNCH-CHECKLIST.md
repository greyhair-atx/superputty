# Public launch checklist — 1.8.0

> **Published September 12, 2026:** [sp-1.8.0](https://github.com/greyhair-atx/superputty/releases/tag/sp-1.8.0), built from clean commit `20b78c60ec02adc0af970e2d975ef615b63e58b8`. Final EXE/DLL scans completed with zero malicious or suspicious detections. This checklist tracks the published release and remaining distribution work; see [release results](RELEASE-1.8.0-RESULTS.md) for final artifacts, identities and validation. Publication and the public project website were approved by the maintainer.


Release publication, Pages and repository metadata are complete. Remaining package submissions and announcements are tracked below. Existing 1.7.x releases must remain available and unchanged.

## Repository settings (manual)

Recommended description:

> Actively maintained community edition of SuperPuTTY—a signed Windows x64 manager for PuTTY, PowerShell, Command Prompt, RDP, VNC, and SCP sessions.

Live website: `https://greyhair-atx.github.io/superputty/` (HTTP 200 verified during the final follow-up).

Recommended topics (already present at the September 12, 2026 inspection):

`superputty`, `putty`, `ssh`, `ssh-client`, `terminal`, `terminal-emulator`, `windows`, `rdp`, `vnc`, `scp`, `network-administration`, `system-administration`, `csharp`, `dotnet`, `community-edition`.

- [x] Repository **About** description, website and discovery topics configured; description and website rechecked through the GitHub API.
- [x] Issues enabled; confirmed through the GitHub API on September 12, 2026. Review the bug/feature templates before launch.
- [ ] **Settings → General → Features → Discussions:** enable Discussions, choose categories, and review the welcome draft. Discussions were disabled at inspection.
- [x] Private vulnerability reporting enabled; confirmed through the GitHub API on September 12, 2026. `SECURITY.md` links directly to the private reporting form. A fallback email is optional; confirm notification preferences.
- [ ] **Settings → General → Social preview:** upload the approved 1280×640 image from [SOCIAL-PREVIEW.md](SOCIAL-PREVIEW.md).
- [ ] On the maintainer's profile, choose **Customize your pins** and select this repository. This is an account setting, not a source change.
- [x] Reviewed repository controls: dependency alerts are enabled; CodeQL passed on the release source. The default `master` branch is unprotected. No duplicate CodeQL workflow was added.

## Release and distribution

- [x] Review historical Azure build 29 logs: completed from the supplied archive with no exposed secrets identified. See [review and scope](AZURE-BUILD-29-REVIEW.md); accessible final GitHub CI and local signing logs were also reviewed, as scoped in [release results](RELEASE-1.8.0-RESULTS.md).
- [x] Review [1.8.0 draft notes](releases/1.8.0.md), [security review](SECURITY-REVIEW-1.8.0.md), and [release preparation](RELEASE-PREPARATION.md).
- [x] Approve final source commit and freeze it. Create annotated tag `sp-1.8.0` only after approval. The published annotated tag targets `20b78c60ec02adc0af970e2d975ef615b63e58b8`.
- [x] Rebuild from the approved commit; sign EXE before packaging; sign both MSIs. Regenerate checksums, SBOM, and WinGet manifests from those exact packages.
- [x] Validate the local candidate with 142 isolated tests, signature/payload checks and the authorized Windows 11 VM matrix: 17 standard-user and 20 elevated operations passed. See [installer results](INSTALLER-MATRIX-1.8.0.md).
- [x] Final rebuild passed 142 tests, live shutdown/console checks and signature/payload verification; the 37-operation installer matrix covered the pre-freeze candidate. See [exact validation scope](RELEASE-1.8.0-RESULTS.md).
- Windows 10 testing: **deferred with disclosed limitations**, by maintainer decision. It is **not a release blocker** for 1.8.0. Compatibility is expected with .NET Framework 4.8 or later, but Windows 10 installation and runtime behavior have not been tested. Keep this disclosure in the README, website, manual and release notes; do not mark Windows 10 testing as passed.
- [x] All six final EXE/DLL VirusTotal analyses completed with zero malicious or suspicious detections before publication; MSIs were not uploaded.
- [x] Approve release publication titled **SuperPuTTY Community Edition 1.8.0 — Signed Windows x64 Release**. Upload both MSIs, ZIP, versioned checksums, SBOM, and validation report. Verify uploaded hashes.
- [x] On GitHub's release editor, clear **pre-release**, choose **Set as the latest release**, and confirm `/releases/latest` points to 1.8.0. Never replace the 1.7.x assets.
- [x] Change the README/site status from **launch candidate** to **released** only after the release is live.
- [x] Pages is published from `master` → `/docs`; deployment succeeded on `c69acae`, and the live site responds with HTTP 200.
- [x] Revalidated WinGet manifests and downloaded both published MSIs: SHA-256 values match the manifests and both Authenticode signatures are valid.
- [ ] Test installation through WinGet for both scopes, then submit to `microsoft/winget-pkgs`. The maintainer has authorized this work; schema 1.12.0 validation passed and the submission branch is prepared. The first VM run stopped before installation because the temporary account lacked App Installer registration. Preservation/restoration checks passed; the corrected runner awaits an elevated retry. No successful WinGet install test or pull request is recorded yet. See [WinGet staging](../packaging/winget/README.md).
- [x] Update announcement drafts to describe 1.8.0 as released. The drafts have not been posted.
- [ ] Approve announcements individually. Ask upstream respectfully; do not imply endorsement. Plan Chocolatey only after WinGet is stable.

## Suggested launch order

Security/compatibility review → freeze source → build/sign/verify → approval → release/tag → verify live downloads → approve Pages and metadata → validate/approve WinGet submission → approve announcements → consider Chocolatey.

GitHub references: [social preview](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview), [Discussions](https://docs.github.com/en/discussions), [Pages publishing source](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site).
