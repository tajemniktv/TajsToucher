[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Dogfood.Retention.ps1')
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $repo ('.codex\temp\retention-test-' + [Guid]::NewGuid().ToString('N'))
$successful = Join-Path $root 'retained\successful'
New-Item -ItemType Directory -Force -Path $successful | Out-Null
foreach ($name in @('current', 'previous', 'retained\failed', 'retained\migration', 'retained\legacy-unclassified')) {
    $path = Join-Path $root $name
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    [IO.File]::WriteAllText((Join-Path $path 'sentinel'), 'preserve')
}
$paths = @()
foreach ($index in 1..6) {
    $path = Join-Path $successful ([Guid]::NewGuid().ToString('N') + '-previous')
    New-Item -ItemType Directory -Path $path | Out-Null
    [IO.File]::WriteAllText((Join-Path $path 'payload'), 'generated')
    (Get-Item -LiteralPath $path).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-$index)
    $paths += $path
}
[IO.File]::WriteAllText((Join-Path $root 'transaction.json'), '{}')
Clear-DogfoodSuccessHistory $root
if (@(Get-ChildItem -LiteralPath $successful -Directory).Count -ne 6) { throw 'Cleanup ran during an unfinished deployment.' }
Remove-Item -LiteralPath (Join-Path $root 'transaction.json')
Clear-DogfoodSuccessHistory $root
foreach ($index in 0..5) {
    if ((Test-Path -LiteralPath $paths[$index]) -ne ($index -lt 3)) { throw 'Retention did not keep the newest three successful generations.' }
}
foreach ($name in @('current', 'previous', 'retained\failed', 'retained\migration', 'retained\legacy-unclassified')) {
    if (!(Test-Path -LiteralPath (Join-Path $root "$name\sentinel"))) { throw "Retention lost protected data: $name" }
}
$junction = Join-Path $successful ([Guid]::NewGuid().ToString('N') + '-previous')
New-Item -ItemType Junction -Path $junction -Target (Join-Path $root 'retained\migration') | Out-Null
try {
    $refused = $false
    try { Remove-DogfoodDirectory $junction $successful } catch { $refused = $true }
    if (!$refused -or !(Test-Path -LiteralPath (Join-Path $root 'retained\migration\sentinel'))) { throw 'Cleanup followed a junction.' }
} finally { [IO.Directory]::Delete($junction) } # Nonrecursive: remove the link only, never its target.
$refused = $false
try { Remove-DogfoodDirectory $successful $successful } catch { $refused = $true }
if (!$refused) { throw 'Cleanup accepted the container itself.' }
Write-Host 'PASS retention: latest three, recovery exclusions, journal guard, path containment, and reparse refusal.'
