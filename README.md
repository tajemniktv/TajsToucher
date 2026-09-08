# TajsToucher

TajsToucher is a small Windows helper that makes hardware-key signing less
invisible. Git configures it as its OpenPGP program; TajsToucher shows a
notification for signing operations by default and then forwards the operation to the
real `gpg.exe`.

The problem it solves is painfully mundane: a YubiKey can be waiting for touch
while blinking somewhere under a desk, and neither Git nor GnuPG gives Windows
users a particularly good visual cue.

```text
Git / Codex
    |
    v
TajsToucher.exe
    |-- native notification (selected operations, fail-open)
    `-- real gpg.exe
            |
            v
         gpg-agent
            |
            v
         YubiKey
```

## v0.1 usage

Build the project with the .NET 10 SDK:

```powershell
dotnet restore TajsToucher.sln
dotnet build src\TajsToucher\TajsToucher.csproj -c Release
```

For a framework-dependent Windows x64 build (the WinUI 3 runtime payload is
kept beside the executable), publish for Windows x64:

```powershell
dotnet publish src\TajsToucher\TajsToucher.csproj `
  -c Release -r win-x64 --self-contained false `
  -o artifacts\publish\framework-dependent
```

For a self-contained Windows x64 folder with no .NET prerequisite:

```powershell
dotnet publish src\TajsToucher\TajsToucher.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts\publish\win-x64
```

For a single self-contained Windows x64 executable:

```powershell
dotnet publish src\TajsToucher\TajsToucher.csproj -c Release -p:PublishProfile=SingleFile
```

The result is `artifacts\publish\single-file\TajsToucher.exe`. It bundles
the .NET and Windows App SDK payloads and extracts them into the .NET runtime's
per-user cache on first launch. Allow extra disk space and startup time for that
first launch. Move the executable to its final location before running `install`.
The ordinary folder-based publish remains supported.

Run the published executable from its final location:

```powershell
artifacts\publish\win-x64\TajsToucher.exe
artifacts\publish\win-x64\TajsToucher.exe install
artifacts\publish\win-x64\TajsToucher.exe diagnose
artifacts\publish\win-x64\TajsToucher.exe settings
artifacts\publish\win-x64\TajsToucher.exe uninstall
```

`install` discovers the real GnuPG executable, stores it in the current user's
`HKCU\Software\TajsToucher` key, and sets the global Git
`gpg.openpgp.program` value to the absolute TajsToucher path. `uninstall`
restores the previous value only if Git still points at the same wrapper; it
will not overwrite a setting changed by the user in the meantime.

Launching the executable without arguments opens the WinUI 3 TajsToucher app.
The Home dashboard is the primary control surface: it shows whether Git signing
is connected, whether the real GnuPG executable is available, exposes install,
uninstall, and test-notification actions, embeds notification personalization,
and lists the enabled adapters. The separate **Settings** and **Enabled for**
pages are navigation shortcuts to the same capabilities. Settings include the
notification title, notification text, and an optional `.ico` file. Use
`{Repository}` in either field to insert the current repository name. If the
text does not contain that token, the repository name is appended automatically
when available.

Settings also offer a Windows notification sound toggle and a cooldown of
0–300 whole seconds. Sound remains enabled by default; Windows controls its
actual audibility. Cooldown defaults to 0 (show every request). When enabled,
it suppresses repeated operation notices across enabled operations and repositories for this user.
Enabled failure alerts and test notifications bypass cooldown without consuming it. Cooldown state stores
only a timestamp; it does not record repository names or signing payloads.

### Broader OpenPGP notices and diagnostic events

Settings now has per-operation notification rules. Signing remains on by
default; encryption (including symmetric encryption), decryption, and failure
alerts are opt-in. Sign-and-encrypt requests use either selected rule. Use
`{Operation}` in title/text so one custom template can describe each operation.
The old stock Git-only message is upgraded to an operation-aware default;
other custom text is preserved. Test notifications still preview signing.

These rules observe only explicit commands **routed through TajsToucher**:

```powershell
TajsToucher.exe --encrypt --recipient RECIPIENT --output message.gpg message.txt
TajsToucher.exe --decrypt --output message.txt message.gpg
```

GnuPG still owns all cryptographic work, prompts, and exit codes. A request
notification does not mean hardware touch is definitely required. Direct
`gpg.exe` calls, operations inferred from input or option files, verification,
and key/PIN management are not monitored. Failure alerts use only the exit
code, never GPG's error text, and bypass cooldown when enabled for that operation.

Optional **Record operation diagnostics locally** writes metadata to
`%LOCALAPPDATA%\TajsToucher\Diagnostics\events.log` and
`events.previous.log` (64 KiB each). Fields are UTC timestamp, correlation ID,
source, operation, phase, exit code, and wrapper elapsed milliseconds. Logs
never include arguments, repositories, key IDs, filenames, stdout/stderr, or
cryptographic payloads. Detached helpers can write out of order: correlate by
ID and use the event timestamp rather than line order. Logs are best-effort,
not an audit trail; helper failures or contention can lose entries. Disabling
recording retains existing files. **Enabled for** shows the recording state
and opens the folder; you can remove the two log files when no longer needed.

### Optional YubiKey diagnostics

Open **Devices** (or run `TajsToucher.exe devices`), then click **Discover /
refresh keys**. The pinned Yubico SDK runs in a separate desktop-only assembly.
Discovery stays active until the tray app exits; it is never started by a GPG
invocation. Select a key explicitly when several are connected. Inventory shows
firmware and available/enabled USB/NFC applications, not credential usability.
Serial-less keys have ephemeral, per-handle identities and are not merged by
similar firmware. No serial numbers are shown or logged.

**Read application status** opens short-lived sessions, only when clicked:

- PIV metadata (firmware 5.3+): algorithms, configured PIN/touch policies and
  PIN/PUK retries. Unknown retry counts remain unknown; no PIN is attempted.
- OATH: password protection only. No unlocking, account labels or code generation.
- FIDO2: supported versions/options and retry information where Windows permits
  direct access. Permission denial, removal, busy access and timeouts are not
  reported as zero retries or proof that no key exists.

**Touch / identify test** uses the SDK's credential-free authenticator selection
on supported firmware (5.5.1+). It provides its own correlated touch prompt and
Cancel action. Completion comes from the command result, never a cleanup callback.
The test creates no credential or signature and does not observe other apps.
Windows may deny direct FIDO access; TajsToucher never elevates itself.

Settings has opt-in arrival/removal and low-retry notices. These use the resident
native tray; repeated presence noise and low-retry warnings are coalesced.
Optional diagnostics reuse the same bounded files, with a `YubiKeySDK` source,
timestamp, correlation ID, signal kind and outcome only. The SDK's own logging
is disabled. A bounded queue keeps notification/disk work off SDK callbacks.

`TajsToucher.exe diagnose-devices` explicitly probes native SDK loading and
inventory, then stops listeners. It performs no application reads or touch test.
SDK licenses accompany folder builds in `licenses/` and are included in the
single-file extraction payload. Missing SDK files do not prevent GPG forwarding.

Hardware touch, reconnect/suspend and coexistence with GnuPG/OpenSSH/Authenticator
still require manual acceptance on physical keys. No system-wide touch/PIN,
browser FIDO, or other-app PIV/OATH monitoring is implemented. OpenSSH integration
remains gated on a separately validated askpass lifecycle.

Closing the app window hides TajsToucher to the system tray instead of stopping
it. Double-click the tray icon, or use its menu, to reopen the dashboard,
Settings, or Enabled for. **Exit TajsToucher** in that menu terminates the app.
The dashboard follows Windows' native light/dark/high-contrast theme. Home's
embedded settings and integration information share its outer scroll surface;
the standalone pages keep their own scrolling.

`settings` opens the same app directly on the Settings screen. `app` opens the
Home screen explicitly.

`diagnose` returns exit code 0 only when saved installation state exists, the
installed wrapper and real GPG are available, and Git's readable global
`gpg.openpgp.program` has exactly one value matching the installed wrapper.
Otherwise it returns 1 and reports which setup condition needs attention.
This is a global OpenPGP configuration check, not proof of signing: repository
configuration and `gpg.format` (for example SSH signing) can override it.
It does not change settings, request a signature, or test hardware-key access.

When Git invokes the executable with GPG arguments, it acts as a transparent
GPG proxy. The `install`, `uninstall`, `diagnose`, `diagnose-devices`, `devices`, `app`, `settings`, `proxy`,
`help`, and `version` subcommands are reserved only when supplied as the single
bare argument, so GPG options such as `--help` and `--version` are forwarded
unchanged. `proxy` is the explicit no-argument proxy mode.

Run the tests with:

```powershell
dotnet test tests\TajsToucher.Tests\TajsToucher.Tests.csproj -c Release
```

## Security and process boundary

The wrapper:

- detects `--sign`, `--detach-sign`, `--clearsign`, and Git's `-bsau`-style
  short option clusters;
- optionally observes explicit encryption/decryption requests and records
  fixed metadata-only request/outcome events;
- does not notify for `--verify` or `--verify-files` operations;
- forwards stdin, stdout, and stderr as raw byte streams and returns GPG's
  exit code;
- never prints or stores GPG arguments, stdin, key material, PINs,
  passphrases, signatures, or other cryptographic payloads;
- uses a short-lived child process for the notification so the notification
  does not extend Git's synchronous GPG operation; and
- treats notification failures as non-fatal.

Notification settings are stored per-user under
`HKCU\Software\TajsToucher` and are separate from installation state, so
uninstalling the Git wrapper does not discard the user's notification
customization.

The older `gpg-git-notify.cmd` and `gpg-touch-notify.ps1` files are retained as
legacy proof-of-concept references. New installations should use the native
executable.

## Translucent Windows compatibility

The dashboard is a WinUI 3/XAML app rather than a WinForms surface. Its page
backgrounds stay transparent and cards use lightly alpha-backed brushes, so
Windhawk's **Translucent Windows** mod can provide the window material without
the old hard-coded WinForms canvas and borders fighting it. The tray and
notification helpers use native Win32 APIs and do not bring WinForms back into
the dashboard.

If the mod still makes text or controls unreadable, add `TajsToucher.exe` as a
per-process rule in the mod settings and disable custom theme rendering and
accent colorization for that process. This is preferable to a global exclusion
when the mod's **New system colors** option is enabled.

## Possible future direction

TajsToucher may eventually grow into a general hardware-key event and policy layer. The architectural direction should prefer adapters and metadata over becoming a cryptographic middleman.

Potential adapters or event sources include:

- Git / GnuPG signing.
- OpenPGP smart-card operations.
- FIDO2 / CTAP2 authentication.
- OpenSSH FIDO security-key operations.
- PIV operations.
- OATH status and prompts.

Potential events include `signing requested`, `touch required`, `PIN requested`, `authentication started`, `operation timed out`, and `PIN retries low`.

The aspirational architecture is roughly:

```text
apps / Git / GPG / SSH
          |
          v
     optional adapters
          |
          v
       event broker
          |
          +--> notifications
          +--> policy
          `--> diagnostics
          |
          v
 actual platform tools / YubiKey
```

The important constraint is that observation should come before interception. TajsToucher should prefer knowing that an operation is happening over owning the secrets needed to perform it.

## Non-goals for now

- Key management.
- Replacing GnuPG, OpenSSH, or Yubico Authenticator.
- Proxying arbitrary USB/HID/CCID traffic.
- Storing credentials or PINs.
- Becoming a tray-based cryptographic control panel before the notifier itself is solid.
- Cross-platform support before the Windows implementation is boringly reliable.

If this eventually becomes a unified cryptographic orchestration platform, it has to earn that absurdity incrementally.
