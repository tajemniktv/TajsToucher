param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (-not ('TouchWindowProbeNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TouchWindowProbeNative {
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam);
}
'@
}

# Synthetic named events only: no GPG, card queries, PINs, or signatures.
foreach ($mode in 'AlreadyEnded', 'WaitEnded', 'Dismiss', 'Expiry') {
    $name = 'Local\TajsToucher.TouchWait.' + [Guid]::NewGuid().ToString('N')
    $ended = [Threading.EventWaitHandle]::new(($mode -eq 'AlreadyEnded'), [Threading.EventResetMode]::ManualReset, $name)
    $process = $null
    try {
        $start = [Diagnostics.ProcessStartInfo]::new($Executable)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.EnvironmentVariables['TAJSTOUCHER_PRIVATE_HELPER'] = 'touch-wait-v1'
        $start.Arguments = '--touch-wait ' + $name # Generated name contains no spaces or quotes.
        $process = [Diagnostics.Process]::Start($start)
        if ($mode -ne 'AlreadyEnded') {
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            $ready = $false
            do {
                Start-Sleep -Milliseconds 100
                $process.Refresh()
                if (!$process.HasExited -and $process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -like 'TajsToucher*signing') {
                    $style = [TouchWindowProbeNative]::GetWindowLongPtr($process.MainWindowHandle, -20).ToInt64()
                    $ready = ($style -band 8) -ne 0
                }
            } while (!$process.HasExited -and !$ready -and [DateTime]::UtcNow -lt $deadline)
            if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw "$mode did not create a visible window (check the opt-in setting)." }
            $style = [TouchWindowProbeNative]::GetWindowLongPtr($process.MainWindowHandle, -20).ToInt64()
            if (!$ready -or ($style -band 8) -eq 0) { throw "$mode window was not topmost (title='$($process.MainWindowTitle)', style=$style, ready=$ready)." }
            if ($mode -eq 'WaitEnded') {
                Start-Sleep -Seconds 2
                [void]$ended.Set()
            } elseif ($mode -eq 'Dismiss') {
                [void][TouchWindowProbeNative]::PostMessageW($process.MainWindowHandle, 0x10, [UIntPtr]::Zero, [IntPtr]::Zero)
            }
        }
        $timeout = if ($mode -eq 'Expiry') { 35000 } else { 5000 }
        if (!$process.WaitForExit($timeout)) { throw "$mode helper did not exit within its bound." }
        if ($process.ExitCode -ne 0) { throw "$mode helper failed: $($process.ExitCode)" }
        if ($mode -eq 'Dismiss' -and $ended.WaitOne(0)) { throw 'Dismissing the window changed the operation-owned event.' }
        "$mode passed"
    } finally {
        [void]$ended.Set()
        if ($null -ne $process) {
            # Only this test's own child, never the resident app or GPG.
            if (!$process.HasExited -and !$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
        $ended.Dispose()
    }
}
