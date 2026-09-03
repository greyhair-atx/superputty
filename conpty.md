# ConPTY terminal panel implementation plan

> **Status (September 2026): deferred alternative, not the current implementation.**
> To avoid adding xterm.js/WebView2 or another renderer, Win CMD and Windows
> PowerShell were restored with an isolated classic-console window host instead.
> `ConsoleApplicationPanel` owns console-only launch, `ConsoleWindowClass`
> discovery, process-tree validation, HWND styling/parenting, focus, resizing,
> and shutdown. PuTTY, VNC, FreeRDP, and external MSTSC remain on the unchanged
> `ApplicationPanel` path. The quick-connect toolbar supplies editable numbered
> local names, and `build\Test-ConsoleApplicationPanel.ps1` exercises real CMD
> and PowerShell capture. The guardrails below apply only if ConPTY work is
> resumed in the future.

## Purpose

Implement reliable embedded `WinCMD` and Windows PowerShell tabs without discovering, reparenting, resizing, styling, focusing, or otherwise manipulating a `conhost.exe` top-level window.

The implementation must use Windows ConPTY for the shell process and render the resulting virtual-terminal stream inside a managed WinForms panel. It must be isolated from PuTTY, VNC, FreeRDP, MinTTY, Cygterm, and other existing external-window hosts.

This document is an implementation handoff for a new coding session. Read it completely before editing source.

## Repository baseline

The baseline at the time this document was written is:

- Branch: `master`
- Commit: `d12e2c09ea9c330bdd7ef6bd2246375d76a68b74`
- Commit subject: `Remove obsolete SSH protocol choices`
- The working tree was clean before this document was added.
- The reverted WinCMD/PowerShell experiments are not present.
- The isolated baseline test count is 56.
- The application and tests target .NET Framework 4.8 and x64.

Before implementation, confirm this with:

```powershell
git status --short
git rev-parse HEAD
dotnet test SuperPuttyUnitTests\SuperPuttyUnitTests.csproj `
  --configuration Release `
  --property:Platform=x64 `
  --filter "TestCategory!=NetworkTest"
```

Do not restore code from any artifact named `console-fix`, `console-fix2`, `console-fix3`, `console-fix4`, `console-fix5`, or `console-fix6`. Those experiments used native console-window capture and caused regressions in PuTTY window framing, resizing, focus, and stability.

## Non-negotiable guardrails

The implementation must obey all of these constraints:

1. Do not modify PuTTY window styles to implement console support.
2. Do not add PuTTY state/location WinEvent hooks.
3. Do not enumerate desktop windows to find `ConsoleWindowClass`.
4. Do not launch `conhost.exe` directly.
5. Do not call `SetParent`, `MoveWindow`, `ShowWindow`, or `SetWindowLong` for WinCMD or PowerShell.
6. Do not use `Process.MainWindowHandle` for WinCMD or PowerShell.
7. Do not use `AttachConsole`/`FreeConsole` to borrow the child console.
8. Do not implement terminal rendering with a plain `TextBox` or `RichTextBox`. Those controls do not correctly implement cursor movement, alternate-screen buffers, colors, wide characters, combining characters, or VT erase operations.
9. Do not log terminal input or output. Commands can contain passwords, tokens, and other secrets.
10. Keep the existing external `ApplicationPanel` path unchanged except for small, protocol-neutral abstractions that have focused tests.
11. Do not silently fall back from ConPTY to the old HWND-capture path. If ConPTY or its renderer is unavailable, show a clear error or offer an explicitly external shell window.

## Why ConPTY solves the actual problem

`cmd.exe` and `powershell.exe` are console clients. On modern Windows, the visible console window is owned by a separate console-host process, so `Process.MainWindowHandle` on the shell is not a reliable window to embed. Searching for a newly created `ConsoleWindowClass` is inherently racy and can select the wrong console. Reparenting that window also brings desktop non-client state into the tab and can interfere with unrelated embedded applications.

ConPTY has no desktop console window to capture. SuperPuTTY creates two pipes and a pseudo-console object, starts the shell with the pseudo-console attached through `STARTUPINFOEX`, reads VT output from one pipe, and writes keyboard/input bytes to the other. A managed child control renders the VT stream inside the tab.

The intended data flow is:

```text
keyboard/paste/toolbar command
            |
            v
 managed terminal renderer ---- resize(cols, rows) ----> ConPTY
            |                                           |
            | UTF-8 input                               | attached shell
            v                                           v
        input pipe  ------------------------------> cmd.exe / powershell.exe
        output pipe <------------------------------ UTF-8 + VT sequences
            |
            v
 managed terminal renderer
