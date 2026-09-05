# Upgrading from SuperPuTTY 1.5

[Back to the manual](README.md)

The community edition preserves the familiar session and docking model while modernizing the Windows runtime and several security-sensitive paths.

## Before upgrading

1. Close SuperPuTTY normally.
2. Back up `SuperPutty.settings`, `Sessions.XML`, layouts, custom icons, and SPSL scripts.
3. Record the paths to PuTTY, PSCP, and optional external clients.
4. Keep the 1.5 package until the new installation and session database are verified.

## Important changes

- The supported build is 64-bit Windows with .NET Framework 4.8.
- Current-user and all-users installations are separate packages.
- Session and layout storage must be writable and can fall back to Local AppData.
- CSV import validates the entire input before changing saved sessions.
- Win CMD and PowerShell use dedicated embedded-console handling.
- RDP supports the in-process Microsoft control and optional external-client paths.
- Tabs use the outlined, rounded-corner community renderer.
- **File > Save Current Session** can preserve quick or modified active sessions.
- SCP is a saved protocol with explicit `.ppk` selection and secure password delivery through a named pipe.
- `<Auto Restore>` restores the workspace but no longer reconnects previously active sessions.
- Logs and command lines redact common password forms.

## Verify after upgrading

- Open one saved SSH session.
- Open one SCP session and verify its local and remote starting paths.
- Confirm the selected private key or password prompt works as expected.
- Close and reopen SuperPuTTY to verify layout restoration without automatic reconnection.
- Confirm **Tools > Open Log File Location** opens the expected directory.

## Credits

Version 1.5.0.0 and the original SuperPuTTY design are credited to Jim Radford and upstream contributors. Community releases and documentation updates are maintained by C. Thornton.
