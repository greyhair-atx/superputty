# Layouts

[Back to the manual](README.md)

Layouts store DockPanelSuite window placement: docked tool panels, floating panels, document areas, pane proportions, and tab placement.

## Automatic layout

Select `<Auto Restore>` as **Tools > Options > General > Default Layout** to retain the application workspace between runs.

The community version deliberately separates workspace restoration from connection restoration:

- Docking geometry and tool-window placement are restored.
- Previously active PuTTY and SCP sessions are not reopened automatically.
- The automatic layout is saved during a normal application close.

This prevents an application restart from unexpectedly reconnecting to every server that was open previously.

## Named layouts

Use **File > Save Layout As** to create a named layout. Named layouts retain their traditional behavior and may include sessions intentionally stored with the layout.

Use the Layouts panel to load, rename, remove, or mark an existing named layout as the default. `Ctrl+S` updates the active named layout.

## Recovery

The automatic layout is stored as `AutoRestoreLayout.XML` in the settings folder. Named layouts are stored under its `layouts` subdirectory.

Layout saves are validated, flushed, and published with an atomic replacement. The previous valid save is kept beside the layout as `<filename>.bak`. A failed write or replacement leaves the existing layout and backup intact. If an existing file is damaged when you save, the damaged file is preserved as `<filename>.corrupt.<unique identifier>` instead of replacing a valid backup with it.

When loading a missing or damaged layout, SuperPuTTY validates its backup and restores it automatically when possible. Recovery preserves a damaged original and displays the backup and preserved-file locations. A named layout with only its `.xml.bak` file remaining is still listed and can be recovered. Renaming or deleting a named layout also renames or deletes its backup; temporary and preserved corruption files are not listed as layouts.

SuperPuTTY validates the complete layout before closing the current workspace. If the selected layout and backup cannot be loaded, the selection is rejected and the current workspace remains open. At startup, the built-in tool windows provide a fallback. If restoring session contents fails after the old workspace has already closed, SuperPuTTY attempts to open the default workspace and reports the failure. Closed session tabs must be reconnected; this fallback does not restore their connections.

Layout files are limited to 16 MiB, with at most 1,024 entries in each layout collection. Invalid element counts, duplicate singleton tool windows, and unsupported XML are rejected before restoration. The retained DockPanel 1.0 format remains supported.

For manual recovery, close SuperPuTTY and keep copies of the original and backup before replacing the layout with a known-good file. These layout backups do not include session configuration or credentials.
