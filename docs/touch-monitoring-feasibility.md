# Touch monitoring feasibility

Investigated on 2026-09-08 against the installed `gpg.exe` and `scdaemon.exe`
2.5.22. The configured real GPG is `C:\Program Files\GnuPG\bin\gpg.exe`.
Source reference: GnuPG tag `gnupg-2.5.22`, commit
`bd253e9fecedd4c0e699530826ee43264581634b`.

## Decision

### Prompt presentation follow-up — 2026-09-17

The opt-in suspected-wait indicator now uses a compact topmost TajsToucher WinUI
card instead of a shell balloon. It uses the same bounded named wait-ended
event; observer semantics and detection delays are unchanged. Dismiss/Escape only
dismisses the prompt and never cancels GPG. Automatic dismissal also does not
prove successful touch. The window does not impersonate Windows Security or
collect a PIN. Published synthetic-event checks verified topmost display,
automatic end, manual dismissal, stale suppression and 30-second expiry. The
user approved the compact card preview after rejecting classic dialog styling.
This supersedes the balloon presentation described in the earlier prototype
notes below, not their limitations on touch attribution.

### Hardware/prototype update — 2026-09-17

The connected OpenPGP app reports firmware 5.7.4. Signing originally had touch
Off while decryption had touch On; two test signatures succeeded, including one
without touch. The user enabled signing touch On (not Fixed). With this policy,
a no-touch signature returned `Timeout` / exit 2, and an isolated touched
signature returned `SIG_CREATED` / exit 0. PIN prompting is independent of touch.

Verbose WUDF CCID tracing worked without elevation on this machine. Both
timeout and success captures remain under `.codex/temp`; Microsoft public PDBs
loaded but lacked the WPP message formats (114 unknown events in the initial
verbose signing capture). No CCID touch byte or confirmed waiting signal has
been decoded. Do not ship raw event-ID guesses as a protocol decoder.

An isolated `SCD GETATTR UIF-1` probe, started 1.5 seconds after signing, blocked
for 3084 ms and returned with signing already successfully exited. A preceding
concurrent-operation test was inconclusive. GETATTR reads policy, not live touch;
its latency remains a heuristic and it can select/read a card application.

The experimental opt-in signing indicator uses one such probe after 500 ms,
shows a suspected-wait notice if the probe exceeds 600 ms, and clears its
notification host when the probe or wrapper ends. It expires after 30 seconds,
kills only its own probe client, and does not wait for cleanup on Git's path.
It never runs LEARN, signs, requests a PIN itself, changes policy, or starts an
agent deliberately (`--no-autostart`). Custom `--homedir`/`--options` invocations
are skipped. An active observer replaces the initial request notification;
request diagnostics and failure alerts are preserved. Suppressed request diagnostics
are deferred until fallback or operation completion, retaining the original request
timestamp and correlation ID without recording the request twice. Skipped/unavailable
observers retain the ordinary request notice; probe-start and helper-launch failures
restore it once while the signing operation remains active.

Limitations: PIN waits/other card users/slow startup can produce false positives;
a single early probe can miss a later touch wait; it does not identify the
signing card or prove a touch occurred. Shell balloon presentation/dismissal is
Windows-controlled. Broad coexistence and policy cases remain acceptance work.
This is not completion of protocol-confirmed or global touch monitoring.

Validation: 137 Release tests passed, including actual hung-probe subprocess
cleanup, stale-prompt suppression, settings round-trip, and unchanged binary
proxy forwarding. Single-file publishing and guarded daily-app deployment
succeeded. The user confirmed the installed wait notice appeared and cleared
after touch; the duplicate initial notice was subsequently suppressed for active
observers. Regression validation also exposed and fixed Git text-output
decoding under a non-UTF-8 Windows code page; config reads now explicitly use UTF-8.

### Original source assessment — 2026-09-08

