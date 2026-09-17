[CmdletBinding()]
param([Parameter(Mandatory)][string]$StagePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $repo ('.codex\temp\dogfood-test-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $repo ('.codex\temp\dogfood\test-stage-' + [Guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $StagePath -Destination $stage -Recurse
$installed = Join-Path $root 'TajsToucher\current'
$exe = Join-Path $installed 'TajsToucher.exe'
$state = Join-Path $root 'TajsToucher'
$counter = 0
function Invoke-Deployment([string[]]$Extra = @(), [bool]$ExpectFailure = $false, [string]$FailurePattern = '') {
    $script:counter++
    $log = Join-Path $root "run-$counter.log"
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue' # Windows PowerShell wraps native stderr as ErrorRecord.
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Dogfood.ps1') -StagePath $stage -TestRoot $root @Extra *> $log
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if (($code -eq 0) -eq $ExpectFailure) { throw "Unexpected deployment result $code : $(Get-Content -LiteralPath $log -Raw)" }
    if ($ExpectFailure -and $FailurePattern -and (Get-Content -LiteralPath $log -Raw) -notlike "*$FailurePattern*") { throw "Wrong failure: $(Get-Content -LiteralPath $log -Raw)" }
    Get-Content -LiteralPath $log | Write-Host
}
function Assert-Payload([string]$Expected) {
    if ((Get-Content -LiteralPath (Join-Path $installed 'payload-test.txt') -Raw).Trim() -ne $Expected) { throw 'Wrong installed payload.' }
    if (Test-Path -LiteralPath (Join-Path $state 'transaction.json')) { throw 'Transaction journal was not cleared.' }
}
function Wait-Tray {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'TajsToucher.exe'" | Where-Object ExecutablePath -EQ $exe)) {
            $event = $null
            try {
                $event = [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Ready.$($p.ProcessId)")
                if ($event.WaitOne(0)) { return $p.ProcessId }
            } catch [Threading.WaitHandleCannotBeOpenedException] { }
            finally { if ($event) { $event.Dispose() } }
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'No ready tray.'
}
try {
    Set-Content -LiteralPath (Join-Path $stage 'payload-test.txt') -Value 'v1'
    Invoke-Deployment
    Assert-Payload 'v1'
    $manifest = Get-Content -LiteralPath (Join-Path $installed 'build-identity.json') -Raw | ConvertFrom-Json
    if ($manifest.runtime -ne 'win-x64' -or $manifest.schemaVersion -ne 1 -or !$manifest.identity -or !$manifest.files.Count) { throw 'Missing build identity/payload hashes.' }
    $oldPid = Wait-Tray
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $identity = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($exe.ToUpperInvariant())))).Replace('-', '') }
    finally { $hash.Dispose() }
    $gate = [Threading.Mutex]::new($false, "Local\TajsToucher.Deploy.$identity")
    $null = $gate.WaitOne()
    try {
        $proxy = Start-Process -FilePath $exe -ArgumentList '--version' -WindowStyle Hidden -PassThru
        if ($proxy.WaitForExit(2000)) { throw 'Proxy did not wait behind the deployment gate.' }
    } finally { $gate.ReleaseMutex(); $gate.Dispose() }
    if (!$proxy.WaitForExit(15000) -or $proxy.ExitCode -ne 0) { throw 'Proxy did not resume successfully after deployment gate release.' }
    $proxy.Dispose()
    $activity = [IO.File]::Open((Join-Path $state '.TajsToucher-dogfood\activity.lock'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Invoke-Deployment -ExpectFailure $true -FailurePattern 'A GPG operation is active' } finally { $activity.Dispose() }
    if ((Wait-Tray) -ne $oldPid) { throw 'Active-operation rejection disturbed the tray.' }
    Assert-Payload 'v1'

    Set-Content -LiteralPath (Join-Path $stage 'payload-test.txt') -Value 'v2'
    Invoke-Deployment -Extra @('-SimulateStartupFailure') -ExpectFailure $true -FailurePattern 'Simulated startup failure'
    Assert-Payload 'v1'
    $null = Wait-Tray
    Invoke-Deployment -Extra @('-SimulateReadinessTimeout') -ExpectFailure $true -FailurePattern 'Simulated readiness timeout'
    Assert-Payload 'v1'
    $null = Wait-Tray
    Invoke-Deployment
    Assert-Payload 'v2'
    if ((Get-Content -LiteralPath (Join-Path $state 'previous\payload-test.txt') -Raw).Trim() -ne 'v1') { throw 'Previous generation was not retained.' }
    Invoke-Deployment -Extra @('-Rollback')
    Assert-Payload 'v1'
    $null = Wait-Tray
    Write-Host 'PASS: full payload, readiness, proxy gate/resume, active-operation refusal, failed-start restoration, restart, and explicit rollback.'
} finally {
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'TajsToucher.exe'" | Where-Object ExecutablePath -EQ $exe)) {
        $owned = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
        try {
            try {
                $signal = [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Stop.$($p.ProcessId)")
                try { $signal.Set() | Out-Null } finally { $signal.Dispose() }
            } catch { Write-Warning "Isolated tray could not be signaled: $_" }
            if ($owned -and !$owned.WaitForExit(15000)) { $owned.Kill(); $owned.WaitForExit() }
        } finally { if ($owned) { $owned.Dispose() } }
    }
}
