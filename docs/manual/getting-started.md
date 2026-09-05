# Getting Started

[Back to the manual](README.md)

## Open a connection

For a quick connection, choose a protocol on the connection toolbar, enter the host, login name, and optional password, then select **Connect**. A password entered there is kept in memory for the active operation and is not written to `Sessions.XML`.

For connections used more than once, create a [saved session](sessions.md). Saved sessions provide consistent PuTTY profiles, ports, file-transfer paths, and private-key selection.

## Main menus

### File

- **Import Sessions** imports XML, CSV, PuTTY, PuTTY Portable, PuTTY Connection Manager, or Windows RDP entries.
- **Export Sessions** writes the session database to XML.
- **Open Session** opens a saved session selector.
- **Switch Session** selects an already open tab.
- **Save Current Session** saves a quick connection or applies changes from the active tab to a saved session.
- **Edit Sessions in Notepad** opens the session XML for advanced inspection.
- **Reload Sessions** reloads the saved session database.
- **Save Layout** and **Save Layout As** store named docking layouts.

### View

Use this menu to show the Sessions, Session Detail, Layouts, and Log Viewer panels; toggle toolbars, status bar, or menu bar; enable Always On Top; or enter full screen.

### Tools

- **PuTTY Configuration** opens PuTTY's configuration window.
- **Open Log File Location** opens the active log directory in File Explorer.
- **Toggle Command Mask** hides command-toolbar text while typing.
- **Options** configures programs, interface behavior, shortcuts, updates, logging-related behavior, and file-transfer defaults.

### Help

- **Documentation** opens this manual online when no local CHM manual is installed.
- **Diagnostics** contains developer and cleanup commands.
- **Check for Updates** checks the selected update channel.
- **About SuperPuTTY** shows version and project credits.

## Tabs

Document tabs use rounded upper corners and outlined states:

- Active: dark gray background with white text and outline.
- Inactive: light gray background with dark text and a middle-gray outline.
- Hovered: middle gray background with dark text and a white outline.

The active tab joins visually with its application frame. Each document tab retains its own close button and normal DockPanelSuite docking behavior.

## Useful defaults

- `F2`: Options
- `F3`: show or hide the menu bar
- `F11`: full screen
- `Ctrl+S`: save the current named layout
- `Ctrl+Shift+8`: toggle command masking

Additional shortcuts can be assigned under **Tools > Options > Shortcuts**.

## Command line

Run `SuperPutty.exe --help` to display the supported switches. Common examples are:

```text
SuperPutty.exe -session "Production/Web Server"
SuperPutty.exe -layout "Operations"
SuperPutty.exe -ssh -P 22 -l admin server.example.com
SuperPutty.exe -scp -P 22 -l admin server.example.com
```

Although `-pw` remains available for compatibility, command-line passwords can be exposed to process-inspection and history tools. Prefer saved key configuration or the interactive password prompt.
