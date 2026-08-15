# SuperPuTTY Usability Test Environments

This document records the environments and access information needed to continue
SuperPuTTY 1.6.0 usability and connectivity testing after a reboot.

## Required environments

### Windows test client

- Windows 11 x64
- .NET Framework 4.8
- PuTTY and PSCP installed
- Network access to the private SSH and RDP test systems
- An interactive desktop session for application and window-management tests

### Clean Windows installation VM

Use a disposable Windows 11 x64 VM snapshot to test:

- Fresh MSI installation
- First-run PuTTY and PSCP discovery
- Start Menu shortcuts
- Installation under native 64-bit Program Files
- Normal launch and shutdown
- Repair and uninstall behavior

### x86-to-x64 migration VM

Use a separate snapshot with the previous x86 SuperPuTTY 1.6.0 MSI installed.
Test installation of the new x64 1.6.0 MSI and verify that it does not leave:

- Duplicate installed products
- Duplicate shortcuts
- Orphaned x86 files
- Conflicting settings

This is the highest-priority installer compatibility test because both packages
identify themselves as version 1.6.0.

### SSH and SCP target

A Linux system running OpenSSH is suitable. Provide:

- TCP port 22 access from the Windows test client
- A dedicated, unprivileged, disposable test account
- A writable disposable test directory
- Permission to upload, download, list, and delete test files
- A hostname with a cached PuTTY host key for known-host tests
- A separate hostname or address whose PuTTY host key is not cached for
  unknown-host tests

The account must not contain sensitive data and must not have `sudo` or other
administrative privileges.

### RDP target

A Linux system running xrdp and xorgxrdp is acceptable for initial RDP testing.
Provide:

- A graphical desktop environment
- TCP port 3389 access from the Windows test client
- A dedicated, unprivileged test account
- Permission to create, disconnect, reconnect, and terminate test sessions

xrdp can test Windows `mstsc.exe` connectivity, embedding in a SuperPuTTY tab,
authentication, resizing, fullscreen behavior, keyboard input, clipboard use,
disconnect/reconnect, tab closure, and application shutdown with an active RDP
session.

xrdp does not fully cover Windows-specific RDP behavior such as domain
authentication, NLA/CredSSP, RD Gateway, RemoteApp, Group Policy, or Windows
certificate handling. A Windows RDP server can be added later for those cases.

## Private-network access

The test services do not need to be exposed publicly. The Windows test client
only needs private-network routing, DNS or IP addressing, and firewall access to:

- TCP 22 for SSH/SCP
- TCP 3389 for RDP

Before automation begins, manually confirm that PuTTY, PSCP, and `mstsc.exe` can
reach the corresponding targets from the Windows test client.

## Supplying connection information

Store configuration outside the Git repository and preferably outside OneDrive.
The recommended location is:

```text
%LOCALAPPDATA%\SuperPuttyTests\test-environment.json
```

Recommended JSON structure:

```json
{
  "ssh": {
    "host": "10.0.0.20",
    "port": 22,
    "username": "superputty-test",
    "passwordEnvironmentVariable": "SUPERPUTTY_TEST_SSH_PASSWORD",
    "remoteTestDirectory": "/home/superputty-test/test-data"
  },
  "rdp": {
    "host": "10.0.0.21",
    "port": 3389,
    "username": "superputty-test",
    "passwordEnvironmentVariable": "SUPERPUTTY_TEST_RDP_PASSWORD",
    "domain": ""
  },
  "putty": {
    "puttyPath": "C:\\Program Files\\PuTTY\\putty.exe",
    "pscpPath": "C:\\Program Files\\PuTTY\\pscp.exe"
  }
}
```

Prefer environment variables or Windows Credential Manager for passwords. Do
not place real credentials in this repository, commit them to Git, paste them
into chat, or include them in command-line arguments or test logs.

If passwords must be stored directly in the JSON file:

- Use dedicated nonprivileged test accounts.
- Restrict the file ACL to the Windows account running the tests.
- Keep the file outside the repository and OneDrive.
- Remove or rotate the credentials after testing.

## Planned usability tests

1. Fresh MSI install, first-run configuration, repair, and uninstall.
2. Previous x86 MSI to new x64 MSI migration.
3. Real PuTTY session creation, resize, reconnect, duplication, and closure.
4. Keyboard shortcuts, special keys, clipboard, command broadcasting, and
   fullscreen behavior.
5. Shutdown with active PuTTY, RDP, SCP, and SPSL operations.
6. All eight automated `NetworkTest` SSH/SCP cases.
7. SCP upload, download, cancellation, invalid paths, bad passwords, unknown
   host keys, Unicode filenames, and large files.
8. xrdp authentication, embedding, resize, clipboard, reconnect, and shutdown.
9. Layout and session save/restore across application restarts.
10. Portable ZIP operation, standard-user permissions, high-DPI displays,
    multiple monitors, keyboard-only navigation, and long-running soak tests.

## Repository state before reboot

- Branch: `sp-1.6.0`
- Latest pushed commit: `614f2f0 Modernize and expand automated release tests`
- Clean x64 solution build passed.
- All 24 isolated automated tests passed.
- x64 application and MSI payload verification passed.
- Title-bar and File > Exit shutdown automation passed.
- Eight external SSH/SCP tests remain intentionally categorized as
  `NetworkTest` until a disposable SSH environment is supplied.

After reboot, open this repository, confirm the test configuration file path,
and run the manual connectivity checks before enabling automated network tests.
