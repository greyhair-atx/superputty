# Security and Privacy

[Back to the manual](README.md)

SuperPuTTY coordinates separately installed connection programs and stores session metadata. It is not a password manager and does not replace the host-key, certificate, encryption, or authentication decisions made by PuTTY, PSCP, RDP, VNC, or FreeRDP.

## Safer authentication

- Prefer SSH public-key authentication through a PuTTY profile, Pageant, or the SCP session's explicit PuTTY `.ppk` file.
- Session passwords are kept in memory for the active operation and are not saved in `Sessions.XML`.
- The standard PSCP password flow uses a user-restricted, one-use named pipe rather than a temporary password file.
- Leave **Allow plain-text PuTTY password arguments** disabled. Compatibility command-line passwords can be observed by other local processes.
- Never save secrets in **Extra Arguments**, CSV files, SPSL scripts, session notes, or URLs.

## Host keys and certificates

Review PuTTY or PSCP host-key prompts carefully and verify new or changed fingerprints through a trusted channel. SuperPuTTY does not make an unknown SSH host trustworthy.

FreeRDP certificate checking is enabled by default. The session option **Ignore FreeRDP certificate errors (insecure)** weakens server authentication and should only be used temporarily after independently verifying the endpoint.

## Scripts and remote collections

SPSL scripts can send commands to connected sessions. Run only scripts you understand and trust. Remote SPSL files and session collections require HTTPS and enforce request, size, redirect, and nesting limits, but trusted transport does not make untrusted content safe.

## Files and logs

Protect the configured settings folder, `SuperPutty.settings`, `Sessions.XML`, layouts, scripts, and private keys with Windows account permissions and backups. Logs can include hostnames, usernames, file paths, and terminal or transfer output even when recognized passwords are redacted. Review and redact a log before attaching it to an issue.

For private vulnerability reporting and implementation-level details, see the repository [security policy](../../SECURITY.md).
