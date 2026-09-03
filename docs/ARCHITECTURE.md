# Architecture

## Design principle

TajsToucher should be an event/UX layer around existing cryptographic tooling, not a replacement cryptographic stack.

For v0.1, Git invokes a wrapper instead of calling `gpg.exe` directly. The wrapper recognizes signing invocations, emits a local notification, and then launches the real GPG executable while preserving process behavior.

```text
Git
 |
 v
TajsToucher adapter
 |\
 | `--> notification
 |
 v
gpg.exe -> gpg-agent -> smart card / YubiKey
```

## Process transparency

The wrapper must preserve command-line arguments, stdin, stdout, stderr, the exit code, and the synchronous lifetime expected by Git.

A notification failure must never cause signing itself to fail.

## Real GPG resolution

The downstream GPG path should be absolute and must never resolve back to TajsToucher. Setup should discover and persist the actual executable explicitly to prevent recursion.

## Future adapter model

If the project grows, protocol-specific integration should live behind adapters that emit a small set of normalized events.

Examples:

```text
Git adapter -----------\
OpenPGP observer -------+--> event broker --> notification sinks
SSH/FIDO observer ------+                 `--> policy/diagnostics
PIV/OATH observer ------/
```

An adapter should expose metadata such as operation type, application/relying-party identifier, repository name, timeout state, or retry count. It should not expose secret payloads unless a future feature absolutely requires it.

## Security boundary

Preferred:

- observe operations already being performed by trusted platform tools;
- surface metadata and state;
- keep signing and authentication in GnuPG/OpenSSH/YubiKey;
- fail open for notification-only functionality.

Avoid:

- collecting PINs or passphrases;
- logging stdin or opaque cryptographic payloads;
- persisting signatures or authentication assertions;
- becoming a mandatory proxy for USB, HID, CTAP, APDU, or CCID traffic without a compelling reason.
