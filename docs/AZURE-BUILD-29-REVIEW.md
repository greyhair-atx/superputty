# Azure build 29 log review

Completed September 12, 2026 using the maintainer-provided `logs_29.zip` from Downloads. The archive resolves the earlier API-access limitation; no Azure settings or remote build were changed.

## Evidence

- Archive SHA-256: `e00b35c87a4879608030dcbe9d417e12bb3683080a696023cb8896e12998f388`.
- Reviewed 20 extracted files totaling 382,086 bytes, including combined/per-task logs and the expanded pipeline YAML. Archive paths were checked before extraction.
- Checkout logs contain source commit `ae432924b37039cc197dbd97a0a60bb39966a0fa`, corresponding to `sp-1.7.6`.
- Raw evidence and scan outputs remain under ignored `artifacts/launch-review/`; they are not release assets or tracked source.

## Security findings

**No exposed secrets identified.** Gitleaks 8.30.1 completed successfully with zero detections. Additional checks found no private-key headers, JWTs, GitHub tokens, unmasked Basic/Bearer credential values, SAS signatures, populated credential assignments or literal password/federated-token arguments matching the reviewed patterns.

Reviewed the expanded pipeline and task inventory alongside build, test, checkout and artifact-publishing results. Mask markers occur in test output, but their presence is not evidence that every possible secret would be masked. Pattern scans cannot identify every arbitrary credential or prove that none exists.

The run used `installerArtifactSuffix: test-x64` and contained **no signing or Authenticode-verification task**. It therefore provides no execution evidence for Azure signing credential handling, signer identity or timestamps. The local 1.8.0 candidate's signature checks are separate evidence.

## Build results

- Application, test assembly and both MSI builds succeeded.
- All 142 isolated tests passed.
- Current-user and all-users MSI verification passed for version 1.7.6.0, x64, three checked runtime DLLs and 47 icons.
- All three application shutdown scenarios passed.
- No Azure `##[error]` or `##[warning]` markers were found in the supplied files.
- Artifact tasks targeted CE-branded unsigned test MSI filenames.

## Gate status

The **historical Azure build 29 log-review gate is complete** with no identified secret-exposure findings. This is not a review of an executed 1.8.0 pipeline or a signed Azure run. Review the final approved 1.8.0 CI run, including signing tasks if enabled, before publication. Disposable installer/upgrade testing and final commit/build verification remain outstanding.
