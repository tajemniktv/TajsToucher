# FIDO read-access preflight (TOUCH-03)

Run unelevated from the repository root:

```powershell
dotnet run --project tools/FidoReadAccessProbe/FidoReadAccessProbe.csproj -c Release -p:DogfoodEnabled=false
```

This development-only tool enumerates Yubico FIDO HID collections (VID 1050,
usage page F1D0, usage 1) through the pinned SDK's OS metadata enumeration. It
attempts `CreateFileW` with `GENERIC_READ`, `FILE_SHARE_READ | FILE_SHARE_WRITE`,
`OPEN_EXISTING`, and `FILE_FLAG_OVERLAPPED`, then immediately closes each handle.
It never opens an SDK protocol session, reads reports, writes INIT/CANCEL,
starts authentication, changes configuration, or requests elevation. SDK logging
is disabled and output excludes paths, serial numbers and container IDs.

Exit codes: 0 = preflight completed with at least one matching collection,
1 = enumeration failed, 2 = unsupported OS, 3 = no matching collection.
**Exit 0 does not mean access was granted or monitoring is supported.** Inspect
each collection result; access denial and sharing violation remain distinct.

A successful open would still require a separate, user-coordinated experiment
to prove independent report delivery and non-interference with WebAuthn/OpenSSH.
Do not turn this tool into a report reader without that review. There is no
app dependency on this project and test/research builds must not deploy it.

Observed 2026-09-17: Windows 10.0.26220.0, unelevated, one matching collection,
`AccessDenied; Win32=5`. No report capture was attempted.

References: [CreateFileW access and sharing](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew),
[Windows WebAuthn transport ownership](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/webauthn-apis).
