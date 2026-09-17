[CmdletBinding()]
param([Parameter(Mandatory)][string]$StagePath, [string]$TestRoot, [switch]$SimulateStartupFailure)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')
Import-Module (Join-Path $PSHOME 'Modules\CimCmdlets\CimCmdlets.psd1')
$repo = Split-Path $PSScriptRoot -Parent
$parent = Join-Path $env:LOCALAPPDATA 'Programs\TajemnikTV'
if ($TestRoot) {
    $parent = [IO.Path]::GetFullPath($TestRoot)
    if (!$parent.StartsWith((Join-Path $repo '.codex\temp\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'TestRoot must be inside repository .codex\temp.' }
}
if ($SimulateStartupFailure -and !$TestRoot) { throw 'Fault injection requires TestRoot.' }
$root = Join-Path $parent 'TajsToucher'
$oldExe = Join-Path $root 'TajsToucher.exe'
$newExe = Join-Path $root 'current\TajsToucher.exe'
$oldStore = Join-Path $parent '.TajsToucher-dogfood'
$journal = Join-Path $root 'layout-migration.json'
# Reject invalid input before stopping anything or writing recovery state.
$StagePath = [IO.Path]::GetFullPath($StagePath)
if (!$StagePath.StartsWith((Join-Path $repo '.codex\temp\dogfood\'), [StringComparison]::OrdinalIgnoreCase) -or
    !(Test-Path -LiteralPath (Join-Path $StagePath 'TajsToucher.exe') -PathType Leaf)) { throw 'A valid dogfood staging directory is required.' }
foreach ($tree in @($root, $oldStore, $StagePath)) {
    $entry = Get-Item -LiteralPath $tree -Force -ErrorAction Stop
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing migration with reparse points.' }
}
if (!(Test-Path -LiteralPath $oldExe)) { throw 'No legacy flat executable found; migration is not needed.' }
if ((Test-Path -LiteralPath $journal) -or (Test-Path -LiteralPath (Join-Path $oldStore 'transaction.json'))) { throw 'An unfinished migration/deployment requires manual recovery first.' }
if ((Test-Path -LiteralPath (Join-Path $root 'current')) -or (Test-Path -LiteralPath (Join-Path $root 'previous'))) { throw 'Destination already contains a deployment; refusing to merge installations.' }
$payload = @(Get-ChildItem -LiteralPath $root -Force)
foreach ($item in @($payload) + @(Get-ChildItem -LiteralPath $root -Force -Recurse) + @(Get-ChildItem -LiteralPath $oldStore -Force -Recurse) + @(Get-ChildItem -LiteralPath $StagePath -Force -Recurse)) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing migration with reparse points.' }
}
function Get-GitPrograms {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = 'git'
    $start.Arguments = 'config --global --get-all gpg.openpgp.program'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit(); throw 'Git configuration read timed out.' }
        if ($process.ExitCode -ne 0) { throw 'Git configuration is unreadable.' }
        return $output.Result.TrimEnd("`r", "`n") -split '\r?\n'
    } finally { $process.Dispose() }
}
function Get-Identity([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($Path.ToUpperInvariant())))).Replace('-', '') }
    finally { $hash.Dispose() }
}
$identity = Get-Identity $oldExe
$locks = @()
$lease = $null
$wasRunning = $false
$deployed = $false
try {
    foreach ($name in @("Local\TajsToucher.Workflow.$identity", "Local\TajsToucher.Deploy.$identity")) {
        $mutex = [Threading.Mutex]::new($false, $name)
        $owned = $false
        try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (!$owned) { $mutex.Dispose(); throw 'Another deployment or operation is starting; retry later.' }
        $locks += $mutex
    }
    try { $lease = [IO.File]::Open((Join-Path $oldStore 'activity.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw 'An active GPG operation or inaccessible legacy lease prevents migration. Finish the operation and retry; no tray was stopped.' }
    if (Get-Process -Name gpg,gpg2 -ErrorAction SilentlyContinue) { throw 'GPG is active; migration refused.' }
    $running = @(Get-CimInstance Win32_Process -Filter "Name='TajsToucher.exe'" | Where-Object ExecutablePath -EQ $oldExe)
    $events = @()
    try {
        foreach ($p in $running) {
            try { $events += [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Stop.$($p.ProcessId)") }
            catch [Threading.WaitHandleCannotBeOpenedException] { throw 'Legacy tray lacks graceful update support. Exit it yourself, then rerun migration; no process was killed.' }
        }
        $wasRunning = $running.Count -gt 0
        foreach ($signal in $events) { $null = $signal.Set() }
        foreach ($p in $running) {
            $process = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
            if ($process) {
                try { if (!$process.WaitForExit(15000)) { throw 'Legacy tray did not exit; no process was killed.' } }
                finally { $process.Dispose() }
            }
        }
    } finally { foreach ($signal in $events) { $signal.Dispose() } }
    @{ old = $oldExe; destination = $newExe; phase = 'deploying' } | ConvertTo-Json | Set-Content -LiteralPath $journal
    $deployArguments = @('-StagePath', $StagePath, '-AllowLegacyLayout')
    if ($TestRoot) { $deployArguments += @('-TestRoot', $parent) }
    if ($SimulateStartupFailure) { $deployArguments += '-SimulateStartupFailure' }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Dogfood.ps1') @deployArguments
    if ($LASTEXITCODE -ne 0) { throw 'New-layout deployment failed; old executable and integration are retained.' }
    if (!(Test-Path -LiteralPath $newExe)) { throw 'Deployment was skipped; the new executable is absent.' }
    $deployed = $true
    # Normal builds were refused while the legacy executable existed. Serialize the remaining migration too.
    $newWorkflow = [Threading.Mutex]::new($false, ('Local\TajsToucher.Workflow.' + (Get-Identity $newExe)))
    try { $owned = $newWorkflow.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (!$owned) { $newWorkflow.Dispose(); throw 'New installation is busy; preserve the migration journal for review.' }
    $locks += $newWorkflow
    if (!$TestRoot) {
        $before = Get-ItemProperty HKCU:\Software\TajsToucher | Select-Object * -ExcludeProperty PS*
        $programs = @(Get-GitPrograms)
        if ($programs.Count -ne 1 -or $programs[0] -ine $oldExe -or $before.WrapperPath -ine $oldExe) {
            throw 'Git/registry ownership changed; refusing to migrate integration.'
        }
        $before | Export-Clixml -LiteralPath (Join-Path $root 'retained\pre-layout-registry.xml')
        $process = Start-Process -FilePath $newExe -ArgumentList install -WindowStyle Hidden -PassThru
        try {
            if (!$process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit(); throw 'Git migration timed out; installer stopped and legacy files retained.' }
            if ($process.ExitCode -ne 0) { throw 'Git migration failed; legacy files are retained.' }
        } finally { $process.Dispose() }
        $after = Get-ItemProperty HKCU:\Software\TajsToucher
        foreach ($property in $before.PSObject.Properties) {
            if ($property.Name -ne 'WrapperPath' -and [string]$after.($property.Name) -cne [string]$property.Value) { throw "Unexpected registry change: $($property.Name). Preserve the journal and registry snapshot." }
        }
        $programs = @(Get-GitPrograms)
        if ($after.WrapperPath -ine $newExe -or $programs.Count -ne 1 -or $programs[0] -ine $newExe) { throw 'Git migration verification failed.' }
    }
    @{ old = $oldExe; destination = $newExe; phase = 'integration-migrated-preserving-legacy' } | ConvertTo-Json | Set-Content -LiteralPath $journal
    $previous = Join-Path $root 'previous'
    New-Item -ItemType Directory -Path $previous | Out-Null
    foreach ($item in $payload) {
        # Only the original flat payload, captured before creating current/retained, is moved.
        if ((Split-Path $item.FullName -Parent) -ine $root) { throw 'Invalid legacy payload source.' }
        Move-Item -LiteralPath $item.FullName -Destination (Join-Path $previous $item.Name)
    }
    $lease.Dispose(); $lease = $null
    $archive = Join-Path $root 'retained\legacy-dogfood'
    if (Test-Path -LiteralPath $archive) { throw 'Legacy archive already exists.' }
    Move-Item -LiteralPath $oldStore -Destination $archive
    Remove-Item -LiteralPath $journal
    Write-Host "Layout migration complete: $newExe. Flat build is previous; original backups are retained/legacy-dogfood."
} finally {
    $stillRunning = @(Get-CimInstance Win32_Process -Filter "Name='TajsToucher.exe'" | Where-Object ExecutablePath -EQ $oldExe)
    if (!$deployed -and $wasRunning -and !$stillRunning.Count -and (Test-Path -LiteralPath $oldExe)) {
        Start-Process -FilePath $oldExe -ArgumentList app -WorkingDirectory $root -WindowStyle Hidden | Out-Null
    }
    if ($lease) { $lease.Dispose() }
    [Array]::Reverse($locks)
    foreach ($mutex in $locks) { $mutex.ReleaseMutex(); $mutex.Dispose() }
}
