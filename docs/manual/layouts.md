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

The automatic layout is stored as `AutoRestoreLayout.XML` in the settings folder. Named layouts are stored under its `layouts` subdirectory. If a layout becomes invalid, close SuperPuTTY, back up the file, and rename it so the application can start with its built-in layout.
