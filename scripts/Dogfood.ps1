[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$StagePath,
    [switch]$Rollback,
    [string]$TestRoot,
    [switch]$SimulateStartupFailure,
    [switch]$AllowLegacyLayout
)
$ErrorActionPreference = 'Stop'
foreach ($name in @('CI', 'GITHUB_ACTIONS', 'TF_BUILD', 'BUILD_BUILDID', 'JENKINS_URL', 'TEAMCITY_VERSION')) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($value -and $value -notin @('false', '0')) { Write-Host "Skipping dogfood in CI ($name)."; return }
}
if ($env:DogfoodEnabled -eq 'false' -or $env:TAJSTOUCHER_DOGFOOD -eq 'false' -or $env:Dogfood -eq 'false') {
    Write-Host 'Dogfooding is disabled by the environment.'; return
}
# MSBuild may inherit a PowerShell 7 module path while invoking Windows PowerShell.
# Resolve the native modules from this host rather than relying on module auto-discovery.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')
Import-Module (Join-Path $PSHOME 'Modules\CimCmdlets\CimCmdlets.psd1')
$repo = Split-Path $PSScriptRoot -Parent
$work = Join-Path $repo '.codex\temp\dogfood'
$install = Join-Path $env:LOCALAPPDATA 'Programs\TajemnikTV\TajsToucher'
if ($TestRoot) {
    $TestRoot = [IO.Path]::GetFullPath($TestRoot)
    if (!$TestRoot.StartsWith((Join-Path $repo '.codex\temp\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'TestRoot must be inside the repository .codex\temp directory.' }
    $install = Join-Path $TestRoot 'TajsToucher'
}
if ($SimulateStartupFailure -and !$TestRoot) { throw 'Fault injection requires an isolated TestRoot.' }
$stateRoot = $install
$install = Join-Path $stateRoot 'current'
$exe = Join-Path $install 'TajsToucher.exe'
$journal = Join-Path $stateRoot 'transaction.json'
$previous = Join-Path $stateRoot 'previous'
$retained = Join-Path $stateRoot 'retained'
New-Item -ItemType Directory -Force -Path $work, $retained | Out-Null
if ((Test-Path -LiteralPath (Join-Path $stateRoot 'TajsToucher.exe')) -and !$AllowLegacyLayout) {
    throw 'Legacy flat installation detected. Run scripts/Migrate-DogfoodLayout.ps1 before ordinary builds.'
}
if ((Test-Path -LiteralPath (Join-Path $stateRoot 'layout-migration.json')) -and !$AllowLegacyLayout) {
    throw 'An unfinished layout migration requires review before deployment.'
}
# Keep the lease's relative location compatible with older binaries when rolling back.
$coordination = Join-Path $stateRoot '.TajsToucher-dogfood'
New-Item -ItemType Directory -Force -Path $coordination | Out-Null

function Move-ManagedDirectory([string]$Source, [string]$Destination) {
    foreach ($path in @($Source, $Destination)) {
        if (![IO.Path]::GetFullPath($path).StartsWith($stateRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing a directory move outside the managed app root.'
        }
    }
    if (Test-Path -LiteralPath $Destination) { throw "Move destination already exists: $Destination" }
    foreach ($entry in @(Get-Item -LiteralPath $Source) + @(Get-ChildItem -LiteralPath $Source -Recurse -Force)) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to move an installation containing reparse points.' }
    }
    for ($attempt = 0; ; $attempt++) {
        try { Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop; break }
        catch [IO.IOException] {
            # Windows may retain an image handle briefly after graceful process exit.
            if ($attempt -ge 9 -or ($_.Exception.HResult -band 0xffff) -notin @(32, 33)) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
}
function Get-PayloadHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
function Assert-PlainTree([string]$Path) {
    foreach ($entry in @(Get-Item -LiteralPath $Path -Force) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force)) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing reparse points in $Path" }
    }
}
function Get-AppProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name = 'TajsToucher.exe'" | Where-Object {
        $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -ieq $exe
    })
}
function Stop-Trays {
    $running = Get-AppProcesses
    # Inspect every process before sending any shutdown request. Unknown/legacy processes are never killed.
    $events = @()
    try {
        foreach ($process in $running) {
            try { $events += [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Stop.$($process.ProcessId)") }
            catch { throw "PID $($process.ProcessId) is busy or predates graceful updates. Exit the tray app and finish GPG operations, then rebuild." }
        }
        foreach ($event in $events) { $event.Set() | Out-Null }
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while ((Get-AppProcesses).Count) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Tray shutdown timed out; no process was killed.' }
            Start-Sleep -Milliseconds 200
        }
    } finally { foreach ($event in $events) { $event.Dispose() } }
}
function Start-Tray {
    $process = Start-Process -FilePath $exe -ArgumentList 'app' -WorkingDirectory $install -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "Tray exited before readiness (exit $($process.ExitCode))." }
        $ready = $null
        try {
            $ready = [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Ready.$($process.Id)")
            if ($ready.WaitOne(0)) {
                if ($process.WaitForExit(3000)) { throw 'Tray exited during the startup smoke interval.' }
                if (!$ready.WaitOne(0)) { throw 'Tray withdrew readiness during the startup smoke interval.' }
                return
            }
        } catch [Threading.WaitHandleCannotBeOpenedException] { }
        finally { if ($ready) { $ready.Dispose() } }
        Start-Sleep -Milliseconds 200
    }
    throw 'New tray did not report readiness within 60 seconds.'
}
function Set-Launcher {
    if ($TestRoot) { return }
    $shell = New-Object -ComObject WScript.Shell
    $path = Join-Path ([Environment]::GetFolderPath('Programs')) 'TajsToucher.lnk'
    $shortcut = $shell.CreateShortcut($path)
    if ((Test-Path -LiteralPath $path) -and $shortcut.TargetPath -ine $exe -and
        $shortcut.TargetPath -ine (Join-Path $stateRoot 'TajsToucher.exe')) {
        throw 'The existing TajsToucher shortcut targets another installation; it was not overwritten.'
    }
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $install
    $shortcut.Save()
}

