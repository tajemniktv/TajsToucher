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

Git values are read with NUL delimiters and retained without trimming or
discarding empty values. The backup limit is checked before changing Git.
Wrapper ownership requires exactly one matching program value; dashboard and
CLI diagnostics share that rule with the installer. Readiness also requires
readable global configuration and an existing installed executable. It does
not assert that repository overrides or non-OpenPGP signing use this wrapper.

## Operation detection and notification

The classifier recognizes explicit signing, encryption, decryption, and
combined sign/encrypt commands, including Git's `-bsau` form. It skips common
value-taking options and stops interpreting arguments after `--`. Explicit
verification and recognized key-management commands suppress observation.
It does not parse option files or infer operations from input. Repository
context is best-effort metadata from `git rev-parse --show-toplevel`; a failed
lookup simply omits the repository name.

Observation is fail-open and intentionally silent on failure. GPG's streams
remain raw and its exit code is returned unchanged. `OperationObservation`
emits typed request and optional outcome events to detached helpers, launched
with handle inheritance disabled so they cannot keep Git pipes open.

Optional cooldown is evaluated inside the detached helper, never in the GPG
forwarding process. A per-user session mutex serializes a persisted timestamp
under the Runtime registry subkey. It is global across repositories, disabled
by default, and bypassed by explicit UI tests. Invalid state or clock rollback
allows a new prompt. Sound uses the native notification flag rather than a
separate audio process.

The desktop shell is an unpackaged WinUI 3 application. XAML page backgrounds
remain transparent and cards use alpha-backed brushes, leaving the DWM and
Windhawk material visible rather than painting a hard-coded WinForms canvas.
The persistent tray icon is also a small native Win32 host, so the dashboard
does not need a WinForms dependency.

## Desktop app and settings UI

Launching the executable without arguments opens the WinUI 3 app shell. Its
Home dashboard reads the current installation state and Git configuration to
show a truthful status for Git signing and real GnuPG availability. The same
dashboard embeds the settings editor, test notification, install/uninstall
actions, and enabled-adapter summary; separate Settings and Enabled for pages
are shortcuts for navigation and future growth. Settings include the
notification title, notification text, and optional `.ico` path; the editor
previews the icon, validates values, and can launch a test notification.
`{Repository}` is a supported template token. Settings live in the same
per-user registry key as installation state but use separate values; uninstall
removes only wrapper state and leaves notification customization intact.

The shell is tray-resident: a user-initiated window close hides the form while
leaving the process and tray icon alive. The tray menu can navigate to each app
page or explicitly exit the process. Shutdown/task-manager close reasons are
not intercepted, so system lifecycle actions can still terminate the app.

## Operation events and outputs

`OperationEvent` carries only an operation ID, occurrence time, operation kind,
phase, exit code, and wrapper elapsed time. `OperationEventCodec` validates the
fixed private helper arguments, enum values, and outcome invariants. Repository
context travels separately for notification display; it is not an event field.

The OpenPGP observer launches one helper for a selected request and an optional
second helper for diagnostic outcomes or enabled failures. No success helper
is launched by default. Settings/context/helper failures cannot change GPG's
operation. Requests are advisory: there is no inferred touch-required signal.

Inside the helper, `OperationEventBroker` isolates each `IOperationEventSink`:

- `NotificationEventSink` applies per-operation notification policy, optional
  failure alerts, and cooldown before using the existing native host.
- `DiagnosticEventSink` optionally writes seven fixed metadata fields to two
  bounded local files, rotating at 64 KiB. A path-scoped session mutex prevents
  simultaneous writers from corrupting rotation. Contention and I/O failure
  are nonfatal. Event timestamps and IDs, not write order, identify lifecycle.

The sink interface is replaceable by code, not an external plugin loading
system. The broker is in-process in short-lived helpers, not a daemon or
general-purpose message transport. Settings only suppress notifications or
enable diagnostics; they never authorize, deny, or modify cryptographic work.

### Desktop device feature

`TajsToucher.Devices` pins Yubico.YubiKey/Core 1.17.3 and the transitive
NativeShims 1.17.2. Only an explicit Devices-page action (or `diagnose-devices`)
constructs `YubiKeyService`. The GPG forwarding path does not load SDK assemblies.
`DesktopDeviceFeature` owns the service and `DeviceEventBroker`; `App` awaits
their disposal for at most two seconds on explicit tray exit. Merely hiding the window leaves discovery
running. Navigation cancels the page's own test and removes UI subscriptions.

`IKeyDiscovery` separates listener/cache lifetime from inventory reconciliation.
The SDK listener has a shared, reference-counted owner; only its final lease stops
discovery. Presence refreshes debounce for 400 ms. Enumeration is serialized
separately from per-device application sessions, so one touch wait does not block
inventory or another key. Queued operations honor cancellation. Re-enumeration
replaces stale handles. Serial numbers are used only in-memory for known-device
identity; anonymous keys use reference identity, not SDK fingerprint equality.
No application status refresh occurs automatically. Known-device removal can
cancel only TajsToucher's own identify operation, never a guessed GPG operation.

Read-only PIV/OATH/FIDO sessions refuse credential collection. HRESULTs preserve
busy, removed, permission-denied and timeout categories. Unsupported and unknown
values remain distinct from zero retries. FIDO selection uses an independently
synchronized touch lifetime because SDK callbacks can outlive command return;
Release clears callback state, not the operation outcome.
Callbacks run outside the lifetime lock. CTAP selection maps explicit response
statuses instead of treating every negative result as cancellation. Empty PIV
asymmetric slots are valid metadata results, not application failures.

`DeviceSignal` is separate from `OpenPgpOperation`. It carries random correlation
and ephemeral device IDs, occurrence time, kind and outcome. Device identity is
not written to logs. `DeviceEventBroker` consumes a bounded 64-entry queue,
isolates notification/diagnostic failures and applies opt-in presence/retry
rules. Native notices are dispatched to the existing resident tray, not a
blocking helper loop. The existing rotating diagnostic writer is shared.
Shutdown discards queued signals rather than draining diagnostic writes.
The SDK's configurable console logger is disabled before discovery.

There is no external plugin loader, automatic elevation, key configuration,
credential enumeration or cross-process touch monitoring. SSH remains
unimplemented pending askpass/authentication lifecycle proof. Key/PIN management
and protocol interception remain deferred.

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