```

There is no child desktop HWND in this flow.

## Microsoft API references

Use the official API contracts as the source of truth:

- [Creating a pseudoconsole session](https://learn.microsoft.com/windows/console/creating-a-pseudoconsole-session)
- [CreatePseudoConsole](https://learn.microsoft.com/windows/console/createpseudoconsole)
- [ResizePseudoConsole](https://learn.microsoft.com/windows/console/resizepseudoconsole)
- [ClosePseudoConsole](https://learn.microsoft.com/windows/console/closepseudoconsole)
- [InitializeProcThreadAttributeList](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-initializeprocthreadattributelist)
- [UpdateProcThreadAttribute](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
- [CreateProcessW](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)
- [Windows Terminal ConPTY sample](https://github.com/microsoft/terminal/tree/main/samples/ConPTY)

ConPTY is available on Windows 10 version 1809/build 17763 and later. Feature-detect `CreatePseudoConsole` at runtime instead of trusting an unmanifested `Environment.OSVersion` result.

## Recommended renderer

ConPTY produces a VT stream; it does not render a terminal. A real terminal emulator is required.

The practical default is:

- Managed WinForms host: Microsoft WebView2 WinForms control.
- Terminal emulator: xterm.js with the fit and Unicode addons.
- Assets: pinned, reviewed, and shipped locally with SuperPuTTY. Never load scripts, CSS, fonts, or terminal code from a CDN.
- Runtime: WebView2 Evergreen Runtime, with a clear diagnostic if unavailable.

Reasons for this default:

- xterm.js already implements the terminal state machine, alternate screen, cursor, selection, colors, Unicode behavior, and common mouse modes.
- WebView2 has a supported WinForms control and avoids writing a new terminal emulator.
- The browser surface remains a normal managed child control; it does not require external-window reparenting.

Before the production implementation, complete a small renderer spike and record:

- Selected WebView2 SDK version and restore mechanism.
- Selected xterm.js version and exact addons.
- License files and notices that must ship.
- Whether the Evergreen Runtime is an installer prerequisite or whether a fixed-version runtime will be bundled. Prefer Evergreen unless offline deployment is a hard requirement.
- Actual copied runtime files in `bin\x64\Release`.
- Measured behavior with at least 10 MiB of continuous output.

If WebView2 is rejected because of its runtime or packaging cost, evaluate an actively maintained, MIT-compatible, pure-managed VT renderer that supports .NET Framework 4.8 and WinForms. Do not begin a custom VT parser as an incidental part of this repair.

## Proposed source layout

Add a dedicated terminal namespace and keep native interop out of the general `NativeMethods` file where practical:

```text
SuperPutty/
  Terminal/
    ConPtyNative.cs
    SafePseudoConsoleHandle.cs
    PseudoConsoleSession.cs
    PseudoConsoleStartInfo.cs
    TerminalPanel.cs
    ITerminalRenderer.cs
    WebViewTerminalRenderer.cs
    VtKeyEncoder.cs
    Assets/
      index.html
      terminal.css
      terminal.js
      xterm.js
      xterm.css
      xterm-addon-fit.js
      xterm-addon-unicode11.js
      LICENSES.txt
```

Tests should be split by responsibility:

```text
SuperPuttyUnitTests/
  ConPtyNativeContractTests.cs
  PseudoConsoleStartInfoTests.cs
  PseudoConsoleSessionTests.cs
  TerminalPanelTests.cs
  VtKeyEncoderTests.cs
  TerminalAssetSecurityTests.cs
