# Settings and Storage

[Back to the manual](README.md)

## File locations

SuperPuTTY uses two related locations:

- `SuperPutty.settings` stores application preferences. It is normally in `%USERPROFILE%`; the file beside `SuperPutty.exe` is used when no profile settings file exists.
- The configured settings folder contains `Sessions.XML`, `AutoRestoreLayout.XML`, named layouts, and theme resources.

New profiles reuse an existing writable Documents-based SuperPuTTY folder when available. Otherwise, the application uses `%LOCALAPPDATA%\SuperPuTTY`. If a configured folder becomes unwritable, SuperPuTTY can fall back to Local AppData and copy settings while preserving files already there. It validates the session catalog before saving the new folder. If copying or validation fails, check the original folder and restart SuperPuTTY. The configured path remains available for retry.

Use **Tools > Options > General** to see or change the active settings folder. The directory must be writable.

When multiple SuperPuTTY instances share these files, a save is rejected if another instance has changed the preferences or session catalog since it was loaded. Use **Reload Sessions** before retrying a session save; restart SuperPuTTY to reload application preferences. This keeps an older instance from silently replacing newer changes. File access retries transient sharing violations for a bounded time and reports a busy file if access remains unavailable.

Options validates and loads a changed settings folder before applying it. Canceling during that load leaves the current folder and sessions active. A failed preference save restores the previous settings and catalog. Session imports do not reset the current catalog's protection against changes made by another instance; failed session or folder deletions retain the original sessions.

Export sessions to a separate file. Export cannot replace the active `Sessions.XML`. Before **Reload Sessions** or a settings folder change, SuperPuTTY asks you to save or discard pending session moves. A folder change keeps the open workspace visible and asks for a destination the next time you use **Save Layout**.

Local session catalog XML files are limited to 64 MiB per file. The application preferences XML file and its backup are each limited to 16 MiB.

Single-instance requests use a local pipe restricted to the current Windows user and session. Startup and shutdown reject new requests until the UI can accept them. If an exit confirmation is canceled, previously accepted requests resume. When an older version is running, close it before using single-instance mode with this version.

If the single-instance connection cannot start, SuperPuTTY can still open an independent window when single-instance mode is disabled.

## Protocol dropdowns

Under **Tools > Options > Protocols**, check the protocols you want in the connection toolbar and new session dropdowns. Uncheck protocols to hide them, or use **Show all** to restore every option. Keep at least one checked. Changes apply when you save, without restarting. All protocols are shown by default.

Hidden protocols still work for saved sessions. When editing an existing session, its current protocol remains available so it is not changed accidentally.

## Closing session tabs (unreleased improvements)

Closing a connected, connecting or reconnecting tab asks for confirmation. A confirmed disconnected session does not need the session-close warning. If SuperPuTTY cannot determine the connection state, it warns that the session may still be connected; Cancel keeps the tab open.

Embedded RDP uses the client's connection events and connection-state property. A running external PuTTY or RDP process can remain open after disconnection, so those clients retain the uncertainty warning. Window titles and TCP sockets are not treated as proof that a remote session has ended. These checks do not determine whether a remote command or job is running. Confirmed bulk closes and application shutdown retain their existing confirmation behavior.

## SCP interactive authentication (unreleased)

Edit a saved SCP or SSH session and enable **Interactive SCP authentication (MFA)**. SCP directory listings and transfers then open an embedded PSCP console. Enter the password, verification code, recovery code or other server-requested response directly in that console. PSCP handles hidden input and host-key confirmation. Stored passwords and password-file arguments are not supplied in this mode.

Use **Cancel** to stop the operation and its console processes. Operations time out after five minutes without output/progress (or a longer configured PSCP timeout). Each directory listing or transfer starts a new connection and may require MFA again. The existing batch authentication mode remains the default. Authentication responses are not captured in SuperPuTTY logs. Enabling this option does not change terminal SSH authentication.

## Application theme (1.8.3)

Under **Tools > Options > GUI > Misc > Theme**, choose Light, Dark, Dark Modern, or System. Save and restart to apply the choice. Light is the default; Dark Modern uses a VS Code inspired palette and flat document tabs. System follows the Windows app preference at startup using the original Dark style. External terminal and remote desktop colors are configured in their own applications. Dark mode was contributed by [dustinreeves](https://github.com/dustinreeves).

## Backups

Close SuperPuTTY before making a manual backup. Copy at least:

- `SuperPutty.settings`
- `Sessions.XML`
- `AutoRestoreLayout.XML`
- the `layouts` directory
- custom theme icons or SPSL scripts

The application also creates timestamped session backups when replacing the session database.

Application preferences are saved to a temporary file beside `SuperPuTTY.settings`, flushed to disk, and then published with an atomic replacement. After replacing an existing file, `SuperPuTTY.settings.bak` contains the previous save; each successful replacement refreshes this single backup. The first save creates the settings file without a backup. If writing or replacement fails, the application logs the error and does not fall back to overwriting the original file directly.

If the preference file is missing or contains invalid XML, SuperPuTTY validates `SuperPuTTY.settings.bak` and restores it automatically. Recovery preserves a damaged original beside the settings file as `SuperPuTTY.settings.corrupt.<unique identifier>`, leaves the backup intact, and displays a startup notice with the file locations. Later saves continue to keep the previous valid save in `.bak`. Preference XML must have a `Settings` root without a namespace; document type declarations are not supported.

If the existing file cannot be read, neither file is valid, or recovery cannot safely preserve and replace the original, startup reports the problem instead of saving default preferences over the files. Close SuperPuTTY, check file permissions, and keep copies of the existing files before repairing the XML or restoring a known-good backup. To restore a backup manually, copy `SuperPuTTY.settings.bak` over `SuperPuTTY.settings` while the application is closed. The preference backup does not include sessions or layouts; keep backing those up separately.

## Logging

Runtime logs are written under `%TEMP%` by default. Choose **Tools > Open Log File Location** to open the directory in File Explorer. Advanced log4net configuration is in `SuperPutty.exe.config`.

Logs may contain hostnames, usernames, paths, and command output. Password-like command-line values are redacted, but logs should still be reviewed before sharing.

See [Security and privacy](security.md) for credential, host-key, certificate, script, and log guidance.

## Updates

The update channel can be selected under **Tools > Options**. Choose the community-fork channel for releases from `greyhair-atx/superputty`, or upstream when following Jim Radford's original project releases.

New profiles default to **Community fork**. Missing or unrecognized channel values also use the community repository. An explicitly saved **Official upstream** or **Community fork** choice is preserved; upgrading does not migrate existing preferences.
