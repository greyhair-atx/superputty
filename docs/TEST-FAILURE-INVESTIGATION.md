# Investigation of the 29 test failures

Investigated on November 12, 2026 against the current working tree and Release test assembly. This investigation does not change application code or test assertions.

## Follow-up implementation and validation

The subsequent implementation caps open terminal/SCP sessions at 200, rejects oversized saved layouts before workspace teardown, reports IPC UI-post failures safely, corrects the malformed-frame exception assertions, and isolates options tests from the real settings folder. Cursor-dependent gestures and HKCU integration now explicitly skip when their prerequisites are unavailable. The 200-tab stress tests exercise selection, overflow, cleanup, and reusing closed slots.

Release x64 application and test-project build succeeded. The completed suite excluding `NetworkTest` recorded **708 passed, 0 failed, 25 skipped, 733 total** in approximately 97 seconds. The skips are 24 cursor-dependent gesture cases and one registry integration case; those integration behaviors still require validation in a suitable Windows desktop/account. Results are stored locally at `artifacts/tab-limit-validation/full.trx`. These results supersede the failing baseline described below; they do not claim that skipped cases passed.

## Reproduction and classification

The broad non-network run reproduced exactly 29 failures. Its partial results recorded 684 passes before the run was stopped to isolate the slow test group. The machine-readable list of all failures is in the locally generated `artifacts/test-investigation/full-investigation.trx`.

| Failures | Area | Finding |
| ---: | --- | --- |
| 22 | `TabCloseGestureTests` | The sandbox cannot move the desktop cursor to the coordinates required by these tests. Product gesture behavior remains unverified here. |
| 2 | `SessionTabActionsTests.GuiLoadsMiddleClickPreferenceAndDoesNotApplyAnUnsavedChange` | The options constructor attempts to create the real user's default settings folder, outside the writable sandbox. |
| 1 | `FiveIssueRegressionTests.RegistryImportSkipsInvalidSessionsAndKeepsValidOnes` | Creating the temporary HKCU test key is denied by the sandbox. |
| 3 | `SingleInstanceProtocolTests` | Incorrect exception-type expectation in the rejection helper. The protocol rejects malformed requests as intended. |
| 1 | `SingleInstanceCommandQueueTests.FailedUiPostDoesNotAcknowledgeOrConsumeCapacity` | Application code fails to notify its error callback when posting to the UI fails. Queue rejection and capacity cleanup work. |

The independent IPC/permission run reproduced the same seven failures, with six other tests passing: `artifacts/test-investigation/ipc-permissions.trx`.

A completed broad run excluding `NetworkTest` and `InteractiveUiTest` recorded **681 passed, 7 failed, 688 total** in approximately 32 seconds. Its seven failures were the same IPC and permission failures above; see `artifacts/test-investigation/noninteractive-investigation.trx`. This filter excludes the desktop gesture and large-tab fixtures, not every test that creates a UI control.

## IPC details

`SingleInstancePipeProtocol.EncodeRequest` and `DecodeRequest` throw `InvalidDataException` for excessive argument counts, oversized payloads, and truncated or trailing frame data. In .NET Framework 4.8, `InvalidDataException` derives from `SystemException`, not `IOException`. The test helper accepts only `IOException` or `ArgumentException`, so these three tests fail on the expected rejection. Correct the helper to recognize `InvalidDataException` and retain the malformed-input checks.

`SingleInstanceCommandQueue.TryEnqueue` removes the pending request and returns `NotReady` after a UI-post exception, but never calls `reportFailure`. The failing assertion expects that notification. Add error reporting to this branch and verify that reporting cannot compromise rejection or cleanup.

## Desktop input and permissions

The tab diagnostic requested cursor positions such as `(93,86)` and `(97,87)`; reading the actual cursor position immediately afterward returned `(0,0)` for all three themes. Explicit hit testing at the requested client point returned tab index zero, but the middle-click did nothing and raised no close prompt.

Inspection of the installed DockPanel Suite assembly confirmed that its mouse-down handler calls parameterless `HitTest()`, which reads `Control.MousePosition`. Sending a synthetic mouse message with valid coordinates does not substitute for moving the desktop cursor. The fixture should detect an unavailable interactive desktop and report the prerequisite clearly. These gesture cases still need execution in a desktop session where cursor movement works; no claim is made that all product gesture behavior is correct.

The options tests leave `SettingsFolder` unset and instantiate `dlgFindPutty`, whose constructor calls `Directory.CreateDirectory` for the user's default AppData folder. Use an isolated temporary settings folder in these tests and restore the original setting afterward. The registry import test requires registry-write capability; it should be identified as an environment-dependent integration test or run in a suitable test account. No persistent permissions or machine settings were changed during this investigation.

## The apparent stall

The aborted broad run was executing `TabStripInteractionTests.OverflowListIncludesEveryTabAndReleasesItemsAndImages(True)`. An isolated run of the entire fixture completed successfully: **16 passed in approximately 2 minutes 20 seconds**.

Measured cases included approximately 29 seconds for each 300-tab selection test, 17–18 seconds for each 300-tab hit-testing test, and 16–17 seconds for each overflow-menu test. Independent step diagnostics also completed creation, all menu clicks, rebuilding, strip disposal, and fixture disposal for both themes.

The earlier report of a stall was therefore premature. A deadlock was not reproduced. The large-tab tests are slow and provide little output at normal verbosity. Use detailed console logging and allow adequate time for this fixture before concluding it is hung. The attempted NUnit default-timeout run was stopped and is not evidence of a successful bounded test run.

Relevant local evidence is under `artifacts/test-investigation/`, including `overflow-isolated.trx`, `overflow-isolated.log`, `tab-input-diagnostics.log`, `overflow-steps.log`, and `hundreds-steps.log`. Generated artifacts are ignored by Git.
