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
- [x] WinUI 3 desktop app shell with a status dashboard, settings page, and enabled-adapters page;
- [x] overview-only Home; installation/adapters under Enabled for, personalization/testing under Settings, and native lower-left Settings navigation;
- [x] native tray-resident app shell with dashboard reopening and explicit exit;
- [x] optional sound;
- [x] notification cooldown/deduplication (opt-in, cross-repository, with test bypass);
- better context for commit vs tag signing (still open: ordinary GPG arguments
  do not identify Git's object type reliably; do not inspect signing payloads
  or guess from the repository name);
- [x] packaged single-file release (self-contained win-x64 publish profile);
- [x] tests for argument forwarding and exit codes (real child process, binary streams, early exit);
- installer/update story only if actually needed.

The current polish pass also preserves exact Git configuration backups and
aligns dashboard/diagnostic readiness checks. Automated tests cover registry
settings compatibility and uninstall preservation using isolated test keys.
Visual/sound acceptance remains a manual Windows check.

## Later: broader hardware-key events

Only after the core wrapper is trustworthy:

- [x] generic OpenPGP operation notifications (explicit signing, encryption, decryption, and sign+encrypt requests through the wrapper);
- OpenSSH/FIDO touch visibility;
- FIDO2/CTAP2 operation awareness;
- PIV and OATH state/UX integrations;
- [x] normalized event broker (typed request/outcome metadata dispatched inside detached helpers);
- [x] pluggable notification or diagnostics sinks (replaceable in-process sink interface, with native notifications and opt-in bounded local logs);
- [x] optional policy rules around events that are safe to observe (per-operation notices and failure alerts; never authorization or blocking rules).

Encryption/decryption notices, failure alerts, and diagnostic recording default
off. Verification, implicit operations, and key/PIN management are not observed.
Notices indicate a request, not proof that hardware touch is required. No
external plugin loader or background broker service is introduced.

Optional device inventory, explicit PIV/OATH/FIDO2 status, and an SDK-owned
identify test are now implemented on the Devices page. This is not passive
observation of other applications. Physical-key acceptance remains outstanding;
SSH/FIDO is not integrated. The milestones below retain their full acceptance
criteria rather than treating a successful build as hardware proof.

### Yubico SDK assessment — 2026-09-08

**Recommended direction:** use the stable `Yubico.YubiKey` .NET SDK for optional
device discovery and explicitly requested diagnostics. Keep GnuPG/OpenSSH as
the owners of normal signing/authentication. SDK availability is not evidence
that another application's operation can be observed.

#### SDK/library choice

