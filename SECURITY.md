# Security

## Reporting a vulnerability

Please do not disclose an unpatched vulnerability, credential, private key, or sensitive log in a public issue.

Use GitHub's private **Report a vulnerability** control for the [community repository](https://github.com/greyhair-atx/superputty/security) when it is available. If private reporting is unavailable, contact the community maintainer before publishing details. Include the affected SuperPuTTY version, reproduction steps, impact, and a minimal redacted log when useful.

Issues in PuTTY, PSCP, FreeRDP, VNC clients, or other separately installed programs should also be reported to that program's maintainer. A SuperPuTTY issue is appropriate when the weakness is caused by how SuperPuTTY launches, configures, or hosts the program.

## Supported code

Security fixes are made on the community `master` branch and included in later community releases. Before reporting an issue, reproduce it with the newest release if practical. Jim Radford's upstream releases through 1.5.0.0 are maintained separately.

## Credential handling

- Session passwords are runtime-only and are not serialized to `Sessions.XML`.
- The default PSCP password path uses a user-restricted, one-use Windows named pipe with PSCP's `-pwfile` option. It does not create a password file on disk.
- PuTTY-format `.ppk` files and Pageant are preferred for repeat SSH and SCP authentication.
- The compatibility setting that permits plain-text password command-line arguments is disabled by default. Enabling it can expose passwords to local process-inspection tools.
- Extra arguments, CSV imports, SPSL files, and settings files are plain text. Do not place passwords, tokens, or private-key contents in them.
- Logs redact recognized password arguments, but can still contain usernames, hostnames, paths, and remote command output. Review logs before sharing.

SuperPuTTY is a session manager, not a credential vault. Protect the Windows account, settings directory, session XML, scripts, and private keys with appropriate file permissions.

## Network-loaded content

Remote SPSL scripts and remote session collections accept HTTPS only. Requests reject embedded URI credentials and redirects, use a ten-second timeout, and limit responses to 1 MiB. Session collection expansion also detects cycles and limits nesting to 16 levels.

Only load scripts and session collections from locations you trust. HTTPS protects transport but does not make untrusted script content safe.

## Remote Desktop certificates

Microsoft's in-process RDP client follows the Windows RDP security behavior. When an external FreeRDP client is configured, certificate validation remains enabled by default. The per-session **Ignore FreeRDP certificate errors (insecure)** option is an explicit compatibility override and should be used only when the endpoint has been independently verified.

## Dependencies and releases

Runtime dependency licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Community release notes identify dependency changes, and signed release artifacts can be checked with Windows Authenticode before installation.
