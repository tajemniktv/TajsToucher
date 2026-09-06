# TajsToucher

TajsToucher is a small Windows helper that makes hardware-key signing less
invisible. Git configures it as its OpenPGP program; TajsToucher shows a
notification for signing operations and then forwards the operation to the
real `gpg.exe`.

The problem it solves is painfully mundane: a YubiKey can be waiting for touch
while blinking somewhere under a desk, and neither Git nor GnuPG gives Windows
users a particularly good visual cue.

```text
Git / Codex
    |
    v
TajsToucher.exe
    |-- native notification (signing only, fail-open)
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

For a small single-file executable on a machine with the .NET 10 Windows
Desktop runtime installed, publish for Windows x64:

```powershell
dotnet publish src\TajsToucher\TajsToucher.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -o artifacts\publish\framework-dependent
```

For a self-contained single-file executable with no .NET prerequisite:

```powershell
dotnet publish src\TajsToucher\TajsToucher.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts\publish\win-x64
```

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

Launching the executable without arguments opens the TajsToucher app. The Home
screen shows whether Git signing is connected, whether the real GnuPG executable
is available, and provides quick actions. The **Settings** screen lets you
configure the notification title, notification text, and an optional `.ico`
file. Use `{Repository}` in either field to insert the current repository name.
If the text does not contain that token, the repository name is appended
automatically when available. The **Test notification** button saves the
current values and launches a sample notification. The **Enabled for** screen
currently lists Git/OpenPGP signing and leaves room for future adapters.

Closing the app window hides TajsToucher to the system tray instead of stopping
it. Double-click the tray icon, or use its menu, to reopen the dashboard,
Settings, or Enabled for. **Exit TajsToucher** in that menu terminates the app.

`settings` opens the same app directly on the Settings screen. `app` opens the
Home screen explicitly.

When Git invokes the executable with GPG arguments, it acts as a transparent
GPG proxy. The `install`, `uninstall`, `diagnose`, `app`, `settings`, `proxy`,
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

The app requests the Windows 11 main-window DWM backdrop and extends that
material through the client area. Its app surfaces use alpha-backed fills and
avoid fixed WinForms borders, so Windhawk's **Translucent Windows** mod can
provide the window material without turning the cards into opaque white boxes
or accent-colored outlines.

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
