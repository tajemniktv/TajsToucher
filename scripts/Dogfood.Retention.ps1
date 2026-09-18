# Only explicitly managed disposable generations are pruned. Recovery data is never inferred from age.
function Remove-DogfoodDirectory([string]$Path, [string]$Container) {
    $containerPath = [IO.Path]::GetFullPath($Container).TrimEnd('\') + '\'
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (!$target.StartsWith($containerPath, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetDirectoryName($target) -ine $containerPath.TrimEnd('\')) { throw 'Cleanup requires an immediate managed child directory.' }
    foreach ($entry in @(Get-Item -LiteralPath $Container -Force) + @(Get-Item -LiteralPath $target -Force) + @(Get-ChildItem -LiteralPath $target -Force -Recurse)) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Cleanup refuses reparse points.' }
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}

function Clear-DogfoodSuccessHistory([string]$StateRoot) {
    if ((Test-Path -LiteralPath (Join-Path $StateRoot 'transaction.json')) -or
        (Test-Path -LiteralPath (Join-Path $StateRoot 'layout-migration.json'))) { return }
    $container = Join-Path $StateRoot 'retained\successful'
    foreach ($path in @($StateRoot, (Join-Path $StateRoot 'retained'), $container)) {
        if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'History cleanup refuses reparse points.' }
    }
    $generations = @(Get-ChildItem -LiteralPath $container -Directory -Force |
        Where-Object { $_.Name -match '^[0-9a-f]{32}-previous$' } |
        Sort-Object LastWriteTimeUtc, Name -Descending)
    foreach ($generation in $generations | Select-Object -Skip 3) {
        Remove-DogfoodDirectory $generation.FullName $container
    }
}