```

The exact filenames may change, but preserve these boundaries.

## Native interop contract

### Types and constants

Define narrowly scoped interop declarations in `Terminal/ConPtyNative.cs`:

- `COORD` with two signed 16-bit fields.
- `SECURITY_ATTRIBUTES`.
- `STARTUPINFO` and `STARTUPINFOEX` with correct x64 layout.
- `PROCESS_INFORMATION`.
- `CreatePipe`.
- `SetHandleInformation`.
- `InitializeProcThreadAttributeList`.
- `UpdateProcThreadAttribute`.
- `DeleteProcThreadAttributeList`.
- `CreateProcessW` with `CharSet.Unicode` and `SetLastError = true`.
- `CreatePseudoConsole`.
- `ResizePseudoConsole`.
- `ClosePseudoConsole`.
- `GetProcAddress`/`GetModuleHandle` or `NativeLibrary` equivalent suitable for .NET Framework 4.8 feature detection.

Required constants include:

```text
PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016
EXTENDED_STARTUPINFO_PRESENT        = 0x00080000
CREATE_UNICODE_ENVIRONMENT          = 0x00000400
HANDLE_FLAG_INHERIT                 = 0x00000001
```

Do not add `CREATE_NEW_CONSOLE`; the new process must attach to the pseudo console supplied through the attribute list.

### Safe handles

Wrap every owned native resource:

- Anonymous pipe handles: `SafeFileHandle`.
- Process handle: `SafeWaitHandle` or a dedicated safe process handle.
- Pseudo-console handle: a dedicated `SafePseudoConsoleHandle` whose release calls `ClosePseudoConsole` exactly once.
- Attribute-list memory: an `IDisposable` owner that always calls `DeleteProcThreadAttributeList` before freeing the allocated memory.
- Close the initial thread handle immediately after `CreateProcessW` succeeds.

Document ownership at every handoff. A handle must have one owner only.

### Pipe orientation

Create two anonymous pipe pairs:

```text
SuperPuTTY writes -> ConPTY input read handle
ConPTY output write handle -> SuperPuTTY reads
```

After `CreatePseudoConsole` succeeds:

- The parent retains the input pipe's write handle.
- The parent retains the output pipe's read handle.
- Dispose the parent copies of the two handles passed into ConPTY as soon as their ownership is no longer needed.
- Mark parent-only pipe handles non-inheritable with `SetHandleInformation`.

Use separate execution paths for reading output and writing input. Microsoft explicitly warns about synchronous pipe deadlocks when pseudoconsole communication is handled on one thread.

### Process creation

The process launch sequence must be deterministic:

1. Validate a nonzero terminal size and clamp columns/rows to `short.MaxValue`.
2. Create input and output pipes.
3. Create the pseudo console with flags set to zero.
4. Call `InitializeProcThreadAttributeList` once to obtain the required byte count, allocate it, and call it again to initialize.
5. Add `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` with `UpdateProcThreadAttribute`.
6. Build a mutable Unicode command-line buffer. `CreateProcessW` may modify that buffer.
7. Call `CreateProcessW` with `EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT` and the selected working directory.
8. Close the returned thread handle.
9. Retain the process handle for exit observation and bounded shutdown.
10. Start the output drain before allowing large amounts of child output to accumulate.

Pass the executable as `lpApplicationName`; do not rely on ambiguous executable lookup from an untrusted command string. Build arguments separately with the existing `CommandLineOptions.QuoteArgument` rules where applicable.

### HRESULT and Win32 errors

- `CreatePseudoConsole` and `ResizePseudoConsole` return an HRESULT. Treat any failing HRESULT as an error and preserve its numeric value in diagnostics.
- Win32 Boolean-returning functions must capture `Marshal.GetLastWin32Error()` immediately.
- Log operation names and error codes, never command text, environment contents, pipe contents, or terminal output.
- Convert unsupported-platform and missing-renderer failures into concise user-facing messages rather than unhandled exceptions.

## `PseudoConsoleStartInfo`

Create a small immutable model instead of routing local shells through `PuttyStartInfo`:

```csharp
internal sealed class PseudoConsoleStartInfo
{
    public string Executable { get; }
    public string Arguments { get; }
    public string WorkingDirectory { get; }
}
```

Factory behavior:

- `ConnectionProtocol.WINCMD`
  - Executable: `%SystemRoot%\System32\cmd.exe`.
  - Arguments: empty initially, preserving current behavior.
- `ConnectionProtocol.PS`
  - Executable: `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`.
  - Arguments: empty initially, preserving current behavior.
- Working directory:
  - Expand `%USERPROFILE%` with `Environment.ExpandEnvironmentVariables`.
  - Require an existing directory.
  - Fall back to `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` and finally `Environment.CurrentDirectory`.
- Reject every other protocol.

Do not add PowerShell Core (`pwsh.exe`) or arbitrary command configuration in the first repair. Those can be separate features after the lifecycle is stable.

## `PseudoConsoleSession`

`PseudoConsoleSession` owns ConPTY, pipes, process lifetime, and byte transport. It must not know about WinForms or WebView2.

Suggested public/internal surface:

```csharp
internal sealed class PseudoConsoleSession : IDisposable
{
    event EventHandler<PseudoConsoleOutputEventArgs> OutputReceived;
    event EventHandler<PseudoConsoleExitedEventArgs> Exited;