No reliable touch-wait transition is exposed by the current wrapper, PC/SC
transaction API, or another SDK session. This does not establish that passive
transport observation is infeasible. Keep Windows CCID tracing and read-only
FIDO HID observation as research candidates, and correlated GPG busy detection
as an explicitly heuristic candidate. Do not advertise any as supported before
physical validation. See [TOUCH-01 through TOUCH-04 in the roadmap](ROADMAP.md#touch-observation-research--2026-09-08).

This assessment validates sources and implementation paths, not hardware
behavior. The proposal's YubiKey 5 / firmware 5.7.4 identification has not been
independently verified in this audit.

## Evidence

- [OpenPGP signing](https://github.com/gpg/gnupg/blob/gnupg-2.5.22/scd/app-openpgp.c#L5596)
  calls `iso7816_compute_ds` and handles its result. This call site does not
  publish a touch-wait transition. UIF attributes describe configuration,
  not whether a request is currently awaiting touch.
- [PC/SC transport](https://github.com/gpg/gnupg/blob/gnupg-2.5.22/scd/apdu.c#L790)
  calls `SCardTransmit` and receives a result; this path does not expose an
  intermediate touch callback. A pending command alone cannot distinguish
  touch waiting from other processing.
- [Device notifications](https://github.com/gpg/gnupg/blob/gnupg-2.5.22/scd/command.c#L2985)
  send `DEVINFO_STATUS new` or `removal`. `DEVINFO --watch` is not a touch feed.
- [CCID button prompting](https://github.com/gpg/gnupg/blob/gnupg-2.5.22/scd/ccid-driver.c#L2183)
  recognizes a time-extension byte of `0xff`, explicitly described as a Gnuk
  enhancement. It invokes `POPUPPINPADPROMPT --ack` / `DISMISSPINPADPROMPT`
  through `popup_prompt`. This proves receiver support for the convention,
  not that the attached YubiKey emits it.
- [Yubico SDK touch callbacks](https://docs.yubico.com/yesdk/users-manual/sdk-programming-guide/key-collector-touch.html)
  serve the operation initiated by that SDK session. TajsToucher's Identify
  operation already uses them; they do not subscribe to other applications.
  Release ends a prompt, while the operation result determines its outcome.

## Proposal validation and corrections

- [GnuPG T5702](https://dev.gnupg.org/T5702.html): the indexed primary-source
  discussion confirms gniibe's 2022-05-25 suggestion that YubiKey could support
  the special `0xff` time-extension convention. Direct retrieval failed during
  this audit; the search index exposed the relevant comment and resolution.
  This is a proposed capability, not a firmware support statement. The ticket
  was resolved via the separate `Confirm` mechanism in T5099, not proof that
  YubiKey firmware gained this transport signal. Generic CCID time extension
  means more processing time, not necessarily user presence.
- [Windows smart-card tracing](https://learn.microsoft.com/en-us/windows/security/identity-protection/smart-cards/smart-card-debugging-information)
  documents `winscard`, `scardsvr`, `scfilter`, and `wudfusbccid` WPP providers,
  including real-time tracing. The last provider's GUID is
  `a3c09ba3-2f62-4be5-a50f-8278a646ac9d`. Documentation does not promise decoded
  CCID response bytes, stable event schemas, or sufficient permissions on this
  machine. Establish the actual driver path, decoding metadata, payload fields,
  and overhead before choosing a production observer. User-mode tracing can
  still require elevation. Provider availability alone is not signal evidence.
- [FIDO CTAP 2.3 Proposed Standard, section 11.2.9.1.7](https://fidoalliance.org/specs/fido-v2.3-ps-20260226/fido-client-to-authenticator-protocol-v2.3-ps-20260226.html#usb-hid-keepalive)
  defines `STATUS_UPNEEDED = 2` as awaiting user presence, distinct from
  `STATUS_PROCESSING = 1`. This supports a protocol-confirmed waiting state,
  not literal LED state or successful touch. The supplied 2.3.1 citation is a
  working draft; the published 2.3 Proposed Standard already defines the signal.
- [Windows HID reads](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
  support user-mode `ReadFile` and buffered reports. They do not establish that
  a second FIDO observer can open the collection or receive independent copies
  while WebAuthn/OpenSSH operates. Validate both access and non-interference;
  opening a handle successfully is insufficient. Do not poll `HidD_GetInputReport`.
- The [Linux detector's GPG implementation](https://github.com/max-baz/yubikey-touch-detector/blob/e6b31829567be95165c67cf40528cef765b1cb17/detector/gpg_linux.go)
  starts a 400 ms timer, sleeps 200 ms, then issues `LEARN`: it is not a full
  400 ms blocked-query threshold. It ends its indication even if the query
  returns an error. These timings are heuristics, not protocol requirements.
  Wrapper invocation is a useful trigger but can precede PIN entry and does
  not identify the selected card or prove the daemon is servicing that request.
- `LEARN` is active daemon/card work, not a passive event subscription. In
  [GnuPG 2.5.22 `agent_handle_learn`](https://github.com/gpg/gnupg/blob/gnupg-2.5.22/agent/learncard.c#L403),
  unknown keys can cause `agent_write_shadow_key` even without `--force`.
  Probe choice, card I/O, contention, local state changes, and bounded shutdown
  therefore need validation before any implementation is enabled.

The current bounded channel belongs to `DeviceEventBroker` (SDK desktop
signals); `OperationEventBroker` dispatches synchronously inside detached
helpers. A future observer must define its own concrete lifetime and delivery
path rather than assuming an existing cross-process queue. Keep SDK-owned
`TouchRequested`/`TouchReleased` semantics unchanged.

## Safety and interpretation

### Windows FIDO preflight and fallback metadata — 2026-09-17

`tools/FidoReadAccessProbe` performs a metadata-only enumeration followed by a
shared, read-only `CreateFileW` attempt, with no HID report reads or writes.
On this machine (Windows 10.0.26220.0, unelevated), one Yubico FIDO collection
was found and opening it returned Win32 error 5 (`ERROR_ACCESS_DENIED`). This
is a concrete blocker for unelevated direct HID observation here. It does not
prove what an elevated process or a different Windows build could do, and no
elevation was attempted. A successful open elsewhere would still not prove
that an observer receives independent copies of reports.

Read-only installed-provider manifest inspection found 5 HIDCLASS event
schemas, 108 USBXHCI schemas, 79 UCX schemas and 126 WebAuthN schemas. HIDCLASS's
binary field is `ReportDescriptor`, not an input report. USBXHCI's top-level
binary fields describe command/event TRBs; UCX has no top-level binary fields.
No WebAuthN event description or template explicitly names keepalive, presence
or touch. These manifest checks do not rule out undocumented numeric states,
nested data, other providers or WPP events. They also provide no validated
CTAPHID keepalive source, so no ETW observer was implemented on speculation.
No trace sessions or authentication operations were started for this check.

Microsoft documents [read access and sharing separately from metadata access](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
and describes [Windows ownership of WebAuthn transport messaging](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/webauthn-apis).
Neither establishes a supported cross-process touch notification feed.

Do not poll the key with synthetic signing/Identify operations: those create
their own requests, may contend with the real operation, and cannot establish
whether the user satisfied that other operation's touch requirement.

Any future integration should distinguish requested, confirmed waiting,
success, failure, and unknown. Report timeout/cancellation only when the
protocol establishes it. Neither a nonzero GPG exit nor an extinguished LED
alone proves a missed touch. Success need not imply a fresh touch when policy
allows caching or does not require touch.

The original September 8 audit performed no hardware operations. The September
17 experiments above did perform user-coordinated signing; the user entered the
PIN locally and changed signing touch policy. No daemon restarts, driver
installations, or Git configuration changes were performed.
