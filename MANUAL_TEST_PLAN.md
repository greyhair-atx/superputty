# CSV Session Import Manual Test Plan

## Purpose

Verify that the CSV session importer creates correct SuperPuTTY sessions, reports all validation problems without partially importing a file, preserves existing sessions, and remains usable after restarting the application.

## Test artifacts

- Application: `bin\Release\SuperPutty.exe`
- Installer: `SuperPuttyInstaller\bin\Release\SuperPuttySetup.msi`
- Valid sample: `Sessions.example.csv`

Record the tested commit, Windows version, PuTTY version, tester, and date with the results.

## Prerequisites and safety

1. Install PuTTY or have a portable `putty.exe` available.
2. Back up the active `Sessions.XML` file before testing. Prefer a disposable SuperPuTTY configuration rather than production session data.
3. Keep credentials out of CSV files, including the `ExtraArgs` column.
4. If connection tests are performed, replace the example hosts with authorized test systems.
5. Record the session count and retain a copy or hash of `Sessions.XML` before every invalid-file test.

## Release-build smoke test

1. Start `bin\Release\SuperPutty.exe`.
2. Confirm that the main window opens without an unhandled error.
3. Open **File > Import Sessions** and confirm both the existing XML option and **From CSV File** are present.
4. Select **From CSV File**, cancel the file dialog, and confirm no sessions are added or changed.

Expected result: the application starts normally, the CSV command is available, and canceling is harmless.

## Valid sample import

1. Select **File > Import Sessions > From CSV File**.
2. Open `Sessions.example.csv` from the repository root.
3. Confirm the success message reports three imported sessions.
4. In the session tree, expand `Imported` and verify:
   - `Production/Production, Primary` exists.
   - `Testing/Test Server` exists.
   - `Network/Legacy Router` exists.
5. Open each session's properties and verify:
   - `Production, Primary`: host `prod.example.com`, SSH, port 22, username `admin`, and the full comma-containing note.
   - `Test Server`: host `192.168.1.50`, SSH, default port 22, username `testuser`, and PuTTY session `Default Settings`.
   - `Legacy Router`: no host, Telnet, default port 23, and PuTTY session `Router Profile`.

Expected result: comments are ignored, quoted commas are preserved, folders are created, defaults are applied, and optional values are retained.

## Nested folders and optional properties

Create a UTF-8 CSV file containing:

```csv
sessionname,host,protocol,port,username,folder,puttysession,extraargs,note,imagekey,spslfilename,remotepath,localpath
Nested Host,test.example.com,ssh,,tester,Region/Site/Servers,,,-Test note-,server,login.spsl,/remote/path,C:\LocalPath
Backslash Folder,test2.example.com,telnet,,,Region\Legacy,,,,,,,
PowerShell Local,,PowerShell,,,,Local PowerShell,,,,,,
Command Local,,Windows Command Prompt,,,,Local Command Prompt,,,,,,
```

Import it and inspect the resulting sessions.

Expected result:

- Headers and protocol names are accepted case-insensitively.
- Forward slashes and backslashes create normalized nested folders beneath `Imported`.
- SSH defaults to port 22 and Telnet defaults to port 23.
- The username, extra arguments, note, icon key, script path, remote path, and local path match the CSV values.
- `PowerShell` maps to the local PowerShell protocol with port 0.
- `Windows Command Prompt` maps to the local command-prompt protocol with port 0.

## Existing-name handling

1. Import `Sessions.example.csv` twice.
2. Inspect the `Imported` folders after the second import.
3. Restart SuperPuTTY and inspect them again.

Expected result: existing sessions are not overwritten; the existing import path assigns unique names to conflicts. All imported sessions persist after restart and remain present in `Sessions.XML`.

## Accumulated validation and atomic failure

Create this invalid file:

```csv
SessionName,Host,Protocol,Port,Folder
,host.example.com,SSH,22,Servers
Missing Target,,SSH,22,Servers
Bad Protocol,host.example.com,NotAProtocol,22,Servers
Bad Port,host.example.com,SSH,70000,Servers
Duplicate,one.example.com,SSH,22,Servers
Duplicate,two.example.com,SSH,22,Servers
```

Import the file.

Expected result:

- One error dialog lists errors for rows 2, 3, 4, 5, and 7.
- The dialog can be resized and scrolled, and **Close** dismisses it.
- No session from the file is added.
- The session count and `Sessions.XML` contents are unchanged.

## Header validation

Test each of these header rows in a separate CSV file with at least one data row:

```csv
Name,Host,Unexpected
SessionName,Host,Host
SessionName,,Host
```

Expected result: the importer reports unsupported columns and a missing `SessionName`, a duplicate header, or an empty header as appropriate. No sessions are added.

## Other invalid-input cases

Test the following files separately:

- An empty file.
- A file containing only `#` comment lines.
- A row with fewer or more fields than its header.
- A quoted field with a missing closing quote.
- A `SessionName` containing `/`.
- A `Folder` containing `//`.
- A row with neither `Host` nor `PuttySession`.
- Ports `-1`, `65536`, and nonnumeric text.
- An unsupported protocol.
- Two rows whose folder and session name differ only by letter case.

Expected result: each file produces a useful error with a row or header location where applicable, imports nothing, and leaves `Sessions.XML` unchanged. Ports 0 and 65535 should be accepted as boundary values.

## Persistence and backup behavior

1. Make a valid import into a disposable configuration.
2. Confirm that `Sessions.XML` is updated and that the application's normal backup behavior occurs.
3. Close SuperPuTTY completely and start it again.
4. Confirm all imported folders, sessions, and properties remain available.

Expected result: the import survives restart without damaging pre-existing sessions or layout data.

## Persistence failure rollback

Use a disposable configuration and keep a separate copy of its current `Sessions.XML`.

1. Mark `Sessions.XML` read-only in Windows Explorer or with `attrib +R Sessions.XML`.
2. Import a valid CSV file.
3. Confirm the application reports that the file could not be imported.
4. Confirm no imported sessions appear in the session tree and `Sessions.XML` is byte-for-byte unchanged.
5. Remove the read-only attribute before continuing.

Expected result: persistence failure leaves both the in-memory session collection and the existing file unchanged, and no temporary `.tmp` file remains in the settings folder.

## Connection smoke test

Use a CSV containing an authorized reachable SSH or Telnet test host and, separately, a valid existing PuTTY profile.

Expected result: a host-based imported session launches with the selected protocol, port, username, and PuTTY defaults. A profile-based session launches through the named PuTTY profile. No credentials are required in the CSV.

## Regression checks

1. Import a known-good XML session file through **File > Import Sessions > From File**.
2. Create, edit, rename, connect to, and delete a normal session using existing UI commands.
3. Restart and confirm ordinary session persistence still works.

Expected result: the pre-existing XML import and session-management workflows behave as before.

## Installer smoke test

Perform this only on a disposable machine or VM when practical.

1. Run `SuperPuttyInstaller\bin\Release\SuperPuttySetup.msi`.
2. Complete installation and launch SuperPuTTY from the installed shortcut.
3. Confirm **File > Import Sessions > From CSV File** is present.
4. Import a copy of `Sessions.example.csv` from a writable user directory.
5. Uninstall the test installation.

Expected result: installation, launch, CSV import, and uninstall complete without an unhandled error. Verify the uninstall behavior for user-created session data before removing anything manually.

## Result record

For every failed step, capture:

- Test-section and step number
- Expected and actual result
- Screenshot or exact error text
- CSV file used
- `Sessions.XML` before and after, with secrets removed
- Application build commit and environment details