| SDK/library | Fit for TajsToucher | Decision |
| --- | --- | --- |
| [Yubico.NET.SDK / Yubico.YubiKey 1.17.3](https://github.com/Yubico/Yubico.NET.SDK/releases/tag/1.17.3) | Latest non-prerelease found in this review, released 2026-08-24. Its [project targets .NET Standard 2.0/2.1 and .NET Framework 4.7.2](https://github.com/Yubico/Yubico.NET.SDK/blob/1.17.3/Yubico.YubiKey/src/Yubico.YubiKey.csproj), making it a candidate for our .NET 10 Windows x64 app. | Preferred baseline; pin and validate it before adoption. Framework compatibility is not proof that native dependencies or single-file publishing work here. |
| [.NET SDK v2 / yubikit branch](https://github.com/Yubico/Yubico.NET.SDK) | Advertises a new async API including OpenPGP, but the repository identifies it as early alpha, not production-ready, and pending formal security review. | Watch, do not make current milestones depend on it. The [1.17.3 application source tree](https://github.com/Yubico/Yubico.NET.SDK/tree/1.17.3/Yubico.YubiKey/src/Yubico/YubiKey) has no high-level OpenPGP session implementation; retain the existing GPG integration. |
| [libfido2](https://github.com/Yubico/libfido2) / [python-fido2](https://developers.yubico.com/python-fido2/) | FIDO client/device libraries for operations the caller performs, not general activity monitors. | Reference or targeted prototype only; avoid a second native binding or Python runtime when the .NET SDK covers the same requirement. |
| [yubikey-manager / ykman](https://developers.yubico.com/yubikey-manager/) | Python library and CLI for YubiKey application access/configuration. | Useful for development comparison and explicit external-tool handoff, not an always-running subprocess poller or new configuration authority. Do not invoke mutating commands as diagnostics. |
| [YubiKit Android](https://developers.yubico.com/yubikit-android/) / [iOS](https://developers.yubico.com/yubikit-ios/) | Different platform stacks. Android also exposes an experimental desktop Java module. | Not a production dependency for this WinUI utility; no Java/mobile rewrite to obtain an extra protocol API. |

The documentation landing page and development branch can describe different
generations. Treat the pinned release source as the implementation baseline;
recheck releases and API availability when implementation begins.

#### Implementable milestones, in order

- [x] **SDK-01 — Isolated SDK integration and deployment proof.** Add the pinned
  SDK behind the desktop device/diagnostic feature, not the synchronous GPG
  forwarding path. Validate Windows x64 native assets, license notices, folder
  publish, and single-file extraction/loading. Default to unelevated operation.
  Missing SDK/native assets or unavailable devices must not break proxying.
  Acceptance: no-device startup, published runtime probes, and unchanged proxy
  argument/stream/exit-code tests. No feature is enabled just because it builds.

- [ ] **SDK-02 — Connected-key inventory and presence events.** Use
  `YubiKeyDevice.FindAll()` and
  [`YubiKeyDeviceListener.Arrived/Removed`](https://docs.yubico.com/yesdk/yubikey-api/Yubico.YubiKey.YubiKeyDeviceListener.html).
  Show firmware and available versus enabled USB/NFC applications from
  [`IYubiKeyDevice`](https://docs.yubico.com/yesdk/yubikey-api/Yubico.YubiKey.IYubiKeyDevice.html),
  with missing/unsupported fields shown as unknown. Add opt-in arrival/removal
  notices and explicit selection when multiple keys exist. No serial numbers
  in diagnostic logs; do not equate device presence with a usable credential.
  Acceptance: multiple/serial-less keys, rapid reconnect, sleep/resume, stale
  device handles, UI-thread dispatch, and listener shutdown without leaked
  subscriptions. SDK enumeration starts cached background listeners, so their
  lifetime must belong to the desktop feature, not every proxy invocation.

- [ ] **SDK-03 — Read-only, user-requested application status.** Add a Refresh
  action for the selected key, with short-lived sessions and no credential
  collection. This is active device querying, not passive monitoring:
  - **PIV:** read supported slot algorithm/PIN/touch-policy metadata and PIN/PUK
    retries using [`PivSession.GetMetadata`](https://docs.yubico.com/yesdk/yubikey-api/Yubico.YubiKey.Piv.PivSession.html).
    Gate on firmware 5.3+ and supported slots. Respect
    [`PivMetadata` unknown values](https://docs.yubico.com/yesdk/yubikey-api/Yubico.YubiKey.Piv.PivMetadata.html);
    never try a PIN to discover retry counts, and do not export public keys or
    certificates into logs. A configured touch policy is not a live touch event.
  - **OATH:** expose application availability and
    [`OathSession.IsPasswordProtected`](https://docs.yubico.com/yesdk/yubikey-api/Yubico.YubiKey.Oath.OathSession.html).
    Label this password protection, not the authenticated state of other apps.
    Do not unlock the app, enumerate account labels, calculate codes, or change
    credentials. Leave those workflows to Yubico Authenticator.
  - **FIDO2:** where access permits, show
    [`AuthenticatorInfo`](https://docs.yubico.com/yesdk/yubikey-api/Yubico.YubiKey.Fido2.AuthenticatorInfo.html)
    and [`GetPinRetriesCommand`](https://docs.yubico.com/yesdk/yubikey-api/Yubico.YubiKey.Fido2.Commands.GetPinRetriesCommand.html)
    results without PIN verification or credential enumeration. Windows direct
    FIDO access has [privilege restrictions](https://docs.yubico.com/software/yubikey/tools/ykman/webdocs.pdf);
    permission-denied must not become "no key" or "zero retries". Do not elevate
    the resident app or Git proxy automatically.
  Acceptance: unsupported firmware, permission denial, key removal and busy
  smart-card access stay distinguishable; sessions close promptly. Prove reads
  coexist with GnuPG/OpenSSH/Authenticator before adding any automatic refresh.

- [ ] **SDK-04 — Explicit touch/identify-key self-test.** Offer a user-triggered
  test using [`Fido2Session.TryAuthenticatorSelection`](https://github.com/Yubico/Yubico.NET.SDK/blob/1.17.3/Yubico.YubiKey/src/Yubico/YubiKey/Fido2/Fido2Session.AuthenticatorSelection.cs),
  present in 1.17.3 and documented for YubiKey firmware 5.5.1+. It requests user
  presence without creating a credential or making a signature. Gate device
  support and access first; never fall back to dummy credential creation.
  Map `TouchRequest` to a correlated prompt and `Release` to cleanup, with
  cancellation through the SDK. `Release` alone is not proof of success: use
  the operation's result to distinguish completion, cancellation, and timeout.
  Acceptance: touch, no touch, cancel, unplug, unsupported device, and denied
  access. This proves only TajsToucher's own test, not global FIDO monitoring.

- [ ] **SDK-05 — Integrate concrete device/status signals into the existing UX.**
  Extend typed event handling only for signals actually supplied by SDK-02–04;
  do not represent device/FIDO events as `OpenPgpOperation` values. Reuse native
  notifications, bounded metadata diagnostics, and opt-in policy settings.
  Coalesce reconnect noise and repeated low-retry warnings from explicit reads.
  Correlate a removed device with an operation only when the adapter knows which
  device owns it; never guess which attached key GnuPG or a browser selected.
  Acceptance: source labels, unknown states, redaction, independent sink failure,
  and no blocking authorization rules or generic plugin/runtime framework.

#### Conditional or not available as a passive SDK feature

- **System-wide touch/PIN notifications:** no passive cross-process operation
  subscription was found in the reviewed APIs. The device listener reports
  arrival/removal. [`KeyCollector` touch callbacks](https://docs.yubico.com/yesdk/users-manual/sdk-programming-guide/key-collector-touch.html)
  and [FIDO2 notifications](https://docs.yubico.com/yesdk/users-manual/application-fido2/fido2-touch-notification.html)
  concern an operation being performed through the caller's SDK session.
  **Assessment:** adding the SDK alone cannot tell us when Chrome, Windows
  Hello, GnuPG, or another process needs touch. Keep such coverage unclaimed.
- [ ] **SSH-01 — Verify an OpenSSH-owned notification hook, separately from the SDK.**
  Upstream [`notify_start` / `notify_complete`](https://github.com/openssh/openssh-portable/blob/master/readpass.c)
  provides a candidate askpass notification path using `SSH_ASKPASS_PROMPT=none`.
  Verify actual Windows OpenSSH and Git-bundled builds and their terminal/display
  behavior first. Preserve existing password/PIN/host-confirmation handling;
  a notification-only helper must not silently replace a general askpass program.
  No forced global environment changes or SSH transport proxy. Ship only after
  the notification lifecycle and ordinary authentication both pass validation.
- **PIV/OATH activity in other apps:** read-only status is feasible, but it is
  not observation of their signing/code-generation sessions. Do not infer live
  activity from configured policies, password-protection state, or device I/O.
- **Full FIDO enrollment/assertion flows, OATH code generation, PIV signing,
  app resets, PIN changes, and capability reconfiguration:** SDK support does
  not make these appropriate utility features. They remain outside this plan
  under the existing key/PIN-management and Authenticator-replacement deferrals.

### Implementation and acceptance status — 2026-09-08

The implementation pins **Yubico.YubiKey/Core 1.17.3** with transitive
**NativeShims 1.17.2**. SDK licensing is included in both publishing formats.

| Milestone | Implemented | Remaining acceptance |
| --- | --- | --- |
| SDK-01 | Separate optional assembly, lazy desktop-only startup, explicit inventory/native-loading diagnostic command, fail-open proxy isolation, folder and single-file payloads. | Published probes and regression results are recorded below. |
| SDK-02 | Devices selector, available/enabled USB/NFC capabilities, anonymous per-handle identity, debounced listener refresh, stale-handle replacement, shutdown ownership, opt-in presence notices. | Physical multiple-key/serial-less reconnect and sleep/resume, visible UI acceptance. |
| SDK-03 | Explicit short-lived PIV slot metadata/PIN/PUK retries, OATH password-protection status, FIDO versions/options/retries; no credential collector. Busy/removal/access-denied/unknown states are preserved. | Physical firmware/permission/busy-removal cases and GnuPG/OpenSSH/Authenticator coexistence. No automatic application reads are enabled. |
| SDK-04 | Firmware-gated credential-free selection, cancellation, correlated prompt/result, removal cancellation for the known selected device, late-callback cleanup. | Physical touch/no-touch/cancel/unplug/Windows access-denial acceptance. No dummy-credential fallback. |
| SDK-05 | Separate typed device signals, existing native tray and rotating diagnostic writer, bounded background queue, independent sink failures, opt-in presence/retry policy, coalesced notices, SDK logging disabled. | Physical notification/sound and UI acceptance; no cross-process operation attribution. |

SDK-02–05 remain unchecked above until their hardware acceptance criteria pass.
Automated state/callback tests do not substitute for those checks.

**SDK-02 discovery follow-up — 2026-09-17:** reproduced an attached USB key
hidden from SDK inventory, with PC/SC shared-connect returning
`SCARD_E_SHARING_VIOLATION (0x8010000b)` while scdaemon remained running. This
confirms incompatible reader ownership, not which process owns it. Retrying
the SDK cache cannot release that connection. Devices now reports present
Yubico USB devnodes independently of SDK handles, including when SDK discovery
fails; metadata-only enumeration neither opens the card nor initiates HID I/O.
Unknown OS presence is not zero, composite interfaces are excluded, and no
fake SDK key or enabled operation is invented. No daemon restart, shared-mode
configuration change, or elevation is performed. Physical access/coexistence
acceptance remains open.
Validation: 34 device tests passed, including OS-presence/SDK-denial separation,
presence-provider failure and composite-interface deduplication. The published
diagnostic reported one attached USB device and zero SDK-accessible keys in
the still-blocked state, without replugging, and exited successfully.

The Devices page now presents OS-detected hardware as connected cards and keeps
SDK controls inside an optional diagnostics section. Opening the page refreshes
inventory; missing SDK handles do not hide connected USB cards. No speculative
USB-to-SDK identity mapping is performed. [GPG ownership research](gpg-card-ownership.md)
confirms intentional cache retention and successful owner-mediated policy/retry
reads while SDK access is blocked. Prefer an explicit bounded GPG diagnostics
path next; automatic disconnects, daemon restarts and shared-mode changes remain
unvalidated and are not enabled.

**Observed validation:** Release tests pass **93/93**, including unchanged binary
stream/argument/exit-code tests, fresh-process proxy SDK-isolation, anonymous-key
identity, stale selection/removal, listener disposal, cancellation/late callbacks,
Windows error categories, settings compatibility, redaction and sink isolation.
Both self-contained win-x64 folder and single-file publishes succeed. Their
`diagnose-devices` probes explicitly load the native shim, report zero connected
keys and exit successfully after listener shutdown. A fresh, isolated single-file
extraction also contains the native shim and license/notice files. A published
copy with SDK managed/native assemblies removed still forwards GPG `--version`
and `--dump-options` with byte-identical stdout/stderr and matching exit codes;
its optional device probe fails cleanly. No live signing, PIN attempts, touch
tests, credential operations or user Git/configuration changes were performed.

**SSH-01 investigation:** installed clients report Windows OpenSSH 9.5p2 and
Git-bundled OpenSSH 10.5p1. Both binaries contain the askpass notification hint
and user-presence prompt. The [10.5p1 source](https://github.com/openssh/openssh-portable/blob/V_10_5_P1/readpass.c)
still shares askpass with ordinary password/confirmation requests, chooses
terminal output in some cases, and terminates the notification child via
`notify_complete`. Binary strings are not lifecycle/authentication proof.
Without a connected security key and an authentication test target, that
acceptance cannot be completed here. No askpass override, forced environment
configuration, or transport proxy has been shipped.

System-wide FIDO/PIV/OATH observation remains unimplemented. The bounded touch
research below is accepted for investigation, not a claim of support; unrelated
deferred features remain out of scope. Installer/updater work remains conditional
on an actual need.

### Touch observation research — 2026-09-08

#### Solution migration and resumed hardware acceptance — 2026-09-17

The solution is now `TajsToucher.slnx`, migrated with the .NET 10 SDK while
preserving all three project entries and the Any CPU/x64 configurations.
README commands use the new filename; the old `.sln` was removed. The full
Release suite passes **140/140** through the new solution. An ordinary Release
solution build passes with zero warnings/errors and automatically publishes,
deploys, and restarts the permanent daily app through the existing workflow.

A user-coordinated signature through the installed wrapper completed in 15.1
seconds with exit 0, `SIG_CREATED`, and a signature output file. This validates
the successful-signing path after duplicate-notice suppression. The user
confirmed one notice (no duplicate), reported probable dismissal after touch,
and observed noticeable delay after blinking began. Dismissal remains tentative;
latency remains an open UX issue. The current floor is 500 ms startup grace plus
600 ms busy threshold, then process/shell presentation latency. It does not close
TOUCH-01/04's PIN-wait, contention, cancellation, or multi-card acceptance.

The subsequent instructed no-touch run returned GPG `Timeout`, exit 2, and no
`SIG_CREATED` after 19.8 seconds. The user saw a probable operation-failed
balloon but could not inspect it afterward; this does not establish wait-notice
cleanup. Failure is an operation outcome, not evidence that a touch occurred.

**TOUCH-04 presentation follow-up:** replaced the suspected-wait balloon with
a compact topmost TajsToucher WinUI card, still opt-in and explicitly
labelled "Key may be waiting for touch". Dismiss/Escape dismisses only the window;
the requesting application owns signing cancellation. The existing wait-ended
event closes the dialog, with a 30-second independent safety bound. It requests
no PIN and is not Windows Security. Ordinary request/failure balloons remain
unchanged and are not notification-history entries. The heuristic's 500/600 ms
delays remain; removing balloon presentation does not prove lower detection
latency. Published synthetic-event checks pass for already-ended suppression,
topmost display, wait-ended cleanup, dismissal without signalling the operation,
and expiry (`scripts/Test-TouchWaitWindow.ps1`). The user rejected the initial
classic task-dialog styling and approved the replacement compact WinUI card
with key icon, semantic theme brushes, short text and Dismiss action. WinUI
starts only in the detached helper; this mode bypasses dashboard/tray/device
initialization. Real signing with the card remains distinct from this approved
synthetic-wait preview. Light/high-contrast and multi-DPI visual acceptance have
not been claimed.

#### Unattended follow-up — 2026-09-17

- **TOUCH-01/04 lifecycle validation advanced:** six observer tests pass,
  including the actual 30-second hung-probe limit, notification-sink failure,
  missing probe executable, signing-end cancellation, fast completion, and
  configuration exclusions. These prove bounded cleanup paths, not accurate
  attribution of busy time to touch. Physical false-positive/coexistence cases
  remain open; no new signing or PIN operation was started in this follow-up.
- **TOUCH-03 direct HID preflight executed:** the standalone
  [`FidoReadAccessProbe`](../tools/FidoReadAccessProbe/README.md) found one Yubico
  FIDO collection on Windows 10.0.26220.0, but a shared `GENERIC_READ` handle
  failed with `ERROR_ACCESS_DENIED (5)` under the unelevated account. No reports
  were read/written, authentication initiated, or elevation attempted. The
  unelevated direct-HID route is blocked on this configuration, not declared
  universally impossible. Independent report delivery remains untested.
- **ETW fallback metadata assessed:** installed HIDCLASS schemas expose a
  report descriptor, not input-report bytes; USBXHCI exposes command/event TRBs,
  not an established CTAPHID payload stream; UCX has no top-level binary data
  fields in the inspected manifest. WebAuthN has no explicitly named
  keepalive/user-presence/touch event in the inspected schemas. Provider
  availability is not proof of a usable signal. No raw trace was captured.
- **Next gates:** obtain usable CCID decoding metadata (TOUCH-02), or validate
  a concrete alternative transport event before implementing another observer.
  Hardware acceptance still needs coordinated touch/timeout/cancel/PIN-wait
  and competing-client tests. Do not install filter drivers or elevate the
  resident app to turn an inconclusive result into a supported feature.

No TOUCH milestone is marked complete by these results. The research tool is
not shipped in the daily app; this follow-up contains test/documentation/tool
changes only and does not restart the installed app.

Original status (2026-09-08): source-validated research plan only.
Update 2026-09-17: an experimental opt-in wrapper-scoped busy indicator is now
implemented using `SCD GETATTR UIF-1`, not LEARN. A physical no-touch timeout and
isolated touch-success were observed after the user enabled signing touch On.
One isolated policy probe tracked the pending signature; this is not confirmed
touch detection. CCID captures contain events but public symbols did not decode
them. Full
TOUCH-01/02/04 acceptance remains open. See the dated feasibility update for
timings, limitations, and remaining hardware coverage.
The high-level API limitation does not rule out lower-layer observation.
See [feasibility evidence and corrections](touch-monitoring-feasibility.md) for
the GnuPG discussion, Windows tracing/HID documentation, FIDO specification,
and pinned Linux detector source. All milestones remain unchecked.

- [ ] **TOUCH-01 — Correlated GPG busy-check prototype.** First evaluate a
  bounded, opt-in probe triggered by the existing wrapper, labelled
  `TouchWaitSuspected` / `ScdaemonBusy`, never confirmed touch. Do not copy
  `LEARN` blindly: it performs card work and can create local shadow-key files.
  Choose and validate a probe with acceptable side effects before integration;
  if none exists, record no-go rather than silently changing user state.
  Cover PIN waiting, software keys, cached/no-touch policies, concurrent GPG
  operations, multiple cards, no device, daemon failure, and slow unrelated I/O.
  Acceptance: measure false positives/latency against user-observed waits,
  demonstrate unchanged configuration/key files and proxy bytes/exit status,
  and prove bounded worker lifetime with no delayed signing completion. No
  continuous polling or cancellation of the user's daemon/operation.

- [ ] **TOUCH-02 — Passive Windows CCID ETW/WPP experiment.** Preferred
  transport-level OpenPGP experiment; not dependent on shipping TOUCH-01.
  Verify actual driver/provider path, elevation needs, usable WPP decoding
  metadata, and which response fields are emitted. During user-coordinated
  signing with non-sensitive test data, correlate command/wait/end timing and
  distinguish generic time extensions from the special `0xff` convention.
  Generic time extension remains suspected. Accept `CcidUserPrompt` only after
  evidence on the exact device/firmware/driver demonstrates that meaning.
  Test touch, no-touch timeout, cancellation, unplug, cached/no-touch operation,
  unrelated card activity, and concurrent devices. Record lost events and
  unsupported builds as unknown, not success. No driver installation or service
  restart. Use a uniquely owned, bounded trace session and stop only that session.
  Raw traces may contain sensitive APDU data: restrict capture scope, keep files
  under `.codex/temp`, do not upload them, and retain only redacted metadata in
  product diagnostics. Missing payload/decoding is a valid no-go result.

- [ ] **TOUCH-03 — Passive Windows FIDO HID experiment.** Test a shared,
  read-only collection handle with no CTAPHID writes, INIT, CANCEL, channel
  allocation, or synthetic authentication. Prove independent report delivery
  and no interference with real Windows WebAuthn and OpenSSH operations;
  an accessible handle alone does not pass. Validate report IDs, frame lengths,
  initialization/continuation framing, device/channel identity and removal.
  Only a valid `CTAPHID_KEEPALIVE / STATUS_UPNEEDED` confirms user-presence
  waiting. Processing, lost reports, silence, disconnect, and end-of-wait do not
  prove a touch. Scope state per device/channel; do not infer a browser/process
  or wrapper operation from timing alone. Cover cancel/timeout/unplug and
  multiple channels/devices. If blocked, assess HID/USB ETW payload availability
  separately; no automatic escalation to USBPcap or filter drivers.

- [ ] **TOUCH-04 — Integrate only validated observers.** Introduce a narrow,
  typed observation lifecycle when a preceding prototype passes, preserving
  the existing SDK-owned touch events. Separate suspected/confirmed waiting,
  source, known correlation, and wait-ended/unknown from the operation owner's
  success/failure. Signal loss must expire stale prompts without claiming touch
  satisfaction. Define bounded delivery and helper/desktop shutdown explicitly;
  the SDK queue is not a cross-process operation broker. Default new observers
  off, expose capability/permission failures, preserve existing notification and
  Git settings, and keep signing fail-open. Acceptance includes sink failures,
  observer crashes, backpressure/lost terminal events, published-runtime
  coexistence, privacy, and user-visible hardware checks. Build/parser tests
  alone cannot mark these milestones complete.

USBPcap/filter drivers and API hooks remain deferred, requiring a separate
scope decision rather than being installed as fallback. Literal LED querying,
background Identify, and synthetic signing as monitoring mechanisms are excluded.

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