# Serialize complete workflows, including staging, across simultaneous IDE/CLI builds.
$hash = [Security.Cryptography.SHA256]::Create()
try { $identity = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($exe.ToUpperInvariant())))).Replace('-', '') }
finally { $hash.Dispose() }
$workflow = [Threading.Mutex]::new($false, "Local\TajsToucher.Workflow.$identity")
$ownsWorkflow = $false
$gate = $null
$ownsGate = $false
$lease = $null
$installationLock = $null
try {
    try { $ownsWorkflow = $workflow.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsWorkflow = $true }
    if (!$ownsWorkflow) { throw 'Another dogfood deployment is running. Rebuild when it finishes.' }
    Assert-PlainTree $stateRoot
    try { $installationLock = [IO.File]::Open((Join-Path $stateRoot 'deploy.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another deployment owns the installation lock. Retry after it finishes.' }
    if (Test-Path -LiteralPath $journal) {
        throw "An interrupted deployment requires review: $journal. Preserve current, previous, retained, and failed candidates; restore the desired build as current before clearing the journal."
    }
    if ($Rollback) {
        if (!(Test-Path -LiteralPath $previous)) { throw 'No previous build is available.' }
        $source = $previous
        $StagePath = Join-Path $work ([Guid]::NewGuid().ToString('N') + '-rollback')
        Copy-Item -LiteralPath $source -Destination $StagePath -Recurse
    } elseif (!$StagePath) {
        $StagePath = Join-Path $work ([Guid]::NewGuid().ToString('N') + '-stage')
        & dotnet publish (Join-Path $repo 'src\TajsToucher\TajsToucher.csproj') -c $Configuration -p:PublishProfile=SingleFile -p:DogfoodEnabled=false -p:Dogfood=false -o $StagePath
        if ($LASTEXITCODE -ne 0) { throw 'Staging publish failed; the installed app was not touched.' }
    }
    $StagePath = [IO.Path]::GetFullPath($StagePath)
    if (!$StagePath.StartsWith($work + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging must be under the repository .codex\temp\dogfood directory.' }
    Assert-PlainTree $StagePath
    $stagedExe = Join-Path $StagePath 'TajsToucher.exe'
    if (!(Test-Path -LiteralPath $stagedExe)) { throw 'Staged executable is missing.' }
    $probe = Start-Process -FilePath $stagedExe -ArgumentList 'version' -WindowStyle Hidden -PassThru
    if (!$probe.WaitForExit(60000) -or $probe.ExitCode -ne 0) { throw 'Staged executable failed its launch probe; installation was not touched.' }
    $probe.Dispose()
    $expectedHash = Get-PayloadHash $stagedExe
    # Durable candidate/rollback store shares the installed volume, even when the repo is on another drive.
    $candidate = Join-Path $stateRoot 'pending'
    if (Test-Path -LiteralPath $candidate) {
        Move-ManagedDirectory $candidate (Join-Path $retained ([Guid]::NewGuid().ToString('N') + '-unused'))
    }
    Copy-Item -LiteralPath $StagePath -Destination $candidate -Recurse
    foreach ($file in Get-ChildItem -LiteralPath $StagePath -File -Recurse) {
        $relative = $file.FullName.Substring($StagePath.TrimEnd('\').Length).TrimStart('\')
        if ((Get-PayloadHash $file.FullName) -ne (Get-PayloadHash (Join-Path $candidate $relative))) {
            throw "Candidate payload mismatch: $relative"
        }
    }
    if (!$Rollback) {
        $files = @(Get-ChildItem -LiteralPath $candidate -File -Recurse | Where-Object Name -NE 'build-identity.json' | ForEach-Object {
            @{ path = $_.FullName.Substring($candidate.Length + 1); sha256 = Get-PayloadHash $_.FullName }
        })
        @{ schemaVersion = 1; appName = 'TajsToucher'; identity = (Get-Item -LiteralPath $stagedExe).VersionInfo.ProductVersion; configuration = $Configuration;
           runtime = 'win-x64'; builtAtUtc = [DateTime]::UtcNow.ToString('o'); files = $files } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $candidate 'build-identity.json') -Encoding UTF8
    }
    $gate = [Threading.Mutex]::new($false, "Local\TajsToucher.Deploy.$identity")
    try { $ownsGate = $gate.WaitOne(10000) } catch [Threading.AbandonedMutexException] { $ownsGate = $true }
    if (!$ownsGate) { throw 'Could not acquire the deployment gate.' }
    $leasePath = Join-Path $coordination 'activity.lock'
    try { $lease = [IO.File]::Open($leasePath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw 'A GPG operation is active; deployment skipped without stopping it. Rebuild after it finishes.' }
    if (Get-Process -Name gpg,gpg2 -ErrorAction SilentlyContinue) { throw 'GPG is running; deployment skipped. Rebuild after it finishes.' }
    $backup = Join-Path $retained ([Guid]::NewGuid().ToString('N') + '-replaced')
    $hadInstall = Test-Path -LiteralPath $install
    $retiredPrevious = Join-Path $retained ([Guid]::NewGuid().ToString('N') + '-previous')
    $promotedPrevious = $false
    $swapped = $false
    $stopped = $false
    try {
        Stop-Trays
        $stopped = $true
        @{ schemaVersion = 1; rollback = [bool]$Rollback; hadCurrent = $hadInstall; backup = $backup;
           retiredPrevious = $retiredPrevious; previous = $previous; pending = $candidate;
           stage = $StagePath; installed = $install } | ConvertTo-Json | Set-Content -LiteralPath $journal
        if ($hadInstall) { Move-ManagedDirectory $install $backup }
        Move-ManagedDirectory $candidate $install
        $swapped = $true
        if ((Get-PayloadHash $exe) -ne $expectedHash) { throw 'Installed executable hash mismatch.' }
        if ($SimulateStartupFailure) { throw 'Simulated startup failure.' }
        Start-Tray
        Set-Launcher
        if ($hadInstall) {
            if (Test-Path -LiteralPath $previous) { Move-ManagedDirectory $previous $retiredPrevious }
            Move-ManagedDirectory $backup $previous
            $promotedPrevious = $true
        }
        Remove-Item -LiteralPath $journal
        Write-Host "Dogfood ready: $exe (SHA256 $expectedHash)"
    } catch {
        $failure = $_
        if ($swapped) {
            Stop-Trays
            Move-ManagedDirectory $install (Join-Path $retained ([Guid]::NewGuid().ToString('N') + '-failed'))
        }
        if ($promotedPrevious) { Move-ManagedDirectory $previous $install }
        elseif (Test-Path -LiteralPath $backup) { Move-ManagedDirectory $backup $install }
        if (Test-Path -LiteralPath $retiredPrevious) { Move-ManagedDirectory $retiredPrevious $previous }
        if ($stopped -and $hadInstall -and (Test-Path -LiteralPath $exe)) {
            # Legacy rollback builds do not have readiness events, but must still be restarted.
            Start-Process -FilePath $exe -ArgumentList 'app' -WorkingDirectory $install -WindowStyle Hidden | Out-Null
        }
        if (Test-Path -LiteralPath $journal) { Remove-Item -LiteralPath $journal }
        throw $failure
    }
} finally {
    if ($lease) { $lease.Dispose() }
    if ($installationLock) { $installationLock.Dispose() }
    if ($ownsGate) { $gate.ReleaseMutex() }
    if ($gate) { $gate.Dispose() }
    if ($ownsWorkflow) { $workflow.ReleaseMutex() }
    $workflow.Dispose()
}
