## 📖 Description

Resolve MSI installation scope using Windows Installer context instead of inferring it solely from the ARP registry hive. A user-unmanaged MSI can appear under HKLM, causing WinGet to reject `--scope user` or select a machine installer during an unscoped upgrade.

Enumerate current-user and machine registrations once per inventory helper, cache case-insensitive ProductCode/context mappings, and filter both ARP hives by the resolved scope. Preserve hive-based behavior for non-MSI entries. Missing/ambiguous MSI context and enumeration errors fail closed by omitting affected MSI inventory rather than guessing a scope. This conservative inventory change deserves particular review; it does not repair MSI transaction rollback.

## 🔗 References

Addresses https://github.com/microsoft/winget-cli/issues/3011. Reproduction and published installer identities: https://github.com/microsoft/winget-cli/issues/3011#issuecomment-6549193906.

## 🔍 Validation

- Release x64 targeted CLI and AppInstallerCLITests builds on base c608376673762a10971e6bf2cbeedb6b6a7cae88; full-solution build not completed (unrelated DesktopBridge/E2E restore issues). Local build used SpectreMitigation=false because the relevant libraries were unavailable; NuGet advisory warnings were not treated as errors. These are test binaries, not distribution builds.
- Selection `[msiscope],[arphelper],[manifest_comparator],[installed]`: 45 cases / 1,521 assertions locally; 45 / 1,289 on the Windows 11 disposable VM's standard-user interactive session. Local selection repeated successfully before submission.
- New cases cover managed/unmanaged/machine contexts, current-user-only enumeration, case folding, sticky ambiguity, short SID-buffer retry, partial enumeration failure, non-MSI retention, and user/machine source filtering across ARP hives.
- VM REST 1.4 catalog: eight successful package-specific upgrades covering user/machine, explicit/unscoped commands, and latest-only/both-version metadata. Verified native context, signed executable/hash, settings and final uninstall. A ninth incompatible-scope scenario rejected before removing the old user MSI.
- Not tested: production publishing/preindexed catalog, `upgrade --all`, all supported Windows releases, or general installer failure recovery. Separate forced-failure MSI tests did not restore complete registration; this PR makes no rollback-safety claim.

## ✅ Checklist

- [x] Signed the Contributor License Agreement
- [x] Linked to an issue
- [x] Added a release note fragment
- [x] Updated documentation (release note)
- [x] Added tests
- [ ] Updated Copilot instructions (no build/architecture/convention changes)

## 🤖 AI Assistance

- [x] AI assistance was used and has been disclosed in this PR
- [ ] No AI assistance was used

AI assistance contributed the implementation, regression tests, disposable-VM test orchestration and this description. The contributor remains responsible for review and validation.

## 📋 Issue Type

- [x] Bug fix
- [ ] Feature
- [ ] Task

 ###### Microsoft Reviewers: [Open in CodeFlow](https://microsoft.github.io/open-pr/?codeflow=https://github.com/microsoft/winget-cli/pull/6644)

Spelling follow-up: commit b7de1ab acknowledges five Windows Installer API/test terms in the existing dictionary. Executable code is unchanged. The CLA and spelling checks passed; native Azure Pipelines validation still awaits an authorized upstream maintainer.
