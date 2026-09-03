# TajsToucher

TajsToucher is a tiny Windows helper that makes hardware-key signing less invisible.

Right now it wraps Git's OpenPGP signing command, shows a desktop notification when Git is about to ask GnuPG to sign something, and then forwards the operation to the real `gpg.exe` unchanged.

The problem it solves is painfully mundane: a YubiKey can be waiting for touch while blinking somewhere under a desk, and neither Git nor GnuPG gives Windows users a particularly good visual cue.

Current proof of concept:

```text
Git / Codex
    |
    v
gpg-git-notify.cmd
    |-- show notification
    `-- real gpg.exe
            |
            v
        gpg-agent
            |
            v
         YubiKey
```

The current notification text is intentionally sophisticated:

> YubiKey yearns touching

> Git is requesting an OpenPGP signature. Please touchy touch the YubiKey while it flashes.

## Scope

The first release should stay deliberately boring:

- Windows only.
- Git/OpenPGP signing only.
- One small executable or wrapper.
- Show a notification immediately before GPG signing.
- Preserve stdin, stdout, stderr, arguments, and GPG's exit code.
- Never handle or log secret key material, PINs, passphrases, signature payloads, or Git input.

The project should remain useful even if it never grows beyond that.

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
