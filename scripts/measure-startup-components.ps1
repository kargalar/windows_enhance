param(
    [ValidateRange(1, 100)][int]$Iterations = 30,
    [string]$Executable,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Executable) { $Executable = Join-Path $root 'artifacts/optimization-build/bin/WinForge.RegressionTests/release/WinForge.RegressionTests.exe' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/performance/startup-components-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = @()
for ($index = 1; $index -le $Iterations; $index++) {
    $path = Join-Path $OutputDirectory ('sample-{0:D3}.json' -f $index)
    $tracePath = Join-Path $OutputDirectory ('sample-{0:D3}.jsonl' -f $index)
    if ((Test-Path -LiteralPath $path) -or (Test-Path -LiteralPath $tracePath)) { throw "Refusing to replace existing sample $index." }
    $oldTrace = $env:WINFORGE_PERF_PATH
    try {
        $env:WINFORGE_PERF_PATH = $tracePath
        & $Executable --startup-probe $path
        if ($LASTEXITCODE -ne 0) { throw "Startup probe $index failed with exit $LASTEXITCODE." }
    }
    finally { $env:WINFORGE_PERF_PATH = $oldTrace }
    $sample = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $trace = @(Get-Content -LiteralPath $tracePath -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
    if (@($trace | Where-Object Operation -eq 'modules.load').Count -ne 1) { throw "Missing module-load trace in sample $index." }
    if (@($trace | Where-Object { $_.Operation -in @('bridge.readPanel', 'defaults.read') }).Count) { throw "Startup unexpectedly read panel data in sample $index." }
    if ($results.Count -and $sample.artifactFingerprint -ne $results[0].artifactFingerprint) { throw "Measured artifacts changed during sample $index." }
    $results += $sample
    Write-Output "Startup sample $index/$Iterations complete."
}
function Get-Statistics([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    [pscustomobject]@{ count = $sorted.Count; minimum = $sorted[0]; median = $sorted[[int][Math]::Floor($sorted.Count / 2)]; p95 = $sorted[[int][Math]::Ceiling($sorted.Count * 0.95) - 1]; maximum = $sorted[-1] }
}
$report = [pscustomobject]@{
    utc = [DateTime]::UtcNow.ToString('o')
    iterations = $Iterations
    executable = $Executable
    executableSha256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
    hostLoadMilliseconds = Get-Statistics @($results | ForEach-Object hostLoadMilliseconds)
    showToCatalogMilliseconds = Get-Statistics @($results | ForEach-Object showToCatalogMilliseconds)
    processToCatalogMilliseconds = Get-Statistics @($results | ForEach-Object processToCatalogMilliseconds)
    scope = $results[0].scope
    samples = $results
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'report.json') -Encoding UTF8
Write-Output ($report | Select-Object iterations, hostLoadMilliseconds, showToCatalogMilliseconds, processToCatalogMilliseconds | Format-List | Out-String)
Write-Output "Startup component report: $OutputDirectory"
