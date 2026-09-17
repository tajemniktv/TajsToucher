# GPG card ownership and diagnostics

Investigated against installed GnuPG 2.5.22 on 2026-09-17. No daemon restart,
card disconnect, shared-mode configuration change, PIN attempt, or signature
was performed for this investigation.

## Why the connection remains open

In the pinned source, `scd/app.c:card_put` deliberately retains the card
context to avoid resetting the card and preserve its PIN and other caches.
`scd/apdu.c:connect_pcsc_card` requests PC/SC exclusive access unless
`pcsc-shared` is enabled. This is expected ownership, not proof of a hung
signature. The running daemon answered non-card GETINFO queries in 171 ms;
no signing or pinentry process was running during that check.

A separate PC/SC shared-connect attempt returned `0x8010000b`
(`SCARD_E_SHARING_VIOLATION`). Windows detected one attached Yubico USB device,
while SDK discovery returned no accessible keys. Scdaemon's cached application
list contained OpenPGP. This strongly implicates GPG, but the PC/SC error does
not name the owner PID and no release/reacquire attribution test was performed.

## Available approaches

| Approach | Evidence and decision |
| --- | --- |
| Read OpenPGP metadata through the existing GPG owner | **Preferred next implementation.** Explicit `SCD GETATTR UIF-1` and `SCD GETATTR CHV-STATUS` queries succeeded through the installed `gpg-connect-agent --no-autostart` while SDK access was blocked. They read policy/retry metadata, not PINs or live touch state. Future UI must label the result as GPG-selected card data, not associate it with a USB card by timing or count. Keep it explicit, bounded and redacted; it may contend with signing. |
| `SCD DISCONNECT` | Exists. The PC/SC backend uses `SCardDisconnect(..., SCARD_LEAVE_CARD)` and clears its handle. The command needs a card context in that Assuan session; it is not a global safe-release primitive. Source inspection does not prove PIN/cache preservation after another application's access or reconnect. A controlled, explicitly coordinated handoff experiment is possible; do not run it automatically after signatures or on refresh. |
| `pcsc-shared` | GnuPG's own manual warns that this can violate its exclusive-access/cache assumptions. Do not silently change it or advertise safe coexistence from the flag alone. |
| `card-timeout` | Deprecated and explicitly has no effect in this version. Not a remedy. |
| Restart/kill scdaemon | Can release connections, but may interrupt other clients and invalidate cached state. Not an automatic recovery policy. |
| OS presence, independent of SDK | Implemented. Detects attached hardware without competing for its interface; optional diagnostics being unavailable no longer presents the key as missing. |

The app cannot establish global quiescence merely by finding no `gpg.exe`:
SSH/agent clients and another process starting immediately afterward are still
possible. The wrapper's activity lease also does not cover every external
GnuPG client. Do not turn a best-effort process check into an automatic unlock.

Source anchors in GnuPG tag `gnupg-2.5.22`: `scd/app.c` (`card_put`),
`scd/apdu.c` (`connect_pcsc_card`, `disconnect_pcsc_card`), `scd/command.c`
(`cmd_disconnect`), and `doc/scdaemon.texi` (`pcsc-shared`, `card-timeout`).
