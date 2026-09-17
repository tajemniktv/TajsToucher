[CmdletBinding()]
param([Parameter(Mandatory)][string]$StagePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$base = Join-Path $repo ('.codex\temp\dogfood-migration-' + [Guid]::NewGuid().ToString('N'))
foreach ($case in @('active-operation', 'startup-failure', 'success')) {
    $parent = Join-Path $base $case
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $root = Join-Path $parent 'TajsToucher'
    Copy-Item -LiteralPath $StagePath -Destination $root -Recurse
    $legacyExe = Join-Path $root 'TajsToucher.exe'
    $oldStore = Join-Path $parent '.TajsToucher-dogfood'
    $backupName = [Guid]::NewGuid().ToString('N') + '-backup'
    New-Item -ItemType Directory -Path (Join-Path $oldStore $backupName) -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $oldStore "$backupName\sentinel.txt") -Value 'preserve backup'
    Set-Content -LiteralPath (Join-Path $oldStore 'previous.txt') -Value (Join-Path $oldStore $backupName)
    [IO.File]::WriteAllText((Join-Path $oldStore 'activity.lock'), '')
    $before = (Get-FileHash -LiteralPath $legacyExe).Hash
    $extra = @()
    $activity = $null
    if ($case -eq 'active-operation') {
        $activity = [IO.File]::Open((Join-Path $oldStore 'activity.lock'), 'Open', 'Read', 'Read')
    }
    if ($case -eq 'startup-failure') { $extra += '-SimulateStartupFailure' }
    $preference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Migrate-DogfoodLayout.ps1') -StagePath $StagePath -TestRoot $parent @extra *> (Join-Path $parent 'migration.log')
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $preference; if ($activity) { $activity.Dispose() } }
    try {
        if ($case -eq 'active-operation') {
            if ($code -eq 0 -or (Get-Content -LiteralPath (Join-Path $parent 'migration.log') -Raw) -notlike '*legacy lease prevents migration*') { throw 'Active legacy operation was not refused.' }
            if ((Get-FileHash -LiteralPath $legacyExe).Hash -ne $before -or (Test-Path -LiteralPath (Join-Path $root 'current'))) { throw 'Active legacy operation was disturbed.' }
        } elseif ($case -eq 'startup-failure') {
            if ((Get-Content -LiteralPath (Join-Path $parent 'migration.log') -Raw) -notlike '*Simulated startup failure*') { throw 'Expected startup fault was not reached.' }
            if ($code -eq 0 -or !(Test-Path -LiteralPath $legacyExe) -or (Get-FileHash -LiteralPath $legacyExe).Hash -ne $before) { throw 'Failed migration lost the legacy app.' }
            if (!(Test-Path -LiteralPath (Join-Path $oldStore "$backupName\sentinel.txt"))) { throw 'Failed migration lost backup state.' }
            if (!(Test-Path -LiteralPath (Join-Path $root 'layout-migration.json'))) { throw 'Failed migration lost recovery journal.' }
        } else {
            if ($code -ne 0) { throw (Get-Content -LiteralPath (Join-Path $parent 'migration.log') -Raw) }
            if ((Test-Path -LiteralPath $legacyExe) -or (Test-Path -LiteralPath $oldStore)) { throw 'Legacy layout was not consolidated.' }
            if ((Get-FileHash -LiteralPath (Join-Path $root 'previous\TajsToucher.exe')).Hash -ne $before) { throw 'Previous binary was not preserved exactly.' }
            if ((Get-Content -LiteralPath (Join-Path $root "retained\legacy-dogfood\$backupName\sentinel.txt") -Raw).Trim() -ne 'preserve backup') { throw 'Legacy backup was not preserved.' }
            if (Test-Path -LiteralPath (Join-Path $root 'layout-migration.json')) { throw 'Completed migration retained its journal.' }
        }
        Write-Host "PASS layout migration $case"
    } finally {
        foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='TajsToucher.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) })) {
            $signal = [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Stop.$($process.ProcessId)")
            try { $null = $signal.Set() } finally { $signal.Dispose() }
        }
    }
}
