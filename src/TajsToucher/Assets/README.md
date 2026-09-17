# App icon

`TajsToucher.png` is the user-supplied artwork, copied unchanged. Its white
background and original framing are intentional; no background removal or
redrawing was performed.

Regenerate the Windows icon from the repository root:

```powershell
./tools/Convert-AppIcon.ps1 -Source src/TajsToucher/Assets/TajsToucher.png -Destination src/TajsToucher/Assets/TajsToucher.ico
```

The ICO contains 16, 24, 32, 48, 64, 128 and 256 pixel frames. It is compiled
into the executable and embedded for process-lifetime window/tray handles, so
single-file deployment does not rely on the original Downloads path. Custom
notification icons still override the default notification-host icon.