    bool IsRunning { get; }
    int ProcessId { get; }

    Task StartAsync(PseudoConsoleStartInfo info, short columns, short rows);
    Task WriteInputAsync(string text, CancellationToken token);
    void Resize(short columns, short rows);
    Task CloseAsync(TimeSpan gracefulTimeout);
}
```

Use an explicit state machine guarded by a lock or atomic transitions:

```text
Created -> Starting -> Running -> Closing -> Closed
                    \-> Faulted
```

Required behavior:

- `StartAsync` is single-use.
- Start failure cleans up every partially acquired handle.
- Output is decoded with a persistent strict or replacement UTF-8 `Decoder`, not `Encoding.UTF8.GetString` independently for each read. A multibyte character can cross a pipe-read boundary.
- Input writes are serialized. Concurrent paste, keyboard, and toolbar writes must not interleave bytes.
- A broken input pipe after process exit is a normal close condition.
- Process exit fires once.
- `CloseAsync` is idempotent.
- Normal close first closes the input writer to deliver EOF, then waits a short bounded interval for the shell.
- If the process does not exit, terminate only the process created for this terminal and wait again. Do not kill unrelated descendants by name.
- Keep draining output during shutdown. Closing a pseudo console while output is blocked can deadlock or lose the final screen update.
- Dispose the pseudo console and pipe handles exactly once after the reader has completed or a bounded forced-close path has been reached.
- Never block the WinForms UI thread on pipe reads, process waits, or close waits.

Because the application targets .NET Framework 4.8, do not assume `System.Threading.Channels` or modern async-stream APIs are present. A `ConcurrentQueue<T>`, `SemaphoreSlim`, `Task`, `CancellationTokenSource`, and carefully bounded counters are sufficient.

### Output backpressure

Terminal output can exceed the UI renderer's throughput. Implement batching:

- Read on a background task using a 16-64 KiB byte buffer.
- Decode incrementally.
- Coalesce decoded output for roughly 8-16 ms or up to a configured chunk threshold.
- Marshal batches to the renderer rather than invoking the UI for every pipe read.
- Track pending decoded bytes/characters.
- Bound pending memory, for example at 8 MiB. When the bound is reached, pause the reader until the UI consumes data; do not discard terminal output silently.
- Ensure closing/disposal wakes every wait so no background task remains stuck.

Stress-test this path with `for /L %i in (1,1,500000) do @echo %i` and an equivalent PowerShell pipeline.

## Renderer abstraction

Define an interface that allows ConPTY and panel lifecycle tests without WebView2:

```csharp
internal interface ITerminalRenderer : IDisposable
{
    event EventHandler<string> InputReceived;
    event EventHandler<TerminalSizeEventArgs> TerminalSizeChanged;
    event EventHandler Ready;

    Control Control { get; }
    bool IsReady { get; }

