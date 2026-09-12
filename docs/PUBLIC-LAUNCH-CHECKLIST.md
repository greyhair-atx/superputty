# Public launch checklist — 1.8.0

**Staging only.** No release, website, metadata change, package submission, or announcement is authorized by this checklist. Obtain the maintainer's explicit approval for each external phase. Existing 1.7.x releases must remain available and unchanged.

## Repository settings (manual)

Recommended description:

> Actively maintained community edition of SuperPuTTY—a signed Windows x64 manager for PuTTY, PowerShell, Command Prompt, RDP, VNC, and SCP sessions.

Recommended website, after Pages deployment is approved: `https://greyhair-atx.github.io/superputty/`. Until then use the public GitHub releases or manual URL, not a private mirror.

Recommended topics (already present at the September 12, 2026 inspection):

`superputty`, `putty`, `ssh`, `ssh-client`, `terminal`, `terminal-emulator`, `windows`, `rdp`, `vnc`, `scp`, `network-administration`, `system-administration`, `csharp`, `dotnet`, `community-edition`.

- [ ] On the repository main page, edit **About** to set description, website, and topics. Requires separate authorization.
- [x] Issues enabled; confirmed through the GitHub API on September 12, 2026. Review the bug/feature templates before launch.
- [ ] **Settings → General → Features → Discussions:** enable Discussions, choose categories, and review the welcome draft. Discussions were disabled at inspection.
- [x] Private vulnerability reporting enabled; confirmed through the GitHub API on September 12, 2026. `SECURITY.md` links directly to the private reporting form. A fallback email is optional; confirm notification preferences.
- [ ] **Settings → General → Social preview:** upload the approved 1280×640 image from [SOCIAL-PREVIEW.md](SOCIAL-PREVIEW.md).
- [ ] On the maintainer's profile, choose **Customize your pins** and select this repository. This is an account setting, not a source change.
- [ ] Review branch protection, dependency alerts, and existing default CodeQL setup. Do not enable a duplicate CodeQL workflow.

## Release and distribution

- [x] Review historical Azure build 29 logs: completed from the supplied archive with no exposed secrets identified. See [review and scope](AZURE-BUILD-29-REVIEW.md); final 1.8.0 CI/signing logs still require review.
- [ ] Review [1.8.0 draft notes](releases/1.8.0.md), [security review](SECURITY-REVIEW-1.8.0.md), and [release preparation](RELEASE-PREPARATION.md).
- [ ] Approve final source commit and freeze it. Create annotated tag `sp-1.8.0` only after approval. The staged source link will resolve after that tag is pushed.
- [ ] Rebuild from the approved commit; sign EXE before packaging; sign both MSIs. Regenerate checksums, SBOM, and WinGet manifests from those exact packages.
- [x] Validate the local candidate with 142 isolated tests, signature/payload checks and the authorized Windows 11 VM matrix: 17 standard-user and 20 elevated operations passed. See [installer results](INSTALLER-MATRIX-1.8.0.md).
- [ ] Validate the final approved build on Windows 11 x64 and record the actual results.
- Windows 10 testing: **deferred with disclosed limitations**, by maintainer decision. It is **not a release blocker** for 1.8.0. Compatibility is expected with .NET Framework 4.8 or later, but Windows 10 installation and runtime behavior have not been tested. Keep this disclosure in the README, website, manual and release notes; do not mark Windows 10 testing as passed.
- [ ] If VirusTotal scanning is desired, separately authorize uploading only the final EXE and DLLs. No earlier scan report covers a new build.
- [ ] Approve release publication titled **SuperPuTTY Community Edition 1.8.0 — Signed Windows x64 Release**. Upload both MSIs, ZIP, versioned checksums, SBOM, and validation report. Verify uploaded hashes.
- [ ] On GitHub's release editor, clear **pre-release**, choose **Set as the latest release**, and confirm `/releases/latest` points to 1.8.0. Never replace the 1.7.x assets.
- [ ] Change the README/site status from **launch candidate** to **released** only after the release is live.
- [ ] Separately approve Pages activation: **Settings → Pages → Deploy from a branch → approved branch → /docs**. Preview `docs/index.html` locally first. No deployment workflow is enabled in this preparation.
- [ ] After direct asset URLs are live, rerun WinGet schema, hash, and Windows Sandbox tests. Obtain approval before any fork or pull request to `microsoft/winget-pkgs`.
- [ ] Approve announcements individually. Ask upstream respectfully; do not imply endorsement. Plan Chocolatey only after WinGet is stable.

## Suggested launch order

Security/compatibility review → freeze source → build/sign/verify → approval → release/tag → verify live downloads → approve Pages and metadata → validate/approve WinGet submission → approve announcements → consider Chocolatey.

GitHub references: [social preview](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview), [Discussions](https://docs.github.com/en/discussions), [Pages publishing source](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site).
