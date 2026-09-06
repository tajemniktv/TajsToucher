# Architecture

## Design principle

TajsToucher is an event/UX layer around existing cryptographic tooling, not a
replacement cryptographic stack.

For v0.1, Git invokes `TajsToucher.exe` instead of calling `gpg.exe` directly.
The executable recognizes signing invocations, launches a short-lived native
Windows notification helper, and then launches the real GPG executable while
preserving process behavior.

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

The wrapper uses `ProcessStartInfo.ArgumentList` and raw stream pumps rather
than a shell command line. It preserves command-line argument semantics,
stdin, stdout, stderr, the exit code, and the synchronous lifetime expected by
Git. The notification helper is a separate invocation of the same executable,
so its ten-second display lifetime does not delay the GPG child or Git.

A notification failure must never cause signing itself to fail.

## Real GPG resolution

The downstream GPG path is absolute and must never resolve back to
TajsToucher. Installation checks the saved path, the GnuPG `gpgconf` bindir,
standard Program Files locations, and PATH. It persists the selected path in
the per-user `HKCU\Software\TajsToucher` key so normal proxy invocations do
not depend on PATH ordering.

The previous global Git program setting is stored alongside it. Uninstall is
conditional: it restores the previous value only when the current value still
identifies the installed wrapper.

## Operation detection and notification

The classifier recognizes GnuPG's long signing options and short options used
by Git, including `-bsau`. It stops interpreting arguments after `--` and
gives explicit verification options precedence over signing options. Repository
context is best-effort metadata from `git rev-parse --show-toplevel`; a failed
lookup simply omits the repository name.

Notification launching is fail-open and intentionally silent on failure. The
helper uses WinForms `NotifyIcon` and is launched with handle inheritance
disabled, preventing it from keeping Git pipes open.

The desktop shell also applies the Windows 11 DWM main-window backdrop and
extends the frame through the client area. Shell cards deliberately avoid
fixed WinForms borders and use alpha-backed panel surfaces where WinForms
supports them. This keeps the native app compatible with Windhawk's
Translucent Windows rendering instead of hard-coding an opaque white canvas.

## Desktop app and settings UI

Launching the executable without arguments opens the WinForms app shell. Its
Home page reads the current installation state and Git configuration to show a
truthful status for Git signing and real GnuPG availability. Settings is a
separate page for the notification title, notification text, and optional `.ico`
path; it previews the icon, validates values, and can launch a test
notification. The Enabled for page currently exposes only the Git/OpenPGP
signing adapter and reserves space for future adapters. `{Repository}` is a
supported template token. Settings live in the same per-user registry key as
installation state but use separate values; uninstall removes only wrapper
state and leaves notification customization intact.

The shell is tray-resident: a user-initiated window close hides the form while
leaving the process and tray icon alive. The tray menu can navigate to each app
page or explicitly exit the process. Shutdown/task-manager close reasons are
not intercepted, so system lifecycle actions can still terminate the app.

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
