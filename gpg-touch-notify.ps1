Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$notification = New-Object System.Windows.Forms.NotifyIcon
$notification.Icon = [System.Drawing.SystemIcons]::Information
$notification.Visible = $true
$notification.BalloonTipTitle = 'YubiKey yearns touching'
$notification.BalloonTipText =
    'Git is requesting an OpenPGP signature. Please touchy touch the YubiKey while it flashes.'

$notification.ShowBalloonTip(10000)

Start-Sleep -Seconds 10
$notification.Dispose()