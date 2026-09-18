[CmdletBinding()]
param([Parameter(Mandatory)][string]$StagePath, [Parameter(Mandatory)][string]$TestInstallerPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$base = Join-Path $repo ('.codex\temp\dogfood-migration-' + [Guid]::NewGuid().ToString('N'))
foreach ($case in @('active-operation', 'invalid-stage', 'startup-failure', 'ownership-mismatch', 'success')) {
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
    $registryName = 'Software\TajsToucher.Tests\Migration\' + [Guid]::NewGuid().ToString('N')
    $registry = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryName)
    try {
        $registry.SetValue('WrapperPath', $legacyExe)
        $registry.SetValue('RealGpgPath', (Get-Command gpg.exe -ErrorAction Stop).Source)
        $registry.SetValue('PreviousProgramCount', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
        $registry.SetValue('PreviousProgram0', 'prior wrapper ' + [char]0x0142 + '.exe')
        $registry.SetValue('PreviousProgram1', '')
        $registry.SetValue('NotificationTitle', 'keep my notification')
    } finally { $registry.Dispose() }
    $gitConfig = Join-Path $parent 'gitconfig'
    & git config --file $gitConfig gpg.openpgp.program $legacyExe
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated Git config.' }
    if ($case -eq 'ownership-mismatch') { & git config --file $gitConfig gpg.openpgp.program 'another-owner.exe' }
    # A refused ordinary build must not contaminate the flat migration payload.
    $preference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Dogfood.ps1') -StagePath $StagePath -TestRoot $parent *> (Join-Path $parent 'refusal.log')
        $refusalCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $preference }
    if ($refusalCode -eq 0 -or (Test-Path -LiteralPath (Join-Path $root 'retained'))) { throw 'Legacy refusal mutated the installation.' }
    $extra = @()
    $activity = $null
    if ($case -eq 'active-operation') {
        $activity = [IO.File]::Open((Join-Path $oldStore 'activity.lock'), 'Open', 'Read', 'Read')
    }
    if ($case -eq 'startup-failure') { $extra += '-SimulateStartupFailure' }
    $inputStage = $StagePath
    if ($case -eq 'invalid-stage') { $inputStage = Join-Path $StagePath 'missing' }
    $preference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Migrate-DogfoodLayout.ps1') -StagePath $inputStage -TestRoot $parent -TestRegistryPath $registryName -TestInstallerPath $TestInstallerPath @extra *> (Join-Path $parent 'migration.log')
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $preference; if ($activity) { $activity.Dispose() } }
    try {
        if ($case -eq 'active-operation') {
            if ($code -eq 0 -or (Get-Content -LiteralPath (Join-Path $parent 'migration.log') -Raw) -notlike '*legacy lease prevents migration*') { throw 'Active legacy operation was not refused.' }
            if ((Get-FileHash -LiteralPath $legacyExe).Hash -ne $before -or (Test-Path -LiteralPath (Join-Path $root 'current'))) { throw 'Active legacy operation was disturbed.' }
        } elseif ($case -eq 'invalid-stage') {
            if ($code -eq 0 -or (Test-Path -LiteralPath (Join-Path $root 'layout-migration.json'))) { throw 'Invalid stage mutated migration state.' }
        } elseif ($case -eq 'startup-failure') {
            if ((Get-Content -LiteralPath (Join-Path $parent 'migration.log') -Raw) -notlike '*Simulated startup failure*') { throw 'Expected startup fault was not reached.' }
            if ($code -eq 0 -or !(Test-Path -LiteralPath $legacyExe) -or (Get-FileHash -LiteralPath $legacyExe).Hash -ne $before) { throw 'Failed migration lost the legacy app.' }
            if (!(Test-Path -LiteralPath (Join-Path $oldStore "$backupName\sentinel.txt"))) { throw 'Failed migration lost backup state.' }
            if (!(Test-Path -LiteralPath (Join-Path $root 'layout-migration.json'))) { throw 'Failed migration lost recovery journal.' }
        } elseif ($case -eq 'ownership-mismatch') {
            if ($code -eq 0 -or (Get-Content -LiteralPath (Join-Path $parent 'migration.log') -Raw) -notlike '*ownership changed*') { throw 'Integration ownership mismatch was not refused.' }
            if ((& git config --file $gitConfig --get gpg.openpgp.program) -ne 'another-owner.exe') { throw 'Foreign Git ownership was overwritten.' }
        } else {
            if ($code -ne 0) { throw (Get-Content -LiteralPath (Join-Path $parent 'migration.log') -Raw) }
            if ((Test-Path -LiteralPath $legacyExe) -or (Test-Path -LiteralPath $oldStore)) { throw 'Legacy layout was not consolidated.' }
            if ((Get-FileHash -LiteralPath (Join-Path $root 'previous\TajsToucher.exe')).Hash -ne $before) { throw 'Previous binary was not preserved exactly.' }
            if ((Get-Content -LiteralPath (Join-Path $root "retained\migration\legacy-dogfood\$backupName\sentinel.txt") -Raw).Trim() -ne 'preserve backup') { throw 'Legacy backup was not preserved.' }
            if (!(Test-Path -LiteralPath (Join-Path $root 'previous\.preserve-migration'))) { throw 'Legacy rollback payload lacks permanent-retention marker.' }
            if (Test-Path -LiteralPath (Join-Path $root 'layout-migration.json')) { throw 'Completed migration retained its journal.' }
        }
        $registry = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryName)
        try {
            $expectedWrapper = if ($case -eq 'success') { Join-Path $root 'current\TajsToucher.exe' } else { $legacyExe }
            if ($registry.GetValue('WrapperPath') -cne $expectedWrapper -or
                $registry.GetValue('PreviousProgramCount') -ne 2 -or
                $registry.GetValue('PreviousProgram0') -cne ('prior wrapper ' + [char]0x0142 + '.exe') -or
                $registry.GetValue('PreviousProgram1') -cne '' -or
                $registry.GetValue('NotificationTitle') -cne 'keep my notification') { throw 'Registry handoff lost ownership, backup, or notification state.' }
        } finally { $registry.Dispose() }
        if ($case -eq 'success' -and (& git config --file $gitConfig --get gpg.openpgp.program) -cne $expectedWrapper) { throw 'Git handoff did not point to current.' }
        Write-Host "PASS layout migration $case"
    } finally {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registryName, $false)
        foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='TajsToucher.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) })) {
            $owned = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
            try {
                try {
                    $signal = [Threading.EventWaitHandle]::OpenExisting("Local\TajsToucher.Dogfood.Stop.$($process.ProcessId)")
                    try { $null = $signal.Set() } finally { $signal.Dispose() }
                } catch { Write-Warning "Isolated tray could not be signaled: $_" }
                if ($owned -and !$owned.WaitForExit(15000)) { $owned.Kill(); $owned.WaitForExit() }
            } finally { if ($owned) { $owned.Dispose() } }
        }
    }
}
