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
| `CLOSESESSION` | session name | Closes a session selected by the command implementation |

Commands are matched without regard to letter case. The argument begins after the first space and continues to the end of the line.

## Example

```text
#!/bin/spsl
# Wait for the shell and run a harmless command.
SLEEP 1000
SENDLINE hostname
SENDLINE whoami
```

Use the Script Editor from the command toolbar to create, load, save, and run scripts. A saved session can also reference an SPSL file to run when the session opens.

A session may reference a local script path or an HTTPS URL. SuperPuTTY asks for confirmation before a remote script can type into a session. Remote scripts reject redirects and embedded URL credentials, time out after ten seconds, and are limited to 1 MiB. Plain HTTP remote scripts are blocked.

## Security

Do not store passwords directly in scripts. Use `PWDPROMPT` when sensitive text must be collected at runtime, and protect scripts that contain other confidential commands or host information. Review the complete content and source of a remote script before approving it.
