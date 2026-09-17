[CmdletBinding()]
param([string]$StagePath)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$StagePath) {
    $StagePath = Join-Path $repo ('.codex/temp/dogfood/test-publish-' + [Guid]::NewGuid().ToString('N'))
    & dotnet publish (Join-Path $repo 'src/TajsToucher/TajsToucher.csproj') -c Release -p:PublishProfile=SingleFile -p:DogfoodEnabled=false -p:Dogfood=false -o $StagePath
    if ($LASTEXITCODE -ne 0) { throw 'Isolated test publish failed.' }
}
$failures = @()
foreach ($test in @('Test-Dogfood.ps1', 'Test-DogfoodLayoutMigration.ps1')) {
    try { & (Join-Path $repo "scripts/$test") -StagePath $StagePath }
    catch { $failures += "${test}: $_"; Write-Warning $failures[-1] }
}
if ($failures.Count) { throw ($failures -join "`n") }
