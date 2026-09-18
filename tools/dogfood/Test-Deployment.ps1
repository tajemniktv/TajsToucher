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
& dotnet build (Join-Path $repo 'tests/TajsToucher.Tests/TajsToucher.Tests.csproj') -c Release -p:DogfoodEnabled=false --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Isolated integration test host build failed.' }
$testInstaller = Join-Path $repo 'artifacts/bin/TajsToucher.Tests/release/TajsToucher.Tests.exe'
foreach ($test in @('Test-Dogfood.ps1', 'Test-DogfoodLayoutMigration.ps1', 'Test-DogfoodRetention.ps1')) {
    try {
        $parameters = @{ StagePath = $StagePath }
        if ($test -eq 'Test-DogfoodRetention.ps1') { $parameters = @{} }
        if ($test -eq 'Test-DogfoodLayoutMigration.ps1') { $parameters.TestInstallerPath = $testInstaller }
        & (Join-Path $repo "scripts/$test") @parameters
    }
    catch {
        # Aggregation must retain the original assertion location, not just this runner's final throw.
        $failures += "${test}: $_`n$($_.InvocationInfo.PositionMessage)`n$($_.ScriptStackTrace)"
        Write-Warning $failures[-1]
    }
}
if ($failures.Count) { throw ($failures -join "`n") }
