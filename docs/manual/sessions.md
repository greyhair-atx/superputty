# Saved Sessions

[Back to the manual](README.md)

Saved sessions appear in the Sessions panel and are stored in `Sessions.XML`. Double-click a session to open it. Right-click a session or folder for editing and organization commands.

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
