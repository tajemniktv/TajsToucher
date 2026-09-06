# Roadmap

## v0.1: ship the notifier

The first milestone should prove one thing reliably: when Git asks GnuPG to sign, the user gets an immediate Windows notification and signing still behaves exactly as before.

Status: implemented in `src/TajsToucher` and published as a self-contained
Windows executable.

Target features:

- [x] Replace the CMD + PowerShell proof of concept with one small Windows executable.
- [x] Detect Git/OpenPGP signing invocations without matching verification operations.
- [x] Show a native Windows notification before invoking real GPG.
- [x] Include repository name when cheaply available.
- [x] Preserve stdin/stdout/stderr and exit status exactly.
- [x] Discover and store the real `gpg.exe` path safely.
- [x] `install` command to set `gpg.openpgp.program`.
- [x] `uninstall` command to restore the previous Git configuration.
- [x] Minimal diagnostic mode.

Success criterion: it can sit in the signing path for weeks without being interesting.

## v0.2: polish

Possible follow-ups after v0.1 is boringly reliable:

- [x] configurable notification icon, title, and text through the settings GUI;
- [x] desktop app shell with a status dashboard, settings page, and enabled-adapters page;
- [x] tray-resident app shell with dashboard reopening and explicit exit;
- optional sound;
- notification cooldown/deduplication;
- better context for commit vs tag signing;
- packaged single-file release;
- tests for argument forwarding and exit codes;
- installer/update story only if actually needed.

## Later: broader hardware-key events

Only after the core wrapper is trustworthy:

- generic OpenPGP operation notifications;
- OpenSSH/FIDO touch visibility;
- FIDO2/CTAP2 operation awareness;
- PIV and OATH state/UX integrations;
- normalized event broker;
- pluggable notification or diagnostics sinks;
- optional policy rules around events that are safe to observe.

## Explicitly deferred

These ideas are interesting enough to become traps, so they are intentionally not early milestones:

- full USB/HID/CCID interception;
- transparent APDU or CTAP proxying;
- key/PIN management;
- replacing Yubico Authenticator;
- full tray-based security suite;
- cross-platform abstraction layer;
- "unified cryptographic orchestration platform" mode.

That last one remains legally permitted after the notifier has actually shipped.
