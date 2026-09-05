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

## Authentication order

PSCP can authenticate using a configured private key, Pageant, GSSAPI, or a password. SuperPuTTY first allows non-password authentication to succeed. If the server requires a password, SuperPuTTY asks for it after PSCP begins the connection and retries securely.

When a password is required, the default implementation sends it to PSCP through a user-restricted, one-use Windows named pipe used with `-pwfile`. The password is not placed in a temporary disk file or persisted in the session database. Plain-text `-pw` arguments remain an explicit compatibility option and are not recommended.

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
