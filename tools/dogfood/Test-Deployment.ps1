[CmdletBinding()]
param([string]$StagePath)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$StagePath) {
    $StagePath = Join-Path $repo ('.codex/temp/dogfood/test-publish-' + [Guid]::NewGuid().ToString('N'))
    & dotnet publish (Join-Path $repo 'src/TajsToucher/TajsToucher.csproj') -c Release -p:PublishProfile=SingleFile -p:DogfoodEnabled=false -p:Dogfood=false -o $StagePath
    if ($LASTEXITCODE -ne 0) { throw 'Isolated test publish failed.' }
}
& (Join-Path $repo 'scripts/Test-Dogfood.ps1') -StagePath $StagePath
& (Join-Path $repo 'scripts/Test-DogfoodLayoutMigration.ps1') -StagePath $StagePath
