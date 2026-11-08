# SCP File Transfers

[Back to the manual](README.md)

SuperPuTTY uses PuTTY's `pscp.exe` for the integrated two-pane file browser. SuperPuTTY does not implement SSH or SCP itself.

## Configure an SCP session

1. Confirm the PSCP location under **Tools > Options > General**.
2. Create or edit a saved session.
3. Select **SCP** as the protocol.
4. Enter the host, port, and username.
5. Optionally select a PuTTY profile.
6. Set the initial Local Path and Remote Path if desired.
7. For key authentication, select a PuTTY-format `.ppk` file in **Private Key**.
8. Save and open the session.

SSH saved sessions can also carry these file-transfer options when SCP is opened for that session.

## Optional WinSCP and FileZilla shortcuts

Paths for WinSCP and FileZilla can be set under **Tools > Options > General**. When a configured executable is available, the saved session's context menu can launch that program with the session's host, port, username, and local or remote starting path.

These are external SFTP clients and do not use SuperPuTTY's protected PSCP named-pipe password flow. With plain-text password arguments disabled, the client must obtain authentication itself. Do not enable command-line password forwarding merely to avoid the client's normal prompt.

## Authentication order

In ordinary non-interactive mode, PSCP can authenticate using a configured private key, Pageant, GSSAPI, or a password. SuperPuTTY first allows non-password authentication to succeed. If the server requires a password, SuperPuTTY asks for it after PSCP begins the connection and retries securely.

When a password is required, the default implementation sends it to PSCP through a user-restricted, one-use Windows named pipe used with `-pwfile`. The password is not placed in a temporary disk file or persisted in the session database. Plain-text `-pw` arguments remain an explicit compatibility option and are not recommended.

## Interactive password and MFA prompts

Edit the saved session and enable **Interactive SCP authentication (MFA)** above **File Transfer Options**, save, then close and reopen the file-transfer tab. This also applies to an SSH session opened through its **File Transfer** context-menu item.

PSCP then presents its own console for passwords, token codes and subsequent challenge/response prompts. This option removes `-batch` and supplied password arguments. Use it whenever a fresh connection requires multiple prompts, including a password followed by another PAM password prompt; the checkbox does not require an OTP provider.

Without connection sharing, each directory listing or transfer creates a new SSH connection and can prompt again. A completed upload can trigger a directory refresh and another login. SuperPuTTY does not retain the console responses for later operations.

## Reuse an open SSH connection

PuTTY connection sharing allows PSCP to open file-transfer channels over an already authenticated SSH connection:

1. Set the **Username** explicitly in the SuperPuTTY saved session. Entering a username only at PuTTY's terminal prompt does not populate this field. A blank PSCP username can fail to match the shared connection.
2. In the selected saved PuTTY profile, enable **Connection > SSH > Share SSH connections if possible**, leaving upstream and downstream sharing enabled, then save the profile.
3. Select that profile in the SuperPuTTY session. If the SSH connection was opened before sharing was enabled, close and reconnect that SSH tab and complete all authentication challenges.
4. Keep the SSH terminal open and choose **File Transfer** for the saved session. A separate SCP entry can share it too when its host, port, username and profile settings match. Keep interactive authentication enabled for a server requiring additional prompts if a fresh connection becomes necessary.

Both clients must run as the same Windows user at a compatible elevation level. Use the same host spelling and port. `-share` in Extra Arguments explicitly enables sharing and is accepted by PuTTY and PSCP. For a shared SSH entry, avoid PSCP-only switches such as `-sftp` in Extra Arguments, because that field also goes to PuTTY; use a separate SCP entry if PSCP-specific arguments are needed.

PSCP still starts a process for each operation, so the console can appear briefly. While the shared connection remains available, the operation should not require another password or token. Closing the upstream SSH connection or losing the network means a fresh connection must authenticate again. This behavior is described in the [PuTTY connection sharing documentation](https://the.earth.li/~sgtatham/putty/0.84/htmldoc/Chapter4.html#config-ssh-sharing).

## Private keys

The **Private Key** field is passed to PSCP with `-i` for directory listings and file copies. PuTTY 0.84 expects a PuTTY Private Key (`.ppk`) and does not directly accept a new-format OpenSSH private key through `-i`.

To use an existing OpenSSH key:

1. Open PuTTYgen.
2. Import the OpenSSH private key.
3. Save a private-key copy in `.ppk` format.
4. Protect it with appropriate Windows file permissions.
5. Select the `.ppk` file in the SuperPuTTY session.

If the `.ppk` is passphrase-protected, load it into Pageant before opening the SCP session. This avoids hidden passphrase prompts while PSCP is running in batch mode.

The matching public key must be installed for the remote account, normally in `~/.ssh/authorized_keys` on an OpenSSH server.

## Diagnostics

Add `-v` to **Extra Arguments** for verbose PSCP diagnostics. Then use **Tools > Open Log File Location** and inspect the current SuperPuTTY log.

Useful messages include:

- `Offering public key`: PSCP found a candidate key.
- `Authenticating with public key`: the server accepted the key.
- `Unable to use this key file`: the key format or file is unusable.
- `Cannot answer interactive prompts in batch mode`: PSCP needs credentials or another interactive answer.
- `Access denied` or `Authentication failed`: the supplied authentication method was rejected.

Remove `-v` after troubleshooting if the additional output is no longer needed.