    Task InitializeAsync();
    void Write(string text);
    void FocusTerminal();
    void Clear();
}
```

Provide a fake renderer for unit tests.

### WebView2/xterm.js bridge

The local page must:

- Construct one xterm.js `Terminal` and `FitAddon` instance.
- Set a monospace font stack and sensible scrollback.
- Call `fit()` after the host panel settles and whenever its pixel size changes.
- Send `{ type: "ready" }` once handlers are installed.
- Send `{ type: "input", data: "..." }` from `terminal.onData`.
- Send `{ type: "resize", columns: n, rows: n }` after fitting.
- Accept host messages `{ type: "output", data: "..." }`, `{ type: "focus" }`, and `{ type: "clear" }`.
- Use `terminal.write(data)` for output.
- Focus the hidden xterm textarea when the tab is activated.
- Prevent page navigation, external links, drag/drop navigation, and new windows.

Host-side security requirements:

- Serve assets from a local virtual host mapping or another WebView2-supported local origin; do not use remote URLs.
- Apply a restrictive Content Security Policy. At minimum, disallow network connections, frames, objects, and arbitrary external scripts.
- Disable developer tools, browser accelerator keys, status bar, zoom controls, default context menus, and autofill unless a required accessibility behavior depends on them.
- Validate every web message as a small tagged schema.
- Bound input message size. Large paste must be chunked rather than allocated as one unbounded message.
- Ignore messages received before initialization or after closing.
- Do not expose arbitrary .NET host objects to JavaScript.
- Store WebView2 user data under the application's per-user data directory, not the installation directory.

### Renderer startup ordering

Initialize the renderer before starting the shell, or buffer output until the renderer sends its ready handshake. The shell can emit a prompt immediately. No initial bytes may be lost.

If WebView2 initialization fails, dispose any partial renderer state and show a single actionable error. Do not start a hidden shell process behind a failed terminal surface.

## `TerminalPanel`

Model this class after `RdpClientPanel`, which is already a managed `ApplicationPanel` subclass:

```csharp
internal sealed class TerminalPanel : ApplicationPanel
```

Responsibilities:

- Own one `ITerminalRenderer` and one `PseudoConsoleSession`.
- Return `true` from `UsesManagedChildHost`.
- Start once, only after its WinForms handle and renderer are ready.
- Dock the renderer control to `DockStyle.Fill`.
- Forward renderer input to `PseudoConsoleSession.WriteInputAsync`.
- Forward ConPTY output to the renderer through the batching mechanism.
- Debounce pixel resize events, obtain xterm's fitted columns/rows, and call `ResizePseudoConsole` only when the character dimensions actually changed.
- Override `ReFocusPuTTY` to focus the renderer.
- Override `RefreshAppWindow` to refit/invalidate the renderer; do not call native `MoveWindow`.
- Override `ExternalProcessCaptured` with semantics suitable for the existing tab code, preferably true while the renderer and session are usable.
- Override `AppWindowHandle` only for compatibility. Returning the managed renderer/panel handle must not imply that commands should be sent with `WM_CHAR`.
- Close asynchronously and notify `m_CloseCallback` once with `Interlocked.Exchange` protection.
- Detach events before disposing the renderer/session.

Do not perform asynchronous work directly in `Dispose(bool)` without coordination. Start closure from `OnHandleDestroyed` or an explicit close method, make it idempotent, and ensure the final unmanaged cleanup cannot be skipped if the UI disappears.

Use a short resize debounce, approximately 100-200 ms. Dock splitter movement produces many intermediate sizes.

## Integration with `ctlPuttyPanel`

Update `ctlPuttyPanel.CreatePanel` in a narrowly scoped branch:

1. If the protocol is `WINCMD` or `PS`, construct `TerminalPanel`.
2. Else if managed RDP applies, retain the current `RdpClientPanel` path.
3. Else retain the existing `ApplicationPanel` path byte-for-byte where possible.

Do not construct `PuttyStartInfo` for managed console sessions. `PuttyStartInfo` should remain responsible for external applications and PuTTY-family arguments; `PseudoConsoleStartInfo` should own local-shell selection.

The block that sets these properties must execute only for external panels:

```text
ApplicationName
ApplicationParameters
ApplicationWorkingDirectory
ApplicationCloseWithDestroy
```

The existing `AppPanel` property can remain typed as `ApplicationPanel`, matching the RDP managed-child pattern.

### PuTTY-specific menus

`AdjustMenu` currently disables PuTTY menu items only for MinTTY. Disable PuTTY-only actions for `WINCMD` and `PS` as well:

- Event log
- Change settings
- Copy all to clipboard, unless a terminal-specific implementation is provided
- Restart/reset/clear commands that currently use PuTTY `WM_SYSCOMMAND` values
- About PuTTY

Terminal-specific clear, copy, paste, and restart actions may be implemented separately through the renderer/session APIs.

## Remove HWND dependence from command sending

The command toolbar currently calls:

```csharp
command.SendToTerminal(panel.AppPanel.AppWindowHandle);
```

That cannot work for ConPTY because there is no console input HWND. Introduce a protocol-neutral method on `ApplicationPanel`:

```csharp
public virtual bool SendTerminalCommand(CommandData command)
```

Base behavior:

- Validate `command` and `AppWindowHandle`.
- Call the existing `command.SendToTerminal(AppWindowHandle)` for PuTTY/external-window sessions.
- Return whether dispatch was accepted.

`TerminalPanel` behavior:

- Convert `CommandData.Command` directly to input text.
- Convert `CommandData.KeyData` through `VtKeyEncoder`.
- Preserve `CommandData.Delay` outside the UI thread. Do not call `Thread.Sleep` on the UI thread.
- Queue the resulting UTF-8 input through `PseudoConsoleSession`.
- Return false after closing.

Change `frmSuperPutty.TrySendCommandsFromToolbar` to call the panel method and increment `sent` only when it returns true.

### `VtKeyEncoder`

Cover every key currently accepted by `Scripting/SendKeyCommand.cs`:

- Enter: `\r`
- Tab: `\t`
- Escape: `\x1b`
- Backspace: normally `\x7f`, verified against xterm.js configuration
- Arrow keys: CSI `A/B/C/D`
- Home/end and insert/delete/page keys using standard xterm-compatible sequences
- Function keys F1-F16
- Ctrl+A through Ctrl+Z as control bytes 1-26
- Alt-modified printable input with an ESC prefix
- Shift/control modifier parameters for navigation keys where supported

Test mappings independently. Do not infer VT sequences by sending fake Windows messages to the WebView HWND.

Physical typing should normally be translated by xterm.js `onData`, so `VtKeyEncoder` is mainly for the toolbar and SPSL scripting path.

## Focus tracking

`ChildWindowFocusHelper` currently indexes `ctlPuttyPanel` objects by `AppWindowHandle`. A managed terminal can have a zero or not-yet-created handle when added, and multiple zero keys cause duplicate-key exceptions.

Do not reintroduce the broad focus changes from the reverted console experiments. Make one small, tested correction:

- Add a public/internal virtual property such as `ParticipatesInExternalWindowFocusTracking` to `ApplicationPanel`.
- Base external panel: `true`.
- `RdpClientPanel` and `TerminalPanel`: `false`.
- `ChildWindowFocusHelper` skips panels for which this property is false.
- For external panels, retain existing handle lookup behavior unless a separate tested bug requires changing it.

Tab activation already calls `ctlPuttyPanel.SetFocusToChildApplication`, which will use the `TerminalPanel.ReFocusPuTTY` override. Focus the renderer's actual input element after WebView2 receives focus.

## Toolbar and saved-session behavior

The session editor already treats `WINCMD` and `PS` as local protocols and assigns the local-host placeholder. Preserve saved-session compatibility.

The quick-connect toolbar still requires a nonempty host. Fix this without touching native window code:

- Add a pure helper that returns true for local protocols (`WINCMD`, `PS`, `Cygterm`, and `Mintty`).
- Permit an empty toolbar host for those protocols.
- Use a stable local session name such as the protocol display name when host is empty.
- Keep remote protocols' host validation unchanged.
- Add unit tests for both local and remote cases.

Dropdown ordering is a separate UI request and should not be coupled to ConPTY lifecycle work unless explicitly requested again.

## Shutdown behavior

Closing must be deterministic and nonblocking:

1. Mark the panel/session as closing atomically.
2. Stop accepting renderer and toolbar input.
3. Detach UI events that can schedule new work.
4. Close the ConPTY input writer to signal EOF.
5. Wait asynchronously for the shell for a bounded period, initially 1-2 seconds.
6. If still running, terminate the owned process and wait for a second bounded period.
7. Continue draining output until EOF or the forced-close bound expires.
8. Dispose process, output reader, input writer, pseudo-console handle, timers, and cancellation sources.
9. Notify the close callback exactly once.

Never use `Thread.Abort`. Never sleep on the WinForms UI thread. Never send `WM_DESTROY` or `WM_CLOSE` to the terminal panel or WebView.

Test all of these paths:

- Tab close while idle.
- Tab close during continuous output.
- Application File > Exit.
- Main-window title-bar close with confirmation enabled and disabled.
- Shell exits itself using `exit`.
- Renderer initialization fails before the process starts.
- Process launch fails after renderer initialization.
- App closes while renderer initialization is in flight.

## Error handling and diagnostics

User-facing errors should distinguish:

- ConPTY unavailable on this Windows version.
- WebView2 runtime unavailable.
- Terminal assets missing or invalid.
- Shell executable missing.
- Invalid working directory and fallback used.
- `CreatePseudoConsole` failure.
- Process creation failure.
- Renderer failure after process start.

Logs may include protocol, state transition, process ID, terminal dimensions, HRESULT/Win32 code, and elapsed times. Logs must not include:

- Typed input
- Pasted text
- Toolbar/SPSL command text
- Terminal output
- Full environment blocks
- Secrets embedded in arguments

## Unit tests

At minimum, add isolated tests for:

### Start information

- WinCMD selects native `cmd.exe`.
- PS selects Windows PowerShell.
- `%USERPROFILE%` is expanded.
- Invalid working directory falls back safely.
- Unsupported protocols are rejected.
- No command or path is logged as terminal content.

### Native contracts

- Struct sizes and offsets are correct on x64.
- P/Invoke return types use pointer-sized handles.
- `STARTUPINFO.cb` and `STARTUPINFOEX.StartupInfo.cb` are set correctly.
- Pseudoconsole attribute value and process flags are correct.
- Column/row validation rejects zero, negative, and overflow values.

### Session lifecycle

Use injectable native/process/pipe adapters rather than invoking real ConPTY in unit tests:

- Start transitions once.
- Partial-start failure disposes acquired resources.
- Concurrent input writes remain ordered.
- Split UTF-8 sequences decode correctly.
- Exit notification fires once.
- Close is idempotent.
- Forced close occurs only after graceful timeout.
- Output drain is not stopped before process/pseudoconsole closure.
- Cancellation wakes blocked producer/consumer paths.

### Panel lifecycle

- Managed-host bypass is active.
- Start waits for renderer ready.
- Output received before ready is preserved.
- Resize is debounced and duplicate character sizes are ignored.
- Focus delegates to the renderer.
- Close callback fires once.
- No call reaches external-window APIs.

### Input encoding

- Text is unchanged.
- Enter, tab, escape, arrows, navigation, function keys, modifiers, and Ctrl+A-Z produce expected bytes.
- Large paste is chunked and ordered.
- Commands after close are rejected.

### Asset security

- All HTML script/style references are local.
- CSP is present and disallows remote connections.
- Required pinned assets and license notices exist.
- No CDN URL appears in terminal assets.

Update the documented test count in `README.md` only after the final filtered test run.

## Integration and manual tests

Add a Windows-only integration-test script under `build/`, separate from isolated unit tests. It should create a real ConPTY session without opening the full UI and verify:

1. `cmd.exe /c echo CONPTY_OK` produces `CONPTY_OK` and exits.
2. PowerShell writes a Unicode value and exits.
3. Input sent after launch is echoed/processed.
4. Resize accepts several valid dimensions.
5. A high-output command completes without deadlock or unbounded memory growth.
6. Closing a long-running command finishes within the configured bound.

Manual UI acceptance matrix:

- Open one WinCMD tab, one PS tab, and two PuTTY SSH tabs.
- Type interactively in every tab.
- Switch repeatedly among all tabs for at least two minutes.
- Resize the main window and drag dock splitters continuously.
- Maximize, restore, minimize, and restore SuperPuTTY.
- Move SuperPuTTY between monitors with different DPI settings.
- Open multiple WinCMD and PS tabs in rapid succession.
- Close console tabs in different orders.
- Run full-screen console programs that use the alternate screen.
- Test selection, copy, paste, scrollback, Unicode, colors, cursor movement, and clear-screen.
- Run toolbar command broadcasting to PuTTY and ConPTY tabs together.
- Confirm PuTTY titles, borders, size, mouse behavior, and keyboard focus are byte-for-byte behaviorally unchanged from baseline commit `d12e2c0`.
- Confirm no orphaned `cmd.exe`, `powershell.exe`, WebView2, or SuperPuTTY processes remain after exit.

## Packaging and signing

The application project is an old-style .NET Framework project. Add dependencies deliberately:

- Prefer `PackageReference` only after verifying restore/build behavior with the existing Visual Studio/MSBuild pipeline.
- Pin the WebView2 SDK version.
- Pin and vendor xterm.js assets rather than resolving npm packages during the release build.
- Include third-party license notices in the portable and MSI payloads.
- Ensure WebView2Loader and any other required architecture-specific files are copied to `bin\x64\Release`.
- Mark terminal assets as content with `CopyToOutputDirectory` behavior appropriate to Debug and Release.
- Add every required runtime file and asset directory to `SuperPuttyInstaller/Product.wxs`.
- Extend `build/Verify-ReleaseArtifacts.ps1` to verify the new DLLs/assets in both the release directory and an administrative MSI extraction.
- Extend Azure signing only for signable PE files actually shipped. JavaScript/CSS/HTML assets are covered by the signed MSI but are not Authenticode targets.
- Re-run `build/Verify-CodeSignatures.ps1` for signed builds.

Do not download renderer assets during Azure Pipelines. Restore managed packages from the locked package source and build from vendored terminal assets.

## Suggested implementation sequence

Keep changes reviewable and do not combine the entire feature into one untested patch.

### Phase 1: renderer decision spike

- Add no production routing changes.
- Prove a local xterm.js/WebView2 control can initialize, accept VT text, emit input, fit, and refocus.
- Confirm packaging footprint and license requirements.
- Stress with 10 MiB output.
- Record the result in this document.

Exit criterion: renderer choice is proven and does not affect PuTTY.

### Phase 2: ConPTY transport

- Add native contracts, safe handles, start-info model, and `PseudoConsoleSession`.
- Add fakeable adapters and unit tests.
- Add the headless integration-test script.
- Do not route real UI sessions yet.

Exit criterion: real cmd/PowerShell headless tests pass repeatedly with clean process shutdown.

### Phase 3: managed terminal panel

- Add renderer abstraction and `TerminalPanel`.
- Test ready/start/output/input/resize/focus/close behavior with fakes.
- Keep it unreachable from normal session creation until tests pass.

Exit criterion: panel lifecycle tests pass without native-window APIs.

### Phase 4: application integration

- Route only `WINCMD` and `PS` through `TerminalPanel`.
- Add command-dispatch abstraction.
- Add focus-helper managed-panel exclusion.
- Disable PuTTY-only menu items.
- Fix local toolbar launch without requiring a host.

Exit criterion: manual mixed-session matrix passes and PuTTY behavior matches baseline.

### Phase 5: release integration

- Add project content/dependencies.
- Update MSI payload and release verifier.
- Update pipeline/signing file lists as needed.
- Update release notes and README test count.
- Build a clearly named prebuild for user testing.

Exit criterion: clean x64 rebuild, all isolated tests, ConPTY integration tests, shutdown tests, release-artifact verification, and signed-build verification pass.

## Commands for final verification

Use the installed Visual Studio MSBuild path discovered with `Get-Command msbuild.exe` or `vswhere`; do not hardcode the example if it differs.

```powershell
msbuild.exe SuperPutty.sln `
  /t:Rebuild `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /m `
  /v:minimal

dotnet test SuperPuttyUnitTests\SuperPuttyUnitTests.csproj `
  --no-build `
  --configuration Release `
  --property:Platform=x64 `
  --filter "TestCategory!=NetworkTest" `
  --logger "console;verbosity=minimal"

