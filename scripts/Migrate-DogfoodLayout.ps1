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
if (!(Test-Path -LiteralPath $oldExe)) { throw 'No legacy flat executable found; migration is not needed.' }
if ((Test-Path -LiteralPath $journal) -or (Test-Path -LiteralPath (Join-Path $oldStore 'transaction.json'))) { throw 'An unfinished migration/deployment requires manual recovery first.' }
if ((Test-Path -LiteralPath (Join-Path $root 'current')) -or (Test-Path -LiteralPath (Join-Path $root 'previous'))) { throw 'Destination already contains a deployment; refusing to merge installations.' }
$payload = @(Get-ChildItem -LiteralPath $root -Force)
foreach ($item in @($payload) + @(Get-ChildItem -LiteralPath $root -Force -Recurse) + @(Get-ChildItem -LiteralPath $oldStore -Force -Recurse -ErrorAction SilentlyContinue)) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing migration with reparse points.' }
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
        foreach ($p in $running) { $events += [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Stop.$($p.ProcessId)") }
        $wasRunning = $running.Count -gt 0
        foreach ($event in $events) { $null = $event.Set() }
        foreach ($p in $running) {
            $process = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
            if ($process -and !$process.WaitForExit(15000)) { throw 'Legacy tray did not exit; no process was killed.' }
            if ($process) { $process.Dispose() }
        }
    } finally { foreach ($event in $events) { $event.Dispose() } }
    @{ old = $oldExe; destination = $newExe; phase = 'deploying' } | ConvertTo-Json | Set-Content -LiteralPath $journal
    $args = @('-StagePath', $StagePath, '-AllowLegacyLayout')
    if ($TestRoot) { $args += @('-TestRoot', $parent) }
    if ($SimulateStartupFailure) { $args += '-SimulateStartupFailure' }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Dogfood.ps1') @args
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
        $programs = @(& git config --global --get-all gpg.openpgp.program)
        if ($LASTEXITCODE -ne 0 -or $programs.Count -ne 1 -or $programs[0] -ine $oldExe -or $before.WrapperPath -ine $oldExe) {
            throw 'Git/registry ownership changed; refusing to migrate integration.'
        }
        $before | Export-Clixml -LiteralPath (Join-Path $root 'retained\pre-layout-registry.xml')
        $process = Start-Process -FilePath $newExe -ArgumentList install -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw 'Git migration failed; legacy files are retained.' }
        $after = Get-ItemProperty HKCU:\Software\TajsToucher
        foreach ($property in $before.PSObject.Properties) {
            if ($property.Name -ne 'WrapperPath' -and [string]$after.($property.Name) -cne [string]$property.Value) { throw "Unexpected registry change: $($property.Name). Preserve the journal and registry snapshot." }
        }
        if ($after.WrapperPath -ine $newExe -or @(& git config --global --get-all gpg.openpgp.program).Count -ne 1 -or (& git config --global --get gpg.openpgp.program) -ine $newExe) { throw 'Git migration verification failed.' }
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
    if (!$deployed -and $wasRunning -and (Test-Path -LiteralPath $oldExe)) {
        Start-Process -FilePath $oldExe -ArgumentList app -WorkingDirectory $root -WindowStyle Hidden | Out-Null
    }
    if ($lease) { $lease.Dispose() }
    [Array]::Reverse($locks)
    foreach ($mutex in $locks) { $mutex.ReleaseMutex(); $mutex.Dispose() }
}
