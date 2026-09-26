# SPSL Scripting

[Back to the manual](README.md)

SuperPuTTY Scripting Language (SPSL) sends scripted input to terminal windows. Scripts are plain text and may begin with `#!/bin/spsl`. Blank lines and lines beginning with `#` are ignored.

## Commands

| Command | Argument | Purpose |
| --- | --- | --- |
| `SENDLINE` | text | Sends text followed by Enter |
| `SENDCHAR` | text | Sends characters without Enter |
| `SENDKEY` | key expression | Sends a named key or key combination |
| `SLEEP` | milliseconds | Pauses script execution |
| `PROMPT` | message | Requests visible user input |
| `PWDPROMPT` | message | Requests masked sensitive input |
| `OPENSESSION` | session name | Opens a saved session |
| `CLOSESESSION` | session name | Not supported; stops the script with an error. Close the tab manually. |

Commands are matched without regard to letter case. The argument begins after the first space and continues to the end of the line.

## Key expressions

`SENDKEY` accepts one letter/digit or a named key in braces, optionally prefixed by `^` (Ctrl), `+` (Shift), or `%` (Alt). Examples: `SENDKEY {ENTER}`, `SENDKEY ^c`, and `SENDKEY +c`. A named key can include a repeat count from 1 to 10000: `SENDKEY {ENTER 3}` sends three complete presses/releases. Use `SENDCHAR abc` for literal text; `SENDKEY abc` is an error.

Character shortcuts use terminal character input: `^c` sends ETX (Ctrl+C), `+c` sends uppercase C, and `%x` sends Escape followed by x. Shifted digits use the US symbol mapping; `{+}`, `{%}`, and `{^}` send the corresponding literal symbols. Ctrl is supported for letters A–Z. Modified special keys such as `^{LEFT}`, `+{F1}`, and `%{TAB}` are rejected because background window messages cannot reliably reproduce their keyboard state. This is terminal input, not general desktop-shortcut automation.

Unknown names, malformed expressions, extra keys, duplicate modifiers, invalid repeat counts, and unsupported modifier combinations stop the script with a line-numbered error.

## Example

```text
#!/bin/spsl
# Wait for the shell and run a harmless command.
SLEEP 1000
SENDLINE hostname
SENDLINE whoami
```

Use the Script Editor from the command toolbar to create, load, save, and run scripts. A saved session can also reference an SPSL file to run when the session opens.

A failed or unsupported command stops the script and reports its line number in the status area. Later commands are not executed. Remote startup scripts download in the background with a ten-second overall deadline; closing the session cancels its pending download. Startup scripts wait until the terminal window has been captured before executing.

Scripts with overlapping target sessions run one at a time, including their sleeps and prompts. A later script waits until all its target sessions are available; closing its targets cancels the wait. Scripts on separate sessions can run concurrently. Command-toolbar input to a session reserved by a script is rejected with a busy status message.

For the embedded Microsoft RDP client, startup scripts wait for the login-complete event, rather than merely the creation of the RDP window.

Canceling either input prompt stops the script without sending text or Enter. Closing a target session stops input to that session; when no original target sessions remain, the script stops, including pending sleeps and prompts. Script Editor targets are fixed when Run is clicked, so changing the toolbar selection does not redirect a running script.

With no initially selected terminals, scripts may still run `OPENSESSION` and `SLEEP`. They do not automatically target newly opened sessions. Input commands and prompts require a terminal selected when the script starts; place subsequent input in the new session's startup script or select it before running another script.

A session may reference a local script path or an HTTPS URL. SuperPuTTY asks for confirmation before a remote script can type into a session. Remote scripts reject redirects and embedded URL credentials, time out after ten seconds, and are limited to 1 MiB. Plain HTTP remote scripts are blocked.

## Security

Do not store passwords directly in scripts. Use `PWDPROMPT` when sensitive text must be collected at runtime, and protect scripts that contain other confidential commands or host information. Review the complete content and source of a remote script before approving it.
