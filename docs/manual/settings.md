# Settings and Storage

[Back to the manual](README.md)

## File locations

SuperPuTTY uses two related locations:

- `SuperPutty.settings` stores application preferences. It is normally in `%USERPROFILE%`; placing it beside `SuperPutty.exe` enables the established portable-settings behavior.
- The configured settings folder contains `Sessions.XML`, `AutoRestoreLayout.XML`, named layouts, and theme resources.

New profiles reuse an existing writable Documents-based SuperPuTTY folder when available. Otherwise, the application uses `%LOCALAPPDATA%\SuperPuTTY`. If a configured folder becomes unwritable, SuperPuTTY can fall back to Local AppData and copy readable settings without overwriting files already there.

Use **Tools > Options > General** to see or change the active settings folder. The directory must be writable.

## Backups

Close SuperPuTTY before making a manual backup. Copy at least:

- `SuperPutty.settings`
- `Sessions.XML`
- `AutoRestoreLayout.XML`
- the `layouts` directory
- custom theme icons or SPSL scripts

The application also creates timestamped session backups when replacing the session database.

## Logging

Runtime logs are written under `%TEMP%` by default. Choose **Tools > Open Log File Location** to open the directory in File Explorer. Advanced log4net configuration is in `SuperPutty.exe.config`.

Logs may contain hostnames, usernames, paths, and command output. Password-like command-line values are redacted, but logs should still be reviewed before sharing.

See [Security and privacy](security.md) for credential, host-key, certificate, script, and log guidance.

## Updates

The update channel can be selected under **Tools > Options**. Choose the community-fork channel for releases from `greyhair-atx/superputty`, or upstream when following Jim Radford's original project releases.