.\build\Test-ConPty.ps1 -Configuration Release -Platform x64
.\build\Verify-ReleaseArtifacts.ps1 `
  -Configuration Release `
  -Platform x64 `
  -ExpectedVersion 1.7.1.0

.\build\Test-ApplicationShutdown.ps1 `
  -Configuration Release `
  -Platform x64
```

For signed builds, also run:

```powershell
.\build\Verify-CodeSignatures.ps1 `
  -Configuration Release `
  -Platform x64 `
  -ExpectedSigner "Christopher Thornton"
```

## Definition of done

The ConPTY repair is complete only when all of the following are true:

- WinCMD and PowerShell render and accept input inside tabs.
- No console desktop window is created, discovered, or reparented.
- PuTTY code paths do not receive console-specific style, focus, resize, or event-hook changes.
- Multiple console tabs can open without duplicate-handle exceptions.
- Switching away from and back to a console tab restores interactive focus.
- Tab and application closure leave no orphaned shell processes.
- Continuous output does not freeze the UI or grow memory without bound.
- Unicode, colors, cursor movement, alternate screen, selection, paste, and scrollback work.
- Toolbar/SPSL command dispatch works through the input pipe, not window messages.
- Terminal input/output and secrets never appear in logs.
- Unsupported Windows/WebView2 states fail clearly and safely.
- Portable and MSI packages contain every required dependency and local asset.
- The complete automated and manual verification matrix passes.

If a proposed shortcut violates any guardrail above, stop and reconsider the design rather than patching PuTTY's embedding behavior again.
