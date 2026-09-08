# PR 1 review resolution

The 29 supplied comments were checked against the implementation and pinned
Yubico SDK 1.17.3 source. Duplicate comments are grouped below.

| Comments | Resolution |
| --- | --- |
| 1, 25 | README describes the shared operation/repository cooldown and failure/test bypasses. |
| 2, 28 | Linked cancellation covers semaphore acquisition; per-key session gates no longer block inventory or another key. Regression tests cover queued cancellation and concurrent inventory/second-key work. |
| 3, 8 | Detached helpers receive a private child-only environment marker. Unmarked helper-shaped arguments are forwarded unchanged, including malformed input. |
| 4, 20 | Disposal cancels channel consumption and explicitly checks cancellation between buffered items. The active callback can finish; queued diagnostics are discarded. |
| 5, 19 | Retain stdin-copy task, cancel after child exit, interrupt pending Windows standard-input I/O, and observe completion. A 250 ms bounded wait preserves early exit for arbitrary non-cooperative streams; eventual faults remain observed. Cooperative cancellation and non-cooperative early-exit tests pass. |
| 6 | Removed misleading x86 solution configurations; the app remains Windows x64. |
| 7 | Optional feature shutdown runs off the UI thread and is bounded to two seconds, including synchronous disposal work. |
| 9 | Repository lookup occurs only in the detached notification helper, never before starting GPG. |
| 10 | License paths use generated NuGet package-path properties and a shared SDK version; missing licenses have an explicit build error. The claimed silent skip was not reproduced: the old missing copied source produced MSB3030. |
| 11, 29 | Touch/cancel delegates execute outside the state lock. The pinned SDK cancellation delegate is a simple flag setter, so that specific SDK deadlock was not demonstrated; external callback lock inversion was still a valid hazard and has regression coverage. |
| 12 | A backward clock jump starts a new device-notice cooldown window. |
| 13 | Save/clear enumerate every numeric PreviousProgram suffix, independently of the bounded stored count. |
| 14 | The classifier consumes attached and separate -R recipient arguments without parsing their letters as operation flags. |
| 15 | Discovery leases share one reference-counted SDK listener owner; disposing one lease cannot stop another. Source initialization/enumeration is serialized across leases. |
| 16, 27 | CTAP statuses map explicitly to unsupported, timed out, cancelled or failed. Unknown failures are failed, not cancelled. In SDK 1.17.3, selection timeouts already throw TimeoutException; false is returned for InvalidCommand/OperationDenied. The concrete correction is chiefly denial classification, with explicit timeout coverage. |
| 17 | Capability checks combine enabled USB/NFC capabilities only for currently available transports. |
| 18 | A fresh-extraction GUI launch exposed an actual XAML startup crash: Icon="Usb" is not a WinUI Symbol. Replaced it with AllApps and added a test validating every navigation icon against the SDK enum. The user owns the subsequent visual pass; successful post-fix rendering is not claimed. |
| 21 | Settings explicitly directs users to the diagnostic-log location on the Enabled for page. |
| 22 | Regression-test cleanup removes created files before deleting its temporary directory. |
| 23 | Cooldown timestamp is committed only after the native notification show call succeeds, while holding the cross-process reservation. Native show/timer failures are checked. Windows acceptance does not guarantee visible delivery under notification suppression settings. |
| 24 | An explicit status-read-failed state avoids inventing either a Git-read failure or a missing installation when loading throws. |
| 26 | Expected NoData for asymmetric PIV slots is shown as an empty slot. PIN/PUK NoData is reported as unavailable metadata (see follow-up below), never as zero retries. Synthetic SDK response coverage includes a mostly empty 27-slot PIV inventory. |

## Evidence and remaining acceptance

- Release regression suite after the follow-up: 120 tests passed, zero failed.
- Folder and single-file Windows x64 publishes are rebuilt after the fixes.
- Both builds load the native shim and finish inventory/listener disposal with
  zero connected keys. Published `--version` and unmarked `--operation-event bad`
  match GPG stdout, stderr and exit status, even while caller stdin stays open.
- The initial single-file launch crash was confirmed by Windows Event 1000 and
  its dump: `Failed to create a 'Microsoft.UI.Xaml.Controls.IconElement' from
  the text 'Usb'`. This is a XAML value defect, not evidence of broken extraction.
- UI rendering, tray interactions, sound, and physical-key acceptance remain
  with the user. No further GUI automation was performed after that request.
- Source references: [PIV metadata behavior](https://docs.yubico.com/yesdk/users-manual/application-piv/pin-touch-policies.html),
  [authenticator selection](https://docs.yubico.com/yesdk/users-manual/application-fido2/apdu/authenticator-selection.html),
  and [Microsoft single-file requirements](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe).

## Follow-up review (11 comments)

| Comments | Validation and resolution |
| --- | --- |
| 1 | Already fixed: capability masks are combined for available transports in `SdkKeyOperations`. The suggested `Transport.Usb` does not exist in the pinned SDK; the implementation uses its three USB transport flags. |
| 2 | Remains user-owned visual acceptance, per the explicit request to leave the visual pass to the user. The startup icon defect was fixed previously; no new GUI run was performed. |
| 3 | Already fixed with an explicit `StatusReadFailed` summary. The related detail-card defect is addressed in comment 8 below. |
| 4 | Not reproduced; the fresh-instance premise is false for SDK 1.17.3. `FindAll` calls `FindByTransport`, which returns the listener cache's objects through `GetAll`. Listener updates retain existing entries by transport-device membership/parent identity. Reference matching therefore preserves anonymous keys across ordinary refreshes without merging identical physical keys. Existing regression coverage verifies stable IDs for two separate anonymous references across refreshes. No speculative OS-path/reflection identity was introduced. |
| 5, 11 | Added NFC-only positive and negative capability assertions. |
| 6 | Touch callbacks use one outside-lock dispatcher. Cancellation invalidates queued request generations and runs after any already-dispatched request callback, so a stale request cannot overtake the SDK cancellation callback. Release/disposal also invalidate queued requests. Regression coverage holds one callback, queues another, then cancels. |
| 7 | Reconnected generations share the old per-device gate. Removed entries with pending sessions retain that gate even across an inventory refresh showing absence. New SDK work sees the new handle only after acquiring the shared gate; the old session keeps its captured handle. Tests cover both immediate reconnection and an observed absent inventory, with an old SDK operation that ignores cancellation. |
| 8 | Unknown setup state now produces unknown signing/GPG detail values and hides install/uninstall actions. These presentation decisions are tested without starting WinUI. |
| 9 | NoData for PIN/PUK now reports unavailable metadata and unknown retries, with no misleading busy/release advice. It does not claim the PIN/PUK is unprovisioned: NoData alone does not prove that. |
| 10 | The coalescing test waits for a non-notifying sentinel's diagnostic callback after every target publication, rather than completing as soon as the expected notice count is reached. Overproduction can no longer pass against a partial queue. |

Pinned SDK evidence for comment 4:
[FindByTransport](https://github.com/Yubico/Yubico.NET.SDK/blob/fe65a725ca9aca468aee16749f51dbc96ac0feeb/Yubico.YubiKey/src/Yubico/YubiKey/YubiKeyDevice.Static.cs#L103-L107)
and [listener cache/update](https://github.com/Yubico/Yubico.NET.SDK/blob/fe65a725ca9aca468aee16749f51dbc96ac0feeb/Yubico.YubiKey/src/Yubico/YubiKey/YubiKeyDeviceListener.cs#L85).
