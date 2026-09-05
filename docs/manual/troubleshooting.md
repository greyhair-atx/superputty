# Troubleshooting

[Back to the manual](README.md)

## Start with the log

Choose **Tools > Open Log File Location** and open the newest SuperPuTTY log. Record the application version from **Help > About SuperPuTTY** and the PuTTY/PSCP versions involved.

## A PuTTY window does not embed

- Confirm that the configured PuTTY executable exists and is 64-bit.
- Start the same PuTTY profile directly to verify its own settings.
- Check the log for process-start, window-discovery, or timeout messages.
- Temporarily disable utilities that inject controls into application title bars.

## SCP asks for a password instead of using a key

- Confirm the session's **Private Key** points to a readable `.ppk` file.
- Add `-v` to **Extra Arguments** and look for `Offering public key`.
- For an encrypted `.ppk`, load the key into Pageant first.
- Confirm the matching public key belongs to the selected remote username.
- Verify the PSCP executable configured in SuperPuTTY is the same one tested at a command prompt.

See [SCP file transfers](scp-file-transfers.md) for the complete authentication flow.

## Settings cannot be saved

- Open **Tools > Options** and confirm the settings folder is writable.
- Check OneDrive or Controlled Folder Access notifications.
- Review whether SuperPuTTY reported a fallback to `%LOCALAPPDATA%\SuperPuTTY`.
- Back up files before manually editing or replacing settings.

## Layout problems

Back up and rename `AutoRestoreLayout.XML` to start with the built-in layout. This does not remove saved sessions. Named layouts are separate files under the `layouts` directory.

## Reporting a problem

Use the [community issue tracker](https://github.com/greyhair-atx/superputty/issues) for changes specific to this fork. Include reproducible steps, versions, and a reviewed/redacted log excerpt. Never post passwords, private keys, tokens, or sensitive host data.
