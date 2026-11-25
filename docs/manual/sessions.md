# Saved Sessions

[Back to the manual](README.md)

Saved sessions appear in the Sessions panel and are stored in `Sessions.XML`. Double-click a session to open it. Right-click a session or folder for editing and organization commands.

SuperPuTTY supports up to **200 open session tabs** per window, shared by terminal and SCP sessions, including docked and floating sessions. Close a session to free a slot. Use the tab list to select tabs hidden by overflow. Layouts containing more than 200 session tabs are rejected before replacing the current workspace.

**Connect All** checks that the folder fits in the available slots before opening any tabs. It asks for confirmation above 10 sessions by default and starts up to three connections at a time. The legacy `SuperPuTTY.MaxSessionsToOpen` configuration key controls the confirmation threshold. Choose **Cancel Connect All** from the folder menu to stop pending launches and keep the tabs already opened. If an RDP viewer is waiting for sign-in, finish signing in to free its startup slot. If an SCP listing needs credentials, refresh its remote browser to sign in.

New tabs and **Reconnect with Saved Settings** use the current saved profile. Existing connections and queued SCP transfers keep the connection details they started with. Docking positions for saved sessions are stored in `SessionDocking.xml` in the workspace settings folder.

In **Tools > Options > GUI > Tabs and Docking**, enable **Middle-click closes tabs without confirmation** to close the tab under the middle mouse button immediately. This is off by default; with it off, middle-click uses the normal close confirmation. Clicking the tab's **X** still asks before closing an active or unknown-state session, using `Close tab '<tab name>'?`. The setting takes effect after saving Options and works in Light, Dark, Dark Modern and System themes. Disconnected sessions retain their normal behavior without a session-close prompt.

## Creating and updating sessions

Create a session from the Sessions panel, or open a quick connection and choose **File > Save Current Session**. When a saved session is active, that command updates it after confirmation through the session editor.

Important session fields include:

| Field | Purpose |
| --- | --- |
| Session Name | Display name and session-tree name |
| Host Name | Server name or IP address |
| Protocol | SSH, SCP, Telnet, Rlogin, Raw, Serial, RDP, VNC, CygTerm, MinTTY, Win CMD, or PowerShell |
| Port | Destination port; SSH and SCP normally use 22 |
| PuTTY Profile | Saved PuTTY configuration loaded for the connection |
| Login Username | Remote account name |
| Extra Arguments | Additional client command-line options; do not store passwords here |
| SPSL Script | Optional script run with the session |
| Local Path | Starting directory for the local side of file transfer |
| Remote Path | Starting directory for the remote side of file transfer |
| Private Key | Explicit PuTTY `.ppk` key used for SCP browsing and transfers |
| Note | Free-form description displayed with the session |

## PuTTY profiles

A PuTTY profile can contain terminal preferences, proxy settings, SSH algorithms, authentication settings, and a private key. Selecting the same profile in SuperPuTTY applies those settings to PuTTY and PSCP where supported.

For SCP, the explicit **Private Key** field is clearer when a single saved session must always use a specific key. See [SCP file transfers](scp-file-transfers.md).

See [Connection types](protocols.md) for the client and hosting behavior used by each protocol.

## Importing CSV

CSV input supports comments beginning with `#`, quoted values, folders, validation before import, and these columns:

```text
SessionName,Host,Protocol,Port,Username,Folder,PuttySession,ExtraArgs,Note,ImageKey,SPSLFileName,RemotePath,LocalPath,PrivateKeyFile
```

`SessionName` is required. Either `Host` or `PuttySession` must be supplied. See `Sessions.example.csv` in the source repository for an example.

## Password storage

The normal Password property is not serialized into `Sessions.XML`. Never put `-pw`, `/password`, or an embedded URI password in **Extra Arguments** or CSV files. Those values are plain text and may be visible to other processes or users.

## Shared session collections

Advanced users can place a collection placeholder in session XML with `CollectionLocation` pointing to another SuperPuTTY session XML file and an optional `CollectionID` folder prefix. Local collections may be nested. Remote collections must use HTTPS without embedded credentials; redirects are rejected, downloads are limited to 1 MiB, cycles are detected, and nesting is limited to 16 levels.

Relative script and nested-collection paths in a remote collection are resolved against that collection's URL. Back up `Sessions.XML` before editing collection entries by hand, and load only collections controlled by a trusted administrator.
