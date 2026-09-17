# Common desktop-app entry point. The original script remains a supported alias.
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$StagePath,
    [switch]$Rollback,
    [string]$TestRoot,
    [switch]$SimulateStartupFailure
)
& (Join-Path $PSScriptRoot '../../scripts/Dogfood.ps1') @PSBoundParameters
